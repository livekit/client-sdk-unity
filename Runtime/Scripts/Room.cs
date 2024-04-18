using System;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using System.Runtime.InteropServices;
using LiveKit.Internal.FFIClients.Requests;

namespace LiveKit
{
    public enum IceTransportType
    {
        TRANSPORT_RELAY = 0,
        TRANSPORT_NOHOST = 1,
        TRANSPORT_ALL = 2
    }

    public enum ContinualGatheringPolicy
    {
        GATHER_ONCE = 0,
        GATHER_CONTINUALLY = 1
    }


    public class IceServer
    {
        public string[] Urls;
        public string Username;
        public string Password;

        public Proto.IceServer ToProto()
        {
            var proto = new Proto.IceServer();
            proto.Username = Username;
            proto.Password = Password;
            proto.Urls.AddRange(Urls);

            return proto;
        }
    }

    public class RTCConfiguration
    {
        IceTransportType IceTransportType = IceTransportType.TRANSPORT_ALL;
        ContinualGatheringPolicy ContinualGatheringPolicy = ContinualGatheringPolicy.GATHER_ONCE;
        IceServer[] IceServers;

        public Proto.RtcConfig ToProto()
        {
            var proto = new Proto.RtcConfig();

            switch(ContinualGatheringPolicy)
            {
                case ContinualGatheringPolicy.GATHER_ONCE:
                    proto.ContinualGatheringPolicy = Proto.ContinualGatheringPolicy.GatherOnce;
                    break;
                case ContinualGatheringPolicy.GATHER_CONTINUALLY:
                    proto.ContinualGatheringPolicy = Proto.ContinualGatheringPolicy.GatherContinually;
                    break;
            }

            switch(IceTransportType)
            {
                case IceTransportType.TRANSPORT_ALL:
                    proto.IceTransportType = Proto.IceTransportType.TransportAll;
                    break;
                case IceTransportType.TRANSPORT_RELAY:
                    proto.IceTransportType = Proto.IceTransportType.TransportRelay;
                    break;
                case IceTransportType.TRANSPORT_NOHOST:
                    proto.IceTransportType = Proto.IceTransportType.TransportNohost;
                    break;
            }

            foreach(var item in IceServers)
            {
                proto.IceServers.Add(item.ToProto());
            }

            return proto;
        }
    }


    public class RoomOptions
    {
        public bool AutoSubscribe = true;
        public bool Dynacast = true;
        public bool AdaptiveStream = true;
        public uint JoinRetries = 3;
        public RTCConfiguration RtcConfig = null;
        public E2EEOptions E2EE = null;

        public Proto.RoomOptions ToProto()
        {
            var proto = new Proto.RoomOptions();

            proto.AutoSubscribe = AutoSubscribe;
            proto.Dynacast = Dynacast;
            proto.AdaptiveStream = AdaptiveStream;
            proto.JoinRetries = JoinRetries;
            proto.RtcConfig = RtcConfig?.ToProto();
            proto.E2Ee = E2EE?.ToProto();

            return proto;
        }
    }

    public class Room
    {
        internal FfiHandle RoomHandle = null;
        private readonly Dictionary<string, RemoteParticipant> _participants = new();

        public delegate void MetaDelegate(string metaData);
        public delegate void ParticipantDelegate(Participant participant);
        public delegate void RemoteParticipantDelegate(RemoteParticipant participant);
        public delegate void LocalPublishDelegate(TrackPublication publication, LocalParticipant participant);
        public delegate void PublishDelegate(RemoteTrackPublication publication, RemoteParticipant participant);
        public delegate void SubscribeDelegate(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant);
        public delegate void MuteDelegate(TrackPublication publication, Participant participant);
        public delegate void SpeakersChangeDelegate(List<Participant> speakers);
        public delegate void ConnectionQualityChangeDelegate(ConnectionQuality quality, Participant participant);
        public delegate void DataDelegate(byte[] data, Participant participant, DataPacketKind kind, string topic);
        public delegate void ConnectionStateChangeDelegate(ConnectionState connectionState);
        public delegate void ConnectionDelegate(Room room);
        public delegate void E2EeStateChangedDelegate(Participant participant, EncryptionState state);

        public string Sid { private set; get; }
        public string Name { private set; get; }
        public string Metadata { private set; get; }
        public LocalParticipant LocalParticipant { private set; get; }
        public ConnectionState ConnectionState { private set; get; }
        public bool IsConnected => RoomHandle == null && ConnectionState != ConnectionState.ConnDisconnected;
        public E2EEManager E2EEManager { internal set; get; }
        public IReadOnlyDictionary<string, RemoteParticipant> RemoteParticipants => _participants;

