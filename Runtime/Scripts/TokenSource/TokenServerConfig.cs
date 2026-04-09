using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    public enum AuthType
    {
        Literal,
        Sandbox
    }

    [Serializable]
    public struct StringPair
    {
        public string key;
        public string value;
    }

    [CreateAssetMenu(fileName = "TokenServerConfig", menuName = "LiveKit/Auth Config")]
    public class TokenServerConfig : ScriptableObject
    {
        [SerializeField] private AuthType _authType;

        // Literal fields
        [SerializeField] private string _serverUrl;
        [SerializeField] private string _token;

        // Sandbox fields
        [SerializeField] private string _sandboxId;
        [SerializeField] private string _roomName;
        [SerializeField] private string _participantName;
        [SerializeField] private string _participantIdentity;
        [SerializeField] private string _participantMetadata;
        [SerializeField] private List<StringPair> _participantAttributes;
        [SerializeField] private string _agentName;
        [SerializeField] private string _agentMetadata;

        public AuthType AuthType => _authType;

        // Literal
        public string ServerUrl => _serverUrl;
        public string Token => _token;

        // Sandbox
        public string SandboxId => _sandboxId?.Trim('"');
        public string RoomName => _roomName;
        public string ParticipantName => _participantName;
        public string ParticipantIdentity => _participantIdentity;
        public string ParticipantMetadata => _participantMetadata;
        public List<StringPair> ParticipantAttributes => _participantAttributes;
        public string AgentName => _agentName;
        public string AgentMetadata => _agentMetadata;

        public bool IsValid => _authType switch
        {
            AuthType.Literal => !string.IsNullOrEmpty(ServerUrl) && ServerUrl.StartsWith("ws") && !string.IsNullOrEmpty(Token),
            AuthType.Sandbox => !string.IsNullOrEmpty(SandboxId),
            _ => false
        };
    }
}
