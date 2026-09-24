using System;
using System.Collections.Generic;
using UnityEngine;

// 요청을 발행 프레임과 함께 보관하고 다음 프레임부터 FIFO로 전달한다.
public sealed class FrameRequestQueue
{
    private readonly Queue<Entry> _entries = new Queue<Entry>();

    public int Count => _entries.Count;

    // 요청과 발행 프레임을 큐 끝에 기록한다.
    public void Enqueue(IGameRequest request, int frame)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        _entries.Enqueue(new Entry(request, frame));
    }

    // 현재 프레임보다 앞서 발행된 요청만 순서대로 전달한다.
    public void DrainPreviousFrames(int frame, Action<IGameRequest> consume)
    {
        if (consume == null)
            throw new ArgumentNullException(nameof(consume));

        int eligibleCount = 0;
        foreach (Entry entry in _entries)
        {
            if (entry.Frame >= frame)
                break;
            eligibleCount++;
        }

        for (int i = 0; i < eligibleCount; i++)
            consume(_entries.Dequeue().Request);
    }

    // 대기 중인 요청을 모두 폐기한다.
    public void Clear()
    {
        _entries.Clear();
    }

    // 요청 객체와 발행 프레임을 한 항목으로 묶는다.
    private readonly struct Entry
    {
        public readonly IGameRequest Request;
        public readonly int Frame;

        // 큐 항목에 요청과 발행 프레임을 저장한다.
        public Entry(IGameRequest request, int frame)
        {
            Request = request;
            Frame = frame;
            Debug.Log($"Enqueued request {request.GetType().Name} at frame {frame}");
        }
    }
}
