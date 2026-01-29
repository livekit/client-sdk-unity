using System;
using System.Collections;
using System.Collections.Generic;

namespace LiveKit.Rooms.ActiveSpeakers
{
    public class NoActiveSpeakers : IActiveSpeakers
    {
        private static readonly List<string> actives = new();

        public int Count => actives.Count;

        public event Action? Updated;

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
