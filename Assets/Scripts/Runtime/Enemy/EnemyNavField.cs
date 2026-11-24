using System;
using System.Collections.Generic;
using UnityEngine;

// The walkable cells of a level on a regular XZ grid. One column can hold several cells above each other (floors),
// and two cells are linked only where an enemy can walk from one onto the other, so a deck and the street under it
// connect through the stairs and nowhere else. Where an edge is open, a one-way drop also links the upper cell to
// the floor below it: enemies come down after a player who jumps, but nobody goes up that way.
// On top of the cells sits a flow field: one shortest-path pass from the goal (the player) gives every cell its
// next step, and all enemies share it. Pure data and arithmetic: no physics and no scene access.
public sealed class EnemyNavField
{
    public const int None = -1;
    public const int Unreachable = int.MaxValue;

    private const int Straight = 10;            // cost of a step to a side neighbour, in tenths of a cell
    private const int Diagonal = 14;
    private const int EdgeCost = 4;             // extra cost of a cell beside a wall or a drop: paths keep off the edges
    private const int DropCost = 30;            // stepping off an edge and landing, about the time of three steps
    private const int DropReach = 4;            // a drop lands at most this many columns out from the edge
    private const int Buckets = DropCost + DropReach * Straight + EdgeCost + 1;
    private const int LookAhead = 8;            // cells followed along the flow when picking a point to walk to
    private const int DirectRangeCells = 48;    // a straight line to the goal is only tried this close to it
    private const float EdgeMargin = 0.01f;     // how far inside its cell a blocked move is stopped (m)

    // Direction index -> column offset. 0..3 are side neighbours (+x, +z, -x, -z), 4..7 diagonal ones.
    private static readonly int[] StepX = { 1, 0, -1, 0, 1, -1, -1, 1 };
    private static readonly int[] StepZ = { 0, 1, 0, -1, 1, 1, -1, -1 };
    // (stepZ + 1) * 3 + (stepX + 1) -> direction index. The middle entry (no step) is never read.
    private static readonly int[] Direction = { 6, 3, 7, 2, -1, 0, 5, 1, 4 };

    private readonly float _originX, _originZ, _cell;
    private readonly int _width, _depth;
    private readonly int[] _columnStart;        // per column: its lowest cell, or None
    private readonly byte[] _columnCount;
    private readonly float[] _height;           // per cell
    private readonly int[] _column;             // per cell
    private readonly int[] _link;               // per cell x 8 directions
    private readonly byte[] _entryCost;         // per cell: EdgeCost when a neighbour is missing
    private readonly int[] _drop;               // per cell x 4 side directions: the cell landed on when stepping off there
    private readonly byte[] _dropSpan;          // per cell x 4: how many columns out that landing is
    private readonly int[] _dropInStart;        // per cell: where its list of cells that drop onto it starts
    private readonly int[] _dropIn;

    private readonly int[] _cost;               // flow: cost of the walk to the goal
    private readonly int[] _next;               // flow: the neighbour that walk starts with
    private readonly int[] _directStamp;        // flow version the cached straight-line answer belongs to
    private readonly bool[] _direct;
    private readonly int[] _bucketHead = new int[Buckets];
    private readonly int[] _queueNode, _queueCost, _queueNext;
    private int _goal = None;
    private int _version;

    public int CellCount => _height.Length;
    public float CellSize => _cell;
    public int Goal => _goal;
    // Changes whenever the flow is recomputed or cleared. Lets callers cache what they read from it.
    public int Version => _version;

    // Collects the standing spots found in a level and turns them into a field.
    public sealed class Builder
    {
        private readonly Vector3 _origin;
        private readonly float _cell;
        private readonly int _width, _depth;
        private readonly List<Spot> _spots = new();

        private struct Spot
        {
            public int Column;
            public float Height;
        }

        // origin is the corner of column (0, 0) with the smallest x and z.
        public Builder(Vector3 origin, float cellSize, int width, int depth)
        {
            _origin = origin;
            _cell = Mathf.Max(0.05f, cellSize);
            _width = Mathf.Max(1, width);
            _depth = Mathf.Max(1, depth);
        }

        // An enemy can stand at this height in the middle of column (x, z).
        public void Add(int x, int z, float height)
        {
            if (x < 0 || z < 0 || x >= _width || z >= _depth)
                return;
            _spots.Add(new Spot { Column = x + z * _width, Height = height });
        }

