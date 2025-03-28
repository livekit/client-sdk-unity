#!/bin/bash

FFI_PROTOCOL=../client-sdk-rust~/livekit-ffi/protocol
OUT_CSHARP=../Runtime/Scripts/Proto

protoc \
    -I=$FFI_PROTOCOL \
    --csharp_out=$OUT_CSHARP \
    $FFI_PROTOCOL/ffi.proto \
    $FFI_PROTOCOL/handle.proto \
    $FFI_PROTOCOL/room.proto \
    $FFI_PROTOCOL/track.proto \
    $FFI_PROTOCOL/track_publication.proto \
    $FFI_PROTOCOL/participant.proto \
    $FFI_PROTOCOL/video_frame.proto \
    $FFI_PROTOCOL/audio_frame.proto \
    $FFI_PROTOCOL/e2ee.proto \
    $FFI_PROTOCOL/stats.proto \
    $FFI_PROTOCOL/rpc.proto \
    $FFI_PROTOCOL/data_stream.proto