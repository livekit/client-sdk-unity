# Changelog

## 2.2.0

### Minor Changes

- Add a `Compress` option to `StreamTextOptions` and `StreamByteOptions`, so `SendText` and `SendFile` can opt out of data stream compression. - [#400](https://github.com/livekit/client-sdk-unity/pull/400) ([@1egoman](https://github.com/1egoman))

### Patch Changes

- `Room.Disconnect()` (and `Dispose()`) called while `Connect()` is still pending is no longer ignored: the room is disconnected as soon as the connect completes, without raising `Connected`, and the pending `ConnectInstruction` completes with `IsError` set. - [#404](https://github.com/livekit/client-sdk-unity/pull/404) ([@MaxHeimbrock](https://github.com/MaxHeimbrock))
  
  `Room.Connect()` no longer overwrites the live room handle and leaks the native room when called on a room that is not disconnected. On a connected room it is now a no-op that logs a warning and returns a completed instruction. While the room is reconnecting, it replaces the reconnection with a new connection, reporting the old session as `Disconnected` with `ClientInitiated`. It throws `InvalidOperationException` while another `Connect()` is still pending. It can be called from a `Disconnected` handler to reconnect right away, except while a `Connect()` is replacing a reconnecting session. `Room.E2EEManager` now receives the room handle (it was created before the handle was set).


The notes for 2.1.1 and earlier are on the [releases page](https://github.com/livekit/client-sdk-unity/releases).
