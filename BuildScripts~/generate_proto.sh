#!/bin/bash

FFI_PROTOCOL=../client-sdk-rust~/livekit-ffi/protocol
OUT_CSHARP=../Runtime/Scripts/Proto

protoc \
    -I=$FFI_PROTOCOL \
    --csharp_out=$OUT_CSHARP \
    $FFI_PROTOCOL/ffi.proto
