using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class UiRequestDispatcherTests
{
    private static readonly object[] RequestCases =
    {
        new object[] { UiRequestType.Start, typeof(StartRequest) },
        new object[] { UiRequestType.DialogueComplete, typeof(DialogueCompleteRequest) },
        new object[] { UiRequestType.CountdownComplete, typeof(CountdownCompleteRequest) },
        new object[] { UiRequestType.Pause, typeof(PauseRequest) },
        new object[] { UiRequestType.Resume, typeof(ResumeRequest) },
        new object[] { UiRequestType.Restart, typeof(RestartRequest) },
        new object[] { UiRequestType.QuitToStart, typeof(QuitToStartRequest) },
        new object[] { UiRequestType.FinishingComplete, typeof(FinishingCompleteRequest) }
    };

    [TestCaseSource(nameof(RequestCases))]
    public void UiInputCreatesExpectedRequestWithCurrentTick(UiRequestType input, Type expectedType)
    {
        var bus = new FakeEventBus();
        int tick = 7;
        var dispatcher = new UiRequestDispatcher(bus, () => tick);

        dispatcher.Enqueue(input);

        Assert.AreEqual(1, bus.Requests.Count);
        Assert.AreEqual(expectedType, bus.Requests[0].GetType());
        Assert.AreEqual(7, bus.Requests[0].Tick);
    }

    [Test]
    public void DisabledUiInputDoesNotEnterQueue()
    {
        var bus = new FakeEventBus();
        var dispatcher = new UiRequestDispatcher(bus, () => 3);
        dispatcher.SetInputEnabled(false);

        dispatcher.Enqueue(UiRequestType.Start);

        Assert.AreEqual(0, bus.Requests.Count);
    }

    [Test]
    public void UiRootParticipatesInInitializationAndRoundReset()
    {
        Assert.IsTrue(typeof(IRoundInitializable).IsAssignableFrom(typeof(UIRoot)));
        Assert.IsTrue(typeof(IRoundResettable).IsAssignableFrom(typeof(UIRoot)));
    }

    [Test]
    public void StartUiIsVisibleDuringAwakeInitializationButAcceptsInputOnlyWhenReady()
    {
        var root = new GameObject("StartViewTest");
        var canvasGroup = root.AddComponent<CanvasGroup>();
        var view = root.AddComponent<StartView>();
        var presenter = new StartPresenter(view, new UiRequestDispatcher(new FakeEventBus(), () => 0));

        try
        {
            root.SetActive(false);
            presenter.OnGameStateChanged(
                new GameStateChangedEvent(GameState.Initializing, GameState.Initializing, 0));

            Assert.IsTrue(root.activeSelf);
            Assert.IsFalse(canvasGroup.interactable);
            Assert.IsFalse(canvasGroup.blocksRaycasts);

            presenter.OnGameStateChanged(
                new GameStateChangedEvent(GameState.Initializing, GameState.Ready, 0));

            Assert.IsTrue(root.activeSelf);
            Assert.IsTrue(canvasGroup.interactable);
            Assert.IsTrue(canvasGroup.blocksRaycasts);
        }
        finally
        {
            presenter.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private sealed class FakeEventBus : IGameEventBus
    {
        public readonly List<IGameRequest> Requests = new List<IGameRequest>();
        public event Action<IGameRequest> RequestDequeued { add { } remove { } }
        public event Action<GameStateChangedEvent> StateChanged;
        public void EnqueueRequest(IGameRequest request) => Requests.Add(request);
        public void PublishStateChanged(GameStateChangedEvent notification) => StateChanged?.Invoke(notification);
    }
}
