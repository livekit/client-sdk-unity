using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MeetManager))]
public class MeetManagerEditor : Editor
{
    private SerializedProperty buttonBar;
    private SerializedProperty videoTrackParent;
    private SerializedProperty participantTilePrefab;
    private SerializedProperty frameRate;
    private SerializedProperty usePlatformAudio;
    private SerializedProperty echoCancellation;
    private SerializedProperty noiseSuppression;
    private SerializedProperty autoGainControl;
    private SerializedProperty preferHardwareProcessing;

    private void OnEnable()
    {
        buttonBar = serializedObject.FindProperty("buttonBar");
        videoTrackParent = serializedObject.FindProperty("videoTrackParent");
        participantTilePrefab = serializedObject.FindProperty("participantTilePrefab");
        frameRate = serializedObject.FindProperty("frameRate");
        usePlatformAudio = serializedObject.FindProperty("usePlatformAudio");
        echoCancellation = serializedObject.FindProperty("echoCancellation");
        noiseSuppression = serializedObject.FindProperty("noiseSuppression");
        autoGainControl = serializedObject.FindProperty("autoGainControl");
        preferHardwareProcessing = serializedObject.FindProperty("preferHardwareProcessing");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("UI", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(buttonBar);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Video Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(videoTrackParent);
        EditorGUILayout.PropertyField(participantTilePrefab);
        EditorGUILayout.PropertyField(frameRate);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio Mode", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(usePlatformAudio, new GUIContent("Use Platform Audio",
            "Use PlatformAudio (WebRTC ADM) for microphone capture and automatic speaker playout. " +
            "Provides AEC, AGC, and NS. Disable to use Unity's Microphone API instead."));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio Processing (PlatformAudio only)", EditorStyles.boldLabel);

        // Gray out audio processing options when PlatformAudio is disabled
        bool platformAudioEnabled = usePlatformAudio.boolValue;

        using (new EditorGUI.DisabledGroupScope(!platformAudioEnabled))
        {
            if (!platformAudioEnabled)
            {
                EditorGUILayout.HelpBox("Audio processing options are only available when 'Use Platform Audio' is enabled.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(echoCancellation, new GUIContent("Echo Cancellation",
                "Enable echo cancellation to remove echo from speaker playback."));
            EditorGUILayout.PropertyField(noiseSuppression, new GUIContent("Noise Suppression",
                "Enable noise suppression to remove background noise."));
            EditorGUILayout.PropertyField(autoGainControl, new GUIContent("Auto Gain Control",
                "Enable auto gain control to normalize audio levels."));
            EditorGUILayout.PropertyField(preferHardwareProcessing, new GUIContent("Prefer Hardware Processing",
                "Prefer hardware audio processing (e.g., iOS VPIO). Lower latency but may have different quality characteristics."));
        }

        serializedObject.ApplyModifiedProperties();
    }
}
