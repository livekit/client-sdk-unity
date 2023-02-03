using System;
using System.Runtime.InteropServices;
using LiveKit.Proto;
using UnityEngine;
using Google.Protobuf;
using System.Threading;

namespace LiveKit.Internal
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FFICallbackDelegate(IntPtr data, int size);

    // Events
    internal delegate void ConnectReceivedDelegate(ulong async_id, ConnectEvent e);
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);
    internal delegate void TrackEventReceivedDelegate(TrackEvent e);
    internal delegate void ParticipantEventReceivedDelegate(ParticipantEvent e);

    internal sealed class FFIClient
    {
        private static readonly Lazy<FFIClient> _instance = new Lazy<FFIClient>(() => new FFIClient());
        public static FFIClient Instance => _instance.Value;

        // https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void GetMainContext()
        {
            Instance._context = SynchronizationContext.Current;
        }

        private SynchronizationContext _context;

        public event ConnectReceivedDelegate ConnectReceived;
        public event RoomEventReceivedDelegate RoomEventReceived;
        public event TrackEventReceivedDelegate TrackEventReceived;
        public event ParticipantEventReceivedDelegate ParticipantEventReceived;

        private FFIClient()
        {
            FFICallbackDelegate callback = FFICallback;

            var configureReq = new InitializeRequest();
            configureReq.EventCallbackPtr = (ulong)Marshal.GetFunctionPointerForDelegate(callback);

            var request = new FFIRequest();
            request.Configure = configureReq;
            SendRequest(request);
        }

        public FFIResponse SendRequest(FFIRequest request)
        {
            var data = request.ToByteArray(); // TODO(theomonnom): Avoid more allocations
            var respPtr = NativeMethods.FFIRequest(data, (uint)data.Length, out FFIHandle handle);

            FFIResponse response;
            unsafe
            {
                // The first 4 bytes are the length of the byte array
                var respSize = Marshal.ReadInt32(respPtr);
                var respData = new Span<byte>(IntPtr.Add(respPtr, 4).ToPointer(), respSize);
                response = FFIResponse.Parser.ParseFrom(respData);
            }

            handle.Dispose();
            return response;
        }

        unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FFIResponse.Parser.ParseFrom(respData);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            _context.Post((resp) =>
            {
                var response = resp as FFIEvent;
                switch (response.MessageCase)
                {
                    case FFIEvent.MessageOneofCase.ConnectEvent:
                        ConnectReceived?.Invoke(response.AsyncId, response.ConnectEvent);
                        break;
                    case FFIEvent.MessageOneofCase.RoomEvent:
                        RoomEventReceived?.Invoke(response.RoomEvent);
                        break;
                    case FFIEvent.MessageOneofCase.TrackEvent:
                        TrackEventReceived?.Invoke(response.TrackEvent);
                        break;
                    case FFIEvent.MessageOneofCase.ParticipantEvent:
                        ParticipantEventReceived?.Invoke(response.ParticipantEvent);
                        break;
                }
            }, response);
        }
    }
}

