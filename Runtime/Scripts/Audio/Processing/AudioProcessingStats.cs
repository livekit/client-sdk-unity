namespace LiveKit
{
    /// <summary>
    /// Counters from the audio processing stage of an <see cref="RtcAudioSource"/> that was
    /// created with <see cref="AudioProcessingOptions"/>. A snapshot; read it from any thread via
    /// <see cref="RtcAudioSource.AudioProcessingStats"/>.
    /// </summary>
    public readonly struct AudioProcessingStats
    {
        /// <summary>
        /// True while capture is actually being processed. False when the source has no processing
        /// stage, is stopped, or bypasses processing because Unity's output sample rate has no
        /// whole-sample 10 ms chunk (it is not a multiple of 100 Hz).
        /// </summary>
        public readonly bool Active;

        /// <summary>
        /// True while a <see cref="PlayoutReference"/> on an enabled <c>AudioListener</c> is feeding
        /// the far-end reference. Without it echo cancellation has nothing to cancel; noise
        /// suppression, gain control and the high-pass filter still run.
        /// </summary>
        public readonly bool ReferenceAttached;

        /// <summary>Format of the capture feed, as delivered by Unity's audio graph.</summary>
        public readonly int CaptureSampleRate;
        public readonly int CaptureChannels;

        /// <summary>Format of the playout reference feed.</summary>
        public readonly int ReferenceSampleRate;
        public readonly int ReferenceChannels;

        /// <summary>10 ms capture chunks run through the module.</summary>
        public readonly long CaptureChunks;

        /// <summary>10 ms reference chunks run through the module.</summary>
        public readonly long ReferenceChunks;

        /// <summary>Samples discarded because a feed outran its buffer. Non-zero means the audio thread stalled.</summary>
        public readonly long DroppedCaptureSamples;
        public readonly long DroppedReferenceSamples;

        /// <summary>Chunks the module rejected; those capture chunks were published unprocessed.</summary>
        public readonly long FailedChunks;

        /// <summary>Most recent module error, or null.</summary>
        public readonly string LastError;

        /// <summary>Render-to-capture delay hint last handed to the module, or -1 if none yet.</summary>
        public readonly int StreamDelayHintMs;

        public AudioProcessingStats(
            bool active,
            bool referenceAttached,
            int captureSampleRate,
            int captureChannels,
            int referenceSampleRate,
            int referenceChannels,
            long captureChunks,
            long referenceChunks,
            long droppedCaptureSamples,
            long droppedReferenceSamples,
            long failedChunks,
            string lastError,
            int streamDelayHintMs)
        {
            Active = active;
            ReferenceAttached = referenceAttached;
            CaptureSampleRate = captureSampleRate;
            CaptureChannels = captureChannels;
            ReferenceSampleRate = referenceSampleRate;
            ReferenceChannels = referenceChannels;
            CaptureChunks = captureChunks;
            ReferenceChunks = referenceChunks;
            DroppedCaptureSamples = droppedCaptureSamples;
            DroppedReferenceSamples = droppedReferenceSamples;
            FailedChunks = failedChunks;
            LastError = lastError;
            StreamDelayHintMs = streamDelayHintMs;
        }

        public override string ToString() =>
            $"active={Active} reference={ReferenceAttached} " +
            $"capture={CaptureChannels}ch@{CaptureSampleRate} chunks={CaptureChunks} dropped={DroppedCaptureSamples} | " +
            $"reference={ReferenceChannels}ch@{ReferenceSampleRate} chunks={ReferenceChunks} dropped={DroppedReferenceSamples} | " +
            $"failed={FailedChunks} lastError={LastError ?? "-"} delayHint={StreamDelayHintMs}ms";
    }
}
