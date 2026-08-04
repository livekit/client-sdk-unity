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

// Snapshot of the AVAudioSession configuration as it was the first time LiveKit
// touched the session (i.e. whatever Unity set up from its iOS Player Settings).
// Captured lazily in LiveKit_ConfigureAudioSessionForVoIP and re-applied by
// LiveKit_RestoreDefaultAudioSession when the last PlatformAudio is disposed.
static BOOL s_hasCachedState = NO;
static NSString* s_cachedCategory = nil;
static NSString* s_cachedMode = nil;
static AVAudioSessionCategoryOptions s_cachedCategoryOptions = 0;

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

extern "C" {

/// Configures the iOS audio session for VoIP/WebRTC use.
/// This sets AVAudioSessionCategoryPlayAndRecord with VoiceChat mode,
/// which enables the VPIO (Voice Processing IO) AudioUnit for:
/// - Hardware echo cancellation (AEC)
/// - Automatic gain control (AGC)
/// - Noise suppression (NS)
///
/// Call this before creating PlatformAudio to ensure WebRTC can
/// properly initialize the microphone and speaker.
void LiveKit_ConfigureAudioSessionForVoIP() {
    // Snapshot the pristine (Unity Player Settings) session before we change it.
    LiveKit_CacheSessionStateIfNeeded();

    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;

    // Configure for VoIP with echo cancellation
    BOOL success = [session setCategory:AVAudioSessionCategoryPlayAndRecord
                                   mode:AVAudioSessionModeVoiceChat
                                options:AVAudioSessionCategoryOptionDefaultToSpeaker |
                                        AVAudioSessionCategoryOptionAllowBluetooth |
                                        AVAudioSessionCategoryOptionAllowBluetoothA2DP
                                  error:&error];

    if (!success || error) {
        NSLog(@"LiveKit: Failed to configure VoIP audio session: %@", error.localizedDescription);
        return;
    }

    // Activate the audio session
    success = [session setActive:YES error:&error];
    if (!success || error) {
        NSLog(@"LiveKit: Failed to activate audio session: %@", error.localizedDescription);
        return;
    }

    NSLog(@"LiveKit: Audio session configured for VoIP (PlayAndRecord + VoiceChat mode)");
}

/// Restores the audio session to the state cached before LiveKit first configured
/// it for VoIP (see LiveKit_CacheSessionStateIfNeeded), and reactivates it so Unity
/// audio plays normally again. Call this when the last PlatformAudio is disposed.
/// Falls back to the ambient category if nothing was ever cached.
void LiveKit_RestoreDefaultAudioSession() {
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
