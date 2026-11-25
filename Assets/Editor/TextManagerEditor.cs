using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TextManager))]
public sealed class TextManagerEditor : Editor
{
    private sealed class Draft
    {
        public TextAsset File;
        public string Imported;
        public string Text;
        public bool Open = true;
    }

    private readonly Draft _korean = new();
    private readonly Draft _english = new();

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var source = Resources.Load<DialogueSequenceData>("DialogueSequence");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            DrawFile("Korean dialogue", "koreanText", _korean, source);
            DrawFile("English dialogue", "englishText", _english, source);
        }
    }

    private void DrawFile(string title, string property, Draft draft, DialogueSequenceData source)
    {
        var file = serializedObject.FindProperty(property).objectReferenceValue as TextAsset;
        if (file == null) return;
        if (draft.File != file || (draft.Text == draft.Imported && draft.Imported != file.text))
            Reload(draft, file);
        draft.Open = EditorGUILayout.Foldout(draft.Open, title, true);
        if (!draft.Open) return;

        draft.Text = EditorGUILayout.TextArea(draft.Text, GUILayout.MinHeight(140));
        bool changed = draft.Text != draft.Imported;
        bool externalEdit = file.text != draft.Imported;
        bool valid = DialogueText.TryRead(draft.Text, source, out _, out string error);
        if (externalEdit)
            EditorGUILayout.HelpBox("The file changed on disk. Reload it before saving.", MessageType.Warning);
        else if (!valid)
            EditorGUILayout.HelpBox(error, MessageType.Error);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!changed || externalEdit || !valid))
                if (GUILayout.Button("Save TXT")) Save(draft);
            using (new EditorGUI.DisabledScope(!changed && !externalEdit))
                if (GUILayout.Button("Reload TXT")) Reload(draft, file);
        }
    }

    private static void Reload(Draft draft, TextAsset file)
    {
        draft.File = file;
        draft.Text = draft.Imported = file.text;
    }

    private static void Save(Draft draft)
    {
        string path = AssetDatabase.GetAssetPath(draft.File);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Dialogue Text", "Select a .txt asset under Assets to save changes.", "OK");
            return;
        }
        try { File.WriteAllText(path, draft.Text); }
        catch (IOException exception)
        {
            EditorUtility.DisplayDialog("Dialogue Text", exception.Message, "OK");
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            EditorUtility.DisplayDialog("Dialogue Text", exception.Message, "OK");
            return;
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        Reload(draft, AssetDatabase.LoadAssetAtPath<TextAsset>(path));
    }
}
