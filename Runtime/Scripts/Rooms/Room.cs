using System;
using System.Buffers;
using LiveKit.Internal;
using LiveKit.Proto;
using Google.Protobuf.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Internal.FFIClients.Pools.Memory;
using LiveKit.Rooms.ActiveSpeakers;
using LiveKit.Rooms.DataPipes;
using LiveKit.Rooms.Info;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Tracks;
using LiveKit.Rooms.Tracks.Hub;
using LiveKit.Rooms.VideoStreaming;
using RichTypes;
using DCL.LiveKit.Public;
using RoomInfo = LiveKit.Proto.RoomInfo;

#if !UNITY_WEBGL
using LiveKit.Rooms.AsyncInstractions;
using LiveKit.Rooms.Participants.Factory;
using LiveKit.Rooms.Tracks.Factory;
using LiveKit.Rooms.Streaming.Audio;
using LiveKit.Rooms.TrackPublications;
#endif

using Utils = LiveKit.Internal.Utils;

#if UNITY_WEBGL
using JsRoom = LiveKit.Room;
#endif

namespace LiveKit.Rooms
{
    public class Room : IRoom
    {
#region delegates
        public delegate void MetaDelegate(string metaData);

        public delegate void SidDelegate(string sid);

        public delegate void RemoteParticipantDelegate(Participant participant);
#endregion


#if !UNITY_WEBGL
        private readonly IMemoryPool memoryPool;
        private readonly IMutableActiveSpeakers activeSpeakers;
        private readonly IMutableParticipantsHub participantsHub;
        private readonly ITracksFactory tracksFactory;
        private readonly IFfiHandleFactory ffiHandleFactory;
        private readonly IParticipantFactory participantFactory;
        private readonly ITrackPublicationFactory trackPublicationFactory;
        private readonly IMutableDataPipe dataPipe;
        private readonly IMutableRoomInfo roomInfo;
        private readonly IVideoStreams videoStreams;
        private readonly IAudioStreams audioStreams;
        private readonly ILocalTracks localTracks;

        public IActiveSpeakers ActiveSpeakers => activeSpeakers;

        public IParticipantsHub Participants => participantsHub;

        public IDataPipe DataPipe => dataPipe;

        public IVideoStreams VideoStreams => videoStreams;

        public IAudioStreams AudioStreams => audioStreams;

        public ILocalTracks LocalTracks => localTracks;

        public IRoomInfo Info => roomInfo;

        private FfiHandle? handle;

        private ulong handleId;

        public event MetaDelegate? RoomMetadataChanged;
        public event SidDelegate? RoomSidChanged;
        public event LocalPublishDelegate? LocalTrackPublished;
        public event LocalPublishDelegate? LocalTrackUnpublished;
        public event PublishDelegate? TrackPublished;
        public event PublishDelegate? TrackUnpublished;
        public event SubscribeDelegate? TrackSubscribed;
        public event SubscribeDelegate? TrackUnsubscribed;
        public event MuteDelegate? TrackMuted;
        public event MuteDelegate? TrackUnmuted;
        public event ConnectionQualityChangeDelegate? ConnectionQualityChanged;
        public event ConnectionStateChangeDelegate? ConnectionStateChanged;
        public event ConnectionDelegate? ConnectionUpdated;

        public Room() : this(
            new ArrayMemoryPool(ArrayPool<byte>.Shared!),
            new DefaultActiveSpeakers(),
            new ParticipantsHub().Capture(out var capturedHub),
            new TracksFactory().Capture(out var tracksFactory),
            IFfiHandleFactory.Default,
            IParticipantFactory.Default,
            ITrackPublicationFactory.Default,
            new DataPipe(),
            new MemoryRoomInfo(),
            new VideoStreams(capturedHub),
            new AudioStreams(capturedHub),
            null // AudioTracks will be created after Room construction
        )
        {
        }

