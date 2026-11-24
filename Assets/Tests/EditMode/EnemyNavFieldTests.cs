using System;
using NUnit.Framework;
using UnityEngine;

// Walkable cells and the shared path to the player, without physics: floors are described by hand.
public sealed class EnemyNavFieldTests
{
    private const float Climb = 40f;   // degrees; with 1 m cells a side step may rise 0.84 m

    // One floor per column where height(x, z) is not NaN. Cells are 1 m, column (x, z) covers x..x+1, z..z+1.
    private static EnemyNavField Build(int width, int depth, Func<int, int, float> height, Vector3? seed = null)
    {
        var builder = new EnemyNavField.Builder(Vector3.zero, 1f, width, depth);
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                float h = height(x, z);
                if (!float.IsNaN(h))
                    builder.Add(x, z, h);
            }
        return builder.Build(Climb, seed);
    }

    private static Vector3 Middle(int x, int z, float y = 0f) => new(x + 0.5f, y, z + 0.5f);

    private static int Walk(EnemyNavField field, int from, Action<Vector3, Vector3> visit = null)
    {
        int steps = 0;
        int cell = from;
        while (cell != field.Goal)
        {
            int next = field.Next(cell);
            Assert.That(next, Is.Not.EqualTo(EnemyNavField.None), "The walk ended before the goal.");
            visit?.Invoke(field.Position(cell), field.Position(next));
            cell = next;
            Assert.That(++steps, Is.LessThan(10_000), "The walk does not end.");
        }
        return steps;
    }

    [Test]
    public void OpenFloorIsCrossedInAStraightLine()
    {
        EnemyNavField field = Build(10, 10, (_, _) => 0f);
        Assert.That(field.SetGoal(Middle(8, 8)), Is.True);

        int start = field.FindCell(Middle(1, 1), 0.5f);
        Assert.That(field.Cost(start), Is.EqualTo(7 * 14), "Seven diagonal steps.");
        Assert.That(Walk(field, start), Is.EqualTo(7));
        Assert.That(field.HasDirectPath(start), Is.True);
        Assert.That(field.PathDistance(start), Is.EqualTo(9.8f).Within(0.001f));
    }

    [Test]
    public void WallIsWalkedAroundAndBlocksTheStraightLine()
    {
        // A wall along x = 5 with a gap at z = 9..10.
        EnemyNavField field = Build(11, 11, (x, z) => x == 5 && z <= 8 ? float.NaN : 0f);
        field.SetGoal(Middle(9, 1));
        int start = field.FindCell(Middle(1, 1), 0.5f);

        Assert.That(field.HasDirectPath(start), Is.False);
        Assert.That(field.PathDistance(start), Is.GreaterThan(16f), "Straight across would be 8 m.");
        bool usedGap = false;
        Walk(field, start, (from, to) =>
        {
            Assert.That(field.FindCell(to, 0.5f), Is.Not.EqualTo(EnemyNavField.None));
            if (Mathf.FloorToInt(to.x) == 5)
            {
                usedGap = true;
                Assert.That(to.z, Is.GreaterThan(9f), "The walk crossed the wall.");
            }
        });
        Assert.That(usedGap, Is.True);
        Assert.That(field.HasDirectPath(field.FindCell(Middle(7, 1), 0.5f)), Is.True, "Same side as the goal.");
    }

    [Test]
    public void SteerPointCanAlwaysBeWalkedToInAStraightLine()
    {
        EnemyNavField field = Build(11, 11, (x, z) => x == 5 && z <= 8 ? float.NaN : 0f);
        field.SetGoal(Middle(9, 1));
        int moved = 0;
        for (int z = 0; z < 11; z++)
            for (int x = 0; x < 5; x++)
            {
                int cell = field.FindCell(Middle(x, z), 0.5f);
                Vector3 from = field.Position(cell), to = field.SteerPoint(cell);
                if ((to - from).sqrMagnitude > 0.5f) moved++;
                for (float t = 0f; t <= 1f; t += 0.05f)
                    Assert.That(field.FindCell(Vector3.Lerp(from, to, t), 0.5f), Is.Not.EqualTo(EnemyNavField.None),
                        $"The point to walk to from ({x}, {z}) lies behind the wall.");
            }
        Assert.That(moved, Is.EqualTo(55), "Every cell is given a point ahead of it.");
    }

    // Ground 12 x 6 at height 0, a deck at height 3 over x = 8..11, and a ramp along z = 0 from x = 2 up to x = 7.
    private static EnemyNavField GroundRampAndDeck(Vector3? seed = null, float maxDrop = 0f, Func<Vector3, Vector3, bool> canDrop = null)
    {
        var builder = new EnemyNavField.Builder(Vector3.zero, 1f, 12, 6);
        for (int z = 0; z < 6; z++)
            for (int x = 0; x < 12; x++)
            {
                bool ramp = z == 0 && x >= 2 && x <= 7;
                if (ramp)
                    builder.Add(x, z, (x - 1) * 0.5f);   // 0.5 .. 3.0
                else
                    builder.Add(x, z, 0f);
                if (x >= 8)
                    builder.Add(x, z, 3f);
            }
        return builder.Build(Climb, seed, maxDrop, canDrop);
    }

    [Test]
    public void FloorsAboveEachOtherConnectOnlyThroughTheRamp()
    {
        EnemyNavField field = GroundRampAndDeck();
        Assert.That(field.FloorsAt(10.5f, 3.5f), Is.EqualTo(2));
        int onDeck = field.FindCell(Middle(10, 3, 3.1f), 1f);
        int under = field.FindCell(Middle(10, 3, 0.1f), 1f);
        Assert.That(field.Height(onDeck), Is.EqualTo(3f));
        Assert.That(field.Height(under), Is.EqualTo(0f));

        field.SetGoal(Middle(10, 3, 3f));
        Assert.That(field.Goal, Is.EqualTo(onDeck));
        Assert.That(field.HasDirectPath(under), Is.False, "Right under the player is not next to the player.");
        Assert.That(field.PathDistance(under), Is.GreaterThan(15f));

        int rampCells = 0;
        Walk(field, under, (from, to) =>
        {
            Assert.That(Mathf.Abs(to.y - from.y), Is.LessThan(0.9f), "Nobody climbs a wall.");
            if (to.y > 0.1f && to.y < 2.9f) rampCells++;
        });
        Assert.That(rampCells, Is.GreaterThanOrEqualTo(4), "The way up is the ramp.");
    }

    [Test]
    public void GroundHeightIsBlendedAlongARamp()
    {
        EnemyNavField field = GroundRampAndDeck();
        int cell = field.FindCell(new Vector3(4.5f, 1.5f, 0.5f), 0.3f);
        Assert.That(field.Height(cell), Is.EqualTo(1.5f));
        Assert.That(field.SampleHeight(cell, new Vector3(4.5f, 0f, 0.5f)), Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(field.SampleHeight(cell, new Vector3(4.75f, 0f, 0.5f)), Is.EqualTo(1.625f).Within(0.001f));
        Assert.That(field.SampleHeight(cell, new Vector3(5f, 0f, 0.5f)), Is.EqualTo(1.75f).Within(0.001f));
        Assert.That(field.SampleHeight(cell, new Vector3(4f, 0f, 0.5f)), Is.EqualTo(1.25f).Within(0.001f));
        // Toward the ground beside the ramp there is no link (1.5 m step), so the height stays the ramp's.
        Assert.That(field.SampleHeight(cell, new Vector3(4.5f, 0f, 0.95f)), Is.EqualTo(1.5f).Within(0.001f));
    }

    [Test]
    public void SteepStepsDoNotConnectAndUnreachableCellsAreDropped()
    {
        // Left half at height 0, right half 1 m higher with no ramp.
        EnemyNavField all = Build(8, 4, (x, _) => x < 4 ? 0f : 1f);
        int edge = all.FindCell(Middle(3, 1), 0.5f);
        Assert.That(all.Neighbour(edge, 0), Is.EqualTo(EnemyNavField.None), "+x is a 1 m step up.");
        Assert.That(all.Neighbour(edge, 2), Is.Not.EqualTo(EnemyNavField.None));
        Assert.That(all.CellCount, Is.EqualTo(32));

        EnemyNavField reachable = Build(8, 4, (x, _) => x < 4 ? 0f : 1f, Middle(1, 1));
        Assert.That(reachable.CellCount, Is.EqualTo(16));
        Assert.That(reachable.FindCell(Middle(6, 1, 1f), 0.5f), Is.EqualTo(EnemyNavField.None));
        Assert.That(reachable.FloorsAt(6.5f, 1.5f), Is.EqualTo(0));
    }

    [Test]
    public void MoveStopsAtAnEdgeAndSlidesAlongIt()
    {
        EnemyNavField field = Build(5, 5, (x, z) => x == 3 && z == 2 ? float.NaN : 0f);
        int start = field.FindCell(Middle(2, 2), 0.5f);

        // Into the hole at (3, 2): stopped at the edge of the own cell.
        Vector3 to = new(3.6f, 0f, 2.5f);
        int cell = field.Move(start, ref to);
        Assert.That(cell, Is.EqualTo(start));
        Assert.That(to.x, Is.EqualTo(2.99f).Within(0.001f));
        Assert.That(to.z, Is.EqualTo(2.5f));

        // Diagonally past the hole: the sideways part of the move is kept.
        to = new Vector3(3.4f, 0f, 3.3f);
        cell = field.Move(start, ref to);
        Assert.That(field.Position(cell), Is.EqualTo(Middle(3, 3)));
        Assert.That(to, Is.EqualTo(new Vector3(3.4f, 0f, 3.3f)));

        // Out of the level: stopped inside the last cell on both axes.
        to = new Vector3(9f, 0f, 9f);
        cell = field.Move(field.FindCell(Middle(4, 4), 0.5f), ref to);
        Assert.That(field.Position(cell), Is.EqualTo(Middle(4, 4)));
        Assert.That(to.x, Is.EqualTo(4.99f).Within(0.001f));
        Assert.That(to.z, Is.EqualTo(4.99f).Within(0.001f));

        // Several cells in one move are walked one by one.
        to = new Vector3(0.2f, 0f, 4.7f);
        cell = field.Move(field.FindCell(Middle(4, 0), 0.5f), ref to);
        Assert.That(field.Position(cell), Is.EqualTo(Middle(0, 4)));
    }

    [Test]
    public void GoalOffTheCellsUsesTheNearestCellAndIsKeptWhileOutOfReach()
    {
        EnemyNavField field = Build(9, 9, (x, z) => x >= 3 && x <= 5 && z >= 3 && z <= 5 ? float.NaN : 0f);
        Assert.That(field.Goal, Is.EqualTo(EnemyNavField.None));
        Assert.That(field.Cost(0), Is.EqualTo(EnemyNavField.Unreachable));

        // Standing on a prop in the hole, slightly toward -x: the cell next to the hole on that side.
        Assert.That(field.SetGoal(new Vector3(4.3f, 0.8f, 4.5f)), Is.True);
        Assert.That(field.Position(field.Goal), Is.EqualTo(Middle(2, 4)));

        // High in the air: no cell within the tolerance, the goal stays.
        int goal = field.Goal, version = field.Version;
        Assert.That(field.SetGoal(new Vector3(7.5f, 9f, 7.5f)), Is.False);
        Assert.That(field.Goal, Is.EqualTo(goal));
        Assert.That(field.Version, Is.EqualTo(version));
    }

    [Test]
    public void PathIsOnlyRecomputedWhenTheGoalEntersAnotherCell()
    {
        EnemyNavField field = Build(6, 6, (_, _) => 0f);
        field.SetGoal(new Vector3(2.2f, 0f, 2.2f));
        int version = field.Version;
        Assert.That(field.SetGoal(new Vector3(2.8f, 0f, 2.7f)), Is.False);
        Assert.That(field.Version, Is.EqualTo(version));
        Assert.That(field.SetGoal(new Vector3(3.1f, 0f, 2.7f)), Is.True);
        Assert.That(field.Version, Is.Not.EqualTo(version));

        field.ClearGoal();
        Assert.That(field.Goal, Is.EqualTo(EnemyNavField.None));
        Assert.That(field.HasDirectPath(0), Is.False);
        Assert.That(float.IsPositiveInfinity(field.PathDistance(0)), Is.True);
    }

    [Test]
    public void CellsBesideAnEdgeCostMoreSoPathsKeepToTheMiddle()
    {
        // A 3-wide corridor: the walk down its length stays in the middle row.
        EnemyNavField field = Build(12, 3, (_, _) => 0f);
        field.SetGoal(Middle(10, 1));
        Walk(field, field.FindCell(Middle(1, 1), 0.5f), (_, to) => Assert.That(Mathf.FloorToInt(to.z), Is.EqualTo(1)));
    }

    [Test]
    public void EnemiesStepOffAnOpenEdgeButNeverClimbIt()
    {
        EnemyNavField field = GroundRampAndDeck(maxDrop: 4f);
        int edge = field.FindCell(Middle(8, 3, 3f), 0.5f);      // west edge of the deck
        int below = field.FindCell(Middle(7, 3), 0.5f);
        Assert.That(field.DropTarget(edge, 2), Is.EqualTo(below), "-x is a 3 m step down.");
        Assert.That(field.DropTarget(below, 0), Is.EqualTo(EnemyNavField.None), "Nobody drops upward.");
        Assert.That(field.Neighbour(edge, 2), Is.EqualTo(EnemyNavField.None), "A drop is not a walk.");

        // Player on the ground west of the deck: an enemy on the deck comes straight down.
        field.SetGoal(Middle(5, 3));
        Assert.That(field.Next(edge), Is.EqualTo(below));
        Assert.That(field.PathDistance(field.FindCell(Middle(10, 3, 3f), 0.5f)), Is.LessThan(10f));
        Assert.That(field.SteerPoint(edge), Is.EqualTo(field.Position(below)), "Standing on the edge, the way on is over it.");
        Assert.That(field.HasDirectPath(edge), Is.False);

        // Player on the deck: an enemy below still walks around to the ramp.
        field.SetGoal(Middle(10, 3, 3f));
        Assert.That(field.PathDistance(below), Is.GreaterThan(12f));

        // Without drops the only way down is the ramp as well.
        EnemyNavField stairsOnly = GroundRampAndDeck();
        stairsOnly.SetGoal(Middle(5, 3));
        Assert.That(stairsOnly.PathDistance(stairsOnly.FindCell(Middle(10, 3, 3f), 0.5f)), Is.GreaterThan(12f));
    }

    [Test]
    public void AnEdgeIsOnlySteppedOffWhileFollowingThePath()
    {
        EnemyNavField field = GroundRampAndDeck(maxDrop: 4f);
        int edge = field.FindCell(Middle(8, 3, 3f), 0.5f);
        int below = field.FindCell(Middle(7, 3), 0.5f);
        field.SetGoal(Middle(5, 3));

        // Pushed toward the edge by the crowd: stays on the deck.
        Vector3 to = new(7.6f, 3f, 3.5f);
        Assert.That(field.Move(edge, ref to), Is.EqualTo(edge));
        Assert.That(to.x, Is.EqualTo(8.01f).Within(0.001f));

        // Chasing, and the path goes over the edge: steps off, still out over the edge.
        to = new Vector3(7.6f, 3f, 3.5f);
        Assert.That(field.Move(edge, ref to, true), Is.EqualTo(below));
        Assert.That(to.x, Is.EqualTo(7.6f));

        // Chasing a player who is on the deck: the path does not go over the edge, so neither does the enemy.
        field.SetGoal(Middle(10, 3, 3f));
        to = new Vector3(7.6f, 3f, 3.5f);
        Assert.That(field.Move(edge, ref to, true), Is.EqualTo(edge));
    }

    [Test]
    public void ARailingStopsADrop()
    {
        EnemyNavField field = GroundRampAndDeck(maxDrop: 4f, canDrop: (_, _) => false);
        int edge = field.FindCell(Middle(8, 3, 3f), 0.5f);
        Assert.That(field.DropTarget(edge, 2), Is.EqualTo(EnemyNavField.None));
        // Too far down is not a drop either.
        EnemyNavField shallow = GroundRampAndDeck(maxDrop: 2f);
        Assert.That(shallow.DropTarget(shallow.FindCell(Middle(8, 3, 3f), 0.5f), 2), Is.EqualTo(EnemyNavField.None));
    }

    [Test]
    public void APitIsKeptWhenItCanBeDroppedIntoAndHasNoWayOut()
    {
        // Left half 3 m up, right half a pit with no ramp. The seed is on the left.
        Func<int, int, float> level = (x, _) => x < 4 ? 3f : 0f;
        Assert.That(Build(8, 4, level, Middle(1, 1, 3f)).CellCount, Is.EqualTo(16));

        var builder = new EnemyNavField.Builder(Vector3.zero, 1f, 8, 4);
        for (int z = 0; z < 4; z++)
            for (int x = 0; x < 8; x++)
                builder.Add(x, z, level(x, z));
        EnemyNavField field = builder.Build(Climb, Middle(1, 1, 3f), 4f);
        Assert.That(field.CellCount, Is.EqualTo(32));

        int up = field.FindCell(Middle(1, 1, 3f), 0.5f), pit = field.FindCell(Middle(6, 1), 0.5f);
        field.SetGoal(Middle(6, 1));
        Assert.That(field.Cost(up), Is.LessThan(EnemyNavField.Unreachable), "Into the pit after the player.");
        field.SetGoal(Middle(1, 1, 3f));
        Assert.That(field.Cost(pit), Is.EqualTo(EnemyNavField.Unreachable), "Nobody in the pit can reach a player above it.");
        Assert.That(float.IsPositiveInfinity(field.PathDistance(pit)), Is.True);
    }

    [Test]
    public void ADropLandsPastTheWallUnderTheEdge()
    {
        // Roof at 3 m over x = 0..3. The column right under its edge (x = 4) is inside the wall: nothing to stand on.
        EnemyNavField field = Build(10, 3, (x, _) => x < 4 ? 3f : x == 4 ? float.NaN : 0f);
        Assert.That(field.DropTarget(field.FindCell(Middle(3, 1, 3f), 0.5f), 0), Is.EqualTo(EnemyNavField.None), "Drops are off unless asked for.");

        var builder = new EnemyNavField.Builder(Vector3.zero, 1f, 10, 3);
        for (int z = 0; z < 3; z++)
            for (int x = 0; x < 10; x++)
                if (x != 4) builder.Add(x, z, x < 4 ? 3f : 0f);
        field = builder.Build(Climb, null, 4f);
        int edge = field.FindCell(Middle(3, 1, 3f), 0.5f);
        Assert.That(field.DropTarget(edge, 0), Is.EqualTo(field.FindCell(Middle(5, 1), 0.5f)));
        field.SetGoal(Middle(8, 1));
        Assert.That(field.Next(edge), Is.EqualTo(field.FindCell(Middle(5, 1), 0.5f)));
    }
}
