using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LiveKit;
using UnityEngine;

// Reads LiveKit transcription text streams and feeds each chunk live into a per-reply text sink.
// The SDK invokes OnTextStream outside of a coroutine, so a MonoBehaviour runner is needed
// to drive the per-stream Pump coroutines. Tracks live pumps so they can be stopped on Dispose.
//
// The player and the agent are published in different shapes by RoomIO:
//   - Agent (NPC): is_delta_stream=True  -> one stream per reply, each chunk is a DELTA to append.
//   - Player:      is_delta_stream=False -> one fresh stream per update, each carrying the FULL
//                  snapshot ("H", "He", "Hello"); the final is yet another stream re-sending the
//                  last snapshot. That final body is just whatever the last STT event carried, which
//                  with VAD endpointing is sometimes empty even though earlier snapshots had the words.
// So the NPC is appended delta-by-delta, while the player is tracked as snapshots: we cache the
// last non-empty snapshot and commit it when the turn finalizes, so an empty final can no longer
// blank the bubble. Player turns are processed one at a time, so a single cached string suffices.
public sealed class TranscriptionReader : IDisposable
{
    const string TranscriptionTopic = "lk.transcription";
    const string FinalAttribute = "lk.transcription_final";

    readonly Room _room;
    readonly MonoBehaviour _runner;
    readonly Func<Speaker, Action<string>> _beginReply;
    readonly List<Coroutine> _pumps = new();

    // Last non-empty snapshot seen for the in-progress player turn, until it finalizes.
    string _lastPlayerSnapshot;

    public TranscriptionReader(Room room, MonoBehaviour runner, Func<Speaker, Action<string>> beginReply)
    {
        _room = room;
        _runner = runner;
        _beginReply = beginReply;
        _room.RegisterTextStreamHandler(TranscriptionTopic, OnTextStream);
        Debug.Log($"[TranscriptionReader] Registered text stream handler on topic '{TranscriptionTopic}'.");
    }

    void OnTextStream(TextStreamReader reader, string identity)
    {
        var attributes = reader.Info.Attributes;

        var isFinal = !(attributes != null &&
                        attributes.TryGetValue(FinalAttribute, out var final) &&
                        final == "false");

        var speaker = identity == _room.LocalParticipant.Identity ? Speaker.Player : Speaker.Npc;

        if (speaker == Speaker.Npc)
        {
            _pumps.Add(_runner.StartCoroutine(PumpNpc(reader, identity)));
            return;
        }

        // Player: snapshot stream. Each interim/final stream carries the full text; the final can
        // fall back to the last non-empty snapshot we cached.
        _pumps.Add(_runner.StartCoroutine(PumpPlayer(reader, isFinal)));
    }

    // Agent path: a single delta stream per reply. Append each chunk into one bubble as it streams.
    // Bracketed [annotations] (e.g. stage directions) are stripped; the filter carries bracket
    // state across chunks so an annotation split between deltas ("[lau" then "ghs]") is still removed.
    IEnumerator PumpNpc(TextStreamReader reader, string identity)
    {
        var append = _beginReply(Speaker.Npc);
        var filter = new AnnotationFilter();
        var r = reader.ReadIncremental();
        while (true)
        {
            yield return r;
            if (r.IsEos) break;

            var chunk = r.Text;
            if (!string.IsNullOrEmpty(chunk))
            {
                var filtered = filter.Filter(chunk);
                if (!string.IsNullOrEmpty(filtered)) append(filtered);
            }

            r.Reset();
        }
    }

    // Removes [annotations] from a stream of text chunks, along with a single space right after the
    // closing bracket so "[sad] I don't feel good" reads as "I don't feel good". State persists
    // between calls so an annotation (or that trailing space) spanning a chunk boundary is still
    // dropped; annotations don't nest, and an unterminated bracket swallows the rest, fine per reply.
    sealed class AnnotationFilter
    {
        bool _inside;
        bool _skipSpace; // swallow one space immediately following a closing ']'

        public string Filter(string chunk)
        {
            var sb = new StringBuilder(chunk.Length);
            foreach (var c in chunk)
            {
                if (_inside)
                {
                    if (c == ']') { _inside = false; _skipSpace = true; }
                    continue;
                }

                if (c == '[') { _inside = true; _skipSpace = false; continue; }

                if (_skipSpace)
                {
                    _skipSpace = false;
                    if (c == ' ') continue;
                }

                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    // Player path: each stream is a full snapshot and we only render at finalization (no live
    // drip), so read the whole thing at once. Cache the last non-empty snapshot, because finals
    // can be empty sometimes. 
    IEnumerator PumpPlayer(TextStreamReader reader, bool isFinal)
    {
        var read = reader.ReadAll();
        yield return read;

        if (!read.IsError && !string.IsNullOrEmpty(read.Text)) _lastPlayerSnapshot = read.Text;

        if (!isFinal) yield break;

        var text = _lastPlayerSnapshot;
        _lastPlayerSnapshot = null;

        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        _beginReply(Speaker.Player)(text);
    }

    public void Dispose()
    {
        foreach (var pump in _pumps)
        {
            if (pump != null) _runner.StopCoroutine(pump);
        }
        _pumps.Clear();
        _lastPlayerSnapshot = null;

        _room?.UnregisterTextStreamHandler(TranscriptionTopic);
    }
}
