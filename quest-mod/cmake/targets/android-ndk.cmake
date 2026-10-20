include_guard()

if(NOT DEFINED CMAKE_ANDROID_NDK)
    if(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/ndkpath.txt")
        file(STRINGS "${CMAKE_CURRENT_SOURCE_DIR}/ndkpath.txt" CMAKE_ANDROID_NDK)
    else()
        if(DEFINED ENV{ANDROID_NDK_HOME} AND EXISTS "$ENV{ANDROID_NDK_HOME}")
            set(CMAKE_ANDROID_NDK $ENV{ANDROID_NDK_HOME})
        elseif(DEFINED ENV{ANDROID_NDK_LATEST_HOME} AND EXISTS "$ENV{ANDROID_NDK_LATEST_HOME}")
            set(CMAKE_ANDROID_NDK $ENV{ANDROID_NDK_LATEST_HOME})
        endif()
    endif()
endif()

if(NOT DEFINED CMAKE_ANDROID_NDK)
    message(FATAL_ERROR "Android NDK not found. Run 'qpm ndk resolve --download' from quest-mod, set ANDROID_NDK_HOME, or add the NDK directory to quest-mod/ndkpath.txt.")
endif()

string(REPLACE "\\" "/" CMAKE_ANDROID_NDK ${CMAKE_ANDROID_NDK})

message(STATUS "Using NDK ${CMAKE_ANDROID_NDK}")

if(NOT EXISTS "${CMAKE_ANDROID_NDK}/build/cmake/android.toolchain.cmake")
    message(FATAL_ERROR "Invalid Android NDK path '${CMAKE_ANDROID_NDK}': build/cmake/android.toolchain.cmake was not found. Check quest-mod/ndkpath.txt or ANDROID_NDK_HOME.")
endif()

# check if contains space
if(CMAKE_ANDROID_NDK MATCHES " ")
    message(FATAL_ERROR "CMAKE_ANDROID_NDK contains a space! Please remove it!")
endif()


# Quest is armv8-64
# Uses Android 12-14 now

set(ANDROID_PLATFORM 24)
set(ANDROID_ABI arm64-v8a)
set(ANDROID_STL c++_static)
set(ANDROID_USE_LEGACY_TOOLCHAIN_FILE OFF)

#TODO: Fix this warning
if(CMAKE_TOOLCHAIN_FILE MATCHES ".+")
    message(WARNING "CMAKE_TOOLCHAIN_FILE already defined, overwriting! ${CMAKE_TOOLCHAIN_FILE}")
endif()

set(CMAKE_TOOLCHAIN_FILE ${CMAKE_ANDROID_NDK}/build/cmake/android.toolchain.cmake)

# Set triplet for vcpkg
set(VCPKG_TARGET_TRIPLET arm64-android)
