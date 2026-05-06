## Summary

FFI events whose handlers don't touch Unity APIs run directly on the FFI callback thread instead of being marshaled to Unity's main thread via `SynchronizationContext.Post`. 

`FFICallback` previously usually routes Rust events (except `AudioStreamEvent`) through `_context.Post` to Unity's main thread. That's the safe default for handlers that touch Unity APIs (`Texture2D`, `GameObject`, `Transform`, …) but it costs one frame of latency for handlers that don't. Four categories of events can skip that:

1. **Audio stream events** that are written to the audio stream ring buffer and consumed on audio thread.
2. **One-shot async completions** that only flip `IsDone` on a `YieldInstruction` — `SetMetadata`, `UnpublishTrack`, all stream `Read/Write/Close` ops.
3. **Stream reader chunk events** that just append bytes/strings to an internal buffer.
4. **Log batches** — `UnityEngine.Debug.unityLogger` is documented thread-safe; the post hop adds latency without benefit, especially during error storms or `LK_VERBOSE` noise.

## Logic in code:

```csharp
internal static void RouteFfiEvent(FfiEvent response)
{
    if (_isDisposed) return;

    // 1. Per-event-type fast paths — invoke handler directly on FFI thread.
    if (response.MessageCase == FfiEvent.MessageOneofCase.AudioStreamEvent)      { ...; return; }
    if (response.MessageCase == FfiEvent.MessageOneofCase.Logs)                  { ...; return; }
    if (response.MessageCase == FfiEvent.MessageOneofCase.ByteStreamReaderEvent) { ...; return; }
    if (response.MessageCase == FfiEvent.MessageOneofCase.TextStreamReaderEvent) { ...; return; }

    // 2. One-shot completion fast path — opted-in pending callbacks complete inline.
    var requestAsyncId = ExtractRequestAsyncId(response);
    if (requestAsyncId.HasValue && Instance.TrySkipDispatch(requestAsyncId.Value, response))
        return;

    // 3. Fallback — post to Unity's main-thread sync context.
    Instance._context?.Post(static (resp) =>
    {
        var r = resp as FfiEvent;
        if (r == null) return;
        DispatchEvent(r);
    }, response);
}
```

## Event Table

| Event | Where it runs | Why |
| --- | --- | --- |
| `AudioStreamEvent` | **FFI thread** (unchanged) | Audio thread consumes the data; main-thread latency would hurt timing |
| `Logs` | **FFI thread** (new) | `Debug.unityLogger` is thread-safe; logs reach console immediately during panics / errors |
| `ByteStreamReaderEvent` | **FFI thread** (new) | Internal buffer is now lock-protected; chunks land without frame delay |
| `TextStreamReaderEvent` | **FFI thread** (new) | Same lock as byte path (shared `ReadIncrementalInstructionBase`) |
| One-shot completions via `FfiInstruction<T>` | **FFI thread** (new) | `SetLocalMetadata`, `SetLocalName`, `SetLocalAttributes`, `UnpublishTrack` — only flip `IsDone`/`IsError` |
| One-shot completions via `FfiStreamInstruction<T>` | **FFI thread** (new) | `ByteStreamWriter.Write/Close`, `TextStreamWriter.Write/Close` |
| One-shot completions via `FfiStreamResultInstruction<T,U>` | **FFI thread** (new) | `ByteStreamReader.ReadAll/WriteToFile`, `TextStreamReader.ReadAll` |
| `RoomEvent` | Main thread | Fires user-facing `ParticipantConnected`, `TrackPublished`, etc. |
| `TrackEvent` | Main thread | (No subscribers today; main-thread default for safety) |
| `RpcMethodInvocation` | Main thread | User RPC handlers commonly touch game state |
| `Disconnect` | Main thread | UI updates typical |
| `VideoStreamEvent` | Main thread | Internal buffering is fast; user-facing raw delivery deferred (see follow-ups) |
| `DataTrackStreamEvent` | Main thread | Deferred until a concrete consumer asks |
| `Connect` (one-shot) | Main thread | Bespoke handler fires participant-connected events |
| `PublishTrack` (one-shot) | Main thread | Bespoke handler |
| `GetStats` (one-shot) | Main thread | Bespoke handler |
| `CaptureAudioFrame` (one-shot) | Main thread | Bespoke handler |
| `PerformRpc` (one-shot) | Main thread | Bespoke handler surfaces response |
| `SendText` / `SendFile` (one-shot) | Main thread | Bespoke handlers |
| `TextStreamOpen` / `ByteStreamOpen` (one-shot) | Main thread | Bespoke handlers return writer objects |
| `PublishDataTrack` (one-shot) | Main thread | Bespoke handler |
