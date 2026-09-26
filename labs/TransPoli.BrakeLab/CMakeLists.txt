cmake_minimum_required(VERSION 3.20)
project(TransPoliBrakeLab LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "BrakeLab currently targets Windows x64 only.")
endif()

set(SCS_SDK_INCLUDE_DIR "" CACHE PATH "Official SCS SDK include directory")
if(NOT EXISTS "${SCS_SDK_INCLUDE_DIR}/scssdk_telemetry.h")
  message(FATAL_ERROR "Set SCS_SDK_INCLUDE_DIR to the official SCS SDK include directory.")
endif()

add_library(TransPoli.BrakeLab SHARED src/BrakeLab.cpp)
target_compile_features(TransPoli.BrakeLab PRIVATE cxx_std_17)
target_include_directories(TransPoli.BrakeLab PRIVATE "${SCS_SDK_INCLUDE_DIR}")
set_target_properties(TransPoli.BrakeLab PROPERTIES PREFIX "" OUTPUT_NAME "TransPoli.BrakeLab")
