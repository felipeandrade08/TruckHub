#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <atomic>
#include <cmath>
#include <cstdio>

#include "scssdk_input.h"
#include "scssdk_telemetry.h"
#include "eurotrucks2/scssdk_input_eut2.h"
#include "eurotrucks2/scssdk_eut2.h"
#include "eurotrucks2/scssdk_telemetry_eut2.h"

namespace {
constexpr wchar_t kMappingName[] = L"Local\\TruckHubVehicleControl";
constexpr DWORD kMagic = 0x54505643; // TPVC
constexpr DWORD kProtocolVersion = 3;
constexpr scs_u32_t kAxlePulseFrames = 4;
constexpr scs_u32_t kUnlockReleaseFrames = 6;
constexpr float kSafeStopSpeedMps = 0.15f;
constexpr scs_u32_t kSafeStopFrames = 30;

enum VehicleState : LONG {
    AUTHORIZED = 0,
    LOCK_PENDING = 1,
    SAFE_STOP = 2,
    LOCKED = 3,
    UNLOCK_RELEASE = 4
};

struct SharedState {
    DWORD magic;
    DWORD version;
    volatile LONG lock_requested;
    volatile LONG actual_state;
    volatile float speed_mps;
    volatile LONG lift_axle_cmd;          // contador: cada incremento = um toque em "liftaxle"
    volatile LONG lift_axle_trailer_cmd;  // contador: cada incremento = um toque em "liftaxlet"
};

HANDLE g_mapping = nullptr;
SharedState* g_state = nullptr;
scs_log_t g_log = nullptr;
bool g_controller_seen = false;
std::atomic<float> g_speed_mps{0.0f};
std::atomic<LONG> g_actual_state{AUTHORIZED};
scs_u32_t g_safe_stop_frames = 0;
scs_u32_t g_unlock_release_frames = 0;
LONG g_axle_handled[2] = {0, 0};
scs_u32_t g_axle_pulse[2] = {0, 0};
bool g_axle_release[2] = {false, false};
LONG g_last_logged_state = -1;

struct InputContext { scs_u32_t index = 0; };
InputContext g_context;

const scs_input_device_input_t kInputs[] = {
    {"ignitionoff", "TruckHub ignition off", SCS_VALUE_TYPE_bool},
    {"ignitionon", "TruckHub ignition on", SCS_VALUE_TYPE_bool},
    {"ignitionstrt", "TruckHub ignition start", SCS_VALUE_TYPE_bool},
    {"aforward", "TruckHub accelerator", SCS_VALUE_TYPE_float},
    {"liftaxle", "TruckHub lift axle", SCS_VALUE_TYPE_bool},
    {"liftaxlet", "TruckHub lift trailer axle", SCS_VALUE_TYPE_bool},
};
constexpr scs_u32_t kLockInputCount = 4;   // índices 0..3 são do bloqueio; 4..5 são dos eixos

void log_message(scs_log_type_t type, const char* message) {
    if (g_log) g_log(type, message);
}

const char* state_name(LONG state) {
    switch (state) {
        case AUTHORIZED: return "AUTHORIZED";
        case LOCK_PENDING: return "LOCK_PENDING";
        case SAFE_STOP: return "SAFE_STOP";
        case LOCKED: return "LOCKED";
        case UNLOCK_RELEASE: return "UNLOCK_RELEASE";
        default: return "UNKNOWN";
    }
}

void log_state(LONG state) {
    if (state == g_last_logged_state) return;
    char buffer[160]{};
    std::snprintf(buffer, sizeof(buffer),
        "TruckHub VehicleControl V1: state -> %s (speed=%.2f m/s).",
        state_name(state), g_speed_mps.load(std::memory_order_relaxed));
    log_message(SCS_LOG_TYPE_message, buffer);
    g_last_logged_state = state;
}

void close_mapping() {
    if (g_state) {
        UnmapViewOfFile(g_state);
        g_state = nullptr;
    }
    if (g_mapping) {
        CloseHandle(g_mapping);
        g_mapping = nullptr;
    }
}

bool connect_controller() {
    if (g_state) return true;

    g_mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, kMappingName);
    if (!g_mapping) return false;

