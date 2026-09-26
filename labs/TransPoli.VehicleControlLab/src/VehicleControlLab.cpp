#include <cstring>
#include "scssdk_input.h"
#include "eurotrucks2/scssdk_input_eut2.h"

namespace {
struct InputContext { scs_u32_t index = 0; };
InputContext g_context;

const scs_input_device_input_t kInputs[] = {
    {"ignitionoff", "TransPoli ignition off", SCS_VALUE_TYPE_bool},
    {"ignitionon", "TransPoli ignition on", SCS_VALUE_TYPE_bool},
    {"ignitionstrt", "TransPoli ignition start", SCS_VALUE_TYPE_bool},
    {"aforward", "TransPoli accelerator", SCS_VALUE_TYPE_float},
};

SCSAPI_RESULT input_event_callback(
    scs_input_event_t* const,
    const scs_u32_t,
    const scs_context_t) {
    // Phase 1 is intentionally inert. It proves loading and registration only.
    return SCS_RESULT_not_found;
}
}

extern "C" SCSAPI_RESULT scs_input_init(
    const scs_u32_t version,
    const scs_input_init_params_t* const params) {

    if (version != SCS_INPUT_VERSION_1_00) return SCS_RESULT_unsupported;

    const auto* version_params =
        static_cast<const scs_input_init_params_v100_t*>(params);

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
            "TransPoli VehicleControlLab: input device registration failed.");
        return result;
    }

    version_params->common.log(
        SCS_LOG_TYPE_message,
        "TransPoli VehicleControlLab: inert input probe registered.");
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_input_shutdown() {}
