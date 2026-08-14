#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using LiveKit.Internal;

namespace LiveKit
{
    /// <summary>
    /// iOS routing backend over the LiveKitAudioSession.mm plugin. The OS owns output
    /// route selection on iOS, so this backend does not pick devices: it reduces
    /// <see cref="PlatformAudio.OutputPreference"/> to the speaker-vs-earpiece relative
    /// order (applied as the audio session mode by the plugin; external devices always
    /// take priority over both built-ins), reports the session's current output route
    /// as the playout device list, and raises <see cref="DevicesChanged"/> from the
    /// plugin's route-change observation. <see cref="SelectOutput"/> throws: apps that
    /// want explicit device picking should present the system route picker
    /// (AVRoutePickerView).
    ///
    /// All plugin P/Invoke for route observation stays inside this class; the session
    /// state machine itself is driven by <see cref="PlatformAudio"/> (which knows the
    /// recording state) through <see cref="IOSAudioSessionHelper"/>.
    /// </summary>
    internal sealed class IosRouteController : IRouteController
    {
        private delegate void RouteChangeDelegate();

        [DllImport("__Internal")]
        private static extern void LiveKit_SetRouteChangeCallback(RouteChangeDelegate callback);

        [DllImport("__Internal")]
        private static extern void LiveKit_SetSpeakerPreferred([MarshalAs(UnmanagedType.I1)] bool preferred);

        [DllImport("__Internal")]
        private static extern IntPtr LiveKit_GetCurrentOutputRoutes();

        [DllImport("__Internal")]
        private static extern void LiveKit_FreeRouteString(IntPtr routes);

        // The native callback slot is registered once for the app lifetime (matching
        // the plugin's app-lifetime notification observers) and fans out to the live
        // controllers; keeping the delegate in a static field pins it for the native
        // side. Instances add and remove themselves under StaticGate.
        private static readonly object StaticGate = new object();
        private static readonly List<IosRouteController> LiveControllers = new List<IosRouteController>();
        private static readonly RouteChangeDelegate NativeRouteChanged = OnNativeRouteChanged;
        private static bool _callbackRegistered;

        private readonly object _gate = new object();
        // The FFI recording list (a single placeholder for the OS default input),
        // captured once: route changes never affect it and re-querying the FFI from
        // the route callback would be wasted work.
        private readonly List<AudioDevice> _recordingSnapshot;
        private string _lastSignature;
        private bool _disposed;

        public event Action<IReadOnlyList<AudioDevice>, IReadOnlyList<AudioDevice>> DevicesChanged;

        internal IosRouteController(PlatformAudio owner, IReadOnlyList<AudioOutputKind> initialPreference)
        {
            _recordingSnapshot = owner.GetDevicesViaFfi().Recording;

            ApplyOutputPreference(initialPreference);
            _lastSignature = Signature(QueryCurrentOutputs());

            lock (StaticGate)
            {
                LiveControllers.Add(this);
                if (!_callbackRegistered)
                {
                    LiveKit_SetRouteChangeCallback(NativeRouteChanged);
                    _callbackRegistered = true;
                }
            }
        }

        public (List<AudioDevice> Recording, List<AudioDevice> Playout) GetDevices()
        {
            return (new List<AudioDevice>(_recordingSnapshot), QueryCurrentOutputs());
        }

        public void ApplyOutputPreference(IReadOnlyList<AudioOutputKind> ranked)
        {
            // Reduce the ranked list per the PAR-019 precedence rule: the only part of
            // the ranking iOS can express is whether Speaker outranks Earpiece.
            var speaker = -1;
            var earpiece = -1;
            for (var i = 0; i < ranked.Count; i++)
            {
                if (ranked[i] == AudioOutputKind.Speaker) speaker = i;
                else if (ranked[i] == AudioOutputKind.Earpiece) earpiece = i;
            }
            var speakerPreferred = speaker >= 0 && (earpiece < 0 || speaker < earpiece);
            LiveKit_SetSpeakerPreferred(speakerPreferred);
        }

        public void SelectOutput(AudioDevice device)
        {
            throw new NotSupportedException(
                "SelectOutput is not supported on iOS: the OS owns output route selection. " +
                "Present the system route picker (AVRoutePickerView) instead, or use " +
                "OutputPreference / IsSpeakerOutputPreferred for the built-in outputs.");
        }

        public void ClearOutputOverride()
        {
            // No override can exist on iOS: SelectOutput throws.
        }

        public void Dispose()
        {
            lock (StaticGate)
            {
                LiveControllers.Remove(this);
            }
            lock (_gate)
            {
                _disposed = true;
            }
        }

        /// <summary>
        /// Native route-change entry point, invoked by the plugin on the iOS main
        /// queue (not the Unity main thread; <see cref="PlatformAudio"/> marshals the
        /// public event).
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(RouteChangeDelegate))]
        private static void OnNativeRouteChanged()
        {
            IosRouteController[] controllers;
            lock (StaticGate)
            {
                controllers = LiveControllers.ToArray();
            }
            foreach (var controller in controllers)
                controller.HandleRouteChanged();
        }

        private void HandleRouteChanged()
        {
            List<AudioDevice> playout;
            lock (_gate)
            {
                if (_disposed) return;

                playout = QueryCurrentOutputs();
                var signature = Signature(playout);
                if (signature == _lastSignature) return;
                _lastSignature = signature;
            }

            DevicesChanged?.Invoke(playout, new List<AudioDevice>(_recordingSnapshot));
        }

        /// <summary>
        /// The current output route reported by the audio session. On iOS this is the
        /// active route (usually one device), not an enumeration of every reachable
        /// device — AVAudioSession exposes no such list for outputs.
        /// </summary>
        private static List<AudioDevice> QueryCurrentOutputs()
        {
            var devices = new List<AudioDevice>();

            var routesPtr = LiveKit_GetCurrentOutputRoutes();
            if (routesPtr == IntPtr.Zero) return devices;

            string routes;
            try
            {
                routes = Marshal.PtrToStringUTF8(routesPtr);
            }
            finally
            {
                LiveKit_FreeRouteString(routesPtr);
            }
            if (string.IsNullOrEmpty(routes)) return devices;

            foreach (var line in routes.Split('\n'))
            {
                if (line.Length == 0) continue;
                var fields = line.Split('\t');
                if (fields.Length != 3)
                {
                    Utils.Warning($"IosRouteController: malformed route entry '{line}'");
                    continue;
                }

                var kind = int.TryParse(fields[0], out var rawKind)
                           && Enum.IsDefined(typeof(AudioOutputKind), rawKind)
                    ? (AudioOutputKind)rawKind
                    : AudioOutputKind.Unknown;
                devices.Add(new AudioDevice
                {
                    Index = (uint)devices.Count,
                    Name = fields[1],
                    Guid = fields[2],
                    Kind = kind,
                    // Everything in the current route is live output by definition.
                    IsSelected = true,
                });
            }

            return devices;
        }

        private static string Signature(List<AudioDevice> playout)
        {
            var builder = new StringBuilder();
            foreach (var device in playout)
                builder.Append(device.Guid).Append('\u001f').Append((int)device.Kind).Append('\u001e');
            return builder.ToString();
        }
    }
}
#endif