        // maxClimbDegrees is the steepest walk between the middles of two neighbouring cells.
        // With a seed, only the cells that can be reached from the cell nearest to it are kept.
        // maxDrop > 0 lets a walker step off an open edge onto a floor at most that far below; canDrop, when given,
        // is asked whether the way from the upper cell to the lower one is free (no railing, nothing in the fall).
        public EnemyNavField Build(float maxClimbDegrees, Vector3? seed = null, float maxDrop = 0f,
            Func<Vector3, Vector3, bool> canDrop = null)
        {
            _spots.Sort((a, b) => a.Column != b.Column ? a.Column.CompareTo(b.Column) : a.Height.CompareTo(b.Height));
            var columns = new int[_spots.Count];
            var heights = new float[_spots.Count];
            for (int i = 0; i < _spots.Count; i++)
            {
                columns[i] = _spots[i].Column;
                heights[i] = _spots[i].Height;
            }

            var field = new EnemyNavField(_origin, _cell, _width, _depth, columns, heights, maxClimbDegrees, maxDrop, canDrop);
            if (!seed.HasValue)
                return field;

            int start = field.FindNearestCell(seed.Value, 8, float.PositiveInfinity);
            if (start == None)
                return field;
            bool[] keep = field.ConnectedTo(start);
            int kept = 0;
            for (int i = 0; i < keep.Length; i++)
                if (keep[i]) kept++;
            if (kept == keep.Length)
                return field;

            var keptColumns = new int[kept];
            var keptHeights = new float[kept];
            for (int i = 0, k = 0; i < keep.Length; i++)
            {
                if (!keep[i]) continue;
                keptColumns[k] = columns[i];
                keptHeights[k++] = heights[i];
            }
            return new EnemyNavField(_origin, _cell, _width, _depth, keptColumns, keptHeights, maxClimbDegrees, maxDrop, canDrop);
        }
    }

    // columns and heights list the cells sorted by column, lowest first within a column.
    private EnemyNavField(Vector3 origin, float cell, int width, int depth, int[] columns, float[] heights,
        float maxClimbDegrees, float maxDrop, Func<Vector3, Vector3, bool> canDrop)
    {
        _originX = origin.x;
        _originZ = origin.z;
        _cell = cell;
        _width = width;
        _depth = depth;
        _column = columns;
        _height = heights;
        int count = heights.Length;

        _columnStart = new int[width * depth];
        _columnCount = new byte[width * depth];
        Array.Fill(_columnStart, None);
        for (int i = 0; i < count; i++)
        {
            int column = columns[i];
            if (_columnStart[column] == None)
                _columnStart[column] = i;
            if (_columnCount[column] < byte.MaxValue)
                _columnCount[column]++;
        }

        _link = new int[count * 8];
        _entryCost = new byte[count];
        Array.Fill(_link, None);
        float climb = Mathf.Tan(Mathf.Clamp(maxClimbDegrees, 0f, 80f) * Mathf.Deg2Rad) * cell;
        for (int cell0 = 0; cell0 < count; cell0++)
            for (int direction = 0; direction < 4; direction++)
                _link[cell0 * 8 + direction] = ClosestInColumn(cell0, StepX[direction], StepZ[direction], climb);
        // A diagonal step needs both side cells too, so nobody cuts a corner of a wall or a deck edge.
        float diagonalClimb = climb * 1.41421356f;
        for (int cell0 = 0; cell0 < count; cell0++)
        {
            for (int direction = 4; direction < 8; direction++)
            {
                int alongX = Direction[3 + StepX[direction] + 1];
                int alongZ = Direction[(StepZ[direction] + 1) * 3 + 1];
                int sideX = _link[cell0 * 8 + alongX];
                int sideZ = _link[cell0 * 8 + alongZ];
                if (sideX == None || sideZ == None)
                    continue;
                int corner = _link[sideX * 8 + alongZ];
                if (corner != None && corner == _link[sideZ * 8 + alongX] &&
                    Mathf.Abs(_height[corner] - _height[cell0]) <= diagonalClimb)
                    _link[cell0 * 8 + direction] = corner;
            }
        }
        for (int cell0 = 0; cell0 < count; cell0++)
            for (int direction = 0; direction < 8; direction++)
                if (_link[cell0 * 8 + direction] == None)
                {
                    _entryCost[cell0] = EdgeCost;
                    break;
                }

        // Drops: from a cell with no neighbour on a side, onto the highest floor below it a few columns out.
        _drop = new int[count * 4];
        _dropSpan = new byte[count * 4];
        _dropInStart = new int[count + 1];
        Array.Fill(_drop, None);
        int drops = 0;
        if (maxDrop > 0f)
        {
            for (int cell0 = 0; cell0 < count; cell0++)
                for (int direction = 0; direction < 4; direction++)
                {
                    if (_link[cell0 * 8 + direction] != None)
                        continue;
                    int landing = LandingBelow(cell0, direction, climb, maxDrop, canDrop, out int span);
                    if (landing == None)
                        continue;
                    _drop[cell0 * 4 + direction] = landing;
                    _dropSpan[cell0 * 4 + direction] = (byte)span;
                    _dropInStart[landing + 1]++;
                    drops++;
                }
        }
        for (int i = 0; i < count; i++)
            _dropInStart[i + 1] += _dropInStart[i];
        _dropIn = new int[drops];
        var filled = new int[count];
        for (int cell0 = 0; cell0 < count && drops > 0; cell0++)
            for (int direction = 0; direction < 4; direction++)
            {
                int landing = _drop[cell0 * 4 + direction];
                if (landing != None)
                    _dropIn[_dropInStart[landing] + filled[landing]++] = cell0 * 4 + direction;
            }

        _cost = new int[count];
        _next = new int[count];
        _directStamp = new int[count];
        _direct = new bool[count];
        Array.Fill(_cost, Unreachable);
        Array.Fill(_next, None);
        int queue = count * 8 + drops + 8;
        _queueNode = new int[queue];
        _queueCost = new int[queue];
        _queueNext = new int[queue];
    }