        public event ParticipantDelegate ParticipantConnected;
        public event ParticipantDelegate ParticipantDisconnected;
        public event LocalPublishDelegate LocalTrackPublished;
        public event LocalPublishDelegate LocalTrackUnpublished;
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
        public event E2EeStateChangedDelegate E2EeStateChanged;
        public event MetaDelegate RoomMetadataChanged;
        public event ParticipantDelegate ParticipantMetadataChanged;
        public event ParticipantDelegate ParticipantNameChanged;

        public ConnectInstruction Connect(string url, string token, RoomOptions options)
        {
            using var response = FFIBridge.Instance.SendConnectRequest(url, token, options);
            Utils.Debug("Connect....");
            FfiResponse res = response;
            Utils.Debug($"Connect response.... {response}");
            return new ConnectInstruction(res.Connect.AsyncId, this, options);
        }
        
        public void Disconnect()
        {
            using var response = FFIBridge.Instance.SendDisconnectRequest(this);
            Utils.Debug($"Disconnect.... {RoomHandle}");
            FfiResponse resp = response;
            Utils.Debug($"Disconnect response.... {resp}");
        }

        internal void UpdateFromInfo(RoomInfo info)
        {
            Sid = info.Sid;
            Name = info.Name;
            Metadata = info.Metadata;
        }


