using System;
using System.Collections.Generic;

namespace LiveKit.Rooms.ActiveSpeakers
{
    public interface IActiveSpeakers : IReadOnlyCollection<string>
    {
        event Action Updated;
    }
    
    public interface IMutableActiveSpeakers : IActiveSpeakers
    {
        void UpdateCurrentActives(IEnumerable<string> sids);

        void Clear();
    }
}