use anyhow::{Context, Result, anyhow};
use arc_swap::ArcSwap;
use cpal::{
    InputCallbackInfo, Stream, StreamConfig,
    traits::{DeviceTrait, HostTrait, StreamTrait},
};
use crossbeam_channel::{Receiver, Sender, bounded};
use dashmap::DashMap;
use lazy_static::lazy_static;
use std::{
    ffi::{CStr, CString},
    os::raw::{c_char, c_void},
    ptr::{self, null},
    sync::{Arc, atomic::AtomicU64},
};

struct SafeStream {
    stream: Stream,
}

unsafe impl Send for SafeStream {}
unsafe impl Sync for SafeStream {}

lazy_static! {
    static ref ERROR_CALLBACK: ArcSwap<Option<ErrorCallback>> = ArcSwap::from_pointee(None);
    static ref NEXT_STREAM_ID: AtomicU64 = AtomicU64::new(1);
    static ref REGISTRY: DashMap<u64, SafeStream> = DashMap::new();
    static ref DATA_CHANNELS: DashMap<u64, (Sender<Vec<f32>>, Receiver<Vec<f32>>)> = DashMap::new();
}

pub type ErrorCallback = extern "C" fn(*const c_char);

pub type StreamId = u64;

#[repr(C)]
pub struct Status {
    pub streams_count: u64,
    pub has_error_callback: bool,
}

#[repr(C)]
pub struct DeviceNamesResult {
    pub names: *const *const c_char,
    pub length: i32,
    pub error_message: *const c_char,
}

#[repr(C)]
pub struct QualityOptionsResult {
    pub options: *const *const c_char,
    pub length: i32,
    pub error_message: *const c_char,
}

#[repr(C)]
pub struct InputStreamResult {
    pub stream_id: StreamId,
    pub sample_rate: u32,
    pub channels: u32,
    pub error_message: *const c_char,
}

#[repr(C)]
pub struct ConsumeFrameResult {
    pub ptr: *const f32,
    pub len: i32,
    pub capacity: i32,
    pub error_message: *const c_char,
}

impl InputStreamResult {
    fn ok(stream_id: StreamId, sample_rate: u32, channels: u32) -> Self {
        Self {
            stream_id,
            sample_rate,
            channels,
            error_message: ptr::null(),
        }
    }

    fn error(message: &str) -> Self {
        Self {
            stream_id: 0,
            sample_rate: 0,
            channels: 0,
            error_message: string_to_c_bytes(message),
        }
    }
}

#[repr(C)]
pub struct ResultFFI {
    pub error_message: *const c_char,
}

impl ResultFFI {
    fn ok() -> Self {
        Self {
            error_message: ptr::null(),
        }
    }

    fn error(message: &str) -> Self {
        Self {
            error_message: string_to_c_bytes(message),
        }
    }

    fn from_anyhow<T>(result: Result<T>) -> Self {
        match result {
            Ok(_) => Self::ok(),
            Err(e) => Self::error(e.to_string().as_str()),
        }
    }
}

fn string_to_c_bytes(s: &str) -> *const c_char {
    CString::new(s).unwrap_or_default().into_raw()
}

