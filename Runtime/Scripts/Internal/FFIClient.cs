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
    internal delegate void ConnectReceivedDelegate(ulong asyncId, ConnectEvent e);
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

            FFIResponse response;
            unsafe
            {
                var handle = NativeMethods.FFINewRequest(data, data.Length, out byte* dataPtr, out int dataLen);
                response = FFIResponse.Parser.ParseFrom(new Span<byte>(dataPtr, dataLen));
                handle.Dispose();
            }

            return response;
        }


        [AOT.MonoPInvokeCallback(typeof(FFICallbackDelegate))]
        static unsafe void FFICallback(IntPtr data, int size)
        {
            var respData = new Span<byte>(data.ToPointer(), size);
            var response = FFIEvent.Parser.ParseFrom(respData);

            // Run on the main thread, the order of execution is guaranteed by Unity
            // It uses a Queue internally
            var client = FFIClient.Instance;
            client._context.Post((resp) =>
               {
                   var response = resp as FFIEvent;
                   switch (response.MessageCase)
                   {
                       case FFIEvent.MessageOneofCase.ConnectEvent:
                           client.ConnectReceived?.Invoke(response.AsyncId, response.ConnectEvent);
                           break;
                       case FFIEvent.MessageOneofCase.RoomEvent:
                           client.RoomEventReceived?.Invoke(response.RoomEvent);
                           break;
                       case FFIEvent.MessageOneofCase.TrackEvent:
                           client.TrackEventReceived?.Invoke(response.TrackEvent);
                           break;
                       case FFIEvent.MessageOneofCase.ParticipantEvent:
                           client.ParticipantEventReceived?.Invoke(response.ParticipantEvent);
                           break;
                   }
               }, response);
        }
    }
}

