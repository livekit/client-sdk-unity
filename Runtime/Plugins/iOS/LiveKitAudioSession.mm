/*
 * Copyright 2026 LiveKit, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>

#include <stdlib.h>
#include <string.h>

// This plugin coordinates the single shared AVAudioSession with WebRTC's iOS
// Audio Device Module (ADM). WebRTC ships an RTCAudioSession proxy that, left in
// its default "automatic" mode, reconfigures the category/route and *deactivates*
// the session whenever a call's playout/recording starts and stops. That fights
// with Unity/FMOD: on join the app's other audio (e.g. ambient music) is rerouted
// to the earpiece and attenuated, and on hang-up the session is deactivated out
// from under Unity so its audio dies.
//
// To make playout stable and keep Unity audio alive across call state, we put
// RTCAudioSession into MANUAL mode and have the app own the session:
//   * We hold exactly one permanent activation (setActive:YES). Because
//     RTCAudioSession ref-counts activation, WebRTC's per-call setActive:YES/NO
//     only cycles the count and never actually deactivates the hardware session.
//   * The category/mode/options are derived from a session STATE machine driven
//     from C# (PlatformAudio knows the call's recording state) plus a
//     speaker-vs-earpiece preference (see the table below). Every apply is also
//     mirrored into WebRTC's RTCAudioSessionConfiguration snapshot so the ADM
//     re-applies the same config on its own restarts.
//   * The speaker preference is expressed via the session MODE only (VideoChat
//     routes to the loudspeaker by default, VoiceChat to the receiver), never via
//     overrideOutputAudioPort, so connected wired/Bluetooth devices always win.
//   * The VPIO voice-processing unit (hardware AEC/AGC/NS) is gated by
//     isAudioEnabled. It defaults to YES so call audio works out of the box (the
//     unit still only initializes once a call actually has an audio track, so
//     pre-call audio is unaffected). Callers can toggle it via
//     LiveKit_SetAudioEnabled -- e.g. OFF on hang-up so the unit stops between
//     calls while our held activation keeps the session alive for Unity.
//   * Backgrounding interrupts the session and WebRTC stops its audio unit. On
//     foreground, RTCAudioSession's own recovery restarts the unit exactly once,
//     with no retry -- and Unity/FMOD restarts *its* audio around the same moment,
//     reconfiguring the shared session (observed with Unity 6). Whoever loses that
//     race stays broken, so we observe foreground/interruption-end ourselves and,
//     after Unity's restart has settled, re-assert the current state's config and
//     cycle isAudioEnabled to force a clean rebuild of the audio unit.
//   * Route changes (headset plug/unplug, Bluetooth connect, mode switches) are
//     observed via AVAudioSessionRouteChangeNotification and forwarded to C#
//     through a registered callback so the SDK can raise its DevicesChanged event.
//
// Session state table (state is set from C# via LiveKit_SetSessionState):
//
//   state          category       mode                     options
//   0 idle         PlayAndRecord  Default                  BT | A2DP | MixWithOthers
//                                                          | DefaultToSpeaker*
//   1 playout-only PlayAndRecord  Default                  same as idle
//   2 recording    PlayAndRecord  VideoChat (speaker) /    BT | A2DP
//                                 VoiceChat (earpiece)
//
//   *DefaultToSpeaker only while the speaker is preferred. In the recording state
//    the speaker preference is carried by the mode alone. Idle and playout-only
//    share a config: PlayAndRecord stays because the ADM initializes its VPIO unit
//    with input disabled for playout-only (InitPlayOrRecord(false)) but nothing
//    guarantees VPIO under the Playback category; mode Default + MixWithOthers is
//    the music-friendliest config the ADM demonstrably supports. The states stay
//    distinct so the mapping can diverge without touching the C# driver.
//
// RTCAudioSession lives inside the statically-linked liblivekit_ffi; we reach it
// dynamically via NSClassFromString + a protocol-typed id so this file never
// creates a link-time dependency on the class. If the class can't be found we
// fall back to configuring AVAudioSession directly (legacy behavior).

/// Minimal subset of WebRTC's RTCAudioSession that we message dynamically.
@protocol LiveKitRTCAudioSession <NSObject>
@property(nonatomic, assign) BOOL useManualAudio;
@property(nonatomic, assign) BOOL isAudioEnabled;
@property(nonatomic, readonly) int activationCount;
- (void)lockForConfiguration;
- (void)unlockForConfiguration;
- (BOOL)setActive:(BOOL)active error:(NSError**)outError;
- (BOOL)setCategory:(AVAudioSessionCategory)category
               mode:(AVAudioSessionMode)mode
            options:(AVAudioSessionCategoryOptions)options
              error:(NSError**)outError;
@end

/// Minimal subset of WebRTC's RTCAudioSessionConfiguration (the snapshot the ADM
/// re-applies on its own restarts), messaged dynamically like RTCAudioSession.
@protocol LiveKitRTCAudioSessionConfiguration <NSObject>
@property(nonatomic, strong) NSString* category;
@property(nonatomic, assign) AVAudioSessionCategoryOptions categoryOptions;
@property(nonatomic, strong) NSString* mode;
@end

/// Session states, mirroring PlatformAudio's driver in C#. Do not renumber.
enum {
    kLiveKitSessionStateIdle = 0,
    kLiveKitSessionStatePlayoutOnly = 1,
    kLiveKitSessionStateRecording = 2,
};

typedef void (*LiveKitRouteChangeCallback)(void);

// Tracks whether *we* currently hold the one app-owned activation, so we add and
// release it exactly once regardless of how many times configure/restore run.
static BOOL s_liveKitHoldsActivation = NO;

// Snapshot of the AVAudioSession configuration as it was the first time LiveKit
// touched the session (i.e. whatever Unity set up from its iOS Player Settings).
// Captured lazily in LiveKit_ConfigureAudioSessionForVoIP and re-applied by
// LiveKit_RestoreDefaultAudioSession when the last PlatformAudio is disposed.
static BOOL s_hasCachedState = NO;
static NSString* s_cachedCategory = nil;
static NSString* s_cachedMode = nil;
static AVAudioSessionCategoryOptions s_cachedCategoryOptions = 0;

// YES between configure and restore: gates the foreground-recovery observers so
// they no-op once LiveKit has handed the session back to the app.
static BOOL s_liveKitConfigured = NO;
// The isAudioEnabled state the caller wants (updated by LiveKit_SetAudioEnabled),
// so recovery knows whether to restart the audio unit after re-asserting config.
static BOOL s_audioDesired = NO;
// Coalesces recovery requests (didBecomeActive and interruption-ended both fire
// on foreground) into one delayed pass.
static BOOL s_recoveryPending = NO;

// The state machine inputs (see the table above). The defaults match what a fresh
// PlatformAudio pushes right after construction, so the config applied by
// configure is already the one the C# driver expects.
static int s_sessionState = kLiveKitSessionStatePlayoutOnly;
static BOOL s_speakerPreferred = YES;

// Invoked (on the main queue) whenever the audio route changes, so the C# side
// can re-query the route and raise DevicesChanged.
static LiveKitRouteChangeCallback s_routeChangeCallback = NULL;

// AllowBluetooth was renamed AllowBluetoothHFP in the iOS 26 SDK; same guard the
// WebRTC fork uses. The speaker preference never rides on these options.
#if defined(__IPHONE_26_0) && __IPHONE_OS_VERSION_MAX_ALLOWED >= __IPHONE_26_0
static const AVAudioSessionCategoryOptions kLiveKitBluetoothOptions =
    AVAudioSessionCategoryOptionAllowBluetoothHFP |
    AVAudioSessionCategoryOptionAllowBluetoothA2DP;
#else
static const AVAudioSessionCategoryOptions kLiveKitBluetoothOptions =
    AVAudioSessionCategoryOptionAllowBluetooth |
    AVAudioSessionCategoryOptionAllowBluetoothA2DP;
#endif

/// Returns WebRTC's shared RTCAudioSession if it's present in the linked binary,
/// or nil if the class can't be found (in which case callers use AVAudioSession).
static id<LiveKitRTCAudioSession> LiveKit_RTCSession() {
    Class cls = NSClassFromString(@"RTCAudioSession");
    if (!cls || ![cls respondsToSelector:@selector(sharedInstance)]) {
        return nil;
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Warc-performSelector-leaks"
    return (id<LiveKitRTCAudioSession>)[cls performSelector:@selector(sharedInstance)];
#pragma clang diagnostic pop
}

static NSString* LiveKit_DesiredMode() {
    if (s_sessionState == kLiveKitSessionStateRecording) {
        return s_speakerPreferred ? AVAudioSessionModeVideoChat
                                  : AVAudioSessionModeVoiceChat;
    }
    return AVAudioSessionModeDefault;
}

static AVAudioSessionCategoryOptions LiveKit_DesiredOptions() {
    AVAudioSessionCategoryOptions options = kLiveKitBluetoothOptions;
    if (s_sessionState != kLiveKitSessionStateRecording) {
        options |= AVAudioSessionCategoryOptionMixWithOthers;
        // Mode Default routes PlayAndRecord to the receiver; outside a call there
        // is no mode that both prefers the speaker and leaves music processing
        // alone, so here -- and only here -- the preference rides on an option.
        if (s_speakerPreferred) {
            options |= AVAudioSessionCategoryOptionDefaultToSpeaker;
        }
    }
    return options;
}

/// Mirrors our category/mode/options into WebRTC's RTCAudioSessionConfiguration
/// snapshot so the ADM re-applies the same config whenever it (re)configures the
/// session itself (audio unit init, interruption recovery).
static void LiveKit_MirrorWebRTCConfiguration(NSString* mode,
                                              AVAudioSessionCategoryOptions options) {
    Class cls = NSClassFromString(@"RTCAudioSessionConfiguration");
    if (!cls || ![cls respondsToSelector:@selector(webRTCConfiguration)] ||
        ![cls respondsToSelector:@selector(setWebRTCConfiguration:)]) {
        return;
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Warc-performSelector-leaks"
    id<LiveKitRTCAudioSessionConfiguration> config =
        (id<LiveKitRTCAudioSessionConfiguration>)[cls performSelector:@selector(webRTCConfiguration)];
    if (config == nil) {
        return;
    }
    config.category = AVAudioSessionCategoryPlayAndRecord;
    config.mode = mode;
    config.categoryOptions = options;
    [cls performSelector:@selector(setWebRTCConfiguration:) withObject:config];
#pragma clang diagnostic pop
}

/// Applies the config derived from (s_sessionState, s_speakerPreferred) to the
/// session and mirrors it into the WebRTC snapshot. Logs expected vs. actual so
/// device tests can see who won when something else reconfigures the session.
static void LiveKit_ApplySessionConfig(NSString* reason) {
    NSString* mode = LiveKit_DesiredMode();
    AVAudioSessionCategoryOptions options = LiveKit_DesiredOptions();

    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();
    NSError* error = nil;
    if (rtc != nil) {
        [rtc lockForConfiguration];
        if (![rtc setCategory:AVAudioSessionCategoryPlayAndRecord
                         mode:mode
                      options:options
                        error:&error] || error) {
            NSLog(@"LiveKit: failed to apply session config (%@): %@",
                  reason, error.localizedDescription);
        }
        [rtc unlockForConfiguration];
    } else {
        AVAudioSession* session = [AVAudioSession sharedInstance];
        if (![session setCategory:AVAudioSessionCategoryPlayAndRecord
                             mode:mode
                          options:options
                            error:&error] || error) {
            NSLog(@"LiveKit: failed to apply session config (%@): %@",
                  reason, error.localizedDescription);
        }
    }

    LiveKit_MirrorWebRTCConfiguration(mode, options);

    AVAudioSession* current = [AVAudioSession sharedInstance];
    NSLog(@"LiveKit: session config (%@): state=%d speakerPreferred=%d expected mode=%@ options=%lu"
           " -> actual category=%@ mode=%@ options=%lu",
          reason, s_sessionState, s_speakerPreferred, mode, (unsigned long)options,
          current.category, current.mode, (unsigned long)current.categoryOptions);
}

/// Re-applies the current state's config and reactivates the session, then
/// cycles isAudioEnabled to force WebRTC to rebuild its VPIO audio unit. Runs on
/// a delay so it lands after Unity/FMOD's own foreground audio restart (which is
/// itself delayed and can reconfigure the shared session underneath WebRTC's
/// one-shot, no-retry interruption recovery -- the Unity 6 focus race).
static void LiveKit_ScheduleSessionRecovery() {
    if (!s_liveKitConfigured || s_recoveryPending) {
        return;
    }
    s_recoveryPending = YES;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.5 * NSEC_PER_SEC)),
                   dispatch_get_main_queue(), ^{
        s_recoveryPending = NO;
        if (!s_liveKitConfigured) {
            return;  // restored/disposed while the recovery was pending
        }

        AVAudioSession* session = [AVAudioSession sharedInstance];
        // "Who won" the focus race: what Unity/FMOD left the session as.
        NSLog(@"LiveKit: foreground recovery; session before re-assert: category=%@ mode=%@ options=%lu",
              session.category, session.mode, (unsigned long)session.categoryOptions);

        LiveKit_ApplySessionConfig(@"foreground recovery");

        // Reactivate directly on AVAudioSession: the OS deactivated the hardware
        // session during the interruption, but RTCAudioSession's activation
        // ref-count still includes our held activation, so reactivating through
        // the proxy would double-count it.
        NSError* error = nil;
        if (![session setActive:YES error:&error] || error) {
            NSLog(@"LiveKit: recovery failed to reactivate session: %@", error.localizedDescription);
        }

        // Cycle isAudioEnabled to rebuild the VPIO unit against the re-asserted
        // session (WebRTC's own foreground restart may have failed, or been undone
        // by Unity's). Harmless when no call audio is active: with playout and
        // recording uninitialized WebRTC ignores the change.
        id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();
        if (rtc != nil && s_audioDesired) {
            rtc.isAudioEnabled = NO;
            rtc.isAudioEnabled = YES;
        }

        NSLog(@"LiveKit: foreground recovery done (audioDesired=%d, activationCount=%d)",
              s_audioDesired, rtc != nil ? rtc.activationCount : -1);
    });
}

/// Registers app-lifetime observers for foreground/interruption recovery and for
/// route-change forwarding. Registered once on first configure; the handlers
/// no-op while LiveKit is not configured.
static void LiveKit_RegisterLifecycleObserversIfNeeded() {
    static BOOL s_observersRegistered = NO;
    if (s_observersRegistered) {
        return;
    }
    s_observersRegistered = YES;

    NSNotificationCenter* center = [NSNotificationCenter defaultCenter];
    [center addObserverForName:UIApplicationDidBecomeActiveNotification
                        object:nil
                         queue:[NSOperationQueue mainQueue]
                    usingBlock:^(NSNotification* note) {
        LiveKit_ScheduleSessionRecovery();
    }];
    [center addObserverForName:AVAudioSessionInterruptionNotification
                        object:nil
                         queue:[NSOperationQueue mainQueue]
                    usingBlock:^(NSNotification* note) {
        NSNumber* type = note.userInfo[AVAudioSessionInterruptionTypeKey];
        if (type.unsignedIntegerValue == AVAudioSessionInterruptionTypeEnded) {
            LiveKit_ScheduleSessionRecovery();
        }
    }];
    [center addObserverForName:AVAudioSessionRouteChangeNotification
                        object:nil
                         queue:[NSOperationQueue mainQueue]
                    usingBlock:^(NSNotification* note) {
        if (!s_liveKitConfigured) {
            return;
        }
        NSNumber* reason = note.userInfo[AVAudioSessionRouteChangeReasonKey];
        NSMutableArray<NSString*>* outputs = [NSMutableArray array];
        for (AVAudioSessionPortDescription* port in
             [AVAudioSession sharedInstance].currentRoute.outputs) {
            [outputs addObject:[NSString stringWithFormat:@"%@ (%@)", port.portName, port.portType]];
        }
        NSLog(@"LiveKit: route changed (reason=%lu) outputs=%@",
              (unsigned long)reason.unsignedIntegerValue,
              [outputs componentsJoinedByString:@", "]);
        LiveKitRouteChangeCallback callback = s_routeChangeCallback;
        if (callback != NULL) {
            callback();
        }
    }];
}

/// Captures the current audio session category/mode/options exactly once, before
/// LiveKit reconfigures the session for VoIP. Subsequent calls are no-ops so the
/// snapshot always reflects the pristine, pre-LiveKit (Unity-configured) state.
static void LiveKit_CacheSessionStateIfNeeded() {
    if (s_hasCachedState) {
        return;
    }
    AVAudioSession* session = [AVAudioSession sharedInstance];
    // copy so the strings persist for the app lifetime regardless of ARC/MRC.
    s_cachedCategory = [session.category copy];
    s_cachedMode = [session.mode copy];
    s_cachedCategoryOptions = session.categoryOptions;
    s_hasCachedState = YES;
    NSLog(@"LiveKit: cached audio session state: category=%@, mode=%@, options=%lu",
          s_cachedCategory, s_cachedMode, (unsigned long)s_cachedCategoryOptions);
}

/// Maps an AVAudioSessionPort type to the C# AudioOutputKind numbering
/// (Unknown=0, Earpiece=1, Speaker=2, WiredHeadset=3, Bluetooth=4, Usb=5,
/// HearingAid=6). Do not renumber. AVAudioSession has no dedicated hearing-aid
/// port type, so 6 is never produced here; AirPlay/HDMI/CarAudio and other
/// unroutable-by-us ports map to Unknown.
static int LiveKit_OutputKindForPortType(NSString* portType) {
    if ([portType isEqualToString:AVAudioSessionPortBuiltInReceiver]) return 1;
    if ([portType isEqualToString:AVAudioSessionPortBuiltInSpeaker]) return 2;
    if ([portType isEqualToString:AVAudioSessionPortHeadphones]) return 3;
    if ([portType isEqualToString:AVAudioSessionPortBluetoothA2DP] ||
        [portType isEqualToString:AVAudioSessionPortBluetoothHFP] ||
        [portType isEqualToString:AVAudioSessionPortBluetoothLE]) return 4;
    if ([portType isEqualToString:AVAudioSessionPortUSBAudio]) return 5;
    return 0;
}

extern "C" {

/// Configures the iOS audio session for VoIP/WebRTC use and takes app ownership
/// of the shared AVAudioSession.
///
/// This applies the config for the current session state (playout-only for a
/// fresh PlatformAudio; see the state table at the top of this file), puts
/// RTCAudioSession into manual mode, and holds a single permanent activation so
/// WebRTC never deactivates the session on its own.
///
/// Call this before creating PlatformAudio. Call audio is enabled by default, so
/// no further call is required for it to work; use LiveKit_SetAudioEnabled(false)
/// to stop the VPIO unit between calls (e.g. on hang-up).
void LiveKit_ConfigureAudioSessionForVoIP() {
    // Snapshot the pristine (Unity Player Settings) session before we change it.
    LiveKit_CacheSessionStateIfNeeded();

    LiveKit_RegisterLifecycleObserversIfNeeded();
    s_liveKitConfigured = YES;
    s_audioDesired = YES;  // mirrors the isAudioEnabled default set below

    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();

    // Manual mode: WebRTC won't activate/deactivate the session on its own, and
    // won't initialize the VPIO unit until we grant permission via isAudioEnabled
    // (set below). This is what lets us own activation and gate the unit. Set
    // before the first apply so the ADM never races the initial configuration.
    if (rtc != nil) {
        rtc.useManualAudio = YES;
    }

    LiveKit_ApplySessionConfig(@"configure");

    if (rtc == nil) {
        // RTCAudioSession unavailable: activate AVAudioSession directly (legacy).
        AVAudioSession* session = [AVAudioSession sharedInstance];
        NSError* error = nil;
        if (![session setActive:YES error:&error] || error) {
            NSLog(@"LiveKit: Failed to activate audio session: %@", error.localizedDescription);
            return;
        }
        NSLog(@"LiveKit: Audio session configured (AVAudioSession fallback)");
        return;
    }

    // Hold exactly one app-owned activation. RTCAudioSession ref-counts activation,
    // so WebRTC's balanced setActive:YES/NO during a call never drops the real
    // session below active while we hold this.
    if (!s_liveKitHoldsActivation) {
        [rtc lockForConfiguration];
        NSError* error = nil;
        if ([rtc setActive:YES error:&error] && !error) {
            s_liveKitHoldsActivation = YES;
        } else {
            NSLog(@"LiveKit: Failed to activate audio session: %@", error.localizedDescription);
        }
        [rtc unlockForConfiguration];
    }

    // Grant WebRTC permission to initialize its audio unit by default so call audio
    // works without an explicit LiveKit_SetAudioEnabled(true). The unit is only
    // actually created once a call has an audio track, so pre-call audio is
    // unaffected. Callers may still disable it (e.g. on hang-up) via
    // LiveKit_SetAudioEnabled(false).
    rtc.isAudioEnabled = YES;

    NSLog(@"LiveKit: Audio session configured for VoIP (manual mode, activationCount=%d)",
          rtc.activationCount);
}

/// Enables or disables WebRTC's VPIO audio unit while the app keeps ownership of
/// the session. Pass true when a call connects and false when it ends.
///
/// This is only effective in manual mode (set up by LiveKit_ConfigureAudioSessionForVoIP).
/// Disabling on hang-up stops incoming/outgoing call audio and the VPIO processing,
/// but leaves the session active (via the app's held activation), so Unity audio
/// keeps playing.
void LiveKit_SetAudioEnabled(bool enabled) {
    s_audioDesired = enabled ? YES : NO;
    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();
    if (rtc == nil) {
        return;
    }
    rtc.isAudioEnabled = enabled ? YES : NO;
    NSLog(@"LiveKit: isAudioEnabled=%@ (activationCount=%d)", enabled ? @"YES" : @"NO", rtc.activationCount);
}

/// Sets whether the loudspeaker is preferred over the earpiece for the built-in
/// outputs and live-applies the resulting config (see the state table). External
/// devices (wired, Bluetooth) always take priority over both; this only decides
/// where audio goes when no external device is connected.
void LiveKit_SetSpeakerPreferred(bool preferred) {
    BOOL value = preferred ? YES : NO;
    if (s_speakerPreferred == value) {
        return;
    }
    s_speakerPreferred = value;
    if (s_liveKitConfigured) {
        LiveKit_ApplySessionConfig(@"speaker preference");
    }
}

/// Sets the session state (0 idle, 1 playout-only, 2 recording; see the state
/// table) and live-applies the resulting config. Driven from C#: PlatformAudio
/// knows whether recording is active and whether call audio is wanted.
void LiveKit_SetSessionState(int state) {
    if (state < kLiveKitSessionStateIdle || state > kLiveKitSessionStateRecording) {
        NSLog(@"LiveKit: ignoring unknown session state %d", state);
        return;
    }
    if (s_sessionState == state) {
        return;
    }
    s_sessionState = state;
    if (s_liveKitConfigured) {
        LiveKit_ApplySessionConfig(@"session state");
    }
}

/// Registers (or clears, with NULL) the callback invoked on the main queue
/// whenever the audio route changes. The callback carries no payload; the C#
/// side re-queries LiveKit_GetCurrentOutputRoutes.
void LiveKit_SetRouteChangeCallback(LiveKitRouteChangeCallback callback) {
    s_routeChangeCallback = callback;
}

/// Returns the current output route as newline-separated "kind\tname\tuid"
/// entries (kind per LiveKit_OutputKindForPortType). The caller must release the
/// returned buffer with LiveKit_FreeRouteString.
char* LiveKit_GetCurrentOutputRoutes() {
    NSMutableString* result = [NSMutableString string];
    for (AVAudioSessionPortDescription* port in
         [AVAudioSession sharedInstance].currentRoute.outputs) {
        [result appendFormat:@"%d\t%@\t%@\n",
                             LiveKit_OutputKindForPortType(port.portType),
                             port.portName ?: @"",
                             port.UID ?: @""];
    }
    return strdup(result.UTF8String);
}

/// Frees a buffer returned by LiveKit_GetCurrentOutputRoutes.
void LiveKit_FreeRouteString(char* str) {
    free(str);
}

/// Restores the audio session Unity had before LiveKit touched it (or the ambient
/// category if LiveKit never configured it), relinquishes the app-owned activation
/// and manual mode, and reactivates the session so Unity audio output resumes.
/// Call this when the last PlatformAudio is disposed.
void LiveKit_RestoreDefaultAudioSession() {
    // Stand down the foreground-recovery observers before touching the session.
    s_liveKitConfigured = NO;
    s_audioDesired = NO;
    // Reset the state machine to the defaults a fresh PlatformAudio expects, so a
    // later reconfigure starts from the same config it will be driven to.
    s_sessionState = kLiveKitSessionStatePlayoutOnly;
    s_speakerPreferred = YES;

    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();

    if (rtc != nil) {
        // Stop the VPIO unit and release our activation before handing control back.
        rtc.isAudioEnabled = NO;
        if (s_liveKitHoldsActivation) {
            NSError* error = nil;
            if (![rtc setActive:NO error:&error] || error) {
                NSLog(@"LiveKit: Failed to deactivate audio session: %@", error.localizedDescription);
            }
            s_liveKitHoldsActivation = NO;
        }
        rtc.useManualAudio = NO;
    }

    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;
    if (s_hasCachedState) {
        if (![session setCategory:s_cachedCategory
                             mode:s_cachedMode
                          options:s_cachedCategoryOptions
                            error:&error] || error) {
            NSLog(@"LiveKit: Failed to restore cached audio session (category=%@, mode=%@): %@",
                  s_cachedCategory, s_cachedMode, error.localizedDescription);
        }
    } else {
        // Configure was never called, so we have nothing to restore to; fall back
        // to the ambient category.
        [session setCategory:AVAudioSessionCategoryAmbient error:&error];
        if (error) {
            NSLog(@"LiveKit: Failed to restore default audio session: %@", error.localizedDescription);
        }
    }

    // Hand an active session back to Unity so its audio output resumes.
    error = nil;
    if (![session setActive:YES error:&error] || error) {
        NSLog(@"LiveKit: Failed to reactivate audio session: %@", error.localizedDescription);
    }
}

}
