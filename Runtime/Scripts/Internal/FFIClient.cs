using System;
using System.IO;
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
    internal delegate void ConnectReceivedDelegate(uint reqId, ConnectResponse res);
    internal delegate void RoomEventReceivedDelegate(RoomEvent e);
    internal delegate void TrackEventReceivedDelegate(TrackEvent e);
    internal delegate void ParticipantEventReceivedDelegate(ParticipantEvent e);

    internal sealed class FFIClient
    {
        private static readonly Lazy<FFIClient> _instance = new Lazy<FFIClient>(() => new FFIClient());
        public static FFIClient Instance
        {
            get => _instance.Value;
        }

        // https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Scripting/UnitySynchronizationContext.cs
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void GetMainContext()
        {
            Instance._context = SynchronizationContext.Current;
        }

        private SynchronizationContext _context;
        private uint _reqId = 0;

        public event ConnectReceivedDelegate ConnectReceived;
        public event RoomEventReceivedDelegate RoomEventReceived;
        public event TrackEventReceivedDelegate TrackEventReceived;
        public event ParticipantEventReceivedDelegate ParticipantEventReceived;

        private FFIClient()
        {
            FFICallbackDelegate callback = FFICallback;

            var configureReq = new InitializeRequest();
            unchecked
            {
                configureReq.CallbackPtr = (ulong)Marshal.GetFunctionPointerForDelegate(callback).ToInt64();
            }

            var request = new FFIRequest();
            request.Configure = configureReq;
            SendRequest(request);
        }

        public uint SendRequest(FFIRequest request)
        {
            request.ReqId = _reqId++;
            var data = request.ToByteArray(); // TODO(theomonnom): Avoid more allocations
            FFIRequest(data, data.Length);
            return request.ReqId;
        }

        unsafe void FFICallback(IntPtr data, int size)
        {
            var stream = new CodedInputStream(new UnmanagedMemoryStream((byte*)data.ToPointer(), size));
            var response = new FFIResponse();
            response.MergeFrom(stream);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            _context.Post((resp) =>
            {
                var response = resp as FFIResponse;
                switch (response.MessageCase)
                {
                    case FFIResponse.MessageOneofCase.AsyncConnect:
                        ConnectReceived?.Invoke(response.ReqId, response.AsyncConnect);
                        break;
                    case FFIResponse.MessageOneofCase.RoomEvent:
                        RoomEventReceived?.Invoke(response.RoomEvent);
                        break;
                    case FFIResponse.MessageOneofCase.TrackEvent:
                        TrackEventReceived?.Invoke(response.TrackEvent);
                        break;
                    case FFIResponse.MessageOneofCase.ParticipantEvent:
                        ParticipantEventReceived?.Invoke(response.ParticipantEvent);
                        break;
                }
            }, response);
        }

        [DllImport("livekit_ffi", CallingConvention = CallingConvention.Cdecl, EntryPoint = "livekit_ffi_request")]
        static extern void FFIRequest(byte[] data, int size);
    }
}

