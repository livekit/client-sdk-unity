---
io.livekit.livekit-sdk: patch
---

`Room.Disconnect()` (and `Dispose()`) called while `Connect()` is still pending is no longer ignored: the room is disconnected as soon as the connect completes, without raising `Connected`, and the pending `ConnectInstruction` completes with `IsError` set.

`Room.Connect()` now throws `InvalidOperationException` when the room is already connecting or connected, instead of overwriting the live room handle and leaking the native room. `Room.E2EEManager` now receives the room handle (it was created before the handle was set).
