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
//   * We set the category once (PlayAndRecord + VideoChat mode). VideoChat routes
//     to the loudspeaker by default (while still honoring connected wired/Bluetooth
//     headphones), so WebRTC re-applying its own config keeps output on the speaker
//     instead of the earpiece. We deliberately do NOT force the speaker via
//     overrideOutputAudioPort, which would override plugged-in headphones.
//   * The VPIO voice-processing unit (hardware AEC/AGC/NS) is gated by
//     isAudioEnabled. It defaults to YES so call audio works out of the box (the
//     unit still only initializes once a call actually has an audio track, so
//     pre-call audio is unaffected). Callers can toggle it via
//     LiveKit_SetAudioEnabled -- e.g. OFF on hang-up so the unit stops between
//     calls while our held activation keeps the session alive for Unity.
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

// Tracks whether *we* currently hold the one app-owned activation, so we add and
// release it exactly once regardless of how many times configure/restore run.
static BOOL s_liveKitHoldsActivation = NO;

static const AVAudioSessionCategoryOptions kLiveKitCategoryOptions =
    AVAudioSessionCategoryOptionDefaultToSpeaker |
    AVAudioSessionCategoryOptionAllowBluetooth |
    AVAudioSessionCategoryOptionAllowBluetoothA2DP;

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

extern "C" {

/// Configures the iOS audio session for VoIP/WebRTC use and takes app ownership
/// of the shared AVAudioSession.
///
/// This sets AVAudioSessionCategoryPlayAndRecord with VideoChat mode (which routes
/// to the loudspeaker by default and enables the VPIO Voice Processing IO unit for
/// hardware AEC/AGC/NS), puts RTCAudioSession into manual mode, and holds a single
/// permanent activation so WebRTC never deactivates the session on its own.
///
/// Call this before creating PlatformAudio. Call audio is enabled by default, so
/// no further call is required for it to work; use LiveKit_SetAudioEnabled(false)
/// to stop the VPIO unit between calls (e.g. on hang-up).
void LiveKit_ConfigureAudioSessionForVoIP() {
    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();

    if (rtc == nil) {
        // RTCAudioSession unavailable: configure AVAudioSession directly (legacy).
        AVAudioSession* session = [AVAudioSession sharedInstance];
        NSError* error = nil;
        if (![session setCategory:AVAudioSessionCategoryPlayAndRecord
                             mode:AVAudioSessionModeVideoChat
                          options:kLiveKitCategoryOptions
                            error:&error] || error) {
            NSLog(@"LiveKit: Failed to configure audio session: %@", error.localizedDescription);
            return;
        }
        if (![session setActive:YES error:&error] || error) {
            NSLog(@"LiveKit: Failed to activate audio session: %@", error.localizedDescription);
            return;
        }
        NSLog(@"LiveKit: Audio session configured (AVAudioSession fallback, VideoChat)");
        return;
    }

    // Manual mode: WebRTC won't activate/deactivate the session on its own, and
    // won't initialize the VPIO unit until we grant permission via isAudioEnabled
    // (set below). This is what lets us own activation and gate the unit.
    rtc.useManualAudio = YES;

    [rtc lockForConfiguration];
    NSError* error = nil;
    if (![rtc setCategory:AVAudioSessionCategoryPlayAndRecord
                     mode:AVAudioSessionModeVideoChat
                  options:kLiveKitCategoryOptions
                    error:&error] || error) {
        NSLog(@"LiveKit: Failed to set audio category: %@", error.localizedDescription);
    }

    // Hold exactly one app-owned activation. RTCAudioSession ref-counts activation,
    // so WebRTC's balanced setActive:YES/NO during a call never drops the real
    // session below active while we hold this.
    if (!s_liveKitHoldsActivation) {
        error = nil;
        if ([rtc setActive:YES error:&error] && !error) {
            s_liveKitHoldsActivation = YES;
        } else {
            NSLog(@"LiveKit: Failed to activate audio session: %@", error.localizedDescription);
        }
    }

    [rtc unlockForConfiguration];

    // Grant WebRTC permission to initialize its audio unit by default so call audio
    // works without an explicit LiveKit_SetAudioEnabled(true). The unit is only
    // actually created once a call has an audio track, so pre-call audio is
    // unaffected. Callers may still disable it (e.g. on hang-up) via
    // LiveKit_SetAudioEnabled(false).
    rtc.isAudioEnabled = YES;

    NSLog(@"LiveKit: Audio session configured for VoIP (PlayAndRecord + VideoChat, manual mode, activationCount=%d)",
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
    id<LiveKitRTCAudioSession> rtc = LiveKit_RTCSession();
    if (rtc == nil) {
        return;
    }
    rtc.isAudioEnabled = enabled ? YES : NO;
    NSLog(@"LiveKit: isAudioEnabled=%@ (activationCount=%d)", enabled ? @"YES" : @"NO", rtc.activationCount);
}

/// Restores the audio session to the default ambient category and relinquishes the
/// app-owned activation and manual mode. Call this when PlatformAudio is disposed.
void LiveKit_RestoreDefaultAudioSession() {
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
    [session setCategory:AVAudioSessionCategoryAmbient error:&error];
    if (error) {
        NSLog(@"LiveKit: Failed to restore default audio session: %@", error.localizedDescription);
    }
}

}
