cmake_minimum_required(VERSION 3.25)
project(PicCompressorNative VERSION 1.0.0 LANGUAGES C CXX)

include(CTest)
include(dependencies.lock.cmake)
include(FetchContent)

option(PC_ENABLE_JPEGLI "Fetch and link the pinned Jpegli revision." ON)
option(PC_ENABLE_GUETZLI "Fetch and link the pinned Guetzli revision." ON)

if(PC_ENABLE_JPEGLI)
    set(PC_PARENT_BUILD_TESTING ${BUILD_TESTING})
    set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
    set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_TOOLS ON CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_DEVTOOLS OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_DOXYGEN OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_MANPAGES OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_BENCHMARK OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_JNI OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_SJPEG OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_OPENEXR OFF CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_SKCMS ON CACHE BOOL "" FORCE)
    set(JPEGLI_ENABLE_TCMALLOC OFF CACHE BOOL "" FORCE)
    set(JPEGLI_BUNDLE_LIBPNG ON CACHE BOOL "" FORCE)
    set(JPEGLI_STATIC ON CACHE BOOL "" FORCE)
    # The bundled libpng regenerates pnglibconf.h only when it finds an AWK on
    # PATH, and that generated path fails to see zlib's headers under the Visual
    # Studio generator. Whether it triggers depends on the developer's PATH, so
    # the build is pinned to libpng's prebuilt configuration instead.
    set(AWK "" CACHE FILEPATH "Disabled: use libpng's prebuilt pnglibconf.h" FORCE)
    FetchContent_Declare(
        jpegli
        GIT_REPOSITORY https://github.com/google/jpegli.git
        GIT_TAG ${PC_JPEGLI_REVISION}
        GIT_SHALLOW FALSE
        GIT_SUBMODULES
            third_party/highway
            third_party/libjpeg-turbo
            third_party/libpng
            third_party/skcms
            third_party/zlib
        EXCLUDE_FROM_ALL)
    FetchContent_MakeAvailable(jpegli)
    get_target_property(PC_JPEGLI_BUILD_VERSION jpegli_cms VERSION)
    if(NOT PC_JPEGLI_BUILD_VERSION STREQUAL PC_JPEGLI_VERSION)
        message(FATAL_ERROR
            "Pinned Jpegli version mismatch: expected ${PC_JPEGLI_VERSION}, "
            "got ${PC_JPEGLI_BUILD_VERSION}")
    endif()
    set(BUILD_TESTING ${PC_PARENT_BUILD_TESTING} CACHE BOOL "" FORCE)
endif()

