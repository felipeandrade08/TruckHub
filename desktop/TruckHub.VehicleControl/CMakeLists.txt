cmake_minimum_required(VERSION 3.20)
project(TruckHubVehicleControl LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "TruckHub VehicleControl targets Windows x64 only.")
endif()

set(SCS_SDK_INCLUDE_DIR "" CACHE PATH "Official SCS SDK include directory")
if(NOT EXISTS "${SCS_SDK_INCLUDE_DIR}/scssdk_input.h")
  message(FATAL_ERROR "Set SCS_SDK_INCLUDE_DIR to the official SCS SDK include directory.")
endif()

add_library(TruckHub.VehicleControl SHARED src/VehicleControl.cpp)
target_compile_features(TruckHub.VehicleControl PRIVATE cxx_std_17)
target_include_directories(TruckHub.VehicleControl PRIVATE "${SCS_SDK_INCLUDE_DIR}")
set_target_properties(TruckHub.VehicleControl PROPERTIES PREFIX "" OUTPUT_NAME "TruckHub.VehicleControl")

if(MSVC)
  target_link_options(TruckHub.VehicleControl PRIVATE
    "/EXPORT:scs_input_init"
    "/EXPORT:scs_input_shutdown"
    "/EXPORT:scs_telemetry_init"
    "/EXPORT:scs_telemetry_shutdown"
  )
endif()
