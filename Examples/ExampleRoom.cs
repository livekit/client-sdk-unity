#if !UNITY_WEBGL

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using Cysharp.Threading.Tasks;
using Examples;
using LiveKit;
using LiveKit.Audio;
using LiveKit.Proto;
using LiveKit.Rooms;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Streaming;
using LiveKit.Rooms.Streaming.Audio;
using LiveKit.Rooms.Streaming.Video;
using LiveKit.Rooms.Tracks;
using LiveKit.Rooms.VideoStreaming;
using LiveKit.RtcSources.Video;
using LiveKit.Runtime.Scripts.Audio;
using RichTypes;
using UnityEngine.Audio;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

public class ExampleRoom : MonoBehaviour
{
    private Room? m_Room;
    private MicrophoneRtcAudioSource? microphoneSource;

    private readonly Dictionary<StreamKey, LivekitAudioSource> audioSourcesMap = new();
    private readonly Dictionary<StreamKey, LivekitVideoSource> videoSourcesMap = new();
    private readonly List<(Room room, IRtcAudioSource bot)> botInstances = new();

    private readonly List<(ITrack track, RtcVideoSource videoSource)> publishedVideoTracks = new();

    public Dropdown MicrophoneDropdownMenu;
    public Button DisconnectButton;
    [Space] public bool microphonePlaybackToSpeakers;
    [Space] public AudioMixer audioMixer;
    public string audioHandleName;
    [SerializeField] private Toggle enableAudioSources;
    [SerializeField] private RectTransform videoParent;

    [Header("Bot Speakers")] [SerializeField]
    private TextAsset botTokens;

    [SerializeField] private AudioClip botClip;
    [SerializeField] private List<BotParticipant> bots = new();
    [SerializeField] private BotCaptureMode botCaptureMode = BotCaptureMode.FromMicrophone;


    private void Start()
    {
        StartAsync().Forget();
    }

