---
io.livekit.livekit-sdk: patch
---

`Room.Disconnect()` (and `Dispose()`) called while `Connect()` is still pending is no longer ignored: the room is disconnected as soon as the connect completes, without raising `Connected`, and the pending `ConnectInstruction` completes with `IsError` set.

`Room.Connect()` no longer overwrites the live room handle and leaks the native room when called on a room that is not disconnected. On a connected room it is now a no-op that logs a warning and returns a completed instruction. While the room is reconnecting, it replaces the reconnection with a new connection, reporting the old session as `Disconnected` with `ClientInitiated`. It throws `InvalidOperationException` while another `Connect()` is still pending. It can be called from a `Disconnected` handler to reconnect right away, except while a `Connect()` is replacing a reconnecting session. `Room.E2EEManager` now receives the room handle (it was created before the handle was set).
