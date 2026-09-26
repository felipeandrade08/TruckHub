cmake_minimum_required(VERSION 3.20)
project(TransPoliVehicleControlLab LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "VehicleControlLab currently targets Windows x64 only.")
endif()

set(SCS_SDK_INCLUDE_DIR "" CACHE PATH "Official SCS SDK include directory")
if(NOT EXISTS "${SCS_SDK_INCLUDE_DIR}/scssdk_input.h")
  message(FATAL_ERROR "Set SCS_SDK_INCLUDE_DIR to the official SCS SDK include directory.")
endif()

add_library(TransPoli.VehicleControlLab SHARED src/VehicleControlLab.cpp)
target_compile_features(TransPoli.VehicleControlLab PRIVATE cxx_std_17)
target_include_directories(TransPoli.VehicleControlLab PRIVATE "${SCS_SDK_INCLUDE_DIR}")
set_target_properties(TransPoli.VehicleControlLab PROPERTIES PREFIX "" OUTPUT_NAME "TransPoli.VehicleControlLab")
