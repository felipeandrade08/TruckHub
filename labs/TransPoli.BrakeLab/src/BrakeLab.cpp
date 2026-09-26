#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <atomic>
#include <cstdio>

#include "scssdk_telemetry.h"
#include "eurotrucks2/scssdk_eut2.h"
#include "eurotrucks2/scssdk_telemetry_eut2.h"

namespace {
std::atomic<float> g_temperature{0.0f};
std::atomic<float> g_input_brake{0.0f};
std::atomic<float> g_effective_brake{0.0f};

float fade_factor(float temperature) {
    if (temperature < 250.0f) return 1.0f;
    if (temperature < 350.0f) return 1.0f - ((temperature - 250.0f) / 100.0f) * 0.20f;
    if (temperature < 450.0f) return 0.8f - ((temperature - 350.0f) / 100.0f) * 0.30f;
    return 0.30f;
}

void telemetry_float(const scs_string_t, const scs_u32_t, const scs_value_t* const value, const scs_context_t context) {
    if (!value || value->type != SCS_VALUE_TYPE_float) return;
    auto* target = static_cast<std::atomic<float>*>(context);
    target->store(value->value_float.value, std::memory_order_relaxed);
}

void frame_end(const scs_event_t, const void* const, const scs_context_t) {
    const float temperature = g_temperature.load(std::memory_order_relaxed);
    const float input = g_input_brake.load(std::memory_order_relaxed);
    const float effective = g_effective_brake.load(std::memory_order_relaxed);

    // Phase 1 is observational: prove the native values and curve before applying control.
    // OutputDebugString keeps the lab independent from TransPoli persistence/API.
    if (input > 0.05f || temperature >= 200.0f) {
        char buffer[192]{};
        std::snprintf(buffer, sizeof(buffer),
            "TransPoli BrakeLab | temp=%.1fC input=%.3f effective=%.3f fade=%.3f\n",
            temperature, input, effective, fade_factor(temperature));
        OutputDebugStringA(buffer);
    }
}
}

extern "C" SCSAPI_RESULT scs_telemetry_init(
    const scs_u32_t version,
    const scs_telemetry_init_params_t* const params) {

    if (version != SCS_TELEMETRY_VERSION_1_01) return SCS_RESULT_unsupported;
    const auto* p = static_cast<const scs_telemetry_init_params_v101_t*>(params);

    if (p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_brake_temperature,
        SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none,
        telemetry_float, &g_temperature) != SCS_RESULT_ok) return SCS_RESULT_generic_error;

    if (p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_input_brake,
        SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none,
        telemetry_float, &g_input_brake) != SCS_RESULT_ok) return SCS_RESULT_generic_error;

    if (p->register_for_channel(SCS_TELEMETRY_TRUCK_CHANNEL_effective_brake,
        SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none,
        telemetry_float, &g_effective_brake) != SCS_RESULT_ok) return SCS_RESULT_generic_error;

    p->register_for_event(SCS_TELEMETRY_EVENT_frame_end, frame_end, nullptr);
    p->common.log(SCS_LOG_TYPE_message,
        "TransPoli BrakeLab: observational brake-temperature probe registered.");
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_VOID scs_telemetry_shutdown() {}
