using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LiveKit
{

    public class RTCConfiguration
    {
        
    }
    public class RoomOptions
    {
        public bool AutoSubscribe;
        public bool Dynacast;
        public bool AdaptiveStream;

        public RTCConfiguration RtcConfig;

        public RoomOptions() { }

        public Proto.RoomOptions ToProto()
        {
            var proto = new Proto.RoomOptions();

            proto.AutoSubscribe = AutoSubscribe;
            proto.Dynacast = Dynacast;
            proto.AdaptiveStream = AdaptiveStream;

            return proto;
        }
    }

    public class Room
    {
        public delegate void ParticipantDelegate(RemoteParticipant participant);
        public delegate void PublishDelegate(RemoteTrackPublication publication, RemoteParticipant participant);
        public delegate void SubscribeDelegate(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant);
        public delegate void MuteDelegate(TrackPublication publication, Participant participant);
        public delegate void SpeakersChangeDelegate(List<Participant> speakers);
        public delegate void ConnectionQualityChangeDelegate(ConnectionQuality quality, Participant participant);
        public delegate void DataDelegate(byte[] data, Participant participant, DataPacketKind kind);
        public delegate void ConnectionStateChangeDelegate(ConnectionState connectionState);
        public delegate void ConnectionDelegate();

        public string Sid { private set; get; }
        public string Name { private set; get; }
        public string Metadata { private set; get; }
        public LocalParticipant LocalParticipant { private set; get; }

        private readonly Dictionary<string, RemoteParticipant> _participants = new();
        public IReadOnlyDictionary<string, RemoteParticipant> Participants => _participants;

        public event ParticipantDelegate ParticipantConnected;
        public event ParticipantDelegate ParticipantDisconnected;
        public event PublishDelegate TrackPublished;
        public event PublishDelegate TrackUnpublished;
        public event SubscribeDelegate TrackSubscribed;
        public event SubscribeDelegate TrackUnsubscribed;
        public event MuteDelegate TrackMuted;
        public event MuteDelegate TrackUnmuted;
        public event SpeakersChangeDelegate ActiveSpeakersChanged;
        public event ConnectionQualityChangeDelegate ConnectionQualityChanged;
        public event DataDelegate DataReceived;
        public event ConnectionStateChangeDelegate ConnectionStateChanged;
        public event ConnectionDelegate Connected;
        public event ConnectionDelegate Disconnected;
        public event ConnectionDelegate Reconnecting;
        public event ConnectionDelegate Reconnected;

        internal FfiHandle Handle;

        public ConnectInstruction Connect(string url, string token, RoomOptions options)
        {
            var connect = new ConnectRequest();
            connect.Url = url;
            connect.Token = token;
            connect.Options = options.ToProto();

            var request = new FfiRequest();
            request.Connect = connect;

            var resp = FfiClient.SendRequest(request);
            return new ConnectInstruction(resp.Connect.AsyncId, this);
        }

        internal void UpdateFromInfo(RoomInfo info)
        {
            Sid = info.Sid;
            Name = info.Name;
            Metadata = info.Metadata;
        }

        internal void OnEventReceived(RoomEvent e)
        {
            if (e.RoomHandle != (ulong)Handle.DangerousGetHandle())
                return;

            switch (e.MessageCase)
            {
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                    {
                        var participant = CreateRemoteParticipant(e.ParticipantConnected.Info.Info);
                        ParticipantConnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    {
                        var sid = e.ParticipantDisconnected.ParticipantSid;
                        var participant = Participants[Sid];
                        _participants.Remove(Sid);
                        ParticipantDisconnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackPublished:
                    {
                        var participant = Participants[e.TrackPublished.ParticipantSid];
                        var publication = new RemoteTrackPublication(e.TrackPublished.Publication.Info);
                        participant._tracks.Add(publication.Sid, publication);
                        participant.OnTrackPublished(publication);
                        TrackPublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    {
                        var participant = Participants[e.TrackUnpublished.ParticipantSid];
                        var publication = participant.Tracks[e.TrackUnpublished.PublicationSid];
                        participant._tracks.Remove(publication.Sid);
                        participant.OnTrackUnpublished(publication);
                        TrackUnpublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackSubscribed:
                    {
                        var track = e.TrackSubscribed.Track;
                        var info = track.Info;
                        var participant = Participants[e.TrackSubscribed.ParticipantSid];
                        var publication = participant.Tracks[info.Sid];

                        if (info.Kind == TrackKind.KindVideo)
                        {
                            var videoTrack = new RemoteVideoTrack(null, info, this, participant);
                            publication.UpdateTrack(videoTrack);
                            TrackSubscribed?.Invoke(videoTrack, publication, participant);
                        }
                        else if (info.Kind == TrackKind.KindAudio)
                        {
                            var audioTrack = new RemoteAudioTrack(null, info, this, participant);
                            publication.UpdateTrack(audioTrack);
                            TrackSubscribed?.Invoke(audioTrack, publication, participant);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnsubscribed:
                    {
                        var participant = Participants[e.TrackUnsubscribed.ParticipantSid];
                        var publication = participant.Tracks[e.TrackUnsubscribed.TrackSid];
                        var track = publication.Track;
                        publication.UpdateTrack(null);
                        TrackUnsubscribed?.Invoke(track, publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackMuted:
                    {
                        var participant = GetParticipant(e.TrackMuted.ParticipantSid);
                        var publication = participant.Tracks[e.TrackMuted.TrackSid];
                        publication.UpdateMuted(true);
                        TrackMuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnmuted:
                    {
                        var participant = GetParticipant(e.TrackUnmuted.ParticipantSid);
                        var publication = participant.Tracks[e.TrackUnmuted.TrackSid];
                        publication.UpdateMuted(false);
                        TrackUnmuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ActiveSpeakersChanged:
                    {
                        var sids = e.ActiveSpeakersChanged.ParticipantSids;
                        var speakers = new List<Participant>(sids.Count);

                        foreach (var sid in sids)
                            speakers.Add(GetParticipant(sid));

                        ActiveSpeakersChanged?.Invoke(speakers);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionQualityChanged:
                    {
                        var participant = GetParticipant(e.ConnectionQualityChanged.ParticipantSid);
                        var quality = e.ConnectionQualityChanged.Quality;
                        ConnectionQualityChanged?.Invoke(quality, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.DataReceived:
                    {
                        var dataInfo = e.DataReceived;

                        var handle = new FfiHandle((IntPtr)dataInfo.Handle.Id);
                        var data = new byte[dataInfo.DataSize];
                        Marshal.Copy((IntPtr)dataInfo.DataPtr, data, 0, data.Length);
                        handle.Dispose();

                        var participant = GetParticipant(e.DataReceived.ParticipantSid);
                        DataReceived?.Invoke(data, participant, dataInfo.Kind);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionStateChanged:
                    ConnectionStateChanged?.Invoke(e.ConnectionStateChanged.State);
                    break;
                case RoomEvent.MessageOneofCase.Connected:
                    Connected?.Invoke();
                    break;
                case RoomEvent.MessageOneofCase.Disconnected:
                    Disconnected?.Invoke();
                    OnDisconnect();
                    break;
                case RoomEvent.MessageOneofCase.Reconnecting:
                    Reconnecting?.Invoke();
                    break;
                case RoomEvent.MessageOneofCase.Reconnected:
                    Reconnected?.Invoke();
                    break;
            }
        }

        internal void OnConnect(RoomInfo info)
        {
            Handle = new FfiHandle((IntPtr)info.Handle.Id);

            UpdateFromInfo(info);
            LocalParticipant = new LocalParticipant(info.LocalParticipant, this);

            // Add already connected participant
            foreach (var p in info.Participants)
                CreateRemoteParticipant(p);

            FfiClient.Instance.RoomEventReceived += OnEventReceived;
        }

        internal void OnDisconnect()
        {
            FfiClient.Instance.RoomEventReceived -= OnEventReceived;
        }

        RemoteParticipant CreateRemoteParticipant(ParticipantInfo info)
        {
            var participant = new RemoteParticipant(info, this);
            _participants.Add(participant.Sid, participant);

            foreach (var pubInfo in info.Publications)
            {
                var publication = new RemoteTrackPublication(pubInfo);
                participant._tracks.Add(publication.Sid, publication);
            }

            return participant;
        }

        public Participant GetParticipant(string sid)
        {
            if (sid == LocalParticipant.Sid)
                return LocalParticipant;

            Participants.TryGetValue(sid, out var remoteParticipant);
            return remoteParticipant;
        }
    }

    public sealed class ConnectInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private Room _room;

        internal ConnectInstruction(ulong asyncId, Room room)
        {
            _asyncId = asyncId;
            _room = room;
            FfiClient.Instance.ConnectReceived += OnConnect;
        }

        void OnConnect(ConnectCallback e)
        {
            if (_asyncId != e.AsyncId)
                return;

            FfiClient.Instance.ConnectReceived -= OnConnect;

            bool success = string.IsNullOrEmpty(e.Error);
            if (success)
                _room.OnConnect(e.Room.Info);

            IsError = !success;
            IsDone = true;
        }
    }
}
