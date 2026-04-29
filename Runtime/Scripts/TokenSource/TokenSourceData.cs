using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiveKit
{
    public class TokenSourceFetchOptions
    {
        public string RoomName { get; init; }
        public string ParticipantName { get; init; }
        public string ParticipantIdentity { get; init; }
        public string ParticipantMetadata { get; init; }
        public Dictionary<string, string> ParticipantAttributes { get; init; }
        public string AgentName { get; init; }
        public string AgentMetadata { get; init; }
    }

    class TokenSourceRequest
    {
        [JsonProperty("room_name", NullValueHandling = NullValueHandling.Ignore)]
        public string RoomName;

        [JsonProperty("participant_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantName;

        [JsonProperty("participant_identity", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantIdentity;

        [JsonProperty("participant_metadata", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantMetadata;

        [JsonProperty("participant_attributes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ParticipantAttributes;

        [JsonProperty("room_config", NullValueHandling = NullValueHandling.Ignore)]
        public RoomConfig RoomConfig;
    }

    class RoomConfig
    {
        [JsonProperty("agents", NullValueHandling = NullValueHandling.Ignore)]
        public List<AgentDispatch> Agents;
    }

    class AgentDispatch
    {
        [JsonProperty("agent_name", NullValueHandling = NullValueHandling.Ignore)]
        public string AgentName;

        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public string Metadata;
    }

    public class ConnectionDetails
    {
        [JsonProperty("server_url")]
        public string ServerUrl;

        [JsonProperty("participant_token")]
        public string ParticipantToken;
    }
}
