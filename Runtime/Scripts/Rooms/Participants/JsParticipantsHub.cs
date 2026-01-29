#if UNITY_WEBGL

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

using JsRoom = LiveKit.Room;

namespace LiveKit.Rooms.Participants
{
    public class JsParticipantsHub : IParticipantsHub
    {
        private readonly JsRoom room;

        public event ParticipantDelegate UpdatesFromParticipant;

        public JsParticipantsHub(JsRoom room)
        {
            this.room = room;


            room.ParticipantMetadataChanged += (string metadata, Participant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.MetadataChanged);
            };

            room.ParticipantConnected += (RemoteParticipant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.Connected);
            };

            room.ParticipantDisconnected += (RemoteParticipant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.Disconnected);
            };

            room.AttributesChanged += (Participant participant, JSMap<string, string> _changedAttributes) =>
            {
                LKParticipant wrap = new LKParticipant(participant);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.AttributesChanged);
            };

            /* TODO (enhance): official JsBridge won't provide the callback, it can be implemented as polling if required
            room.ParticipantNameChanged += (string _name, Participant participant) =>
            {
                LKParticipant wrap = new LKParticipant(wrap);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.NameChanged);
            };
            */
        }
        
        public LKParticipant LocalParticipant()
        {
            LiveKit.LocalParticipant inner = room.LocalParticipant;
            LKParticipant wrap = new LKParticipant(inner);
            return wrap;
        }

        public LKParticipant? RemoteParticipant(string identity)
        {
            JSMap<string, RemoteParticipant> map = room.RemoteParticipants;

            if (map.TryGetValue(identity, out RemoteParticipant inner))
            {
                LKParticipant wrap = new LKParticipant(inner);
                return wrap;
            }

            return null;
        }

        public IReadOnlyDictionary<string, LKParticipant> RemoteParticipantIdentities()
        {
            JSMap<string, RemoteParticipant> map = room.RemoteParticipants;
            DictionaryWrap wrap = new DictionaryWrap(map);
            return wrap;
        }


        public readonly struct DictionaryWrap : IReadOnlyDictionary<string, LKParticipant>
        {
            private readonly JSMap<string, RemoteParticipant> map;

            public int Count => map.Count;

            public LKParticipant this[string key] => new LKParticipant(map[key]);

            public IEnumerable<string> Keys => map.Keys;

            public IEnumerable<LKParticipant> Values => map.Values.Select(p => new LKParticipant(p));

            public DictionaryWrap(JSMap<string, RemoteParticipant> map)
            {
                this.map = map;
            }

            public bool ContainsKey(string key)
            {
                return map.ContainsKey(key);
            }

            public bool TryGetValue(string key, out LKParticipant value)
            {
                if (map.TryGetValue(key, out RemoteParticipant inner))
                {
                    value = new LKParticipant(inner);
                    return true;
                }

                value = default;
                return false;
            }

            public IEnumerator<KeyValuePair<string, LKParticipant>> GetEnumerator()
            {
                foreach (var kv in map)
                    yield return new KeyValuePair<string, LKParticipant>(kv.Key, new LKParticipant(kv.Value));
            }

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }
    }
}

#endif