if(PC_ENABLE_GUETZLI)
    # Guetzli ships no CMake build; MakeAvailable only fetches the pinned source.
    # The core library (guetzli/*.cc without the CLI main) plus butteraugli have no
    # external dependencies, so they are compiled directly into a static library.
    FetchContent_Declare(
        guetzli
        GIT_REPOSITORY https://github.com/google/guetzli.git
        GIT_TAG ${PC_GUETZLI_REVISION}
        GIT_SHALLOW FALSE
        EXCLUDE_FROM_ALL)
    FetchContent_MakeAvailable(guetzli)

    file(GLOB PC_GUETZLI_SOURCES CONFIGURE_DEPENDS
        ${guetzli_SOURCE_DIR}/guetzli/*.cc)
    # guetzli.cc is the command-line front-end and pulls in libpng; it is excluded.
    list(REMOVE_ITEM PC_GUETZLI_SOURCES ${guetzli_SOURCE_DIR}/guetzli/guetzli.cc)

    add_library(guetzli_static STATIC
        ${PC_GUETZLI_SOURCES}
        ${guetzli_SOURCE_DIR}/third_party/butteraugli/butteraugli/butteraugli.cc)
    # The core sources include "guetzli/..." relative to the root and
    # "butteraugli/butteraugli.h" relative to third_party/butteraugli.
    target_include_directories(guetzli_static PUBLIC
        ${guetzli_SOURCE_DIR}
        ${guetzli_SOURCE_DIR}/third_party/butteraugli)
    target_compile_features(guetzli_static PRIVATE cxx_std_17)
    # The static library is linked into the shared piccompressor_native. ELF shared
    # objects require every constituent object to be position-independent; Windows
    # DLLs do not, so this only matters on Linux/macOS but is harmless elsewhere.
    set_target_properties(guetzli_static PROPERTIES POSITION_INDEPENDENT_CODE ON)
    # Upstream third-party code: silence its warnings instead of failing the build.
    if(MSVC)
        target_compile_options(guetzli_static PRIVATE /w /EHsc)
        target_compile_definitions(guetzli_static PRIVATE
            _USE_MATH_DEFINES _CRT_SECURE_NO_WARNINGS NOMINMAX)
    else()
        target_compile_options(guetzli_static PRIVATE -w)
        target_compile_definitions(guetzli_static PRIVATE _USE_MATH_DEFINES)
    endif()
endif()

add_library(piccompressor_native SHARED
    src/piccompressor_native.cpp
    src/pc_exif.cpp)
set_target_properties(piccompressor_native PROPERTIES PREFIX "")
target_compile_features(piccompressor_native PRIVATE cxx_std_20)
target_compile_definitions(piccompressor_native PRIVATE PC_NATIVE_BUILD)
target_compile_definitions(piccompressor_native PRIVATE
    PC_JPEGLI_SOURCE_REVISION="${PC_JPEGLI_REVISION}"
    PC_GUETZLI_SOURCE_REVISION="${PC_GUETZLI_REVISION}")
target_include_directories(piccompressor_native
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>)

if(PC_ENABLE_JPEGLI)
    target_compile_definitions(piccompressor_native PRIVATE
        PC_HAVE_JPEGLI
        PC_JPEGLI_BUILD_VERSION="${PC_JPEGLI_BUILD_VERSION}")
    target_include_directories(piccompressor_native SYSTEM PRIVATE
        ${jpegli_SOURCE_DIR})
    target_link_libraries(piccompressor_native PRIVATE
        jpegli_extras-internal
        jpegli_cms)
endif()

if(PC_ENABLE_GUETZLI)
    target_compile_definitions(piccompressor_native PRIVATE
        PC_HAVE_GUETZLI
        PC_GUETZLI_BUILD_VERSION="${PC_GUETZLI_VERSION}")
    target_include_directories(piccompressor_native SYSTEM PRIVATE
        ${guetzli_SOURCE_DIR})
    target_link_libraries(piccompressor_native PRIVATE guetzli_static)
endif()

if(MSVC)
    target_compile_options(piccompressor_native PRIVATE /W4 /WX /permissive-)
else()
    target_compile_options(piccompressor_native PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

if(MINGW)
    target_link_options(piccompressor_native PRIVATE -static)
endif()

if(PC_OUTPUT_DIR)
    foreach(config DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
        set_target_properties(piccompressor_native PROPERTIES
            RUNTIME_OUTPUT_DIRECTORY_${config} "${PC_OUTPUT_DIR}"
            LIBRARY_OUTPUT_DIRECTORY_${config} "${PC_OUTPUT_DIR}"
            PDB_OUTPUT_DIRECTORY_${config} "${PC_OUTPUT_DIR}"
            COMPILE_PDB_OUTPUT_DIRECTORY_${config} "${PC_OUTPUT_DIR}")
    endforeach()
endif()

if(BUILD_TESTING)
    add_executable(piccompressor_native_tests tests/abi_smoke.cpp)
    target_compile_features(piccompressor_native_tests PRIVATE cxx_std_20)
    if(PC_ENABLE_JPEGLI)
        target_compile_definitions(piccompressor_native_tests PRIVATE
            PC_EXPECT_JPEGLI)
    endif()
    if(PC_ENABLE_GUETZLI)
        target_compile_definitions(piccompressor_native_tests PRIVATE
            PC_EXPECT_GUETZLI)
    endif()
    target_link_libraries(piccompressor_native_tests PRIVATE piccompressor_native)
    add_test(NAME piccompressor_native_abi COMMAND piccompressor_native_tests)

    # The EXIF handling is independent of Jpegli and is tested directly.
    add_executable(piccompressor_exif_tests
        tests/exif_test.cpp
        src/pc_exif.cpp)
    target_compile_features(piccompressor_exif_tests PRIVATE cxx_std_20)
    target_include_directories(piccompressor_exif_tests PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src)
    if(MSVC)
        target_compile_options(piccompressor_exif_tests PRIVATE /W4 /WX /permissive-)
    else()
        target_compile_options(piccompressor_exif_tests PRIVATE
            -Wall -Wextra -Wpedantic -Werror)
    endif()
    add_test(NAME piccompressor_native_exif COMMAND piccompressor_exif_tests)
endif()
