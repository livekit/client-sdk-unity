using UnityEditor;
using UnityEngine;

/// <summary>
/// User-scoped Preferences entry (Edit > Preferences > iOS Signing) for the Apple
/// Team ID used to auto-sign iOS builds. The value is kept in EditorPrefs, which is
/// per-user and per-machine and never part of the repo — so there is nothing to
/// gitignore — and the build post-processor can read it without an environment
/// variable or launching the Editor from a console.
/// </summary>
public static class IosSigningPreferences
{
    public const string TeamIdKey = "iOS.TeamID";

    [SettingsProvider]
    public static SettingsProvider CreateProvider()
    {
        return new SettingsProvider("Preferences/iOS Signing", SettingsScope.User)
        {
            label = "iOS Signing",
            guiHandler = _ =>
            {
                EditorGUILayout.HelpBox(
                    "Apple Team ID used to auto-sign iOS builds. Stored locally in EditorPrefs " +
                    "(per-machine, never committed) and applied on every build. Leave empty to " +
                    "sign manually in Xcode.",
                    MessageType.Info);

                var current = EditorPrefs.GetString(TeamIdKey, string.Empty);
                EditorGUI.BeginChangeCheck();
                var updated = EditorGUILayout.TextField("Team ID", current);
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetString(TeamIdKey, updated.Trim());
            },
            keywords = new[] { "iOS", "Team", "Signing", "Apple", "Team ID", "Xcode" }
        };
    }
}
