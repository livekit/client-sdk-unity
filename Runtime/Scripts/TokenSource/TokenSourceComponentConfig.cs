using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    public enum TokenSourceType
    {
        Literal,
        Sandbox,
        Endpoint
    }

    [Serializable]
    public struct StringPair
    {
        public string key;
        public string value;
    }

    [CreateAssetMenu(fileName = "TokenSourceComponentConfig", menuName = "LiveKit/TokenSourceComponentConfig")]
    public class TokenSourceComponentConfig : ScriptableObject
    {
        [SerializeField] private TokenSourceType _tokenSourceType;

        // Literal fields
        [SerializeField] private string _serverUrl;
        [SerializeField] private string _token;

        // Sandbox fields
        [SerializeField] private string _sandboxId;

        // Endpoint fields
        [SerializeField] private string _endpointUrl;
        [SerializeField] private List<StringPair> _endpointHeaders;

        // Shared connection options (Sandbox + Endpoint)
        [SerializeField] private string _roomName;
        [SerializeField] private string _participantName;
        [SerializeField] private string _participantIdentity;
        [SerializeField] private string _participantMetadata;
        [SerializeField] private List<StringPair> _participantAttributes;
        [SerializeField] private string _agentName;
        [SerializeField] private string _agentMetadata;

        public TokenSourceType TokenSourceType => _tokenSourceType;

        // Literal
        public string ServerUrl => _serverUrl;
        public string Token => _token;

        // Sandbox
        public string SandboxId => _sandboxId?.Trim('"');

        // Endpoint
        public string EndpointUrl => _endpointUrl;
        public List<StringPair> EndpointHeaders => _endpointHeaders;

        // Shared connection options
        public string RoomName => _roomName;
        public string ParticipantName => _participantName;
        public string ParticipantIdentity => _participantIdentity;
        public string ParticipantMetadata => _participantMetadata;
        public List<StringPair> ParticipantAttributes => _participantAttributes;
        public string AgentName => _agentName;
        public string AgentMetadata => _agentMetadata;

        public bool IsValid => _tokenSourceType switch
        {
            TokenSourceType.Literal => !string.IsNullOrEmpty(ServerUrl) && ServerUrl.StartsWith("ws") && !string.IsNullOrEmpty(Token),
            TokenSourceType.Sandbox => !string.IsNullOrEmpty(SandboxId),
            TokenSourceType.Endpoint => !string.IsNullOrEmpty(EndpointUrl),
            _ => false
        };
    }
}
