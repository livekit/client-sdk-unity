/*
 * Copyright 2024 LiveKit, Inc.
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

/// Restores the audio session to the default ambient category.
/// Call this when PlatformAudio is disposed if you want to restore
/// the original audio behavior.
void LiveKit_RestoreDefaultAudioSession() {
    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;

    [session setCategory:AVAudioSessionCategoryAmbient error:&error];
    if (error) {
        NSLog(@"LiveKit: Failed to restore default audio session: %@", error.localizedDescription);
    }
}

}
