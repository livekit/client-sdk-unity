using System;
using System.Collections.Generic;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks;

namespace LiveKit.Rooms.Streaming
{
    public abstract class Streams<T> : IStreams<T> where T : class, IDisposable
    {
        [Serializable]
        private readonly struct Key : IEquatable<Key>
        {
            public readonly string identity;
            public readonly string sid;

            public Key(string identity, string sid)
            {
                this.identity = identity;
                this.sid = sid;
            }

            public bool Equals(Key other) =>
                identity == other.identity
                && sid == other.sid;

            public override bool Equals(object? obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(identity, sid);
            }
        }

        private readonly TrackKind requiredKind;
        private readonly IParticipantsHub participantsHub;
        private readonly Dictionary<Key, T> streams = new();
        private readonly Dictionary<T, Key> reverseLookup = new();
        private bool isDisposing = false;

        public Streams(IParticipantsHub participantsHub, TrackKind requiredKind)
        {
            this.participantsHub = participantsHub;
            this.requiredKind = requiredKind;
        }

        public WeakReference<T>? ActiveStream(string identity, string sid)
        {
            lock (this)
            {
                var key = new Key(identity, sid);
                if (streams.TryGetValue(key, out var stream) == false)
                {
                    var participant = participantsHub.RemoteParticipant(identity);
                    
                    if (participant == null)
                        if (identity == participantsHub.LocalParticipant().Identity)
                            participant = participantsHub.LocalParticipant();
                        else
                            return null;        
                    
                    if (participant.Tracks.TryGetValue(sid, out var trackPublication) == false)
                        return null;

                    if (trackPublication!.Track == null || trackPublication.Track!.Kind != requiredKind)
                        return null;

                    var track = trackPublication.Track!;

                    streams[key] = stream = NewStreamInstance(track);
                    reverseLookup[stream] = key;
                }

                return new WeakReference<T>(stream);
            }
        }

        public bool Release(T stream)
        {
            lock (this)
            {
                if (isDisposing) return false;
                
                if (reverseLookup.TryGetValue(stream, out var key))
                {
                    streams.Remove(key);
                    reverseLookup.Remove(stream);
                    return true;
                }

                return false;
            }
        }

        public void Free()
        {
            lock (this)
            {
                isDisposing = true;
                foreach (var stream in streams.Values) stream.Dispose();
                streams.Clear();
                reverseLookup.Clear();
                isDisposing = false;
            }
        }

        protected abstract T NewStreamInstance(ITrack track);
    }
}