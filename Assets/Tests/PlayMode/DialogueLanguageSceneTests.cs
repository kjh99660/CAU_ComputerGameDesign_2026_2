using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class DialogueLanguageSceneTests
{
    [UnityTest]
    public IEnumerator LanguageButtonLocalizesTheRoundAndPersistsAcrossScenes()
    {
        string key = TextManager.PreferenceKey;
        bool hadPreference = PlayerPrefs.HasKey(key);
        int previous = PlayerPrefs.GetInt(key);
        PlayerPrefs.DeleteKey(key);

        try
        {
            yield return TestScenes.Load(TestScenes.NeonCity);
            var manager = Object.FindFirstObjectByType<TextManager>();
            var state = Object.FindFirstObjectByType<GameStateManager>();
            var events = Object.FindFirstObjectByType<EventManager>();
            var ui = Object.FindFirstObjectByType<UIRoot>();
            Assert.That(manager.transform.parent.name, Is.EqualTo("Manager"));
            yield return WaitFor(state, GameState.Ready);

            var start = Object.FindFirstObjectByType<StartView>();
            Button language = Child<Button>(start.transform, "LanguageButton");
            var label = Child<TMP_Text>(language.transform, "Label");
            var exitRect = (RectTransform)Child<Button>(start.transform, "ExitButton").transform;
            var languageRect = (RectTransform)language.transform;
            Assert.That(languageRect.anchorMin, Is.EqualTo(exitRect.anchorMin));
            Assert.That(languageRect.anchoredPosition.y, Is.LessThan(exitRect.anchoredPosition.y - exitRect.rect.height));
            Assert.That(language.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(label.text, Is.EqualTo("LANG: KOR"));
            language.onClick.Invoke();
            Assert.That(label.text, Is.EqualTo("LANG: ENG"));
            Assert.That(state.CurrentState, Is.EqualTo(GameState.Ready));

            events.EnqueueRequest(new StartRequest(state.Tick));
            yield return WaitFor(state, GameState.IntroDialogue);
            Assert.That(Child<TMP_Text>(ui.DialogueScreen.transform, "SpeakerName").text, Is.EqualTo("Noa"));
            Assert.That(Child<TMP_Text>(ui.DialogueScreen.transform, "BodyText").text, Is.EqualTo("I'm in the hub. Emergency reboot started."));
            Assert.That(language.gameObject.activeInHierarchy, Is.False);
            events.EnqueueRequest(new DialogueCompleteRequest(state.Tick));
            yield return WaitFor(state, GameState.Countdown);
            events.EnqueueRequest(new CountdownCompleteRequest(state.Tick));
            yield return WaitFor(state, GameState.Battle);

            ui.PresentSpawnWarning(0);
            TMP_Text radio = Child<TMP_Text>(ui.BattleScreen.transform, "RadioText");
            Assert.That(radio.text, Does.Contain("Multiple robot signals"));
            ui.PresentBattleTimer(11f);
            ui.PresentBattleTimer(10f);
            Assert.That(radio.text, Is.EqualTo("Ten seconds left. Hang in there."));

            events.EnqueueRequest(new TimerReachedZeroRequest(state.Tick));
            yield return WaitFor(state, GameState.Finishing);
            events.EnqueueRequest(new FinishingCompleteRequest(state.Tick));
            yield return WaitFor(state, GameState.OutroDialogue);
            Assert.That(Child<TMP_Text>(ui.DialogueScreen.transform, "BodyText").text, Does.StartWith("Reboot complete."));

            yield return TestScenes.Load(TestScenes.Arena);
            state = Object.FindFirstObjectByType<GameStateManager>();
            yield return WaitFor(state, GameState.Ready);
            start = Object.FindFirstObjectByType<StartView>();
            language = Child<Button>(start.transform, "LanguageButton");
            label = Child<TMP_Text>(language.transform, "Label");
            Assert.That(label.text, Is.EqualTo("LANG: ENG"));
            language.onClick.Invoke();
            Assert.That(label.text, Is.EqualTo("LANG: KOR"));
            events = Object.FindFirstObjectByType<EventManager>();
            ui = Object.FindFirstObjectByType<UIRoot>();
            events.EnqueueRequest(new StartRequest(state.Tick));
            yield return WaitFor(state, GameState.IntroDialogue);
            Assert.That(Child<TMP_Text>(ui.DialogueScreen.transform, "SpeakerName").text, Is.EqualTo("노아"));
        }
        finally
        {
            if (hadPreference) PlayerPrefs.SetInt(key, previous);
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    private static T Child<T>(Transform root, string name) where T : Component
    {
        foreach (T child in root.GetComponentsInChildren<T>(true))
            if (child.name == name) return child;
        Assert.Fail("Missing UI object: " + name);
        return null;
    }

    private static IEnumerator WaitFor(GameStateManager state, GameState expected)
    {
        for (int i = 0; i < 180 && state.CurrentState != expected; i++) yield return null;
        Assert.That(state.CurrentState, Is.EqualTo(expected));
    }
}
