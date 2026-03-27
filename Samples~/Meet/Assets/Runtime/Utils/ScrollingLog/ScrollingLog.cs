using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Runtime.Common
{
  // Simple scrolling window that prints to the application screen whatever is printed through
  // calls to the UnityEngine.Debug.Log method.
  [DefaultExecutionOrder(Int32.MinValue)]
  public class ScrollingLog: MonoBehaviour
  {
    /// Font size for log text entries. Spacing between log entries is also set to half this value.
    [SerializeField]
    private int LogEntryFontSize = 32;

    /// The maximum number of log entries to keep history of
    [SerializeField][Range(1, 100)]
    private int MaxLogCount = 100;

    /// Layout box containing the log entries
    [SerializeField]
    private VerticalLayoutGroup LogHistory = null;

    /// Log entry prefab used to generate new entries when requested
    [SerializeField]
    private Text LogEntryPrefab = null;

    private readonly List<Text> _logEntries = new List<Text>();

    private static ScrollingLog _instance;

    private void Awake()
    {
      _instance = this;
      LogHistory.spacing = LogEntryFontSize / 2f;
    }

    private void OnEnable()
    {
        // Using logMessageReceived (instead of logMessageReceivedThreaded) to ensure that
        // HandleDebugLog is only called from one thread (the main thread).
        Application.logMessageReceived += AddLogEntry;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= AddLogEntry;
    }

    private void OnDestroy()
    {
      _instance = null;
    }

    // Creates a new log entry using the provided string.
    private void AddLogEntry(string str, string stackTrace, LogType type)
    {
      var newLogEntry = Instantiate(LogEntryPrefab, Vector3.zero, Quaternion.identity);
      newLogEntry.text = str;
      newLogEntry.fontSize = LogEntryFontSize;
      newLogEntry.color = Color.white;

      var transform = newLogEntry.transform;
      transform.SetParent(LogHistory.transform);
      transform.localScale = Vector3.one;

      _logEntries.Add(newLogEntry);

      if (_logEntries.Count > MaxLogCount)
      {
        var textObj = _logEntries.First();
        _logEntries.RemoveAt(0);
        Destroy(textObj.gameObject);
      }
    }

    public static void Clear()
    {
      if (_instance == null)
        return;

      foreach (var entry in _instance._logEntries)
        Destroy(entry.gameObject);

      _instance._logEntries.Clear();
    }
  }
}