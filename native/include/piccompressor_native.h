#ifndef PICCOMPRESSOR_NATIVE_H_
#define PICCOMPRESSOR_NATIVE_H_

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(PC_NATIVE_BUILD)
#define PC_API __declspec(dllexport)
#else
#define PC_API __declspec(dllimport)
#endif
#else
#define PC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define PC_ABI_VERSION 6u

typedef enum pc_status {
  PC_STATUS_OK = 0,
  PC_STATUS_INVALID_ARGUMENT = 1,
  PC_STATUS_ENGINE_UNAVAILABLE = 2,
  PC_STATUS_ENCODE_FAILED = 3,
  PC_STATUS_CANCELED = 4
} pc_status;

typedef enum pc_engine {
  PC_ENGINE_JPEGLI = 1,
  PC_ENGINE_GUETZLI = 2
} pc_engine;

typedef enum pc_chroma_subsampling {
  PC_CHROMA_444 = 0,
  PC_CHROMA_440 = 1,
  PC_CHROMA_422 = 2,
  PC_CHROMA_420 = 3
} pc_chroma_subsampling;

typedef enum pc_exif_policy {
  PC_EXIF_KEEP = 0,
  PC_EXIF_PRIVATE = 1,
  PC_EXIF_REMOVE = 2
} pc_exif_policy;

typedef enum pc_color_profile_policy {
  PC_COLOR_PROFILE_PRESERVE = 0,
  PC_COLOR_PROFILE_SRGB = 1,
  PC_COLOR_PROFILE_REMOVE = 2
} pc_color_profile_policy;

typedef struct pc_jpegli_options {
  uint32_t struct_size;
  int32_t quality;
  pc_chroma_subsampling chroma_subsampling;
  int32_t progressive_level;
  int32_t alpha_red;
  int32_t alpha_green;
  int32_t alpha_blue;
  pc_exif_policy exif_policy;
  pc_color_profile_policy color_profile_policy;
} pc_jpegli_options;

// Downscaled, upright, sRGB preview of an input image. The wrapper owns the
// pixel buffer until pc_preview_release is called.
typedef struct pc_preview_options {
  uint32_t struct_size;
  int32_t max_edge;
  int32_t alpha_red;
  int32_t alpha_green;
  int32_t alpha_blue;
} pc_preview_options;

typedef struct pc_preview {
  uint32_t struct_size;
  int32_t width;
  int32_t height;
  uint8_t* rgb;
  size_t rgb_size;
  // Maße des aufrecht gedrehten Originals vor dem Verkleinern. Nur damit kann der
  // Aufrufer die tatsächliche Anzeigeskalierung bestimmen.
  int32_t source_width;
  int32_t source_height;
} pc_preview;

typedef struct pc_cancel_handle pc_cancel_handle;

PC_API uint32_t pc_abi_version(void);
PC_API int32_t pc_engine_available(pc_engine engine);
PC_API const char* pc_engine_build_version(pc_engine engine);
PC_API const char* pc_engine_source_revision(pc_engine engine);
PC_API const char* pc_engine_unavailable_reason(pc_engine engine);

PC_API pc_cancel_handle* pc_cancel_create(void);
PC_API void pc_cancel_request(pc_cancel_handle* handle);
PC_API int32_t pc_cancel_is_requested(const pc_cancel_handle* handle);
PC_API void pc_cancel_destroy(pc_cancel_handle* handle);

PC_API pc_status pc_encode_jpegli(
    const char* input_path_utf8,
    const char* output_path_utf8,
    const pc_jpegli_options* options,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity);

PC_API pc_status pc_render_preview(
    const char* input_path_utf8,
    const pc_preview_options* options,
    pc_preview* preview,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity);

// Kodiert die Eingabe mit den angegebenen Optionen ausschliesslich im Speicher und
// liefert die Vorschau des Ergebnisses samt seiner Groesse in Bytes. Es wird keine
// Datei geschrieben: die Vorschau eines noch nicht komprimierten Bildes darf nichts
// veroeffentlichen.
PC_API pc_status pc_render_encoded_preview(
    const char* input_path_utf8,
    const pc_jpegli_options* options,
    const pc_preview_options* preview_options,
    pc_preview* preview,
    int64_t* encoded_size,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity);

PC_API void pc_preview_release(pc_preview* preview);

PC_API pc_status pc_encode_guetzli(
    const char* input_path_utf8,
    const char* output_path_utf8,
    int32_t quality,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity);

#ifdef __cplusplus
}
#endif

#endif
