#if UNITY_WEBGL && !UNITY_EDITOR

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using DCL.LiveKit.Public;

using JsRoom = LiveKit.Room;

namespace LiveKit.Rooms.Participants
{
    public class JsParticipantsHub : IParticipantsHub
    {
        private readonly JsRoom room;
        private readonly IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap;

        public event ParticipantDelegate UpdatesFromParticipant;

        public JsParticipantsHub(
                JsRoom room,
                IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap
                )
        {
            this.room = room;
            this.qualityMap = qualityMap;

            room.ParticipantMetadataChanged += (string metadata, Participant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant, qualityMap);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.MetadataChanged);
            };

            room.ParticipantConnected += (RemoteParticipant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant, qualityMap);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.Connected);
            };

            room.ParticipantDisconnected += (RemoteParticipant participant) =>
            {
                LKParticipant wrap = new LKParticipant(participant, qualityMap);
                UpdatesFromParticipant?.Invoke(wrap, UpdateFromParticipant.Disconnected);
            };

            room.AttributesChanged += (Participant participant, JSMap<string, string> _changedAttributes) =>
            {
                LKParticipant wrap = new LKParticipant(participant, qualityMap);
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
            LKParticipant wrap = new LKParticipant(inner, qualityMap);
            return wrap;
        }

        public LKParticipant? RemoteParticipant(string identity)
        {
            JSMap<string, RemoteParticipant> map = room.RemoteParticipants;

            if (map.TryGetValue(identity, out RemoteParticipant inner))
            {
                LKParticipant wrap = new LKParticipant(inner, qualityMap);
                return wrap;
            }

            return null;
        }

        public IReadOnlyDictionary<string, LKParticipant> RemoteParticipantIdentities()
        {
            JSMap<string, RemoteParticipant> map = room.RemoteParticipants;
            DictionaryWrap wrap = new DictionaryWrap(map, qualityMap);
            return wrap;
        }


        public readonly struct DictionaryWrap : IReadOnlyDictionary<string, LKParticipant>
        {
            private readonly JSMap<string, RemoteParticipant> map;
            private readonly IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap;

            public int Count => map.Count;

            public LKParticipant this[string key] => new LKParticipant(map[key], qualityMap);

            public IEnumerable<string> Keys => map.Keys;

            public IEnumerable<LKParticipant> Values 
            {
                get
                {
                    IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qMap = qualityMap;
                    return map.Values.Select(p => new LKParticipant(p, qMap));
                }
            }

            public DictionaryWrap(
                    JSMap<string, RemoteParticipant> map,
                    IReadOnlyDictionary<(string sid, string identity), LKConnectionQuality> qualityMap)
            {
                this.map = map;
                this.qualityMap = qualityMap;
            }

            public bool ContainsKey(string key)
            {
                return map.ContainsKey(key);
            }

            public bool TryGetValue(string key, out LKParticipant value)
            {
                if (map.TryGetValue(key, out RemoteParticipant inner))
                {
                    value = new LKParticipant(inner, qualityMap);
                    return true;
                }

                value = default;
                return false;
            }

            public IEnumerator<KeyValuePair<string, LKParticipant>> GetEnumerator()
            {
                foreach (var kv in map)
                    yield return new KeyValuePair<string, LKParticipant>(kv.Key, new LKParticipant(kv.Value, qualityMap));
            }

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }
    }
}

#endif





