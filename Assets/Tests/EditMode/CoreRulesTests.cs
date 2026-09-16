using System;
using System.Collections.Generic;
using NUnit.Framework;

// 허용 전이, Reset 규칙과 요청 큐의 프레임 경계를 검증한다.
public sealed class CoreRulesTests
{
    private static readonly Type[] RequestTypes =
    {
        typeof(InitializationCompletedRequest), typeof(StartRequest), typeof(DialogueCompleteRequest),
        typeof(CountdownCompleteRequest), typeof(PauseRequest), typeof(ResumeRequest),
        typeof(TimerReachedZeroRequest), typeof(FinishingCompleteRequest),
        typeof(RestartRequest), typeof(QuitToStartRequest)
    };

    // 허용된 요청이 문서에 정한 상태로 전환되는지 확인한다.
    [TestCase(GameState.Initializing, typeof(InitializationCompletedRequest), GameState.Ready)]
    [TestCase(GameState.Ready, typeof(StartRequest), GameState.IntroDialogue)]
    [TestCase(GameState.IntroDialogue, typeof(DialogueCompleteRequest), GameState.Countdown)]
    [TestCase(GameState.Countdown, typeof(CountdownCompleteRequest), GameState.Battle)]
    [TestCase(GameState.Battle, typeof(PauseRequest), GameState.Paused)]
    [TestCase(GameState.Paused, typeof(ResumeRequest), GameState.Battle)]
    [TestCase(GameState.Battle, typeof(TimerReachedZeroRequest), GameState.Finishing)]
    [TestCase(GameState.Finishing, typeof(FinishingCompleteRequest), GameState.OutroDialogue)]
    [TestCase(GameState.OutroDialogue, typeof(DialogueCompleteRequest), GameState.Result)]
    public void AllowedTransitionHasExpectedTarget(GameState current, Type requestType, GameState expected)
    {
        Assert.IsTrue(GameStateRules.TryGetNext(current, Create(requestType), out GameState actual));
        Assert.AreEqual(expected, actual);
    }

    // 금지된 요청이 현재 상태를 바꾸지 않는지 확인한다.
    [Test]
    public void DisallowedTransitionsNeverChangeTheState()
    {
        foreach (GameState state in Enum.GetValues(typeof(GameState)))
        foreach (Type requestType in RequestTypes)
        {
            bool allowed = GameStateRules.TryGetNext(state, Create(requestType), out GameState next);
            if (!allowed)
                Assert.AreEqual(state, next);
        }

        Assert.IsFalse(GameStateRules.TryGetNext(GameState.Ready, new PauseRequest(1), out _));
        Assert.IsFalse(GameStateRules.TryGetNext(GameState.Result, new ResumeRequest(1), out _));
    }

    // Retry와 Quit 요청의 허용 상태를 확인한다.
    [Test]
    public void ResetRequestsAreLimitedToPausedAndResult()
    {
        Assert.IsTrue(GameStateRules.IsResetRequest(GameState.Paused, new RestartRequest(1)));
        Assert.IsTrue(GameStateRules.IsResetRequest(GameState.Result, new RestartRequest(1)));
        Assert.IsTrue(GameStateRules.IsResetRequest(GameState.Paused, new QuitToStartRequest(1)));
        Assert.IsFalse(GameStateRules.IsResetRequest(GameState.Result, new QuitToStartRequest(1)));
    }

    // 요청과 상태 변경 알림이 같은 라운드 Tick을 보관하는지 확인한다.
    [Test]
    public void RequestAndStateNotificationKeepTheRoundTick()
    {
        IGameRequest request = new PauseRequest(7);
        var notification = new GameStateChangedEvent(GameState.Battle, GameState.Paused, 7);

        Assert.AreEqual(7, request.Tick);
        Assert.AreEqual(7, notification.Tick);
        Assert.AreEqual(GameState.Battle, notification.Previous);
        Assert.AreEqual(GameState.Paused, notification.Current);
    }

    // 요청이 다음 프레임에 FIFO로 전달되고 재진입하지 않는지 확인한다.
    [Test]
    public void RequestsWaitUntilTheFollowingFrameAndKeepFifoOrder()
    {
        var queue = new FrameRequestQueue();
        var delivered = new List<Type>();
        queue.Enqueue(new PauseRequest(1), 10);
        queue.Enqueue(new ResumeRequest(1), 10);

        queue.DrainPreviousFrames(10, request => delivered.Add(request.GetType()));
        Assert.AreEqual(0, delivered.Count);

        queue.DrainPreviousFrames(11, request =>
        {
            delivered.Add(request.GetType());
            if (request is PauseRequest)
                queue.Enqueue(new StartRequest(1), 11);
        });

        CollectionAssert.AreEqual(new[] { typeof(PauseRequest), typeof(ResumeRequest) }, delivered);
        queue.DrainPreviousFrames(12, request => delivered.Add(request.GetType()));
        CollectionAssert.AreEqual(new[] { typeof(PauseRequest), typeof(ResumeRequest), typeof(StartRequest) }, delivered);
    }

    // 테스트할 요청 타입의 인스턴스를 생성한다.
    private static IGameRequest Create(Type requestType)
    {
        return (IGameRequest)Activator.CreateInstance(requestType, 1);
    }
}
