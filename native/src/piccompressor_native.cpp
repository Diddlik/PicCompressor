#include "piccompressor_native.h"

#include <algorithm>
#include <atomic>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <new>
#include <string>
#include <vector>

#if defined(PC_HAVE_JPEGLI)
#include "lib/base/span.h"
#include "lib/extras/alpha_blend.h"
#include "lib/extras/dec/color_hints.h"
#include "lib/extras/dec/decode.h"
#include "lib/extras/dec/jpegli.h"
#include "lib/extras/enc/jpegli.h"
#include "lib/extras/packed_image.h"
#endif

struct pc_cancel_handle {
  std::atomic_bool requested{false};
};

namespace {

constexpr const char* kJpegliUnavailable =
    "Jpegli is not linked into this native wrapper build.";
constexpr const char* kGuetzliUnavailable =
    "Guetzli is not linked into this native wrapper build.";

void CopyError(const char* message, char* output, size_t capacity) {
  if (output == nullptr || capacity == 0) {
    return;
  }

  const size_t length = std::min(std::strlen(message), capacity - 1);
  std::memcpy(output, message, length);
  output[length] = '\0';
}

bool MissingPath(const char* path) {
  return path == nullptr || path[0] == '\0';
}

#if defined(PC_HAVE_JPEGLI)
std::filesystem::path Utf8Path(const char* value) {
  return std::filesystem::path(reinterpret_cast<const char8_t*>(value));
}

bool ReadFile(const char* path, std::vector<uint8_t>* bytes) {
  std::ifstream stream(Utf8Path(path), std::ios::binary);
  if (!stream) {
    return false;
  }

  stream.seekg(0, std::ios::end);
  const std::streamoff size = stream.tellg();
  if (size <= 0) {
    return false;
  }

  bytes->resize(static_cast<size_t>(size));
  stream.seekg(0, std::ios::beg);
  return static_cast<bool>(stream.read(
      reinterpret_cast<char*>(bytes->data()),
      static_cast<std::streamsize>(size)));
}

bool WriteFile(const char* path, const std::vector<uint8_t>& bytes) {
  std::ofstream stream(
      Utf8Path(path), std::ios::binary | std::ios::trunc);
  return stream &&
         static_cast<bool>(stream.write(
             reinterpret_cast<const char*>(bytes.data()),
             static_cast<std::streamsize>(bytes.size())));
}

bool IsJpeg(const std::vector<uint8_t>& bytes) {
  return bytes.size() >= 2 && bytes[0] == 0xff && bytes[1] == 0xd8;
}

const char* ChromaSubsampling(pc_chroma_subsampling value) {
  switch (value) {
    case PC_CHROMA_444:
      return "444";
    case PC_CHROMA_440:
      return "440";
    case PC_CHROMA_422:
      return "422";
    case PC_CHROMA_420:
      return "420";
  }
  return "";
}
#endif

}  // namespace

uint32_t pc_abi_version(void) { return PC_ABI_VERSION; }

int32_t pc_engine_available(pc_engine engine) {
  switch (engine) {
    case PC_ENGINE_JPEGLI:
#if defined(PC_HAVE_JPEGLI)
      return 1;
#else
      return 0;
#endif
    case PC_ENGINE_GUETZLI:
      return 0;
    default:
      return 0;
  }
}

const char* pc_engine_build_version(pc_engine engine) {
  switch (engine) {
    case PC_ENGINE_JPEGLI:
#if defined(PC_HAVE_JPEGLI)
      return PC_JPEGLI_BUILD_VERSION;
#else
      return "";
#endif
    default:
      return "";
  }
}

const char* pc_engine_source_revision(pc_engine engine) {
  switch (engine) {
    case PC_ENGINE_JPEGLI:
      return PC_JPEGLI_SOURCE_REVISION;
    case PC_ENGINE_GUETZLI:
      return PC_GUETZLI_SOURCE_REVISION;
    default:
      return "";
  }
}

