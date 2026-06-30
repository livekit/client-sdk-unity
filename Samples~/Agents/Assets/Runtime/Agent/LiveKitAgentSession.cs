using System;
using System.Collections;
using LiveKit;
using UnityEngine;
using RoomOptions = LiveKit.RoomOptions;

namespace AgentsRPG
{
    // Orchestrates a LiveKit agent conversation: fetches a token, connects the room, and wires
    // up the microphone, transcription, and chat-bubble log.
    public class LiveKitAgentSession : MonoBehaviour
    {

        [SerializeField] ChatLog _chatLog;

        TokenSourceComponent _tokenSourceComponent;
        Room _room;
        PlatformAudioController _audio;
        TranscriptionReader _transcription;

        bool _isApplicationQuitting;

        public void Start()
        {
            StartSession();
        }

        // Creates a bubble for a new reply and returns its sink; the transcription reader pushes
        // chunks into it live, and ChatBubble.StreamText drips them in character-by-character.
        Action<string> BeginReply(Speaker speaker)
        {
            if (_chatLog == null) return _ => { };
            return _chatLog.AddBubble(speaker).StreamText;
        }

        public void StartSession()
        {
            Debug.Log($"[LiveKitAgentSession] StartSession");
            _tokenSourceComponent = GetComponent<TokenSourceComponent>();
            if (_tokenSourceComponent == null)
            {
                Debug.LogError("[LiveKitAgentSession] TokenSource missing on GameLoop.");
                return;
            }
            StartCoroutine(Connect());
        }

        public void EndSession()
        {
            Debug.Log($"[LiveKitAgentSession] EndSession (room '{_room?.Name ?? "<none>"}')");

            _transcription?.Dispose();
            _transcription = null;

            _audio?.Dispose();
            _audio = null;

            if (_room != null)
            {
                OnRoomDisconnecting(_room);
                Debug.Log("[LiveKitAgentSession] Disconnecting room.");
                _room.Disconnect();
                _room = null;
            }
        }

        protected virtual void OnRoomConnected(Room room) { }
        protected virtual void OnRoomDisconnecting(Room room) { }

        IEnumerator Connect()
        {
            Debug.Log($"[LiveKitAgentSession] Fetching connection details...");
            var fetch = _tokenSourceComponent.FetchConnectionDetails();
            yield return fetch;
            if (fetch.IsError)
            {
                Debug.LogError($"Failed to fetch connection details: {fetch.Exception?.Message}");
                yield break;
            }
            var details = fetch.Result;
            Debug.Log($"[LiveKitAgentSession] Got connection details (serverUrl='{details.ServerUrl}', token length={details.ParticipantToken?.Length ?? 0}).");

            // Create the WebRTC ADM before connecting. The SDK only wires automatic speaker
            // playout for remote tracks to a PlatformAudio that already exists at connect time;
            // initializing it after Connect leaves remote (agent) audio silent.
            _audio = new PlatformAudioController();
            if (!_audio.Initialize())
            {
                Debug.LogError("[LiveKitAgentSession] Failed to initialize platform audio; aborting.");
                _audio = null;
                yield break;
            }

            _room = new Room();

            Debug.Log($"[LiveKitAgentSession] Connecting to '{details.ServerUrl}'...");
            yield return _room.Connect(details.ServerUrl, details.ParticipantToken, new RoomOptions());
            Debug.Log(
                $"[LiveKitAgentSession] Connected to room '{_room.Name}' " +
                $"(local identity='{_room.LocalParticipant?.Identity}'");

            OnRoomConnected(_room);

            yield return _audio.Publish(_room);
            if (!_audio.IsPublished)
            {
                EndSession();
                yield break;
            }

            _transcription = new TranscriptionReader(_room, this, BeginReply);
            Debug.Log("[LiveKitAgentSession] Session ready.");
        }

        void OnApplicationQuit()
        {
            _isApplicationQuitting = true;
        }

        void OnDestroy()
        {
            if (_isApplicationQuitting)
            {
                // Scene is tearing down — SDK helper singletons (e.g. MonoBehaviourContext)
                // may already have been destroyed, so calling into PlatformAudio.Dispose()
                // or Room.Disconnect() throws MissingReferenceException. The OS reclaims
                // the mic and socket on exit, so skipping SDK cleanup here is safe.
                return;
            }
            EndSession();
        }
    }
}
