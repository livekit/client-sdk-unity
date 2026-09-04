#import <AVFoundation/AVFoundation.h>

// Latency terms for seeding libwebrtc's AEC3 stream delay; consumed by
// Assets/Runtime/Audio/AudioProcessingDelaySeed.cs. AVAudioSession only reports meaningful
// values once the session is active; it returns 0 before that.
extern "C" {
    double MeetSample_AudioSessionOutputLatency() {
        return [[AVAudioSession sharedInstance] outputLatency];
    }

    double MeetSample_AudioSessionInputLatency() {
        return [[AVAudioSession sharedInstance] inputLatency];
    }

    double MeetSample_AudioSessionIOBufferDuration() {
        return [[AVAudioSession sharedInstance] IOBufferDuration];
    }
}