const char* pc_engine_unavailable_reason(pc_engine engine) {
  switch (engine) {
    case PC_ENGINE_JPEGLI:
#if defined(PC_HAVE_JPEGLI)
      return "";
#else
      return kJpegliUnavailable;
#endif
    case PC_ENGINE_GUETZLI:
      return kGuetzliUnavailable;
    default:
      return "Unknown native engine.";
  }
}

pc_cancel_handle* pc_cancel_create(void) {
  return new (std::nothrow) pc_cancel_handle();
}

void pc_cancel_request(pc_cancel_handle* handle) {
  if (handle != nullptr) {
    handle->requested.store(true, std::memory_order_release);
  }
}

int32_t pc_cancel_is_requested(const pc_cancel_handle* handle) {
  return handle != nullptr &&
         handle->requested.load(std::memory_order_acquire);
}

void pc_cancel_destroy(pc_cancel_handle* handle) { delete handle; }

pc_status pc_encode_jpegli(
    const char* input_path_utf8,
    const char* output_path_utf8,
    const pc_jpegli_options* options,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity) {
  if (MissingPath(input_path_utf8) || MissingPath(output_path_utf8) ||
      options == nullptr || options->struct_size != sizeof(pc_jpegli_options) ||
      options->quality < 1 || options->quality > 100 ||
      options->chroma_subsampling < PC_CHROMA_444 ||
      options->chroma_subsampling > PC_CHROMA_420 ||
      options->progressive_level < 0 || options->progressive_level > 2 ||
      options->alpha_red < 0 || options->alpha_red > 255 ||
      options->alpha_green < 0 || options->alpha_green > 255 ||
      options->alpha_blue < 0 || options->alpha_blue > 255) {
    CopyError("Invalid Jpegli encode request.", error_utf8, error_capacity);
    return PC_STATUS_INVALID_ARGUMENT;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

#if defined(PC_HAVE_JPEGLI)
  std::vector<uint8_t> input;
  if (!ReadFile(input_path_utf8, &input)) {
    CopyError("Input image could not be read.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  jpegli::extras::PackedPixelFile pixels;
  const bool decoded =
      IsJpeg(input)
          ? static_cast<bool>(jpegli::extras::DecodeJpeg(
                input, jpegli::extras::JpegDecompressParams{}, nullptr, &pixels))
          : static_cast<bool>(jpegli::extras::DecodeBytes(
                jpegli::Bytes(input), jpegli::extras::ColorHints{}, &pixels));
  if (!decoded) {
    CopyError("Input image could not be decoded.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  const float background[3] = {
      options->alpha_red / 255.0f,
      options->alpha_green / 255.0f,
      options->alpha_blue / 255.0f};
  if (!jpegli::extras::AlphaBlend(&pixels, background)) {
    CopyError("Input alpha channel could not be flattened.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

  jpegli::extras::JpegSettings settings;
  settings.quality = static_cast<float>(options->quality);
  settings.chroma_subsampling =
      ChromaSubsampling(options->chroma_subsampling);
  settings.progressive_level = options->progressive_level;
  std::vector<uint8_t> output;
  if (!jpegli::extras::EncodeJpeg(pixels, settings, nullptr, &output)) {
    CopyError("Jpegli encoding failed.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

  if (!WriteFile(output_path_utf8, output)) {
    CopyError("Encoded output could not be written.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }
  return PC_STATUS_OK;
#else
  CopyError(kJpegliUnavailable, error_utf8, error_capacity);
  return PC_STATUS_ENGINE_UNAVAILABLE;
#endif
}

pc_status pc_encode_guetzli(
    const char* input_path_utf8,
    const char* output_path_utf8,
    int32_t quality,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity) {
  if (MissingPath(input_path_utf8) || MissingPath(output_path_utf8) ||
      quality < 1 || quality > 100) {
    CopyError("Invalid Guetzli encode request.", error_utf8, error_capacity);
    return PC_STATUS_INVALID_ARGUMENT;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

  CopyError(kGuetzliUnavailable, error_utf8, error_capacity);
  return PC_STATUS_ENGINE_UNAVAILABLE;
}
