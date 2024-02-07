namespace LiveKit.Rooms.Participants
{
    public interface IParticipantsHub
    {
        Participant LocalParticipant();

        Participant? RemoteParticipant(string sid);
    }

    public interface IMutableParticipantsHub : IParticipantsHub
    {
        void AssignLocal(Participant participant);

        void AddRemote(Participant participant);
        
        void RemoveRemote(Participant participant);
    }
}