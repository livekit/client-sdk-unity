#if UNITY_WEBGL

using System;
using Cysharp.Threading.Tasks;
using DCL.LiveKit.Public;
using JsRoom = LiveKit.Room;
using JsConnectionState = LiveKit.ConnectionState;
using LiveKit;

namespace LiveKit.Rooms.Info
{
    // TODO might be disposable
    public class JsRoomInfo : IRoomInfo, IDisposable
    {
        private readonly JsRoom room;
        private readonly Action<string> triggerOnSidChanged;

        private static readonly TimeSpan POLL_DELAY = TimeSpan.FromMilliseconds(500);

        // bool is okay, JS is single threaded
        private bool disposed;

        public LKConnectionState ConnectionState => FromJsState(room.State);
        public string Sid { get; private set; }
        public string Name => room.Name;
        public string Metadata => room.Metadata;

        private JsRoomInfo(JsRoom jsRoom, Action<string> triggerOnSidChanged)
        {
            this.room = jsRoom;
            this.triggerOnSidChanged = triggerOnSidChanged;
        }

        public void Dispose()
        {
            disposed = true;
        }

        public static JsRoomInfo NewAndStart(JsRoom jsRoom, Action<string> triggerOnSidChanged)
        {
            JsRoomInfo instance = new JsRoomInfo(jsRoom, triggerOnSidChanged);
            ListenLoopAsync(instance).Forget();
            return instance;
        }

        private static async UniTaskVoid ListenLoopAsync(JsRoomInfo instance)
        {
            while (instance.disposed == false)
            {
                global::LiveKit.JSPromise<JSObject> promise = instance.room.GetSid();
                await promise;

                if (promise.IsError)
                {
                    LiveKit.Internal.Utils.Error($"Error during ListenLoopAsync");
                }
                else
                {
                    string lastSid = instance.Sid;

                    JSObject jsObject = promise.ResolveValue;
                    // TODO more presice Sid retrieve
                    instance.Sid = jsObject.ToString(); // JSObject wierdly won't expose any properties;

                    if (lastSid != instance.Sid)
                    {
                        instance.triggerOnSidChanged(instance.Sid);
                    }
                }

                await UniTask.Delay(POLL_DELAY);
            }
        }

        private static LKConnectionState FromJsState(JsConnectionState state)
        {
            return state switch
            {
                JsConnectionState.Disconnected => LKConnectionState.ConnDisconnected,
                JsConnectionState.Connecting => LKConnectionState.ConnDisconnected, // Yes, PROTO doesn't support the 'connecting' state, thus map it to disconnected, 'reconnected' state won't fit because it may have specific logic
                JsConnectionState.Connected => LKConnectionState.ConnConnected,
                JsConnectionState.Reconnecting => LKConnectionState.ConnReconnecting,
            };
        }
    }
}

#endif