        public Room(
            IMemoryPool memoryPool,
            IMutableActiveSpeakers activeSpeakers,
            IMutableParticipantsHub participantsHub,
            ITracksFactory tracksFactory,
            IFfiHandleFactory ffiHandleFactory,
            IParticipantFactory participantFactory,
            ITrackPublicationFactory trackPublicationFactory,
            IMutableDataPipe dataPipe,
            IMutableRoomInfo roomInfo,
            IVideoStreams videoStreams,
            IAudioStreams audioStreams,
            ILocalTracks? audioTracks
        )
        {
            this.memoryPool = memoryPool;
            this.activeSpeakers = activeSpeakers;
            this.participantsHub = participantsHub;
            this.tracksFactory = tracksFactory;
            this.ffiHandleFactory = ffiHandleFactory;
            this.participantFactory = participantFactory;
            this.trackPublicationFactory = trackPublicationFactory;
            this.dataPipe = dataPipe;
            this.roomInfo = roomInfo;
            this.videoStreams = videoStreams;
            this.audioStreams = audioStreams;
            this.localTracks = audioTracks ?? new LocalTracks(tracksFactory, this);
            dataPipe.Assign(participantsHub);
            videoStreams.AssignRoom(this);
            audioStreams.AssignRoom(this);
        }

        public void UpdateLocalMetadata(string metadata)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalMetadataRequest>();

            var localParticipant = participantsHub.LocalParticipant();

            request.request.LocalParticipantHandle = (uint)localParticipant.Handle.DangerousGetHandle();
            request.request.Metadata = metadata;

            localParticipant.UpdateMeta(metadata);

            using var response = request.Send();
        }

        public void SetLocalName(string name)
        {
            using var request = FFIBridge.Instance.NewRequest<SetLocalNameRequest>();

            var localParticipant = participantsHub.LocalParticipant();

            request.request.LocalParticipantHandle = (uint)localParticipant.Handle.DangerousGetHandle();
            request.request.Name = name;

            localParticipant.UpdateName(name);

            using var response = request.Send();
        }

        public async UniTask<Result> ConnectAsync(string url, string authToken, CancellationToken cancelToken, bool autoSubscribe)
        {
            using var response = FFIBridge.Instance.SendConnectRequest(url, authToken, autoSubscribe);
            FfiResponse res = response;
            return await new ConnectInstruction(res.Connect!.AsyncId, this, cancelToken)
                .AwaitWithSuccess();
        }

        public async UniTask DisconnectAsync(CancellationToken cancellationToken)
        {
            if (handle == null)
            {
                return;
            }

            using var response = FFIBridge.Instance.SendDisconnectRequest(handle);
            FfiResponse res = response;
            videoStreams.Free();
            audioStreams.Free();
            var instruction = new DisconnectInstruction(res.Disconnect!.AsyncId, this, cancellationToken);
            await instruction.AwaitWithSuccess();
            ffiHandleFactory.Release(handle);
            handle = null;
        }


