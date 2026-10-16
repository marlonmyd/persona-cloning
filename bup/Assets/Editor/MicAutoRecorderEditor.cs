using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MicAutoRecorder))]
public class MicAutoRecorderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MicAutoRecorder recorder = (MicAutoRecorder)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("📁 Piliin ang Save Folder"))
        {
            recorder.BrowseFolder();
        }
    }
}