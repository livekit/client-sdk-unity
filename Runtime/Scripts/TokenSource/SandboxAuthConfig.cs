using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    [Serializable]
    public struct StringPair
    {
        public string key;
        public string value;
    }

    [CreateAssetMenu(fileName = "TokenSource", menuName = "LiveKit/Sandbox Auth")]
    public class SandboxAuthConfig : AuthConfig
    {
        [SerializeField] private string _sandboxId;
        [SerializeField] private string _roomName;
        [SerializeField] private string _participantName;
        [SerializeField] private string _participantIdentity;
        [SerializeField] private string _participantMetadata;
        [SerializeField] private List<StringPair> _participantAttributes;
        [SerializeField] private string _agentName;
        [SerializeField] private string _agentMetadata;

        public string SandboxId => _sandboxId?.Trim('"');
        public string RoomName => _roomName;
        public string ParticipantName => _participantName;
        public string ParticipantIdentity => _participantIdentity;
        public string ParticipantMetadata => _participantMetadata;
        public List<StringPair> ParticipantAttributes => _participantAttributes;
        public string AgentName => _agentName;
        public string AgentMetadata => _agentMetadata;

        public override bool IsValid =>
            !string.IsNullOrEmpty(SandboxId);
    }
}
