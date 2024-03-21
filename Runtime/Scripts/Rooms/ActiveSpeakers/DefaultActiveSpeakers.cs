using System;
using System.Collections;
using System.Collections.Generic;

namespace LiveKit.Rooms.ActiveSpeakers
{
    public class DefaultActiveSpeakers : IMutableActiveSpeakers
    {
        private readonly List<string> actives = new();

        public int Count => actives.Count;

        public event Action? Updated;

        public void UpdateCurrentActives(IEnumerable<string> sids)
        {
            actives.Clear();
            actives.AddRange(sids);
            Updated?.Invoke();
        }

        public void Clear()
        {
            actives.Clear();
            Updated?.Invoke();
        }

        public IEnumerator<string> GetEnumerator()
        {
            return actives.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}