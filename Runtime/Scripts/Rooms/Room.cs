using System;
using System.Buffers;
using System.Collections.Generic;
using LiveKit.Internal;
using LiveKit.Proto;
using Google.Protobuf.Collections;
using System.Threading;
using LiveKit.Internal.FFIClients.Pools.Memory;
using LiveKit.Internal.FFIClients.Requests;
using LiveKit.Rooms.ActiveSpeakers;
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.Participants;

namespace LiveKit.Rooms
{
    public class Room : IRoom
    {
        public delegate void MetaDelegate(string metaData);
        public delegate void ParticipantDelegate(Participant participant);
        public delegate void RemoteParticipantDelegate(Participant participant);
        public delegate void LocalPublishDelegate(TrackPublication publication, Participant participant);
        public delegate void PublishDelegate(TrackPublication publication, Participant participant);
        public delegate void SubscribeDelegate(ITrack track, TrackPublication publication, Participant participant);
        public delegate void MuteDelegate(TrackPublication publication, Participant participant);
        public delegate void SpeakersChangeDelegate(IReadOnlyCollection<string> speakers);
        public delegate void ConnectionQualityChangeDelegate(ConnectionQuality quality, Participant participant);
        public delegate void DataDelegate(ReadOnlySpan<byte> data, Participant participant, DataPacketKind kind);
        public delegate void ConnectionStateChangeDelegate(ConnectionState connectionState);
        public delegate void ConnectionDelegate(Room room);

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public string Sid { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public string Metadata { get; private set; } = string.Empty;

        internal FfiHandle Handle => handle;
        
        public event MetaDelegate? RoomMetadataChanged;
        public event ParticipantDelegate? ParticipantConnected;
        public event ParticipantDelegate? ParticipantMetadataChanged;
        public event ParticipantDelegate? ParticipantDisconnected;
        public event LocalPublishDelegate? LocalTrackPublished;
        public event LocalPublishDelegate? LocalTrackUnpublished;
        public event PublishDelegate? TrackPublished;
        public event PublishDelegate? TrackUnpublished;
        public event SubscribeDelegate? TrackSubscribed;
        public event SubscribeDelegate? TrackUnsubscribed;
        public event MuteDelegate? TrackMuted;
        public event MuteDelegate? TrackUnmuted;
        public event SpeakersChangeDelegate? ActiveSpeakersChanged;
        public event ConnectionQualityChangeDelegate? ConnectionQualityChanged;
        public event DataDelegate? DataReceived;
        public event ConnectionStateChangeDelegate? ConnectionStateChanged;
        public event ConnectionDelegate? Connected;
        public event ConnectionDelegate? Disconnected;
        public event ConnectionDelegate? Reconnecting;
        public event ConnectionDelegate? Reconnected;

        public IActiveSpeakers ActiveSpeakers => activeSpeakers;

        public IParticipantsHub Participants => participantsHub;
        private FfiHandle handle = null!;
        private readonly IMemoryPool memoryPool;
        private readonly IMutableActiveSpeakers activeSpeakers;
        private readonly IMutableParticipantsHub participantsHub;

        public Room() : this(
            new ArrayMemoryPool(ArrayPool<byte>.Shared!),
            new DefaultActiveSpeakers(),
            new ParticipantsHub()
        )
        {
        }

        public Room(IMemoryPool memoryPool, IMutableActiveSpeakers activeSpeakers, IMutableParticipantsHub participantsHub)
        {
            this.memoryPool = memoryPool;
            this.activeSpeakers = activeSpeakers;
            this.participantsHub = participantsHub;
        }

        public ConnectInstruction Connect(string url, string authToken, CancellationToken cancelToken)
        {
            using var request = FFIBridge.Instance.NewRequest<ConnectRequest>();
            using var roomOptions = request.TempResource<RoomOptions>();
            var connect = request.request;
            connect.Url = url;
            connect.Token = authToken;
            connect.Options = roomOptions;
            connect.Options.AutoSubscribe = false;

            Utils.Debug("Connect....");
            using var response = request.Send();
            FfiResponse res = response;
            Utils.Debug($"Connect response.... {response}");
            return new ConnectInstruction(res.Connect!.AsyncId, this, cancelToken);
        }
        
        public void PublishData(Span<byte> data, string topic, DataPacketKind kind = DataPacketKind.KindLossy)
        {
            unsafe
            {
                fixed (byte* pointer = data)
                {
                    PublishData(pointer, data.Length, topic, kind);
                }   
            }
        }
        
        private unsafe void PublishData(byte* data, int len, string topic, DataPacketKind kind = DataPacketKind.KindLossy)
        {
            using var request = FFIBridge.Instance.NewRequest<PublishDataRequest>();
            var dataRequest = request.request;
            dataRequest.DataLen = (ulong)len;
            dataRequest.DataPtr = (ulong)data;
            dataRequest.Kind = kind;
            dataRequest.Topic = topic;
            dataRequest.LocalParticipantHandle = (ulong)Participants.LocalParticipant().Handle.DangerousGetHandle();
            Utils.Debug("Sending message: " + topic);
            using var response = request.Send();
        }

        public void Disconnect()
        {
            using var request = FFIBridge.Instance.NewRequest<DisconnectRequest>();
            var disconnect = request.request;
            disconnect.RoomHandle = (ulong)handle.DangerousGetHandle();

            Utils.Debug($"Disconnect.... {disconnect.RoomHandle}");
            using var response = request.Send();
            // ReSharper disable once RedundantAssignment
            FfiResponse resp = response;
            Utils.Debug($"Disconnect response.... {resp}");
        }

        private void UpdateFromInfo(RoomInfo info)
        {
            Sid = info.Sid!;
            Name = info.Name!;
            Metadata = info.Metadata!;
        }

        private void OnEventReceived(RoomEvent e)
        {

            if (e.RoomHandle != (ulong)handle.DangerousGetHandle())
            {
                Utils.Debug("Ignoring. Different Room... " + e);
                return;
            }
            Utils.Debug($"Room {Name} Event Type: {e.MessageCase}   ---> ({e.RoomHandle} <=> {(ulong)handle.DangerousGetHandle()})");
            switch (e.MessageCase)
            {
                case RoomEvent.MessageOneofCase.RoomMetadataChanged:
                    {
                        Metadata = e.RoomMetadataChanged!.Metadata!;
                        RoomMetadataChanged?.Invoke(Metadata);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantMetadataChanged:
                    {
                        var participant = this.ParticipantEnsured(e.ParticipantNameChanged!.ParticipantSid!);
                        participant.UpdateMeta(e.ParticipantMetadataChanged!.Metadata!);
                        ParticipantMetadataChanged?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantConnected:
                {
                    var participant = Participant.NewRemote(
                        this,
                            e.ParticipantConnected!.Info!.Info!,
                            null,
                            new FfiHandle((IntPtr)e.ParticipantConnected.Info.Handle!.Id
                        )
                    );
                    participantsHub.AddRemote(participant);
                    ParticipantConnected?.Invoke(participant);
                }
                    break;
                case RoomEvent.MessageOneofCase.ParticipantDisconnected:
                    {
                        var info = e.ParticipantDisconnected!;
                        var participant = this.RemoteParticipantEnsured(info.ParticipantSid!);
                        participantsHub.RemoteParticipant(info.ParticipantSid!);
                        ParticipantDisconnected?.Invoke(participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackUnpublished:
                    {
                        if (Participants.LocalParticipant().Tracks.ContainsKey(e.LocalTrackUnpublished!.PublicationSid))
                        {
                            var publication = Participants.LocalParticipant().TrackPublication(e.LocalTrackUnpublished.PublicationSid!);
                            LocalTrackUnpublished?.Invoke(publication, Participants.LocalParticipant());
                        }
                        else
                        {
                            Utils.Debug("Unable to find local track after unpublish: " + e.LocalTrackPublished!.TrackSid);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.LocalTrackPublished:
                    {
                        if(Participants.LocalParticipant().Tracks.ContainsKey(e.LocalTrackPublished!.TrackSid))
                        {
                            var publication = Participants.LocalParticipant().TrackPublication(e.LocalTrackPublished.TrackSid!);
                            LocalTrackPublished?.Invoke(publication, Participants.LocalParticipant());
                        } else
                        {
                            Utils.Debug("Unable to find local track after publish: " + e.LocalTrackPublished.TrackSid);
                        }
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackPublished:
                    {
                        var participant = this.RemoteParticipantEnsured(e.TrackPublished!.ParticipantSid!);
                        var publication = new TrackPublication(e.TrackPublished.Publication!.Info!);
                        participant.Publish(publication);
                        TrackPublished?.Invoke(publication, participant);
                        publication.SetSubscribedForRemote(true);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnpublished:
                    {
                        var participant = this.RemoteParticipantEnsured(e.TrackUnpublished!.ParticipantSid!);
                        participant.UnPublish(e.TrackUnpublished.PublicationSid!, out var unpublishedTrack);
                        TrackUnpublished?.Invoke(unpublishedTrack, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackSubscribed:
                    {
                        var info = e.TrackSubscribed!.Track!.Info!;
                        var participant = this.RemoteParticipantEnsured(e.TrackSubscribed.ParticipantSid!);
                        var publication = participant.TrackPublication(info.Sid!);

                        var track = new Track(null, info, this, participant);
                        publication.UpdateTrack(track);
                        TrackSubscribed?.Invoke(track, publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnsubscribed:
                    {
                        var participant = this.ParticipantEnsured(e.TrackUnsubscribed!.ParticipantSid!);
                        var publication = participant.TrackPublication(e.TrackUnsubscribed.TrackSid!);
                        publication.RemoveTrack(out var removedTrack);
                        TrackUnsubscribed?.Invoke(removedTrack!, publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackMuted:
                    {
                        var trackMuted = e.TrackMuted!;
                        var participant = this.ParticipantEnsured(trackMuted.ParticipantSid!);
                        var publication = participant.TrackPublication(trackMuted.TrackSid!);
                        publication.UpdateMuted(true);
                        TrackMuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.TrackUnmuted:
                    {
                        var trackUnmuted = e.TrackUnmuted!;
                        var participant = this.ParticipantEnsured(trackUnmuted.ParticipantSid!);
                        var publication = participant.TrackPublication(trackUnmuted.TrackSid!);
                        publication.UpdateMuted(false);
                        TrackUnmuted?.Invoke(publication, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ActiveSpeakersChanged:
                    {
                        activeSpeakers.UpdateCurrentActives(e.ActiveSpeakersChanged!.ParticipantSids!);
                        ActiveSpeakersChanged?.Invoke(activeSpeakers);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionQualityChanged:
                    {
                        var participant = this.ParticipantEnsured(e.ConnectionQualityChanged!.ParticipantSid!);
                        var quality = e.ConnectionQualityChanged.Quality;
                        ConnectionQualityChanged?.Invoke(quality, participant);
                    }
                    break;
                case RoomEvent.MessageOneofCase.DataReceived:
                    {
                        var dataInfo = e.DataReceived!.Data!;
                        
                        using var memory = memoryPool.Memory(dataInfo.Data!.DataLen);
                        var data = memory.Span();

                        unsafe
                        {
                            var unmanagedBuffer = new Span<byte>((void*)dataInfo.Data.DataPtr, data.Length);
                            unmanagedBuffer.CopyTo(data);
                        }

                        NativeMethods.FfiDropHandle(dataInfo.Handle!.Id);

                        var participant = this.ParticipantEnsured(e.DataReceived.ParticipantSid!);
                        DataReceived?.Invoke(data, participant, e.DataReceived.Kind);
                    }
                    break;
                case RoomEvent.MessageOneofCase.ConnectionStateChanged:
                    ConnectionStateChanged?.Invoke(e.ConnectionStateChanged!.State);
                    break;
                /*case RoomEvent.MessageOneofCase.Connected:
                    Connected?.Invoke(this);
                    break;*/
                case RoomEvent.MessageOneofCase.Eos:
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
                case RoomEvent.MessageOneofCase.None:
                    //ignore
                    break;
                case RoomEvent.MessageOneofCase.TrackSubscriptionFailed:
                    //ignore
                    break;
                case RoomEvent.MessageOneofCase.ParticipantNameChanged:
                    //ignore
                    break;
                case RoomEvent.MessageOneofCase.E2EeStateChanged:
                    //ignore
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        internal void OnConnect(FfiOwnedHandle roomHandle, RoomInfo info, OwnedParticipant participant, RepeatedField<ConnectCallback.Types.ParticipantWithTracks> participants)
        {
            Utils.Debug($"OnConnect.... {roomHandle.Id}  {participant.Handle!.Id}");
            Utils.Debug(info);

            handle = new FfiHandle((IntPtr)roomHandle.Id);
            UpdateFromInfo(info);

            var selfParticipant = new Participant(
                participant.Info!,
                this,
                new FfiHandle((IntPtr)participant.Handle.Id),
                Origin.Local
            );
            participantsHub.AssignLocal(selfParticipant);
            // Add already connected participant
            foreach (var p in participants)
            {
                var remote = Participant.NewRemote(
                    this,
                    p.Participant!.Info!, p.Publications,
                    new FfiHandle((IntPtr)p.Participant.Handle!.Id)
                );
                participantsHub.AddRemote(remote);
            }

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
    }
}
