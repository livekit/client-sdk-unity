using System.Collections.Generic;

namespace LiveKit.Rooms.Participants
{
    public delegate void ParticipantDelegate(Participant participant, UpdateFromParticipant update);
    
    public enum UpdateFromParticipant
    {
        Connected,
        MetadataChanged,
        Disconnected
    }
    
    public interface IParticipantsHub
    {
        event ParticipantDelegate UpdatesFromParticipant;
        
        Participant LocalParticipant();

        Participant? RemoteParticipant(string sid);
        
        IReadOnlyCollection<string> RemoteParticipantSids();
    }

    public interface IMutableParticipantsHub : IParticipantsHub
    {
        void AssignLocal(Participant participant);

        void AddRemote(Participant participant);
        
        void RemoveRemote(Participant participant);
        
        void NotifyParticipantUpdate(Participant participant, UpdateFromParticipant update);
    }
}