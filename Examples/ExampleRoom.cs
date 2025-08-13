using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiveKit.Audio;
using LiveKit.Proto;
using LiveKit.Rooms;
using LiveKit.Rooms.Participants;
using LiveKit.Rooms.Streaming.Audio;
using UnityEngine.SceneManagement;

public class ExampleRoom : MonoBehaviour
{
    private Room? m_Room;
    private MicrophoneRtcAudioSource? microphoneSource;

    private readonly Dictionary<IAudioStream, LivekitAudioSource> sourcesMap = new();

    public Button DisconnectButton;

    private void Start()
    {
        StartAsync().Forget();
    }

    private void Update()
    {
        foreach (var (remoteParticipantIdentity, _) in m_Room.Participants.RemoteParticipantIdentities())
        {
            var participant = m_Room.Participants.RemoteParticipant(remoteParticipantIdentity)!;
            foreach (var (key, value) in participant.Tracks)
            {
                var track = m_Room.AudioStreams.ActiveStream(remoteParticipantIdentity, key!);
                if (track != null)
                {
                    if (track.TryGetTarget(out var audioStream))
                    {
                        if (sourcesMap.ContainsKey(audioStream) == false)
                        {
                            var livekitAudioSource = LivekitAudioSource.New(true);
                            livekitAudioSource.Construct(track);
                            livekitAudioSource.Play();
                            Debug.Log($"Participant {remoteParticipantIdentity} added track {key}");
                            sourcesMap[audioStream] = livekitAudioSource;
                        }
                    }
                }
            }
        }
    }

    private async UniTaskVoid StartAsync()
    {
        // New Room must be called when WebGL assembly is loaded
        m_Room = new Room();

        // Setup the callbacks before connecting to the Room
        m_Room.Participants.UpdatesFromParticipant += (p, update) =>
        {
            if (update == UpdateFromParticipant.Connected)
                Debug.Log($"Participant connected: {p.Sid}");
        };

        var c = await m_Room.ConnectAsync(JoinMenu.LivekitURL, JoinMenu.RoomToken, CancellationToken.None, true);

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
        var sourceResult = MicrophoneRtcAudioSource.New();
        if (sourceResult.Success == false)
        {
            Debug.LogError($"Cannot create microphone source: {sourceResult.ErrorMessage}");
            return;
        }

        microphoneSource = sourceResult.Value;
        microphoneSource.Start();

        var myTrack = m_Room.AudioTracks.CreateAudioTrack("own", microphoneSource);
        var trackOptions = new TrackPublishOptions
        {
            AudioEncoding = new AudioEncoding
            {
                MaxBitrate = 124000
            },
            Source = TrackSource.SourceMicrophone
        };
        var publishTask = m_Room.Participants.LocalParticipant()
            .PublishTrack(myTrack, trackOptions, CancellationToken.None);
        await UniTask.WaitUntil(() => publishTask.IsDone);
        Debug.Log("Init finished");
    }

    private void OnDestroy()
    {
        m_Room?.DisconnectAsync(CancellationToken.None);
        microphoneSource?.Dispose();
    }
}