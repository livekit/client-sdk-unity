using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveKit
{
    public enum TokenSourceType
    {
        Literal,
        Development,
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

        // Development fields
        [SerializeField] private string _tokenServerId;

        // Endpoint fields
        [SerializeField] private string _endpointUrl;
        [SerializeField] private List<StringPair> _endpointHeaders;

        // Shared connection options (Development + Endpoint)
        [SerializeField] private string _roomName;
        [SerializeField] private string _participantName;
        [SerializeField] private string _participantIdentity;
        [SerializeField] private string _participantMetadata;
        [SerializeField] private List<StringPair> _participantAttributes;
        [SerializeField] private string _agentName;
        [SerializeField] private string _agentMetadata;
        [SerializeField] private string _agentDeployment;

        public TokenSourceType TokenSourceType => _tokenSourceType;

        // Literal
        public string ServerUrl => _serverUrl;
        public string Token => _token;

        // Development
        public string TokenServerId => _tokenServerId?.Trim('"');

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
        public string AgentDeployment => _agentDeployment;

        public bool IsValid => _tokenSourceType switch
        {
            TokenSourceType.Literal => !string.IsNullOrEmpty(ServerUrl) && ServerUrl.StartsWith("ws") && !string.IsNullOrEmpty(Token),
            TokenSourceType.Development => !string.IsNullOrEmpty(TokenServerId),
            TokenSourceType.Endpoint => !string.IsNullOrEmpty(EndpointUrl),
            _ => false
        };
    }
}
