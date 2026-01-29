using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public delegate void ParticipantDelegate(LKParticipant participant, UpdateFromParticipant update);
    
    public enum UpdateFromParticipant
    {
        Connected,
        MetadataChanged,
        NameChanged,
        AttributesChanged,
        Disconnected
    }
    
    public interface IParticipantsHub
    {
        event ParticipantDelegate UpdatesFromParticipant;
        
        LKParticipant LocalParticipant();

        LKParticipant? RemoteParticipant(string identity);

        IReadOnlyDictionary<string, LKParticipant> RemoteParticipantIdentities();
    }

    public interface IMutableParticipantsHub : IParticipantsHub
    {
        void AssignLocal(LKParticipant participant);

        void AddRemote(LKParticipant participant);
        
        void RemoveRemote(LKParticipant participant);
        
        void NotifyParticipantUpdate(LKParticipant participant, UpdateFromParticipant update);

        void Clear();
    }
}