    private int ClosestInColumn(int cell, int stepX, int stepZ, float climb)
    {
        int column = _column[cell];
        int x = column % _width + stepX, z = column / _width + stepZ;
        if (x < 0 || z < 0 || x >= _width || z >= _depth)
            return None;
        int other = x + z * _width;
        int first = _columnStart[other];
        if (first == None)
            return None;
        int best = None;
        float bestGap = climb;
        for (int i = first, end = first + _columnCount[other]; i < end; i++)
        {
            float gap = Mathf.Abs(_height[i] - _height[cell]);
            if (gap <= bestGap)
            {
                bestGap = gap;
                best = i;
            }
        }
        return best;
    }

    // Looks outward from a cell's open side for the floor a walker stepping off there lands on.
    private int LandingBelow(int cell, int direction, float climb, float maxDrop, Func<Vector3, Vector3, bool> canDrop, out int span)
    {
        int column = _column[cell];
        int x = column % _width, z = column / _width;
        for (span = 1; span <= DropReach; span++)
        {
            x += StepX[direction];
            z += StepZ[direction];
            if (x < 0 || z < 0 || x >= _width || z >= _depth)
                return None;
            int other = x + z * _width;
            int first = _columnStart[other];
            if (first == None)
                continue;   // nothing to stand on here: the wall under the edge, or still part of the gap
            int landing = None;
            for (int i = first, end = first + _columnCount[other]; i < end; i++)
            {
                float fall = _height[cell] - _height[i];
                if (fall > climb && fall <= maxDrop)
                    landing = i;   // floors are sorted upward, so the last one that fits is the highest
            }
            if (landing == None)
                return None;   // something to stand on, but not below: a wall or a higher floor
            if (canDrop == null || canDrop(Position(cell), Position(landing)))
                return landing;
            // The way down is not free here (the eaves of the roof, a sign): try one column further out.
        }
        return None;
    }

    private bool[] ConnectedTo(int start)
    {
        var seen = new bool[_height.Length];
        var open = new Stack<int>();
        seen[start] = true;
        open.Push(start);
        while (open.Count > 0)
        {
            int cell = open.Pop();
            for (int direction = 0; direction < 8; direction++)
            {
                int other = _link[cell * 8 + direction];
                if (other == None || seen[other])
                    continue;
                seen[other] = true;
                open.Push(other);
            }
            for (int direction = 0; direction < 4; direction++)
            {
                int landing = _drop[cell * 4 + direction];
                if (landing == None || seen[landing])
                    continue;
                seen[landing] = true;
                open.Push(landing);
            }
        }
        return seen;
    }

