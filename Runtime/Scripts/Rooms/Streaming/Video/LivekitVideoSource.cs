using LiveKit.Internal;
using LiveKit.Rooms.VideoStreaming;
using RichTypes;
using UnityEngine;
using UnityEngine.UI;

namespace LiveKit.Rooms.Streaming.Video
{
    [RequireComponent(typeof(RawImage))]
    public class LivekitVideoSource : MonoBehaviour
    {
        private static ulong counter;

        private Weak<IVideoStream> stream = Weak<IVideoStream>.Null;
        private RawImage image = null!;
        private bool isPlaying;

        public static LivekitVideoSource New(RectTransform parent, bool explicitName = false)
        {
            var gm = new GameObject();
            gm.transform.SetParent(parent);
            var source = gm.AddComponent<LivekitVideoSource>()!;
            gm.AddComponent<RectTransform>();
            source.image = gm.AddComponent<RawImage>()!;
            if (explicitName) source.name = $"{nameof(LivekitVideoSource)}_{counter++}";


            return source;
        }

        public void Construct(Weak<IVideoStream> audioStream)
        {
            stream = audioStream;
            if (image == null)
            {
                image = GetComponent<RawImage>();
                if (image == null) Utils.Error("Cannot assign raw image");
            }
        }

        public void Free()
        {
            stream = Weak<IVideoStream>.Null;
        }

        public void Play()
        {
            isPlaying = true;
        }

        public void Stop()
        {
            isPlaying = false;
        }

        private void Update()
        {
            if (isPlaying == false) return;

            Option<IVideoStream> resource = stream.Resource;
            if (resource.Has)
            {
                IVideoStream videoStream = resource.Value;
                Texture2D? lastFrame = videoStream.DecodeLastFrame();
                image.texture = lastFrame ? lastFrame! : null!;
            }
            else
            {
                image.texture = null!;
            }
        }
    }
}