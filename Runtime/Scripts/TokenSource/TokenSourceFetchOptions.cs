using System.Collections.Generic;

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
}