    // ---------------------------------------------------------------- cells
    public Vector3 Position(int cell)
    {
        int column = _column[cell];
        return new Vector3(_originX + (column % _width + 0.5f) * _cell, _height[cell],
            _originZ + (column / _width + 0.5f) * _cell);
    }

    public float Height(int cell) => _height[cell];

    // direction: 0..3 side neighbours (+x, +z, -x, -z), 4..7 diagonal ones.
    public int Neighbour(int cell, int direction) => _link[cell * 8 + direction];

    // The floor landed on when stepping off the cell's open side (direction 0..3). None where there is no drop.
    public int DropTarget(int cell, int direction) => _drop[cell * 4 + direction];

    // How many floors the column under a point has.
    public int FloorsAt(float x, float z) => TryColumn(x, z, out int column) ? _columnCount[column] : 0;

    // One of the floors of the column under a point, chosen by pick (0..1). None where nobody can stand.
    public int CellAt(float x, float z, float pick)
    {
        if (!TryColumn(x, z, out int column) || _columnCount[column] == 0)
            return None;
        int count = _columnCount[column];
        return _columnStart[column] + Mathf.Clamp((int)(pick * count), 0, count - 1);
    }

    // The floor of the column under a point whose height is closest to the point's, within the tolerance.
    public int FindCell(Vector3 position, float verticalTolerance)
    {
        return TryColumn(position.x, position.z, out int column) ? ClosestFloor(column, position.y, verticalTolerance, out _) : None;
    }

