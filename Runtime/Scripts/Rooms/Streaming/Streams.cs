using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.TrackPublications;
using LiveKit.Rooms.Tracks;
using RichTypes;

namespace LiveKit.Rooms.Streaming
{
    public abstract class Streams<T, TInfo> : IStreams<T, TInfo> where T : class, IDisposable
    {
        private readonly TrackKind requiredKind;
        private readonly IParticipantsHub participantsHub;
        private readonly Dictionary<StreamKey, Owned<T>> streams = new();

        private Room? room;
        private bool isDisposed;

        public Streams(IParticipantsHub participantsHub, TrackKind requiredKind)
        {
            this.participantsHub = participantsHub;
            this.requiredKind = requiredKind;
        }

        public void Dispose()
        {
            lock (this)
            {
                if (isDisposed) Utils.Error("Attempt to dispose an already disposed Streams instance");
                Free();

                if (room != null)
                {
                    room.TrackUnpublished -= RoomOnTrackUnpublished;
                }

                isDisposed = true;
            }
        }

        public void AssignRoom(Room newRoom)
        {
            lock (this)
            {
                if (room != null)
                {
                    throw new Exception("Cannot reassign room");
                }

                room = newRoom;
                room.TrackUnpublished += RoomOnTrackUnpublished;
            }
        }

        private void RoomOnTrackUnpublished(TrackPublication publication, Participant participant)
        {
            Release(new StreamKey(participant.Identity, publication.Sid));
        }

        public Weak<T> ActiveStream(StreamKey key)
        {
            lock (this)
            {
                if (isDisposed)
                {
                    Utils.Error("Attempt to access to an already disposed Streams instance");
                    return Weak<T>.Null;
                }

                if (streams.TryGetValue(key, out Owned<T>? stream) == false)
                {
                    var participant = participantsHub.RemoteParticipant(key.identity);

                    if (participant == null)
                        if (key.identity == participantsHub.LocalParticipant().Identity)
                            participant = participantsHub.LocalParticipant();
                        else
                            return Weak<T>.Null;

                    if (participant.Tracks.TryGetValue(key.sid, out var trackPublication) == false)
                        return Weak<T>.Null;

                    if (trackPublication!.Track == null || trackPublication.Track!.Kind != requiredKind)
                        return Weak<T>.Null;

                    var track = trackPublication.Track!;

                    streams[key] = stream = new Owned<T>(NewStreamInstance(key, track));
                    return stream.Downgrade();
                }

                return stream!.Downgrade();
            }
        }

        public bool Release(StreamKey key)
        {
            lock (this)
            {
                if (isDisposed)
                {
                    Utils.Error("Attempt to access to an already disposed Streams instance");
                    return false;
                }

                if (streams.TryGetValue(key, out Owned<T> stream))
                {
                    stream!.Dispose(out T? inner);
                    inner!.Dispose();
                    streams.Remove(key);
                    return true;
                }

                return false;
            }
        }

        public void Free()
        {
            lock (this)
            {
                if (isDisposed)
                {
                    Utils.Error("Attempt to access to an already disposed Streams instance");
                }

                foreach (Owned<T> stream in streams.Values)
                {
                    stream.Dispose(out T? inner);
                    inner!.Dispose();
                }

                streams.Clear();
            }
        }

        public void ListInfo(List<StreamInfo<TInfo>> output)
        {
            lock (this)
            {
                if (isDisposed)
                {
                    Utils.Error("Attempt to access to an already disposed Streams instance");
                    return;
                }

                output.Clear();

                foreach ((var key, Owned<T> value) in streams)
                {
                    output.Add(new StreamInfo<TInfo>(key, InfoFromStream(value.Resource)));
                }
            }
        }

        protected abstract TInfo InfoFromStream(T stream);

        protected abstract T NewStreamInstance(StreamKey streamKey, ITrack track);
    }
}