    private void Update()
    {
        foreach (var (remoteParticipantIdentity, _) in m_Room.Participants.RemoteParticipantIdentities())
        {
            var participant = m_Room.Participants.RemoteParticipant(remoteParticipantIdentity)!;

            // Add new tracks
            foreach (var (key, value) in participant.Tracks)
            {
                StreamKey streamKey = new StreamKey(remoteParticipantIdentity, key!);
                switch (value.Kind)
                {
                    case TrackKind.KindUnknown:
                        break;
                    case TrackKind.KindAudio:
                    {
                        Weak<AudioStream> track = m_Room.AudioStreams.ActiveStream(streamKey);
                        if (track.Resource.Has)
                        {
                            var audioStream = track.Resource.Value;
                            if (audioSourcesMap.ContainsKey(streamKey) == false)
                            {
                                var livekitAudioSource = LivekitAudioSource.New(true);
                                livekitAudioSource.Construct(track);
                                livekitAudioSource.Play();
                                Debug.Log($"Participant {remoteParticipantIdentity} added track {key}");
                                audioSourcesMap[streamKey] = livekitAudioSource;
                            }
                        }
                    }
                        break;
                    case TrackKind.KindVideo:
                    {
                        Weak<IVideoStream> track = m_Room.VideoStreams.ActiveStream(streamKey);
                        if (track.Resource.Has)
                        {
                            if (videoSourcesMap.ContainsKey(streamKey) == false)
                            {
                                var livekitVideoSource = LivekitVideoSource.New(videoParent, true);
                                livekitVideoSource.Construct(track);
                                livekitVideoSource.Play();
                                Debug.Log($"Participant {remoteParticipantIdentity} added track {key}");
                                videoSourcesMap[streamKey] = livekitVideoSource;
                            }
                        }
                    }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Remove unpublished
            using PooledObject<List<StreamKey>> _ = ListPool<StreamKey>.Get(out List<StreamKey> removeCandidates);
            foreach (KeyValuePair<StreamKey, LivekitVideoSource> source in videoSourcesMap)
            {
                Weak<IVideoStream> stream = m_Room.VideoStreams.ActiveStream(source.Key);
                Option<IVideoStream> resource = stream.Resource;
                if (resource.Has == false)
                {
                    removeCandidates!.Add(source.Key);
                }
            }

            foreach (var candidate in removeCandidates!)
            {
                if (videoSourcesMap.Remove(candidate, out var source))
                {
                    Debug.Log($"Remove remote video track: {candidate}");
                    Destroy(source!.gameObject);
                }
            }

            removeCandidates.Clear();
            foreach (KeyValuePair<StreamKey, LivekitAudioSource> source in audioSourcesMap)
            {
                Weak<AudioStream> stream = m_Room.AudioStreams.ActiveStream(source.Key);
                Option<AudioStream> resource = stream.Resource;
                if (resource.Has == false)
                {
                    removeCandidates.Add(source.Key);
                }
            }

            foreach (var candidate in removeCandidates)
            {
                if (audioSourcesMap.Remove(candidate, out var source))
                {
                    Debug.Log($"Remove remote audio track: {candidate}");
                    Destroy(source!.gameObject);
                }
            }
        }
    }

    private async UniTaskVoid StartAsync()
    {
        enableAudioSources.onValueChanged.AddListener(enable =>
        {
            foreach (KeyValuePair<StreamKey, LivekitAudioSource> pair in audioSourcesMap)
            {
                if (enable) pair.Value.Play();
                else pair.Value.Stop();
            }
        });

        var tokens = botTokens ? botTokens.text!.Split('\n')! : Array.Empty<string>();
        bots = tokens.Select(t => new BotParticipant { token = t, audioClip = botClip }).ToList();

        // New Room must be called when WebGL assembly is loaded
        m_Room = new Room();

        // Setup the callbacks before connecting to the Room
        m_Room.Participants.UpdatesFromParticipant += (p, update) =>
        {
            if (update == UpdateFromParticipant.Connected)
                Debug.Log($"Participant connected: {p.Sid}");
        };


        var c = await m_Room.ConnectAsync(
            PlayerPrefs.GetString(nameof(JoinMenu.LivekitURL))!,
            PlayerPrefs.GetString(nameof(JoinMenu.RoomToken))!,
            CancellationToken.None,
            true
        );

        if (c.Success == false)
        {
            Debug.Log($"Failed to connect to the room !: {c.ErrorMessage}");
            return;
        }

        Debug.Log("Connected to the room");

        DisconnectButton.onClick.AddListener(() =>
        {
            m_Room.DisconnectAsync(CancellationToken.None);
            SceneManager.LoadScene("JoinScene", LoadSceneMode.Single);
        });

        // Microphone usage

        MicrophoneSelection? selection = null;
        Result<MicrophoneSelection> result = MicrophoneDropdown.CurrentMicrophoneSelection();
        if (result.Success)
        {
            selection = result.Value;
        }

        Debug.Log($"Selected Microphone: {selection?.name}");

        Result<MicrophoneRtcAudioSource> sourceResult = MicrophoneRtcAudioSource.New(
            selection,
            (audioMixer, audioHandleName),
            microphonePlaybackToSpeakers
        );
        if (sourceResult.Success == false)
        {
            Debug.LogError($"Cannot create microphone source: {sourceResult.ErrorMessage}");
            return;
        }

        microphoneSource = sourceResult.Value;
        microphoneSource.Start();

        MicrophoneDropdown.Bind(MicrophoneDropdownMenu, microphoneSource);

        var audioTrack = m_Room.LocalTracks.CreateAudioTrack("own", microphoneSource);
        var trackOptions = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding
            {
                MaxBitrate = 124000
            },
            Source = TrackSource.SourceMicrophone
        };
        var publishTask = m_Room.Participants.LocalParticipant()
            .PublishTrack(audioTrack, trackOptions, CancellationToken.None);
        await UniTask.WaitUntil(() => publishTask.IsDone);

        // Camera usage
        await PublishNewVideoTrackAsync();
        Debug.Log("Init finished");
    }

    [ContextMenu(nameof(PublishNewVideoTrack))]
    public void PublishNewVideoTrack()
    {
        PublishNewVideoTrackAsync().Forget();
    }

    private async UniTask PublishNewVideoTrackAsync()
    {
        Result<WebCameraVideoInput> inputResult = WebCameraVideoInput.NewDefault();
        if (inputResult.Success == false)
        {
            Debug.LogError($"Cannot create video source: {inputResult.ErrorMessage}");
            return;
        }

        RtcVideoSource videoSource = new(inputResult.Value);
        videoSource.Start();

        var videoTrack = m_Room.LocalTracks.CreateVideoTrack("own", videoSource);
        var videoTrackOptions = new TrackPublishOptions
        {
            VideoEncoding = new VideoEncoding
            {
                MaxFramerate = 60
            },
            Source = TrackSource.SourceCamera
        };
        var publishVideoTask = m_Room.Participants.LocalParticipant()
            .PublishTrack(videoTrack, videoTrackOptions, CancellationToken.None);
        await UniTask.WaitUntil(() => publishVideoTask.IsDone);
        publishedVideoTracks.Add((videoTrack, videoSource));
        Debug.Log($"Published video track, current count: {publishedVideoTracks.Count}");
    }

    [ContextMenu(nameof(UnpublishLastVideoTrack))]
    private void UnpublishLastVideoTrack()
    {
        if (publishedVideoTracks.Count == 0)
        {
            return;
        }

        int index = publishedVideoTracks.Count - 1;
        (ITrack track, RtcVideoSource videoSource) last = publishedVideoTracks[index];
        publishedVideoTracks.RemoveAt(index);
        m_Room?.Participants.LocalParticipant().UnpublishTrack(last.track, stopOnUnpublish: true);
        Debug.Log($"Unpublished video track, current count: {publishedVideoTracks.Count}");
    }

    private void OnDestroy()
    {
        DisconnectBotsAsync().Forget();
        m_Room?.DisconnectAsync(destroyCancellationToken);
        microphoneSource?.Dispose();
    }

    [ContextMenu(nameof(AddBots))]
    public void AddBots()
    {
        AddBotsInternal().Forget();
    }

    private async UniTask DisconnectBotsAsync()
    {
        await UniTask.WhenAll(
            botInstances
                .Select(r => r.room!.DisconnectAsync(destroyCancellationToken))!
                .ToArray()
        );
        foreach (var botInstance in botInstances)
        {
            botInstance.bot.Dispose();
        }

        botInstances.Clear();
    }

    private async UniTaskVoid AddBotsInternal()
    {
        await DisconnectBotsAsync();

        string url = PlayerPrefs.GetString(nameof(JoinMenu.LivekitURL))!;

        int id = 0;
        List<UniTask> tasks = bots.Select(b => SetupAsync(b, ++id))!.ToList();
        await UniTask.WhenAll(tasks);
        return;

        async UniTask SetupAsync(BotParticipant botParticipant, int botId)
        {
            Room room = new Room();
            await room.ConnectAsync(url, botParticipant.token, destroyCancellationToken, false);

            IRtcAudioSource source = NewBotAudioSource(botCaptureMode, botId, botParticipant.audioClip);
            source.Start();

            var myTrack = room.LocalTracks.CreateAudioTrack("own", source);
            var trackOptions = new TrackPublishOptions
            {
                AudioEncoding = new AudioEncoding
                {
                    MaxBitrate = 124000
                },
                Source = TrackSource.SourceMicrophone
            };
            var publishTask = room.Participants.LocalParticipant()
                .PublishTrack(myTrack, trackOptions, CancellationToken.None);
            await UniTask.WaitUntil(() => publishTask.IsDone);

            Debug.Log($"Bot publish finished: {botId}");

            botInstances.Add((room, source));
        }
    }

    private IRtcAudioSource NewBotAudioSource(BotCaptureMode captureMode, int id, AudioClip? audioClip)
    {
        if (captureMode is BotCaptureMode.FromMicrophone)
        {
            MicrophoneSelection? selection = null;
            Result<MicrophoneSelection> result = MicrophoneDropdown.CurrentMicrophoneSelection();
            if (result.Success)
            {
                selection = result.Value;
            }

            Debug.Log($"Selected Microphone: {selection?.name}");
            Result<MicrophoneRtcAudioSource> sourceResult = MicrophoneRtcAudioSource.New(
                selection,
                (audioMixer, audioHandleName),
                microphonePlaybackToSpeakers
            );
            if (sourceResult.Success == false)
            {
                Debug.LogError($"Cannot create microphone source: {sourceResult.ErrorMessage}");
                throw new Exception();
            }

            var botSource = sourceResult.Value;
            botSource.Start();

            MicrophoneDropdown.Bind(MicrophoneDropdownMenu, botSource);
            return botSource;
        }

        return AudioClipRtcAudioSource.New(audioClip!);
    }
}

[Serializable]
public struct BotParticipant
{
    public string token;
    public AudioClip audioClip;
}

public enum BotCaptureMode
{
    FromClip,
    FromMicrophone
}

#endif
