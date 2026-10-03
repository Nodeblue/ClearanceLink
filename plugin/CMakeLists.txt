# XPConnect X-Plane plugin: draws BeyondATC's AI traffic with XPMP2 and CSL models.
# Windows (the release target): cmake -S plugin -B build/plugin -A x64 && cmake --build build/plugin --config Release
# Linux (compile check): cmake -S plugin -B build/plugin && cmake --build build/plugin
# Output: build/plugin/XPConnect/64/<win|lin|mac>.xpl plus XPConnect/Resources (XPMP2's support files).
cmake_minimum_required(VERSION 3.16)
project(XPConnectPlugin VERSION 0.4.2 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
# XPMP2 compiles with the DLL C runtime (/MD), as X-Plane plugins usually do; match it.
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()

include(FetchContent)
# XPMP2 (MIT, https://github.com/TwinFan/XPMP2) brings the X-Plane SDK headers and XPLM_64.lib in lib/SDK.
FetchContent_Declare(XPMP2
    GIT_REPOSITORY https://github.com/TwinFan/XPMP2.git
    GIT_TAG        v3.7.0
    GIT_SHALLOW    TRUE)
FetchContent_GetProperties(XPMP2)
if(NOT xpmp2_POPULATED)
    FetchContent_Populate(XPMP2)
endif()
# Patch: CSL packages split over several folders under one EXPORT_NAME (Bluebell's BB_Boeing1..3, BB_Airbus..2) load
# all their models; XPMP2 3.7.0 looks for every .obj in the first folder only and drops about 750 Bluebell models.
set(XPMP2_PATCH "${CMAKE_CURRENT_SOURCE_DIR}/cmake/xpmp2-split-packages.patch")
execute_process(COMMAND git apply --reverse --check "${XPMP2_PATCH}"
    WORKING_DIRECTORY "${xpmp2_SOURCE_DIR}" RESULT_VARIABLE XPMP2_PATCHED OUTPUT_QUIET ERROR_QUIET)
if(NOT XPMP2_PATCHED EQUAL 0)
    execute_process(COMMAND git apply "${XPMP2_PATCH}" WORKING_DIRECTORY "${xpmp2_SOURCE_DIR}" RESULT_VARIABLE XPMP2_PATCH_RESULT)
    if(NOT XPMP2_PATCH_RESULT EQUAL 0)
        message(FATAL_ERROR "Could not apply ${XPMP2_PATCH} to XPMP2 (delete ${xpmp2_SOURCE_DIR} after changing the patch)")
    endif()
endif()
set(XPlaneSDK_DIR "${xpmp2_SOURCE_DIR}/lib/SDK" CACHE PATH "X-Plane SDK" FORCE)
list(APPEND CMAKE_MODULE_PATH "${xpmp2_SOURCE_DIR}/cmake")
add_subdirectory(${xpmp2_SOURCE_DIR} ${xpmp2_BINARY_DIR} EXCLUDE_FROM_ALL)

add_library(XPConnectPlugin MODULE
    src/Plugin.cpp
    src/Protocol.h
    src/Protocol.cpp
    src/Udp.h
    src/Udp.cpp
    src/TrafficAircraft.h
    src/TrafficAircraft.cpp
    src/CslFolders.h
    src/CslFolders.cpp
    src/GroundFit.h
    src/GroundFit.cpp
    src/Motion.h
    src/Motion.cpp)

target_include_directories(XPConnectPlugin PRIVATE src "${xpmp2_SOURCE_DIR}/inc")
target_compile_definitions(XPConnectPlugin PRIVATE
    XPLM200=1 XPLM210=1 XPLM300=1 XPLM301=1 XPLM303=1 XPLM400=1
    APL=$<BOOL:${APPLE}> IBM=$<BOOL:${WIN32}> LIN=$<AND:$<BOOL:${UNIX}>,$<NOT:$<BOOL:${APPLE}>>>
    XPCONNECT_PLUGIN_VERSION="${PROJECT_VERSION}")

if(WIN32)
    find_package(XPlaneSDK REQUIRED XPLM)
    target_compile_definitions(XPConnectPlugin PRIVATE _WIN32_WINNT=0x0601 WIN32_LEAN_AND_MEAN NOMINMAX _CRT_SECURE_NO_WARNINGS)
    target_link_libraries(XPConnectPlugin PRIVATE XPMP2 XPlaneSDK::XPLM ws2_32 wsock32 iphlpapi)
    set(XPL_NAME win)
elseif(APPLE)
    find_package(XPlaneSDK REQUIRED XPLM)
    target_link_libraries(XPConnectPlugin PRIVATE XPMP2 XPlaneSDK::XPLM)
    set(XPL_NAME mac)
else()
    find_package(XPlaneSDK REQUIRED)
    target_include_directories(XPConnectPlugin PRIVATE ${XPlaneSDK_INCLUDE_DIRS})
    target_link_libraries(XPConnectPlugin PRIVATE XPMP2 pthread)
    target_link_options(XPConnectPlugin PRIVATE -Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/src/exports.txt)
    set(XPL_NAME lin)
endif()

if(MSVC)
    target_compile_options(XPConnectPlugin PRIVATE /W4 /WX /EHsc /wd4996)
else()
    target_compile_options(XPConnectPlugin PRIVATE -Wall -Wextra -Werror -fvisibility=hidden)
endif()

set_target_properties(XPConnectPlugin PROPERTIES
    PREFIX ""
    OUTPUT_NAME "${XPL_NAME}"
    SUFFIX ".xpl"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/XPConnect/64"
    LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/XPConnect/64"
    LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/XPConnect/64"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/XPConnect/64"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/XPConnect/64"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/XPConnect/64")

# XPMP2 needs its support files (Doc8643.txt, related.txt, ...) next to the plugin.
add_custom_command(TARGET XPConnectPlugin POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${xpmp2_SOURCE_DIR}/Resources" "${CMAKE_BINARY_DIR}/XPConnect/Resources"
    COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_CURRENT_SOURCE_DIR}/README.txt" "${CMAKE_BINARY_DIR}/XPConnect/README.txt"
    COMMAND ${CMAKE_COMMAND} -E copy "${xpmp2_SOURCE_DIR}/LICENSE" "${CMAKE_BINARY_DIR}/XPConnect/XPMP2-LICENSE.txt")

# Tests for the X-Plane-independent parts. Run: ctest --test-dir build/plugin -C Release
enable_testing()
add_executable(XPConnectPluginTests tests/PluginTests.cpp src/Protocol.cpp src/CslFolders.cpp src/GroundFit.cpp src/Motion.cpp)
target_include_directories(XPConnectPluginTests PRIVATE src)
add_test(NAME PluginTests COMMAND XPConnectPluginTests)

# Checks the ground fit against a whole CSL library: xpconnect-groundfit <CSL folder>... > table.csv
add_executable(xpconnect-groundfit tools/GroundFitTable.cpp src/GroundFit.cpp)
target_include_directories(xpconnect-groundfit PRIVATE src)
set_target_properties(xpconnect-groundfit PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/XPConnect/tools"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/XPConnect/tools"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/XPConnect/tools")
