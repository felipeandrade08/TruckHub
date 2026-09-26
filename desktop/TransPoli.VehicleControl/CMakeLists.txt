cmake_minimum_required(VERSION 3.20)
project(TransPoliVehicleControl LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "TransPoli VehicleControl targets Windows x64 only.")
endif()

set(SCS_SDK_INCLUDE_DIR "" CACHE PATH "Official SCS SDK include directory")
if(NOT EXISTS "${SCS_SDK_INCLUDE_DIR}/scssdk_input.h")
  message(FATAL_ERROR "Set SCS_SDK_INCLUDE_DIR to the official SCS SDK include directory.")
endif()

add_library(TransPoli.VehicleControl SHARED src/VehicleControl.cpp)
target_compile_features(TransPoli.VehicleControl PRIVATE cxx_std_17)
target_include_directories(TransPoli.VehicleControl PRIVATE "${SCS_SDK_INCLUDE_DIR}")
set_target_properties(TransPoli.VehicleControl PROPERTIES PREFIX "" OUTPUT_NAME "TransPoli.VehicleControl")

if(MSVC)
  target_link_options(TransPoli.VehicleControl PRIVATE
    "/EXPORT:scs_input_init"
    "/EXPORT:scs_input_shutdown"
    "/EXPORT:scs_telemetry_init"
    "/EXPORT:scs_telemetry_shutdown"
  )
endif()