        internal void OnEventReceived(RoomEvent e)
        {
            if (e.RoomHandle != (ulong)RoomHandle.DangerousGetHandle())
                return;

            switch (e.MessageCase)
            {
                case RoomEvent.MessageOneofCase.RoomMetadataChanged:
                    {
                        Metadata = e.RoomMetadataChanged.Metadata;
                        RoomMetadataChanged?.Invoke(e.RoomMetadataChanged.Metadata);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantMetadataChanged:
                    {
                        var participant = GetParticipant(e.ParticipantMetadataChanged.ParticipantSid);
                        participant.SetMeta(e.ParticipantMetadataChanged.Metadata);
                        if (participant != null) ParticipantMetadataChanged?.Invoke(participant);
                        else Utils.Debug("Unable to find participant: " + e.ParticipantMetadataChanged.ParticipantSid + " in Meta data Change Event");
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantNameChanged:
                    {
                        var participant = GetParticipant(e.ParticipantNameChanged.ParticipantSid);
                        participant.SetName(e.ParticipantNameChanged.Name);
                        if (participant != null) ParticipantNameChanged?.Invoke(participant);
                        else Utils.Debug("Unable to find participant: " + e.ParticipantNameChanged.ParticipantSid + " in Meta data Change Event");
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                    {
                        var participant = CreateRemoteParticipant(e.ParticipantConnected.Info);
                        ParticipantConnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    {
                        var sid = e.ParticipantDisconnected.ParticipantSid;
                        var participant = RemoteParticipants[sid];
                        _participants.Remove(sid);
                        ParticipantDisconnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackPublished:
                    {
                        var participant = RemoteParticipants[e.TrackPublished.ParticipantSid];
                        var publication = new RemoteTrackPublication(e.TrackPublished.Publication.Info, FfiHandle.FromOwnedHandle(e.TrackPublished.Publication.Handle));
                        participant._tracks.Add(publication.Sid, publication);
                        participant.OnTrackPublished(publication);
                        TrackPublished?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    {
                        var participant = RemoteParticipants[e.TrackUnpublished.ParticipantSid];
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
                        var participant = RemoteParticipants[e.TrackSubscribed.ParticipantSid];
                        var publication = participant.Tracks[info.Sid];

                        if(publication == null)
                        {
                            participant._tracks.Add(publication.Sid, publication);
                        }

                        if (info.Kind == TrackKind.KindVideo)
                        {
                            var videoTrack = new RemoteVideoTrack(track, this, participant);
                            publication.UpdateTrack(videoTrack);
                            TrackSubscribed?.Invoke(videoTrack, publication, participant);
                        }
                        else if (info.Kind == TrackKind.KindAudio)
                        {
                            var audioTrack = new RemoteAudioTrack(track, this, participant);
                            publication.UpdateTrack(audioTrack);
                            TrackSubscribed?.Invoke(audioTrack, publication, participant);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnsubscribed:
                    {
                        var participant = RemoteParticipants[e.TrackUnsubscribed.ParticipantSid];
                        var publication = participant.Tracks[e.TrackUnsubscribed.TrackSid];
                        var track = publication.Track;
                        publication.UpdateTrack(null);
                        TrackUnsubscribed?.Invoke(track, publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackUnpublished:
                    {
                        if (LocalParticipant._tracks.ContainsKey(e.LocalTrackUnpublished.PublicationSid))
                        {
                            var publication = LocalParticipant._tracks[e.LocalTrackUnpublished.PublicationSid];
                            LocalTrackUnpublished?.Invoke(publication, LocalParticipant);
                        }
                        else
                        {
                            Utils.Debug("Unable to find local track after unpublish: " + e.LocalTrackPublished.TrackSid);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackPublished:
                    {
                        if (LocalParticipant._tracks.ContainsKey(e.LocalTrackPublished.TrackSid))
                        {
                            var publication = LocalParticipant._tracks[e.LocalTrackPublished.TrackSid];
                            LocalTrackPublished?.Invoke(publication, LocalParticipant);
                        }
                        else
                        {
                            Utils.Debug("Unable to find local track after publish: " + e.LocalTrackPublished.TrackSid);
                        }
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
                        participant.ConnectionQuality = quality;
                        ConnectionQualityChanged?.Invoke(quality, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.DataReceived:
                    {
                        var dataInfo = e.DataReceived;
                        var data = new byte[dataInfo.Data.Data.DataLen];
                        Marshal.Copy((IntPtr)dataInfo.Data.Data.DataPtr, data, 0, data.Length);
                        var participant = GetParticipant(e.DataReceived.ParticipantSid);
                        DataReceived?.Invoke(data, participant, dataInfo.Kind, dataInfo.Topic);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionStateChanged:
                    ConnectionState = e.ConnectionStateChanged.State;
                    ConnectionStateChanged?.Invoke(e.ConnectionStateChanged.State);
                    break;
                case RoomEvent.MessageOneofCase.Disconnected:
                    Disconnected?.Invoke(this);
                    OnDisconnect();
                    break;
                case RoomEvent.MessageOneofCase.Reconnecting:
                    Reconnecting?.Invoke(this);
                    break;
                case RoomEvent.MessageOneofCase.Reconnected:
                    Reconnected?.Invoke(this);
                    break;
                case RoomEvent.MessageOneofCase.E2EeStateChanged:
                    {
                        var participant = GetParticipant(e.E2EeStateChanged.ParticipantSid);
                        E2EeStateChanged?.Invoke(participant, e.E2EeStateChanged.State);
                    }
                    break;
            }
        }

        internal void OnConnect(ConnectCallback info)
        {
            RoomHandle = FfiHandle.FromOwnedHandle(info.Room.Handle);

            UpdateFromInfo(info.Room.Info);
            LocalParticipant = new LocalParticipant(info.LocalParticipant, this);

            // Add already connected participant
            foreach (var p in info.Participants)
                CreateRemoteParticipantWithTracks(p);

            FfiClient.Instance.RoomEventReceived += OnEventReceived;
            FfiClient.Instance.DisconnectReceived += OnDisconnectReceived;
            Connected?.Invoke(this);
        }

        private void OnDisconnectReceived(DisconnectCallback e)
        {
            FfiClient.Instance.DisconnectReceived -= OnDisconnectReceived;
            Utils.Debug($"OnDisconnect.... {e}");
        }

        private void OnDisconnect()
        {
            FfiClient.Instance.RoomEventReceived -= OnEventReceived;
        }

        internal RemoteParticipant CreateRemoteParticipantWithTracks(ConnectCallback.Types.ParticipantWithTracks item)
        {
            var participant = item.Participant;
            var publications = item.Publications;
            var newParticipant = new RemoteParticipant(participant, this);
            _participants.Add(participant.Info.Sid, newParticipant);
            foreach (var pub in publications)
            {
                var publication = new RemoteTrackPublication(pub.Info, FfiHandle.FromOwnedHandle(pub.Handle));
                newParticipant._tracks.Add(publication.Sid, publication);
                newParticipant.OnTrackPublished(publication);
            }
            return newParticipant;
        }

        internal RemoteParticipant CreateRemoteParticipant(OwnedParticipant participant)
        {
            var newParticipant = new RemoteParticipant(participant, this);
            _participants.Add(participant.Info.Sid, newParticipant);
            return newParticipant;
        }

        internal Participant GetParticipant(string sid)
        {
            if (sid == LocalParticipant.Sid)
                return LocalParticipant;

            RemoteParticipants.TryGetValue(sid, out var remoteParticipant);
            return remoteParticipant;
        }
    }

    public sealed class ConnectInstruction : YieldInstruction
    {
        private ulong _asyncId;
        private Room _room;
        private RoomOptions _roomOptions;

        internal ConnectInstruction(ulong asyncId, Room room, RoomOptions options)
        {
            _asyncId = asyncId;
            _room = room;
            _roomOptions = options;
            FfiClient.Instance.ConnectReceived += OnConnect;
        }

        void OnConnect(ConnectCallback e)
        {
            if (_asyncId != e.AsyncId)
                return;

            FfiClient.Instance.ConnectReceived -= OnConnect;

            bool success = string.IsNullOrEmpty(e.Error);
            if (success)
            {
                if(_roomOptions.E2EE != null)
                {
                    _room.E2EEManager = new E2EEManager(_room.RoomHandle, _roomOptions.E2EE);
                }

                _room.OnConnect(e);
            }

            IsError = !success;
            IsDone = true;
        }
    }
}
