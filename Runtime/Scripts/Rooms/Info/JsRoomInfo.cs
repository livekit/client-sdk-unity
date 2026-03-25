#if UNITY_WEBGL && !UNITY_EDITOR

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

        public LKConnectionState ConnectionState => LKConnectionStateUtils.FromJsState(room.State);
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
                    // TODO more precise Sid retrieve
                    instance.Sid = jsObject.ToString(); // JSObject wierdly won't expose any properties;

                    if (lastSid != instance.Sid)
                    {
                        instance.triggerOnSidChanged(instance.Sid);
                    }
                }

                await UniTask.Delay(POLL_DELAY);
            }
        }

    }
}

#endif
