#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstring>
#include <cstdio>

#include "scssdk_input.h"
#include "eurotrucks2/scssdk_input_eut2.h"

namespace {
constexpr wchar_t kMappingName[] = L"Local\\TransPoliVehicleControlLab";
constexpr DWORD kMagic = 0x54505643; // TPVC
constexpr DWORD kProtocolVersion = 1;

struct SharedState {
    DWORD magic;
    DWORD version;
    volatile LONG locked;
};

HANDLE g_mapping = nullptr;
SharedState* g_state = nullptr;
scs_log_t g_log = nullptr;
bool g_controller_seen = false;
bool g_last_locked = false;
bool g_lock_known = false;

struct InputContext { scs_u32_t index = 0; };
InputContext g_context;

const scs_input_device_input_t kInputs[] = {
    {"ignitionoff", "TransPoli ignition off", SCS_VALUE_TYPE_bool},
    {"ignitionon", "TransPoli ignition on", SCS_VALUE_TYPE_bool},
    {"ignitionstrt", "TransPoli ignition start", SCS_VALUE_TYPE_bool},
    {"aforward", "TransPoli accelerator", SCS_VALUE_TYPE_float},
};

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

void log_message(const scs_log_type_t type, const char* message) {
    if (g_log) g_log(type, message);
}

bool connect_controller() {
    if (g_state) return true;

    g_mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, kMappingName);
    if (!g_mapping) return false;

    g_state = static_cast<SharedState*>(
        MapViewOfFile(g_mapping, FILE_MAP_READ, 0, 0, sizeof(SharedState)));

    if (!g_state || g_state->magic != kMagic || g_state->version != kProtocolVersion) {
        close_mapping();
        return false;
    }
    if (!g_controller_seen) {
        log_message(SCS_LOG_TYPE_message, "TransPoli VehicleControlLab V2: controller connected.");
        g_controller_seen = true;
    }
    return true;
}

bool is_locked() {
    // Fail-open: without a valid local controller the plugin contributes no inputs.
    if (!connect_controller()) {
        if (g_controller_seen) {
            log_message(SCS_LOG_TYPE_warning, "TransPoli VehicleControlLab V2: controller unavailable; fail-open UNLOCK.");
            g_controller_seen = false;
            g_lock_known = false;
        }
        return false;
    }
    const bool locked = InterlockedCompareExchange(
        const_cast<volatile LONG*>(&g_state->locked), 0, 0) != 0;
    if (!g_lock_known || locked != g_last_locked) {
        log_message(SCS_LOG_TYPE_message, locked
            ? "TransPoli VehicleControlLab V2: LOCK received; semantic overrides active."
            : "TransPoli VehicleControlLab V2: UNLOCK received; semantic overrides inactive.");
        g_last_locked = locked;
        g_lock_known = true;
    }
    return locked;
}

SCSAPI_RESULT input_event_callback(
    scs_input_event_t* const event_info,
    const scs_u32_t flags,
    const scs_context_t context) {

    auto& input_context = *static_cast<InputContext*>(context);

    if (flags & SCS_INPUT_EVENT_CALLBACK_FLAG_first_in_frame) {
        input_context.index = 0;
    }

    if (!is_locked()) return SCS_RESULT_not_found;

    if (input_context.index >= (sizeof(kInputs) / sizeof(kInputs[0]))) {
        return SCS_RESULT_not_found;
    }

    event_info->input_index = input_context.index;

    switch (input_context.index) {
        case 0:
            event_info->value_bool.value = 1; // request ignition off
            break;
        case 1:
        case 2:
            event_info->value_bool.value = 0; // do not request ignition/start
            break;
        case 3:
            event_info->value_float.value = 0.0f; // probe accelerator suppression
            break;
        default:
            return SCS_RESULT_not_found;
    }

    ++input_context.index;
    return SCS_RESULT_ok;
}
}

extern "C" SCSAPI_RESULT scs_input_init(
    const scs_u32_t version,
    const scs_input_init_params_t* const params) {

    if (version != SCS_INPUT_VERSION_1_00) return SCS_RESULT_unsupported;

    const auto* version_params =
        static_cast<const scs_input_init_params_v100_t*>(params);
    g_log = version_params->common.log;
    log_message(SCS_LOG_TYPE_message, "TransPoli VehicleControlLab V2: plugin initialized by ETS2.");

    scs_input_device_t device{};
    device.name = "transpoli_vehicle_control_lab";
    device.display_name = "TransPoli Vehicle Control Lab";
    device.type = SCS_INPUT_DEVICE_TYPE_semantical;
    device.input_count = sizeof(kInputs) / sizeof(kInputs[0]);
    device.inputs = kInputs;
    device.callback_context = &g_context;
    device.input_event_callback = input_event_callback;

    const auto result = version_params->register_device(&device);
    if (result != SCS_RESULT_ok) {
        version_params->common.log(
            SCS_LOG_TYPE_error,
            "TransPoli VehicleControlLab V2: input device registration failed.");
        return result;
    }

    version_params->common.log(
        SCS_LOG_TYPE_message,
        "TransPoli VehicleControlLab V2: semantic input device registered (fail-open).");
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_input_shutdown() {
    log_message(SCS_LOG_TYPE_message, "TransPoli VehicleControlLab V2: plugin shutdown.");
    close_mapping();
    g_log = nullptr;
}

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH) close_mapping();
    return TRUE;
}
