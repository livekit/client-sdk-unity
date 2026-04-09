using UnityEditor;
using UnityEngine;
using LiveKit;

[CustomEditor(typeof(LiteralAuthConfig))]
public class LiteralAuthEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Use this if you have a pregenerated token from your own token source. \nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE ONLY ONE PARTICIPANT CAN USE THE TOKEN AT A TIME AND TOKENS EXPIRE.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_serverUrl"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_token"));
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(SandboxAuthConfig))]
public class SandboxAuthEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Use this for development to create tokens from a sandbox token server. \nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE THE SANDBOX TOKEN SERVER NEEDS NO AUTHENTICATION.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_sandboxId"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Connection Options", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_roomName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantIdentity"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantMetadata"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_agentName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_agentMetadata"));

        serializedObject.ApplyModifiedProperties();
    }
}
