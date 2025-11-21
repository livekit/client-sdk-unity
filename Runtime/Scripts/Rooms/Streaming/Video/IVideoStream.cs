using System;
using UnityEngine;

namespace LiveKit.Rooms.VideoStreaming
{
    public interface IVideoStream : IDisposable
    {
        /// <summary>
        /// Main thread only
        /// </summary>
        Texture2D? DecodeLastFrame();
    }
}