        private void OnEventReceived(LiveKit.Proto.RoomEvent e)
        {
            if (e.RoomHandle != handleId)
            {
                Utils.Debug("Ignoring. Different Room... " + e);
                return;
            }

            Utils.Debug(
                $"Room {Info.Name} Event Type: {e.MessageCase}   ---> ({e.RoomHandle} <=> {handleId})");
            switch (e.MessageCase)
            {
                case LiveKit.Proto.RoomEvent.MessageOneofCase.RoomMetadataChanged:
                    roomInfo.UpdateMetadata(e.RoomMetadataChanged!.Metadata!);
                    RoomMetadataChanged?.Invoke(roomInfo.Metadata);
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.RoomSidChanged:
                    roomInfo.UpdateSid(e.RoomSidChanged!.Sid!);
                    RoomSidChanged?.Invoke(roomInfo.Sid);
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ParticipantMetadataChanged:
                {
                    var participant = this.ParticipantEnsured(e.ParticipantMetadataChanged!.ParticipantIdentity!);
                    participant.UpdateMeta(e.ParticipantMetadataChanged!.Metadata!);
                    participantsHub.NotifyParticipantUpdate(participant, UpdateFromParticipant.MetadataChanged);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ParticipantNameChanged:
                {
                    var participant = this.ParticipantEnsured(e.ParticipantNameChanged!.ParticipantIdentity!);
                    participant.UpdateName(e.ParticipantNameChanged!.Name!);
                    participantsHub.NotifyParticipantUpdate(participant, UpdateFromParticipant.NameChanged);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ParticipantAttributesChanged:
                {
                    var participant = this.ParticipantEnsured(e.ParticipantAttributesChanged!.ParticipantIdentity!);
                    participant.UpdateAttributes(e.ParticipantAttributesChanged.Attributes);
                    participantsHub.NotifyParticipantUpdate(participant, UpdateFromParticipant.AttributesChanged);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ParticipantConnected:
                {
                    var participant = participantFactory.NewRemote(
                        this,
                        e.ParticipantConnected!.Info!.Info!,
                        null,
                        ffiHandleFactory.NewFfiHandle(e.ParticipantConnected.Info.Handle!.Id
                        )
                    );
                    participantsHub.AddRemote(participant);
                    participantsHub.NotifyParticipantUpdate(participant, UpdateFromParticipant.Connected);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ParticipantDisconnected:
                {
                    var info = e.ParticipantDisconnected!;
                    var participant = participantsHub.RemoteParticipantEnsured(info.ParticipantIdentity!);
                    participantsHub.RemoveRemote(participant);
                    participantsHub.NotifyParticipantUpdate(participant, UpdateFromParticipant.Disconnected);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.LocalTrackUnpublished:
                {
                    if (Participants.LocalParticipant().Tracks.ContainsKey(e.LocalTrackUnpublished!.PublicationSid))
                    {
                        var publication = Participants.LocalParticipant()
                            .TrackPublication(e.LocalTrackUnpublished.PublicationSid!);
                        LocalTrackUnpublished?.Invoke(publication, Participants.LocalParticipant());
                    }
                    else
                    {
                        Utils.Debug(
                            "Unable to find local track after unpublish: " + e.LocalTrackUnpublished!.PublicationSid);
                    }
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.LocalTrackPublished:
                {
                    if (Participants.LocalParticipant().Tracks.ContainsKey(e.LocalTrackPublished!.TrackSid))
                    {
                        var publication = Participants.LocalParticipant()
                            .TrackPublication(e.LocalTrackPublished.TrackSid!);
                        LocalTrackPublished?.Invoke(publication, Participants.LocalParticipant());
                    }
                    else
                    {
                        LiveKit.Internal.Utils.Debug("Unable to find local track after publish: " + e.LocalTrackPublished.TrackSid);
                    }
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackPublished:
                {
                    var participant = participantsHub.RemoteParticipantEnsured(e.TrackPublished!.ParticipantIdentity!);
                    var publication = trackPublicationFactory.NewTrackPublication(e.TrackPublished.Publication!.Handle,
                        e.TrackPublished.Publication!.Info!);
                    participant.Publish(publication);
                    TrackPublished?.Invoke(publication, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackUnpublished:
                {
                    var participant =
                        participantsHub.RemoteParticipantEnsured(e.TrackUnpublished!.ParticipantIdentity!);
                    participant.UnPublish(e.TrackUnpublished.PublicationSid!, out var unpublishedTrack);
                    TrackUnpublished?.Invoke(unpublishedTrack, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackSubscribed:
                {
                    var info = e.TrackSubscribed!.Track!.Info!;
                    var participant = participantsHub.RemoteParticipantEnsured(e.TrackSubscribed.ParticipantIdentity!);
                    var publication = participant.TrackPublication(info.Sid!);
                    var trackHandle = ffiHandleFactory.NewFfiHandle((IntPtr)e.TrackSubscribed.Track.Handle.Id);

                    var track = tracksFactory.NewTrack(trackHandle, info, this, participant);
                    publication.UpdateTrack(track);
                    TrackSubscribed?.Invoke(track, publication, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackUnsubscribed:
                {
                    var participant = this.ParticipantEnsured(e.TrackUnsubscribed!.ParticipantIdentity!);
                    var publication = participant.TrackPublication(e.TrackUnsubscribed.TrackSid!);
                    publication.RemoveTrack(out var removedTrack);
                    TrackUnsubscribed?.Invoke(removedTrack!, publication, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackMuted:
                {
                    var trackMuted = e.TrackMuted!;
                    var participant = this.ParticipantEnsured(trackMuted.ParticipantIdentity!);
                    var publication = participant.TrackPublication(trackMuted.TrackSid!);
                    publication.UpdateMuted(true);
                    TrackMuted?.Invoke(publication, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackUnmuted:
                {
                    var trackUnmuted = e.TrackUnmuted!;
                    var participant = this.ParticipantEnsured(trackUnmuted.ParticipantIdentity!);
                    var publication = participant.TrackPublication(trackUnmuted.TrackSid!);
                    publication.UpdateMuted(false);
                    TrackUnmuted?.Invoke(publication, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ActiveSpeakersChanged:
                {
                    activeSpeakers.UpdateCurrentActives(e.ActiveSpeakersChanged!.ParticipantIdentities!);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ConnectionQualityChanged:
                {
                    var participant = this.ParticipantEnsured(e.ConnectionQualityChanged!.ParticipantIdentity!);
                    var quality = e.ConnectionQualityChanged.Quality;
                    participant.UpdateQuality(quality);
                    ConnectionQualityChanged?.Invoke(quality, participant);
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.DataPacketReceived:
                {
                    var dataReceivedPacket = e.DataPacketReceived;

                    if (dataReceivedPacket.ValueCase == DataPacketReceived.ValueOneofCase.User)
                    {
                        var dataInfo = dataReceivedPacket.User!.Data!;
                        using var memory = dataInfo.ReadAndDispose(memoryPool);
                        var participant = this.ParticipantEnsured(dataReceivedPacket.ParticipantIdentity!);
                        dataPipe.Notify(memory.Span(), participant, e.DataPacketReceived.User.Topic,
                            e.DataPacketReceived.Kind);
                    }
                }
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.ConnectionStateChanged:
                    roomInfo.UpdateConnectionState(e.ConnectionStateChanged!.State);
                    ConnectionStateChanged?.Invoke(e.ConnectionStateChanged!.State);
                    break;
                /*case LiveKit.Proto.RoomEvent.MessageOneofCase.Connected:
                    Connected?.Invoke(this);
                    break;*/
                case LiveKit.Proto.RoomEvent.MessageOneofCase.Eos:
                case LiveKit.Proto.RoomEvent.MessageOneofCase.Disconnected:
                    var disconnectReason = e.Disconnected?.Reason ?? DisconnectReason.UnknownReason;
                    ConnectionUpdated?.Invoke(this, ConnectionUpdate.Disconnected, disconnectReason);
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.Reconnecting:
                    ConnectionUpdated?.Invoke(this, ConnectionUpdate.Reconnecting);
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.Reconnected:
                    ConnectionUpdated?.Invoke(this, ConnectionUpdate.Reconnected);
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.None:
                    //ignore
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TrackSubscriptionFailed:
                    //ignore
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.E2EeStateChanged:
                    //ignore
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.TranscriptionReceived:
                    //ignore
                    break;
                case LiveKit.Proto.RoomEvent.MessageOneofCase.LocalTrackSubscribed:
                    //ignore
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(e), e.MessageCase.ToString());
            }
        }

        internal void OnConnect(
            FfiOwnedHandle roomHandle,
            RoomInfo info,
            OwnedParticipant participant,
            RepeatedField<ConnectCallback.Types.ParticipantWithTracks> participants)
        {
            LiveKit.Internal.Utils.Debug($"OnConnect.... {roomHandle.Id}  {participant.Handle!.Id}");
            LiveKit.Internal.Utils.Debug(info);

            activeSpeakers.Clear();
            participantsHub.Clear();

            handle = ffiHandleFactory.NewFfiHandle(roomHandle.Id);
            handleId = (ulong)handle.DangerousGetHandle();
            roomInfo.UpdateFromInfo(info);

            var selfParticipant = participantFactory.NewParticipant(
                participant.Info!,
                this,
                ffiHandleFactory.NewFfiHandle(participant.Handle.Id),
                Origin.Local
            );
            participantsHub.AssignLocal(selfParticipant);
            // Add already connected participant
            foreach (var p in participants)
            {
                var remote = participantFactory.NewRemote(
                    this,
                    p.Participant!.Info!, p.Publications,
                    ffiHandleFactory.NewFfiHandle(p.Participant.Handle!.Id)
                );
                participantsHub.AddRemote(remote);
            }

            FfiClient.Instance.RoomEventReceived += OnEventReceived;
            FfiClient.Instance.DisconnectReceived += OnDisconnectReceived;
            ConnectionUpdated?.Invoke(this, ConnectionUpdate.Connected);
        }

        private void OnDisconnectReceived(DisconnectCallback e)
        {
            FfiClient.Instance.DisconnectReceived -= OnDisconnectReceived;
            LiveKit.Internal.Utils.Debug($"OnDisconnect.... {e}");
        }

        public void OnDisconnect()
        {
            FfiClient.Instance.RoomEventReceived -= OnEventReceived;
            activeSpeakers.Clear();
        }

#else
        // WebGL implementation

        private static readonly TimeSpan POLL_DELAY = TimeSpan.FromMilliseconds(500);

        private readonly JsRoom jsRoom;

        private readonly JsRoomInfo roomInfo;
        private readonly NoActiveSpeakers activeSpeakers;
        private readonly JsParticipantsHub jsParticipantsHub;
        private readonly JsDataPipe jsDataPipe;
        
        private bool disposed;


        public event MetaDelegate? RoomMetadataChanged;
        public event SidDelegate? RoomSidChanged;

        public event ConnectionQualityChangeDelegate? ConnectionQualityChanged;
        public event ConnectionStateChangeDelegate? ConnectionStateChanged;
        public event ConnectionDelegate? ConnectionUpdated;
 
        public IRoomInfo Info => roomInfo;
        
        public IActiveSpeakers ActiveSpeakers => activeSpeakers;
        
        public IParticipantsHub Participants => jsParticipantsHub;
        
        public IDataPipe DataPipe => jsDataPipe;

        /* Js Implementation accepts options, can be expanded later
           public struct RoomOptions
           {
           [JsonProperty("adaptiveStream")]
           public bool AdaptiveStream;
           [JsonProperty("dynacast")]
           public bool Dynacast;
           [JsonProperty("audioCaptureDefaults")]
           public AudioCaptureOptions? AudioCaptureDefaults;
           [JsonProperty("videoCaptureDefaults")]
           public VideoCaptureOptions? VideoCaptureDefaults;
           [JsonProperty("publishDefaults")]
           public TrackPublishDefaults? PublishDefaults;
           [JsonProperty("stopLocalTrackOnUnpublish")]
           public bool StopLocalTrackOnUnpublish;
           [JsonProperty("expDisableLayerPause")]
           public bool ExpDisableLayerPause;
           }
        */
        public Room()
        {
            jsRoom = new JsRoom();

            // TODO (enhance) dispose to avoid the UniTask loop leakage (private ListenLoopAsync)?
            // From other side the current design suppose to reuse the room and won't dispose it
            roomInfo = JsRoomInfo.NewAndStart(jsRoom, newSid => RoomSidChanged?.Invoke(newSid));
            activeSpeakers = new NoActiveSpeakers(); // Not needed for this iteration
            jsParticipantsHub = new JsParticipantsHub(jsRoom);
            jsDataPipe = new JsDataPipe(jsRoom);

            disposed = false;

            WireEvents(jsRoom);
            PollStateAsync().Forget();
        }

        private void WireEvents(JsRoom r)
        {
            r.RoomMetadataChanged += (string metadata) => 
            {
                RoomMetadataChanged?.Invoke(metadata);
            };

            r.ConnectionQualityChanged += (LiveKit.ConnectionQuality quality, LiveKit.Participant participant) =>
            {
                LKConnectionQuality q = quality switch
                {
                    LiveKit.ConnectionQuality.Unknown => LKConnectionQuality.QualityLost,
                    LiveKit.ConnectionQuality.Poor => LKConnectionQuality.QualityPoor,
                    LiveKit.ConnectionQuality.Good => LKConnectionQuality.QualityGood,
                    LiveKit.ConnectionQuality.Excellent => LKConnectionQuality.QualityExcellent,
                };

                LKParticipant wrap =  new LKParticipant(participant);
                ConnectionQualityChanged?.Invoke(q, wrap);
            };

            r.Reconnecting += () => ConnectionUpdated?.Invoke(this, ConnectionUpdate.Reconnecting, null);
            r.Reconnected += () => ConnectionUpdated?.Invoke(this, ConnectionUpdate.Reconnected, null);
            r.Disconnected += (LiveKit.DisconnectReason? reason) =>
            {
                LKDisconnectReason? lkDisconnectReason = null;
                if (reason != null)
                {
                    lkDisconnectReason = reason.Value switch
                    {
                        DisconnectReason.UNKNOWN_REASON => LKDisconnectReason.UnknownReason,
                        DisconnectReason.CLIENT_INITIATED => LKDisconnectReason.ClientInitiated,
                        DisconnectReason.DUPLICATE_IDENTITY => LKDisconnectReason.DuplicateIdentity,
                        DisconnectReason.SERVER_SHUTDOWN => LKDisconnectReason.ServerShutdown,
                        DisconnectReason.PARTICIPANT_REMOVED => LKDisconnectReason.ParticipantRemoved,
                        DisconnectReason.ROOM_DELETED => LKDisconnectReason.RoomDeleted,
                        DisconnectReason.STATE_MISMATCH => LKDisconnectReason.StateMismatch,
                        DisconnectReason.UNRECOGNIZED => LKDisconnectReason.UnknownReason, // Weird, but Web version won't represent all reasons

                    };
                }

                ConnectionUpdated?.Invoke(this, ConnectionUpdate.Disconnected, lkDisconnectReason);
            };
        }

        private async UniTaskVoid PollStateAsync()
        {
            LKConnectionState lastState = roomInfo.ConnectionState;
            while (disposed == false)
            {
                await UniTask.Delay(POLL_DELAY);
                LKConnectionState newState = roomInfo.ConnectionState;
                if (lastState != newState)
                {
                    ConnectionStateChanged?.Invoke(newState);
                    lastState = newState;
                }
            }
        }

        public void UpdateLocalMetadata(string metadata)
        {
            jsRoom.LocalParticipant?.SetMetadata(metadata);
        }

        public void SetLocalName(string name)
        {
            jsRoom.LocalParticipant?.SetName(name);
        }

        public async UniTask<Result> ConnectAsync(string url, string authToken, CancellationToken cancelToken, bool autoSubscribe)
        {
            try
            {
                RoomConnectOptions options = new RoomConnectOptions();
                options.AutoSubscribe = autoSubscribe;
                LiveKit.ConnectOperation operation = jsRoom.Connect(url, authToken, options);
                await operation;

                if (operation.IsError)
                {
                    global::LiveKit.JSError error = operation.Error;
                    return Result.ErrorResult($"Cannot connect to room {url} due an js error: {error.Name} - {error.Message}");
                }

                ConnectionUpdated?.Invoke(this, ConnectionUpdate.Connected, null);
                return Result.SuccessResult();
            }
            catch (Exception e)
            {
                return Result.ErrorResult($"Cannot connect to room {url} due an exception: {e}");
            }
        }

        public async UniTask DisconnectAsync(CancellationToken cancellationToken)
        {
            jsRoom.Disconnect();
        }

#endif

    
    }

    internal static class Extensions
    {
        //Captures value to reuse it withing the scope
        public static T Capture<T>(this T value, out T captured)
        {
            captured = value;
            return value;
        }
    }
}
