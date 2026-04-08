using UnityEditor;
using UnityEngine;
using LiveKit;

public class AuthConfigEditor : Editor
{
    protected void DrawNameFields()
    {
        var randomRoomName = serializedObject.FindProperty("_randomRoomName");
        var roomName = serializedObject.FindProperty("_roomName");
        var randomParticipantName = serializedObject.FindProperty("_randomParticipantName");
        var participantName = serializedObject.FindProperty("_participantName");

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(randomRoomName, new GUIContent("Random Room Name"));
        if (!randomRoomName.boolValue)
            EditorGUILayout.PropertyField(roomName, new GUIContent("Room Name"));

        EditorGUILayout.PropertyField(randomParticipantName, new GUIContent("Random Participant Name"));
        if (!randomParticipantName.boolValue)
            EditorGUILayout.PropertyField(participantName, new GUIContent("Participant Name"));
    }
}

[CustomEditor(typeof(HardcodedAuthConfig))]
public class HardcodedAuthEditor : AuthConfigEditor
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
public class SandboxAuthEditor : AuthConfigEditor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Use this for development to create tokens from a sandbox token server. \nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE THE SANDBOX TOKEN SERVER NEEDS NO AUTHENTICATION.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_sandboxId"));
        DrawNameFields();
        serializedObject.ApplyModifiedProperties();
    }
}

[CustomEditor(typeof(LocalAuthConfig))]
public class LocalAuthEditor : AuthConfigEditor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Use this for local development by creating tokens locally.\nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE YOU EXPOSE YOUR CREDENTIALS.", MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_liveKitUrl"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_apiKey"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_apiSecret"));
        DrawNameFields();
        serializedObject.ApplyModifiedProperties();
    }
}