fn vec_to_c_array(strings: Vec<String>) -> *const *const c_char {
    let cstrings: Vec<*const c_char> = strings.into_iter().map(|s| string_to_c_bytes(&s)).collect();

    let boxed_slice = cstrings.into_boxed_slice();

    let ptr = boxed_slice.as_ptr();
    std::mem::forget(boxed_slice);

    ptr
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_init(error_callback: Option<ErrorCallback>) -> ResultFFI {
    let error_callback = match error_callback {
        Some(e) => e,
        None => {
            return ResultFFI::error("Error callback is null");
        }
    };

    ERROR_CALLBACK.store(Arc::new(Some(error_callback)));
    ResultFFI::ok()
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_deinit() {
    ERROR_CALLBACK.store(Arc::new(None));
    REGISTRY.clear();
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_status() -> Status {
    let has_error_callback = ERROR_CALLBACK.load().is_some();
    let streams_count = REGISTRY.len() as u64;

    Status {
        streams_count,
        has_error_callback,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_free_c_char_array(ptr: *const *const c_char, len: usize) {
    unsafe {
        if ptr.is_null() {
            return;
        }

        let slice = std::slice::from_raw_parts(ptr, len);

        for &cstr_ptr in slice {
            if !cstr_ptr.is_null() {
                drop(CString::from_raw(cstr_ptr as *mut c_char));
            }
        }

        drop(Box::from_raw(ptr as *mut *const c_char));
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_free(ptr: *mut c_void) {
    if !ptr.is_null() {
        unsafe { drop(Box::from_raw(ptr)) };
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_device_names() -> DeviceNamesResult {
    let result = rust_audio_input_device_names_internal();
    match result {
        Ok(names) => DeviceNamesResult {
            error_message: ptr::null(),
            length: names.len() as i32,
            names: if names.len() > 0 {
                vec_to_c_array(names)
            } else {
                ptr::null()
            },
        },
        Err(e) => DeviceNamesResult {
            names: ptr::null(),
            length: 0,
            error_message: string_to_c_bytes(&e.to_string()),
        },
    }
}

fn rust_audio_input_device_names_internal() -> Result<Vec<String>> {
    let host = cpal::default_host();
    let devices: Vec<_> = host
        .input_devices()
        .context("cannot get input devices")?
        .collect();
    let mut names: Vec<String> = Vec::new();
    for d in devices {
        let name = d.name().context("cannot get device name")?;
        names.push(name);
    }
    Ok(names)
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_device_quality_options(
    device_name: *const c_char,
) -> QualityOptionsResult {
    if device_name.is_null() {
        return QualityOptionsResult {
            options: null(),
            length: 0,
            error_message: string_to_c_bytes("Device name is null"),
        };
    }

    unsafe {
        let cstr = CStr::from_ptr(device_name);
        let device_name = match cstr.to_str() {
            Ok(name) => name,
            Err(_) => {
                return QualityOptionsResult {
                    options: null(),
                    length: 0,
                    error_message: string_to_c_bytes("Invalid UTF-8 in device name"),
                };
            }
        };
        let result = rust_audio_device_quality_options_internal(device_name);
        match result {
            Ok(options) => {
                return QualityOptionsResult {
                    error_message: ptr::null(),
                    length: options.len() as i32,
                    options: vec_to_c_array(options),
                };
            }
            Err(e) => {
                return QualityOptionsResult {
                    options: ptr::null(),
                    length: 0,
                    error_message: string_to_c_bytes(&e.to_string()),
                };
            }
        }
    }
}

fn rust_audio_device_quality_options_internal(device_name: &str) -> Result<Vec<String>> {
    let host = cpal::default_host();
    let device = host
        .input_devices()
        .context("cannot get input devices")?
        .find(|d| d.name().unwrap_or_default() == device_name)
        .ok_or(anyhow!("device with specified name not found"))?;

    let list = device
        .supported_input_configs()
        .context("device doesn't have supported configs")?
        .map(|e| {
            format!(
                "channels: {}, sample_rate: {:?}, sample_format: {}, buffer_size: {:?}",
                e.channels(),
                e.max_sample_rate(),
                e.sample_format(),
                e.buffer_size(),
            )
        })
        .collect();

    Ok(list)
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_stream_new(device_name: *const c_char) -> InputStreamResult {
    if device_name.is_null() {
        return InputStreamResult::error("Device name is null");
    }

    unsafe {
        let cstr = CStr::from_ptr(device_name);
        let device_name = match cstr.to_str() {
            Ok(name) => name,
            Err(_) => {
                return InputStreamResult::error("Invalid UTF-8 in device name");
            }
        };

        match rust_audio_input_stream_new_internal(device_name) {
            Ok(s) => {
                let stream_id = s.0;
                let config = s.1;

                InputStreamResult::ok(stream_id, config.sample_rate.0, config.channels as u32)
            }
            Err(e) => {
                let message = e.to_string();
                InputStreamResult::error(message.as_str())
            }
        }
    }
}

fn rust_audio_input_stream_new_internal(device_name: &str) -> Result<(StreamId, StreamConfig)> {
    let host = cpal::default_host();
    let device = host
        .input_devices()
        .context("cannot get input devices")?
        .find(|d| d.name().unwrap_or_default() == device_name)
        .ok_or(anyhow!("device with specified name not found"))?;

    let config_range = device
        .supported_input_configs()
        .context("device doesn't have supported configs")?
        .find(|c| c.sample_format() == cpal::SampleFormat::F32)
        .ok_or(anyhow!("device doesn't support f32 samples"))?;

    let config = config_range.with_max_sample_rate().config();

    let next_id = NEXT_STREAM_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
    let moved_id = next_id;

    // both 2 audio callbacks must be lightweight and NOT call to other callbacks
    // callbacks used from audio thread and if call is heavy OS may kill the app
    let stream = device
        .build_input_stream(
            &config,
            move |data: &[f32], _: &InputCallbackInfo| {
                let data = data.to_vec(); // Allocates, but avoids lifetime issues
                if let Some(channel) = DATA_CHANNELS.get(&moved_id) {
                    let _ = channel.0.try_send(data);
                }
            },
            |e| eprintln!("error on stream: {e}"),
            None,
        )
        .context("cannot build input stream")?;

    REGISTRY.insert(next_id, SafeStream { stream });
    DATA_CHANNELS.insert(next_id, bounded(1));

    Ok((next_id, config))
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_stream_start(stream_id: StreamId) -> ResultFFI {
    let stream = REGISTRY.get(&stream_id);
    match stream {
        Some(s) => {
            let result = s.stream.play().context("cannot play stream");
            ResultFFI::from_anyhow(result)
        }
        None => ResultFFI::error("stream with specified id not found"),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_stream_consume_frame(stream_id: StreamId) -> ConsumeFrameResult {
    fn empty_result() -> ConsumeFrameResult {
        ConsumeFrameResult {
            ptr: ptr::null(),
            len: 0,
            capacity: 0,
            error_message: ptr::null(),
        }
    }

    if let Some(channel) = DATA_CHANNELS.get(&stream_id) {
        if let Ok(frame) = channel.1.try_recv() {
            if frame.is_empty() {
                return empty_result();
            }

            let result = ConsumeFrameResult {
                ptr: frame.as_ptr(),
                len: frame.len() as i32,
                capacity: frame.capacity() as i32,
                error_message: ptr::null(),
            };
            std::mem::forget(frame);
            result
        } else {
            empty_result()
        }
    } else {
        empty_result()
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_stream_free_frame(ptr: *mut f32, len: i32, capacity: i32) {
    unsafe {
        let frame = Vec::from_raw_parts(ptr, len as usize, capacity as usize);
        drop(frame)
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn rust_audio_input_stream_pause(stream_id: StreamId) -> ResultFFI {
    let stream = REGISTRY.get(&stream_id);
    match stream {
        Some(s) => {
            let result = s.stream.pause().context("cannot play stream");
            ResultFFI::from_anyhow(result)
        }
        None => ResultFFI::error("stream with specified id not found"),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn rust_audio_input_stream_free(stream_id: StreamId) {
    REGISTRY.remove(&stream_id);
    DATA_CHANNELS.remove(&stream_id);
}
