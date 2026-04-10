using UnityEditor;
using UnityEngine;
using LiveKit;

[CustomEditor(typeof(TokenSourceConfig))]
public class TokenSourceConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var tokenSourceType = serializedObject.FindProperty("_tokenSourceType");
        EditorGUILayout.PropertyField(tokenSourceType);

        EditorGUILayout.Space();

        switch ((TokenSourceType)tokenSourceType.enumValueIndex)
        {
            case TokenSourceType.Literal:
                EditorGUILayout.HelpBox(
                    "Use this if you have a pregenerated token from your own token source. " +
                    "\nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE ONLY ONE PARTICIPANT CAN USE THE TOKEN AT A TIME AND TOKENS EXPIRE.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_serverUrl"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_token"));
                break;

            case TokenSourceType.Sandbox:
                EditorGUILayout.HelpBox(
                    "Use this for development to create tokens from a sandbox token server. " +
                    "\nWARNING: ONLY USE THIS OPTION FOR LOCAL DEVELOPMENT, SINCE THE SANDBOX TOKEN SERVER NEEDS NO AUTHENTICATION.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_sandboxId"));
                DrawConnectionOptions();
                break;

            case TokenSourceType.Endpoint:
                EditorGUILayout.HelpBox(
                    "Use this for production with your own token endpoint. " +
                    "Provide the URL and any authentication headers your endpoint requires.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_endpointUrl"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_endpointHeaders"), true);
                DrawConnectionOptions();
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawConnectionOptions()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Connection Options", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_roomName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantIdentity"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantMetadata"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_participantAttributes"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_agentName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_agentMetadata"));
    }
}