    // Like FindCell, but also looks at the columns around the point, nearest first, up to searchCells away.
    public int FindNearestCell(Vector3 position, int searchCells, float verticalTolerance)
    {
        int centreX = Mathf.Clamp(Mathf.FloorToInt((position.x - _originX) / _cell), 0, _width - 1);
        int centreZ = Mathf.Clamp(Mathf.FloorToInt((position.z - _originZ) / _cell), 0, _depth - 1);
        int best = ClosestFloor(centreX + centreZ * _width, position.y, verticalTolerance, out _);
        if (best != None && TryColumn(position.x, position.z, out _))
            return best;

        float bestDistance = float.PositiveInfinity;
        best = None;
        for (int ring = 0; ring <= searchCells && best == None; ring++)
        {
            for (int z = centreZ - ring; z <= centreZ + ring; z++)
            {
                if (z < 0 || z >= _depth) continue;
                bool edgeRow = z == centreZ - ring || z == centreZ + ring;
                for (int x = centreX - ring; x <= centreX + ring; x += edgeRow ? 1 : Mathf.Max(1, 2 * ring))
                {
                    if (x < 0 || x >= _width) continue;
                    int cell = ClosestFloor(x + z * _width, position.y, verticalTolerance, out float gap);
                    if (cell == None) continue;
                    float dx = _originX + (x + 0.5f) * _cell - position.x;
                    float dz = _originZ + (z + 0.5f) * _cell - position.z;
                    float distance = dx * dx + dz * dz + gap * gap;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = cell;
                    }
                }
            }
        }
        return best;
    }

    private int ClosestFloor(int column, float height, float tolerance, out float gap)
    {
        gap = tolerance;
        int first = _columnStart[column];
        if (first == None)
            return None;
        int best = None;
        for (int i = first, end = first + _columnCount[column]; i < end; i++)
        {
            float distance = Mathf.Abs(_height[i] - height);
            if (distance <= gap)
            {
                gap = distance;
                best = i;
            }
        }
        return best;
    }

    private bool TryColumn(float x, float z, out int column)
    {
        int cx = Mathf.FloorToInt((x - _originX) / _cell);
        int cz = Mathf.FloorToInt((z - _originZ) / _cell);
        column = cx + cz * _width;
        return cx >= 0 && cz >= 0 && cx < _width && cz < _depth;
    }

    // Ground height under a point for someone standing on the given cell: blended between the cell and the
    // neighbours on that side, so ramps and stairs are walked as a slope instead of in cell-sized jumps.
    public float SampleHeight(int cell, Vector3 position)
    {
        int column = _column[cell];
        float offsetX = (position.x - _originX) / _cell - (column % _width + 0.5f);
        float offsetZ = (position.z - _originZ) / _cell - (column / _width + 0.5f);
        int stepX = offsetX >= 0f ? 1 : -1, stepZ = offsetZ >= 0f ? 1 : -1;
        float weightX = Mathf.Min(1f, Mathf.Abs(offsetX)), weightZ = Mathf.Min(1f, Mathf.Abs(offsetZ));

        float here = _height[cell];
        int sideX = _link[cell * 8 + Direction[3 + stepX + 1]];
        int sideZ = _link[cell * 8 + Direction[(stepZ + 1) * 3 + 1]];
        int corner = _link[cell * 8 + Direction[(stepZ + 1) * 3 + stepX + 1]];
        float alongX = sideX != None ? _height[sideX] : here;
        float alongZ = sideZ != None ? _height[sideZ] : here;
        float across = corner != None ? _height[corner] : alongX + alongZ - here;
        return Mathf.Lerp(Mathf.Lerp(here, alongX, weightX), Mathf.Lerp(alongZ, across, weightX), weightZ);
    }

    // Walks from a point on the given cell toward 'to'. Where the cells end (a wall, a drop, a floor too high to
    // step onto) the move stops just inside the last cell and keeps whatever part of it runs along the edge.
    // Returns the cell the walker ends on; 'to' is changed to where it ends. Height is not touched.
    // followPath lets the walker step off an edge, and only where the path to the goal does: being pushed toward
    // an edge by the crowd never sends anyone over it. After such a step the returned cell is the one below, while
    // 'to' is still out over the edge.
    public int Move(int cell, ref Vector3 to, bool followPath = false)
    {
        for (int guard = 0; guard < 32; guard++)
        {
            int column = _column[cell];
            int x = column % _width, z = column / _width;
            int stepX = Math.Sign(Mathf.FloorToInt((to.x - _originX) / _cell) - x);
            int stepZ = Math.Sign(Mathf.FloorToInt((to.z - _originZ) / _cell) - z);
            if (stepX == 0 && stepZ == 0)
                return cell;

            if (stepX != 0 && stepZ != 0)
            {
                int corner = _link[cell * 8 + Direction[(stepZ + 1) * 3 + stepX + 1]];
                if (corner != None)
                {
                    cell = corner;
                    continue;
                }
            }
            int sideX = stepX != 0 ? _link[cell * 8 + Direction[3 + stepX + 1]] : None;
            if (sideX != None)
            {
                cell = sideX;
                continue;
            }
            int sideZ = stepZ != 0 ? _link[cell * 8 + Direction[(stepZ + 1) * 3 + 1]] : None;
            if (sideZ != None)
            {
                cell = sideZ;
                continue;
            }
            if (followPath && _goal != None)
            {
                int landing = None;
                if (stepX != 0) landing = _drop[cell * 4 + Direction[3 + stepX + 1]];
                if ((landing == None || landing != _next[cell]) && stepZ != 0) landing = _drop[cell * 4 + Direction[(stepZ + 1) * 3 + 1]];
                if (landing != None && landing == _next[cell])
                    return landing;
            }
            // Nowhere to go from this cell: stay inside it on every axis that wanted to leave.
            if (stepX != 0)
                to.x = Mathf.Clamp(to.x, _originX + x * _cell + EdgeMargin, _originX + (x + 1) * _cell - EdgeMargin);
            if (stepZ != 0)
                to.z = Mathf.Clamp(to.z, _originZ + z * _cell + EdgeMargin, _originZ + (z + 1) * _cell - EdgeMargin);
        }
        return cell;
    }

    // ---------------------------------------------------------------- flow
    // Points the flow at the cell nearest to a position. While the position is off the cells (in the air, on top
    // of a prop) the last goal stays. Returns true when the flow was recomputed.
    public bool SetGoal(Vector3 position, int searchCells = 6, float verticalTolerance = 2.5f)
    {
        int goal = FindNearestCell(position, searchCells, verticalTolerance);
        if (goal == None || goal == _goal)
            return false;
        _goal = goal;
        Recompute();
        return true;
    }

    public void ClearGoal()
    {
        _goal = None;
        _version++;
    }

    // Cost of the walk to the goal in tenths of a cell, Unreachable where there is none.
    public int Cost(int cell) => _goal == None ? Unreachable : _cost[cell];

    // Length of the walk to the goal in metres. Infinity where there is none.
    public float PathDistance(int cell)
    {
        int cost = Cost(cell);
        return cost == Unreachable ? float.PositiveInfinity : cost * _cell / Straight;
    }

    // The neighbour the walk to the goal starts with. None on the goal itself and where there is no walk.
    public int Next(int cell) => _goal == None ? None : _next[cell];

    // True when the goal can be walked to in a straight line: nothing to go around, same floor all the way.
    public bool HasDirectPath(int cell)
    {
        if (_goal == None)
            return false;
        if (_directStamp[cell] != _version)
        {
            _directStamp[cell] = _version;
            _direct[cell] = _cost[cell] <= DirectRangeCells * Straight && CanWalkStraight(cell, _goal);
        }
        return _direct[cell];
    }

    // A point to walk to from the given cell: the farthest cell a few steps down the flow that can still be
    // reached in a straight line. Walking at it rounds corners instead of zig-zagging from cell to cell.
    public Vector3 SteerPoint(int cell)
    {
        int target = cell;
        if (_goal != None)
        {
            int probe = cell;
            for (int i = 0; i < LookAhead; i++)
            {
                probe = _next[probe];
                if (probe == None || !CanWalkStraight(cell, probe))
                    break;
                target = probe;
            }
            // The path starts by stepping off an edge: walk at the cell below it.
            if (target == cell && _next[cell] != None)
                target = _next[cell];
        }
        return Position(target);
    }

    // Steps cell by cell along the line between two cells. Fails where a step is not linked.
    private bool CanWalkStraight(int from, int to)
    {
        int column = _column[from], goalColumn = _column[to];
        int x = column % _width, z = column / _width;
        int goalX = goalColumn % _width, goalZ = goalColumn / _width;
        int spanX = Math.Abs(goalX - x), spanZ = Math.Abs(goalZ - z);
        int signX = Math.Sign(goalX - x), signZ = Math.Sign(goalZ - z);
        int error = spanX - spanZ;
        int cell = from;
        while (x != goalX || z != goalZ)
        {
            int twice = error * 2, stepX = 0, stepZ = 0;
            if (twice > -spanZ)
            {
                error -= spanZ;
                stepX = signX;
            }
            if (twice < spanX)
            {
                error += spanX;
                stepZ = signZ;
            }
            cell = _link[cell * 8 + Direction[(stepZ + 1) * 3 + stepX + 1]];
            if (cell == None)
                return false;
            x += stepX;
            z += stepZ;
        }
        return cell == to;
    }

    // One pass outward from the goal. Step costs are small whole numbers, so the open cells are kept in a ring of
    // buckets instead of a heap: every cell and link is visited once and nothing is allocated.
    private void Recompute()
    {
        _version++;
        Array.Fill(_cost, Unreachable);
        Array.Fill(_next, None);
        Array.Fill(_bucketHead, None);
        int used = 0, pending = 1;
        _cost[_goal] = 0;
        _queueNode[0] = _goal;
        _queueCost[0] = 0;
        _queueNext[0] = None;
        _bucketHead[0] = used++;

        for (int current = 0; pending > 0; current++)
        {
            int bucket = current % Buckets;
            while (_bucketHead[bucket] != None)
            {
                int entry = _bucketHead[bucket];
                _bucketHead[bucket] = _queueNext[entry];
                pending--;
                int cell = _queueNode[entry];
                if (_queueCost[entry] != _cost[cell])
                    continue;   // a cheaper way to this cell was found after it was queued

                for (int direction = 0; direction < 8; direction++)
                {
                    int other = _link[cell * 8 + direction];
                    if (other == None)
                        continue;
                    int cost = current + (direction < 4 ? Straight : Diagonal) + _entryCost[other];
                    if (cost >= _cost[other])
                        continue;
                    _cost[other] = cost;
                    _next[other] = cell;
                    int target = cost % Buckets;
                    _queueNode[used] = other;
                    _queueCost[used] = cost;
                    _queueNext[used] = _bucketHead[target];
                    _bucketHead[target] = used++;
                    pending++;
                }

                // Cells that reach this one by stepping off an edge. One-way: the walk never goes up a drop.
                for (int i = _dropInStart[cell], end = _dropInStart[cell + 1]; i < end; i++)
                {
                    int other = _dropIn[i] >> 2;
                    int cost = current + DropCost + (_dropSpan[_dropIn[i]] - 1) * Straight + _entryCost[other];
                    if (cost >= _cost[other])
                        continue;
                    _cost[other] = cost;
                    _next[other] = cell;
                    int target = cost % Buckets;
                    _queueNode[used] = other;
                    _queueCost[used] = cost;
                    _queueNext[used] = _bucketHead[target];
                    _bucketHead[target] = used++;
                    pending++;
                }
            }
        }
    }
}
