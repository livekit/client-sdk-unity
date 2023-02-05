using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LiveKit
{
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

        public String Sid { private set; get; }
        public String Name { private set; get; }
        public String Metadata { private set; get; }
        public LocalParticipant LocalParticipant { private set; get; }

        private readonly Dictionary<String, RemoteParticipant> _participants = new();
        public IReadOnlyDictionary<String, RemoteParticipant> Participants => _participants;

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

        public ConnectInstruction Connect(String url, String token)
        {
            var connect = new ConnectRequest();
            connect.Url = url;
            connect.Token = token;

            var request = new FFIRequest();
            request.AsyncConnect = connect;

            var resp = FFIClient.Instance.SendRequest(request);
            return new ConnectInstruction(resp.AsyncId, this);
        }

        internal void UpdateFromInfo(RoomInfo info)
        {
            Sid = info.Sid;
            Name = info.Name;
            Metadata = info.Metadata;
        }

        internal void OnEventReceived(RoomEvent e)
        {
            if (e.RoomSid != Sid)
                return;

            switch (e.MessageCase)
            {
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                    {
                        var participant = new RemoteParticipant(e.ParticipantConnected.Info);
                        _participants.Add(participant.Sid, participant);
                        ParticipantConnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    {
                        var info = e.ParticipantConnected.Info;
                        var participant = Participants[info.Sid];
                        _participants.Remove(info.Sid);
                        ParticipantDisconnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackPublished:
                    {
                        var participant = Participants[e.TrackPublished.ParticipantSid];
                        var publication = new RemoteTrackPublication(e.TrackPublished.Publication);
                        participant.OnTrackPublished(publication);
                        TrackPublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    {
                        var participant = Participants[e.TrackUnpublished.ParticipantSid];
                        var publication = participant.Tracks[e.TrackUnpublished.PublicationSid];
                        participant.OnTrackUnpublished(publication);
                        TrackUnpublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackSubscribed:
                    {
                        var info = e.TrackSubscribed.Track;
                        var participant = Participants[e.TrackSubscribed.ParticipantSid];
                        var publication = participant.Tracks[info.Sid];

                        if (info.Kind == TrackKind.KindVideo)
                        {
                            var videoTrack = new RemoteVideoTrack(info);
                            publication.UpdateTrack(videoTrack);
                            TrackSubscribed?.Invoke(videoTrack, publication, participant);
                        }
                        else if (info.Kind == TrackKind.KindAudio)
                        {
                            var audioTrack = new RemoteAudioTrack(info);
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
                case RoomEvent.MessageOneofCase.SpeakersChanged:
                    {
                        var sids = e.SpeakersChanged.ParticipantSids;
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

                        var handle = new FFIHandle((IntPtr)dataInfo.Handle.Id);
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
            UpdateFromInfo(info);
            FFIClient.Instance.RoomEventReceived += OnEventReceived;
        }

        internal void OnDisconnect()
        {
            FFIClient.Instance.RoomEventReceived -= OnEventReceived;
        }

        public Participant GetParticipant(string sid)
        {
            if (sid == LocalParticipant.Sid)
                return LocalParticipant;

            Participants.TryGetValue(sid, out var remoteParticipant);
            return remoteParticipant;
        }

        public sealed class ConnectInstruction : YieldInstruction
        {
            private ulong _asyncId;
            private Room _room;

            internal ConnectInstruction(ulong asyncId, Room room)
            {
                _asyncId = asyncId;
                _room = room;
                FFIClient.Instance.ConnectReceived += OnConnect;
            }

            void OnConnect(ulong asyncId, ConnectEvent e)
            {
                if (_asyncId != asyncId)
                    return;

                FFIClient.Instance.ConnectReceived -= OnConnect;

                if (e.Success)
                    _room.OnConnect(e.Room);

                IsError = !e.Success;
                IsDone = true;
            }
        }
    }
}
