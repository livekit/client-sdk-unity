using System.Collections.Generic;

namespace LiveKit
{
    public class TokenSourceFetchOptions
    {
        public string RoomName { get; set; }
        public string ParticipantName { get; set; }
        public string ParticipantIdentity { get; set; }
        public string ParticipantMetadata { get; set; }
        public Dictionary<string, string> ParticipantAttributes { get; set; }
        public string AgentName { get; set; }
        public string AgentMetadata { get; set; }
    }
}
