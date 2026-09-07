// Bridges android.media.AudioDeviceCallback to C#.
//
// AudioDeviceCallback is an abstract class and Unity's AndroidJavaProxy can only
// implement Java interfaces, so the subclass has to live in Java. This class does
// nothing but forward device add/remove notifications to the Listener interface,
// which AndroidRouteController implements on the C# side through an AndroidJavaProxy.
// Unity compiles this source as part of the Gradle build (Android plugin, source form).
package io.livekit.unity;

import android.media.AudioDeviceCallback;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

public final class LiveKitAudioDeviceMonitor extends AudioDeviceCallback {
    // Diagnostic logging for the on-device verification of this bridge: shows whether
    // Android invokes the callback and whether the forward into C# returns.
    private static final String TAG = "LiveKit";

    /** Implemented on the C# side via AndroidJavaProxy. */
    public interface Listener {
        /**
         * Invoked on the main looper whenever audio output devices were added to or
         * removed from the system. The counts only include sinks (output devices); a
         * change that touches inputs alone is not reported, because input routing on
         * Android follows the communication device and never needs a re-evaluation.
         */
        void onAudioDevicesChanged(int addedSinks, int removedSinks);
    }

    private final AudioManager audioManager;
    private final Listener listener;
    private boolean registered;

    public LiveKitAudioDeviceMonitor(AudioManager audioManager, Listener listener) {
        this.audioManager = audioManager;
        this.listener = listener;
    }

    /**
     * Starts receiving callbacks on the main looper. Android delivers one immediate
     * onAudioDevicesAdded with the currently connected devices right after registering.
     */
    public synchronized void register() {
        if (registered) {
            return;
        }
        audioManager.registerAudioDeviceCallback(this, new Handler(Looper.getMainLooper()));
        registered = true;
        Log.i(TAG, "AudioDeviceMonitor: registered; outputs currently enumerable: "
                + audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS).length);
    }

    public synchronized void unregister() {
        if (!registered) {
            return;
        }
        audioManager.unregisterAudioDeviceCallback(this);
        registered = false;
    }

    @Override
    public void onAudioDevicesAdded(AudioDeviceInfo[] addedDevices) {
        int sinks = countSinks(addedDevices);
        Log.i(TAG, "AudioDeviceMonitor: onAudioDevicesAdded total=" + length(addedDevices)
                + " sinks=" + sinks + " thread=" + Thread.currentThread().getName());
        if (sinks > 0) {
            listener.onAudioDevicesChanged(sinks, 0);
            Log.i(TAG, "AudioDeviceMonitor: forwarded added=" + sinks + " to C#");
        }
    }

    @Override
    public void onAudioDevicesRemoved(AudioDeviceInfo[] removedDevices) {
        int sinks = countSinks(removedDevices);
        Log.i(TAG, "AudioDeviceMonitor: onAudioDevicesRemoved total=" + length(removedDevices)
                + " sinks=" + sinks + " thread=" + Thread.currentThread().getName());
        if (sinks > 0) {
            listener.onAudioDevicesChanged(0, sinks);
            Log.i(TAG, "AudioDeviceMonitor: forwarded removed=" + sinks + " to C#");
        }
    }

    private static int length(AudioDeviceInfo[] devices) {
        return devices == null ? 0 : devices.length;
    }

    private static int countSinks(AudioDeviceInfo[] devices) {
        if (devices == null) {
            return 0;
        }
        int count = 0;
        for (AudioDeviceInfo device : devices) {
            if (device.isSink()) {
                count++;
            }
        }
        return count;
    }
}