    g_state = static_cast<SharedState*>(
        MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));

    if (!g_state || g_state->magic != kMagic || g_state->version != kProtocolVersion) {
        close_mapping();
        return false;
    }

    if (!g_controller_seen) {
        log_message(SCS_LOG_TYPE_message, "TruckHub VehicleControl V1: controller connected.");
        g_controller_seen = true;
    }
    return true;
}

void publish_state(LONG state) {
    g_actual_state.store(state, std::memory_order_relaxed);
    if (g_state) {
        g_state->actual_state = state;
        g_state->speed_mps = g_speed_mps.load(std::memory_order_relaxed);
    }
    log_state(state);
}

bool lock_requested() {
    if (!connect_controller()) {
        if (g_controller_seen) {
            log_message(SCS_LOG_TYPE_warning,
                "TruckHub VehicleControl V1: controller unavailable; fail-open AUTHORIZED.");
            g_controller_seen = false;
        }
        return false;
    }
    return g_state->lock_requested != 0;
}

void update_state_machine() {
    const bool requested = lock_requested();
    const float speed = std::fabs(g_speed_mps.load(std::memory_order_relaxed));
    LONG state = g_actual_state.load(std::memory_order_relaxed);

    if (!requested) {
        g_safe_stop_frames = 0;
        if (state == LOCKED || state == SAFE_STOP || state == LOCK_PENDING) {
            g_unlock_release_frames = kUnlockReleaseFrames;
            publish_state(UNLOCK_RELEASE);
        } else if (state == UNLOCK_RELEASE && g_unlock_release_frames == 0) {
            publish_state(AUTHORIZED);
        } else if (state != UNLOCK_RELEASE) {
            publish_state(AUTHORIZED);
        }
        return;
    }

    if (state == LOCKED) return;

    if (speed > kSafeStopSpeedMps) {
        g_safe_stop_frames = 0;
        publish_state(LOCK_PENDING);
        return;
    }

    if (state != SAFE_STOP) {
        g_safe_stop_frames = 0;
        publish_state(SAFE_STOP);
    }

    if (++g_safe_stop_frames >= kSafeStopFrames) {
        g_safe_stop_frames = 0;
        publish_state(LOCKED);
    }
}

void telemetry_speed(const scs_string_t, const scs_u32_t,
    const scs_value_t* const value, const scs_context_t) {
    if (!value || value->type != SCS_VALUE_TYPE_float) return;
    g_speed_mps.store(value->value_float.value, std::memory_order_relaxed);
}

void update_axle_commands() {
    if (!g_state) { g_axle_pulse[0] = g_axle_pulse[1] = 0; return; }
    const LONG cmds[2] = {g_state->lift_axle_cmd, g_state->lift_axle_trailer_cmd};
    for (int i = 0; i < 2; ++i) {
        if (cmds[i] != g_axle_handled[i]) {
            g_axle_handled[i] = cmds[i];
            g_axle_pulse[i] = kAxlePulseFrames;
            g_axle_release[i] = false;
        } else if (g_axle_pulse[i] > 0) {
            if (--g_axle_pulse[i] == 0) g_axle_release[i] = true;
        }
    }
}

void frame_end(const scs_event_t, const void* const, const scs_context_t) {
    update_state_machine();
    update_axle_commands();
    if (g_state) g_state->speed_mps = g_speed_mps.load(std::memory_order_relaxed);
}

