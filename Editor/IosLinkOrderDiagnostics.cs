#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static class IosLinkOrderDiagnostics
{
    private const string Prefix = "LiveKit";
    private const string Libilivekit = "liblivekit_ffi.a in Frameworks";
    // Unity exports the engine as Libraries/libiPhone-lib.a up to 6000.4 and as a static
    // Frameworks/UnityRuntime.framework from 6000.5; both bundle the old CELT objects.
    private static readonly string[] EngineLibraryEntries =
    {
        "libiPhone-lib.a in Frameworks",
        "UnityRuntime.framework in Frameworks"
    };
    private static readonly string[] ConflictingEngineArchiveMembers =
    {
        "bands.o",
        "celt.o",
        "cwrs.o",
        "entcode.o",
        "entdec.o",
        "entenc.o",
        "header.o",
        "kiss_fft.o",
        "laplace.o",
        "mathops.o",
        "mdct-acf77e498fcf88b2edc7736c8f447bee8d7b174d050f1e9bd161576066636768.o",
        "modes.o",
        "pitch.o",
        "plc.o",
        "quant_bands.o",
        "rate.o",
        "vq.o",
        "fmod_codec_celt.o"
    };

    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
            return;

        var projectPath = Path.Combine(pathToBuiltProject, "Unity-iPhone.xcodeproj", "project.pbxproj");
        if (!File.Exists(projectPath))
        {
            Debug.LogWarning($"{Prefix}: iOS link-order diagnostic could not find {projectPath}");
            return;
        }

        var projectText = File.ReadAllText(projectPath);
        var engineEntry = EngineLibraryEntries.FirstOrDefault(entry => projectText.Contains(entry));
        var engineName = engineEntry?.Replace(" in Frameworks", string.Empty);
        var fixedProjectText = EnsureSafeUnityFrameworkLinkOrder(projectText, engineEntry, out var wasModified);
        if (wasModified)
        {
            File.WriteAllText(projectPath, fixedProjectText);
            projectText = fixedProjectText;
            Debug.Log($"{Prefix}: iOS link-order fix applied. Moved {engineName} after liblivekit_ffi.a in UnityFramework -> Frameworks and Libraries.");
        }

        StripConflictingCodecObjects(pathToBuiltProject);

        var frameworkSection = ExtractUnityFrameworkSection(projectText);
        if (string.IsNullOrEmpty(frameworkSection))
        {
            Debug.LogWarning($"{Prefix}: iOS link-order diagnostic could not locate the UnityFramework frameworks section in the exported Xcode project.");
            return;
        }

        var livekitIndex = frameworkSection.IndexOf(Libilivekit, StringComparison.Ordinal);
        var engineIndex = engineEntry == null ? -1 : frameworkSection.IndexOf(engineEntry, StringComparison.Ordinal);
        if (livekitIndex < 0 || engineIndex < 0)
        {
            Debug.LogWarning($"{Prefix}: iOS link-order diagnostic could not locate both {Libilivekit} and a Unity engine library in UnityFramework -> Frameworks and Libraries.");
            return;
        }

        if (livekitIndex < engineIndex)
        {
            Debug.Log($"{Prefix}: iOS link-order diagnostic OK. UnityFramework links liblivekit_ffi.a before {engineName}.");
            return;
        }

        Debug.LogWarning(
            $"{Prefix}: iOS link-order diagnostic found a risky order in UnityFramework -> Frameworks and Libraries. " +
            $"{engineName} appears before liblivekit_ffi.a. This repo documents that this can crash Opus/CELT on iOS. " +
            "The post-process fix could not rewrite the exported project automatically."
        );
    }

    private static string EnsureSafeUnityFrameworkLinkOrder(string projectText, string engineEntry, out bool wasModified)
    {
        wasModified = false;

        if (engineEntry == null)
            return projectText;

        if (!TryGetUnityFrameworkSectionBounds(projectText, out var sectionStart, out var sectionEnd))
            return projectText;

        var frameworkSection = projectText.Substring(sectionStart, sectionEnd - sectionStart);
        var livekitIndex = frameworkSection.IndexOf(Libilivekit, StringComparison.Ordinal);
        var engineIndex = frameworkSection.IndexOf(engineEntry, StringComparison.Ordinal);
        if (livekitIndex < 0 || engineIndex < 0 || livekitIndex < engineIndex)
            return projectText;

        var engineLineStart = frameworkSection.LastIndexOf('\n', engineIndex);
        if (engineLineStart < 0)
            engineLineStart = 0;
        else
            engineLineStart += 1;

        var engineLineEnd = frameworkSection.IndexOf('\n', engineIndex);
        if (engineLineEnd < 0)
            engineLineEnd = frameworkSection.Length;
        else
            engineLineEnd += 1;

        var engineLine = frameworkSection.Substring(engineLineStart, engineLineEnd - engineLineStart);
        var sectionWithoutEngine = frameworkSection.Remove(engineLineStart, engineLineEnd - engineLineStart);

        var livekitIndexAfterRemoval = sectionWithoutEngine.IndexOf(Libilivekit, StringComparison.Ordinal);
        if (livekitIndexAfterRemoval < 0)
            return projectText;

        var insertIndex = sectionWithoutEngine.IndexOf('\n', livekitIndexAfterRemoval);
        if (insertIndex < 0)
            return projectText;

        insertIndex += 1;
        var reorderedSection = sectionWithoutEngine.Insert(insertIndex, engineLine);
        wasModified = !string.Equals(frameworkSection, reorderedSection, StringComparison.Ordinal);
        if (!wasModified)
            return projectText;

        return projectText.Substring(0, sectionStart) + reorderedSection + projectText.Substring(sectionEnd);
    }

    private static string ExtractUnityFrameworkSection(string projectText)
    {
        if (!TryGetUnityFrameworkSectionBounds(projectText, out var sectionStart, out var sectionEnd))
            return string.Empty;

        return projectText.Substring(sectionStart, sectionEnd - sectionStart);
    }

    private static bool TryGetUnityFrameworkSectionBounds(string projectText, out int sectionStart, out int sectionEnd)
    {
        sectionStart = -1;
        sectionEnd = -1;

        // The frameworks build phase is not labeled consistently across Unity versions,
        // so identify it as the one that links liblivekit_ffi.a.
        const string marker = "isa = PBXFrameworksBuildPhase;";
        var searchIndex = 0;
        while (true)
        {
            var markerIndex = projectText.IndexOf(marker, searchIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
                return false;

            var filesIndex = projectText.IndexOf("\t\t\tfiles = (", markerIndex, StringComparison.Ordinal);
            if (filesIndex < 0)
                return false;

            var endIndex = projectText.IndexOf("\t\t\t);", filesIndex, StringComparison.Ordinal);
            if (endIndex < 0)
                return false;

            var section = projectText.Substring(filesIndex, endIndex - filesIndex);
            if (section.Contains(Libilivekit))
            {
                sectionStart = filesIndex;
                sectionEnd = endIndex;
                return true;
            }

            searchIndex = markerIndex + marker.Length;
        }
    }

    private static void StripConflictingCodecObjects(string pathToBuiltProject)
    {
        var archivePath = FindEngineArchivePath(pathToBuiltProject);
        if (archivePath == null)
        {
            Debug.Log($"{Prefix}: iOS archive fix skipped because no Unity engine archive was found in the exported project.");
            return;
        }

        var archiveName = Path.GetFileName(archivePath);
        if (!TryRunProcess("/usr/bin/ar", $" -t \"{archivePath}\"", out var memberOutput, out var listError))
        {
            Debug.LogWarning($"{Prefix}: iOS archive fix could not inspect {archiveName} members. {listError}");
            return;
        }

        var members = new HashSet<string>(
            memberOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()),
            StringComparer.Ordinal
        );

        var membersToRemove = ConflictingEngineArchiveMembers.Where(members.Contains).ToArray();
        if (membersToRemove.Length == 0)
        {
            Debug.Log($"{Prefix}: iOS archive fix found no conflicting CELT objects in {archiveName}.");
            return;
        }

        var deleteArguments = $" -d \"{archivePath}\" {string.Join(" ", membersToRemove.Select(QuoteArgument))}";
        if (!TryRunProcess("/usr/bin/ar", deleteArguments, out _, out var deleteError))
        {
            Debug.LogWarning($"{Prefix}: iOS archive fix could not strip conflicting objects from {archiveName}. {deleteError}");
            return;
        }

        if (!TryRunProcess("/usr/bin/ranlib", $" \"{archivePath}\"", out _, out var ranlibError))
        {
            Debug.LogWarning($"{Prefix}: iOS archive fix stripped conflicting objects but ranlib failed. {ranlibError}");
            return;
        }

        Debug.Log($"{Prefix}: iOS archive fix stripped {membersToRemove.Length} conflicting CELT objects from exported {archiveName}.");
    }

    private static string FindEngineArchivePath(string pathToBuiltProject)
    {
        var candidates = new[]
        {
            Path.Combine(pathToBuiltProject, "Libraries", "libiPhone-lib.a"),
            Path.Combine(pathToBuiltProject, "Frameworks", "UnityRuntime.framework", "UnityRuntime")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool TryRunProcess(string fileName, string arguments, out string stdout, out string error)
    {
        stdout = string.Empty;
        error = string.Empty;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
                return true;

            error = string.IsNullOrWhiteSpace(stderr)
                ? $"{fileName} exited with code {process.ExitCode}."
                : stderr.Trim();
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
#endif
