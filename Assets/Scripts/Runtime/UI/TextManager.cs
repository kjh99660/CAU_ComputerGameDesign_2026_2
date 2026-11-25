using System;
using UnityEngine;

public enum DialogueLanguage { Korean, English }

[DisallowMultipleComponent]
public sealed class TextManager : MonoBehaviour
{
    public const string PreferenceKey = "REBOOT.DialogueLanguage";

    [SerializeField] private TextAsset koreanText;
    [SerializeField] private TextAsset englishText;

    private DialogueSequenceData _original;
    private DialogueSequenceData _korean;
    private DialogueSequenceData _english;

    public DialogueLanguage Language { get; private set; }
    public DialogueSequenceData Sequence => Language == DialogueLanguage.English
        ? _english != null ? _english : _original
        : _korean != null ? _korean : _original;

    internal void Configure(DialogueSequenceData original)
    {
        _original = original;
        Language = PlayerPrefs.GetInt(PreferenceKey) == (int)DialogueLanguage.English
            ? DialogueLanguage.English : DialogueLanguage.Korean;
        _korean = Load(koreanText, DialogueLanguage.Korean);
        _english = Load(englishText, DialogueLanguage.English);
    }

    internal void ToggleLanguage()
    {
        Language = Language == DialogueLanguage.Korean ? DialogueLanguage.English : DialogueLanguage.Korean;
        PlayerPrefs.SetInt(PreferenceKey, (int)Language);
        PlayerPrefs.Save();
    }

    private DialogueSequenceData Load(TextAsset file, DialogueLanguage language)
    {
        if (!DialogueText.TryRead(file != null ? file.text : null, _original, out var translation, out string error))
        {
            Debug.LogWarning($"{nameof(TextManager)}: {language} dialogue file is invalid. {error} Using the source sequence.", this);
            return null;
        }

        DialogueSequenceData sequence = Instantiate(_original);
        translation.ApplyTo(sequence);
        return sequence;
    }

    private void OnDestroy()
    {
        if (_korean != null) Destroy(_korean);
        if (_english != null) Destroy(_english);
    }
}

[Serializable]
public sealed class DialogueText
{
    public string noa;
    public string k7;
    public string[] intro;
    public string[] outro;
    public string[] outroManyHits;
    public string[] outroPrecise;
    public string[] battleRadio;
    public string[] spawnWarnings;

    public static bool TryRead(string json, DialogueSequenceData source, out DialogueText text, out string error)
    {
        text = null;
        if (source == null || string.IsNullOrWhiteSpace(json))
        {
            error = "Dialogue sequence or text is missing.";
            return false;
        }

        try { text = JsonUtility.FromJson<DialogueText>(json); }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        if (text == null || string.IsNullOrWhiteSpace(text.noa) || string.IsNullOrWhiteSpace(text.k7))
        {
            error = "Both speaker names (noa and k7) are required.";
            return false;
        }

        error = Validate(nameof(intro), text.intro, source.intro?.Length ?? 0)
            ?? Validate(nameof(outro), text.outro, source.outro?.Length ?? 0)
            ?? Validate(nameof(outroManyHits), text.outroManyHits, source.outroManyHits?.Length ?? 0)
            ?? Validate(nameof(outroPrecise), text.outroPrecise, source.outroPrecise?.Length ?? 0)
            ?? Validate(nameof(battleRadio), text.battleRadio, source.battleRadio?.Length ?? 0)
            ?? Validate(nameof(spawnWarnings), text.spawnWarnings, source.spawnWarnings?.Length ?? 0);
        return error == null;
    }

    private static string Validate(string section, string[] lines, int count)
    {
        if (lines == null || lines.Length != count)
            return $"{section} must contain exactly {count} lines.";
        for (int i = 0; i < count; i++)
            if (string.IsNullOrWhiteSpace(lines[i]))
                return $"{section}, line {i + 1}, cannot be blank.";
        return null;
    }

    internal void ApplyTo(DialogueSequenceData sequence)
    {
        ApplyLines(sequence.intro, intro);
        ApplyLines(sequence.outro, outro);
        ApplyLines(sequence.outroManyHits, outroManyHits);
        ApplyLines(sequence.outroPrecise, outroPrecise);
        ApplyLines(sequence.spawnWarnings, spawnWarnings);
        for (int i = 0; i < battleRadio.Length; i++)
        {
            sequence.battleRadio[i].speaker = Speaker(sequence.battleRadio[i].leftSpeaker);
            sequence.battleRadio[i].text = battleRadio[i];
        }
    }

    private void ApplyLines(DialogueLine[] target, string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            target[i].speaker = Speaker(target[i].leftSpeaker);
            target[i].text = lines[i];
        }
    }

    private string Speaker(bool left) => left ? noa : k7;
}