SCSAPI_RESULT input_event_callback(
    scs_input_event_t* const event_info,
    const scs_u32_t flags,
    const scs_context_t context) {

    auto& input_context = *static_cast<InputContext*>(context);
    if (flags & SCS_INPUT_EVENT_CALLBACK_FLAG_first_in_frame) input_context.index = 0;

    // If telemetry is unavailable, controller loss or no state update remains fail-open.
    if (!connect_controller()) {
        g_actual_state.store(AUTHORIZED, std::memory_order_relaxed);
        return SCS_RESULT_not_found;
    }

    const LONG state = g_actual_state.load(std::memory_order_relaxed);
    const bool locked = state == LOCKED;
    const bool releasing = state == UNLOCK_RELEASE && g_unlock_release_frames > 0;
    const scs_u32_t total = sizeof(kInputs) / sizeof(kInputs[0]);

    while (input_context.index < total) {
        const scs_u32_t idx = input_context.index;
        if (idx < kLockInputCount) {
            if (!locked && !releasing) { ++input_context.index; continue; }
            event_info->input_index = idx;
            if (locked) {
                switch (idx) {
                    case 0: event_info->value_bool.value = 1; break;
                    case 1:
                    case 2: event_info->value_bool.value = 0; break;
                    default: event_info->value_float.value = 0.0f; break;
                }
            } else {
                if (idx == 3) event_info->value_float.value = 0.0f;
                else event_info->value_bool.value = 0;
            }
        } else {
            const scs_u32_t a = idx - kLockInputCount;
            if (g_axle_pulse[a] > 0) {
                event_info->input_index = idx;
                event_info->value_bool.value = 1;
            } else if (g_axle_release[a]) {
                g_axle_release[a] = false;
                event_info->input_index = idx;
                event_info->value_bool.value = 0;
            } else { ++input_context.index; continue; }
        }
        ++input_context.index;
        return SCS_RESULT_ok;
    }

    if (releasing && g_unlock_release_frames > 0) {
        --g_unlock_release_frames;
        if (g_unlock_release_frames == 0) {
            publish_state(AUTHORIZED);
            log_message(SCS_LOG_TYPE_message,
                "TruckHub VehicleControl V1: UNLOCK PASSIVE; semantic overrides released.");
        }
    }
    return SCS_RESULT_not_found;
}
}

extern "C" SCSAPI_RESULT scs_input_init(
    const scs_u32_t version, const scs_input_init_params_t* const params) {
    if (version != SCS_INPUT_VERSION_1_00) return SCS_RESULT_unsupported;
    const auto* p = static_cast<const scs_input_init_params_v100_t*>(params);
    g_log = p->common.log;
    log_message(SCS_LOG_TYPE_message, "TruckHub VehicleControl V1: input plugin initialized.");

    scs_input_device_t device{};
    device.name = "truckhub_vehicle_control";
    device.display_name = "TruckHub Vehicle Control";
    device.type = SCS_INPUT_DEVICE_TYPE_semantical;
    device.input_count = sizeof(kInputs) / sizeof(kInputs[0]);
    device.inputs = kInputs;
    device.callback_context = &g_context;
    device.input_event_callback = input_event_callback;

    const auto result = p->register_device(&device);
    if (result != SCS_RESULT_ok) return result;
    log_message(SCS_LOG_TYPE_message, "TruckHub VehicleControl V1: semantic input device registered.");
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_input_shutdown() {
    log_message(SCS_LOG_TYPE_message, "TruckHub VehicleControl V1: input shutdown.");
}

extern "C" SCSAPI_RESULT scs_telemetry_init(
    const scs_u32_t version, const scs_telemetry_init_params_t* const params) {
    if (version != SCS_TELEMETRY_VERSION_1_01) return SCS_RESULT_unsupported;
    const auto* p = static_cast<const scs_telemetry_init_params_v101_t*>(params);
    if (!g_log) g_log = p->common.log;

    if (p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_speed,
        SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none,
        telemetry_speed, nullptr) != SCS_RESULT_ok) {
        return SCS_RESULT_generic_error;
    }
    if (p->register_for_event(SCS_TELEMETRY_EVENT_frame_end, frame_end, nullptr) != SCS_RESULT_ok) {
        return SCS_RESULT_generic_error;
    }

    log_message(SCS_LOG_TYPE_message,
        "TruckHub VehicleControl V1: telemetry safety gate registered.");
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_telemetry_shutdown() {
    // Fail open on telemetry shutdown.
    g_actual_state.store(AUTHORIZED, std::memory_order_relaxed);
    if (g_state) {
        g_state->actual_state = AUTHORIZED;
        g_state->speed_mps = 0.0f;
    }
    log_message(SCS_LOG_TYPE_message, "TruckHub VehicleControl V1: telemetry shutdown; fail-open AUTHORIZED.");
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH) close_mapping();
    return TRUE;
}
