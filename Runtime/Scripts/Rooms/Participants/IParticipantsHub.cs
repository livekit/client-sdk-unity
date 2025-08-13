using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public delegate void ParticipantDelegate(Participant participant, UpdateFromParticipant update);
    
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
        
        Participant LocalParticipant();

        Participant? RemoteParticipant(string identity);

        IReadOnlyDictionary<string, Participant> RemoteParticipantIdentities();
    }

    public interface IMutableParticipantsHub : IParticipantsHub
    {
        void AssignLocal(Participant participant);

        void AddRemote(Participant participant);
        
        void RemoveRemote(Participant participant);
        
        void NotifyParticipantUpdate(Participant participant, UpdateFromParticipant update);

        void Clear();
    }
}