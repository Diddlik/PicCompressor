#include "piccompressor_native.h"

#include <algorithm>
#include <atomic>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <new>
#include <string>
#include <utility>
#include <vector>

#include "pc_exif.h"

#if defined(PC_HAVE_JPEGLI)
#include "lib/base/span.h"
#include "lib/cms/cms.h"
#include "lib/cms/color_encoding_internal.h"
#include "lib/extras/alpha_blend.h"
#include "lib/extras/dec/color_hints.h"
#include "lib/extras/dec/decode.h"
#include "lib/extras/dec/jpegli.h"
#include "lib/extras/enc/jpegli.h"
#include "lib/extras/packed_image.h"
#endif

#if defined(PC_HAVE_GUETZLI)
#include "guetzli/processor.h"
#include "guetzli/quality.h"
#include "guetzli/stats.h"
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

// Maps a source pixel to its position after the EXIF orientation has been
// applied. Orientations 5..8 swap the axes, so the caller must size the
// destination accordingly.
void MapOrientedCoordinate(int orientation, size_t x, size_t y, size_t width,
                           size_t height, size_t* out_x, size_t* out_y) {
  switch (orientation) {
    case 2:  // mirror horizontal
      *out_x = width - 1 - x;
      *out_y = y;
      return;
    case 3:  // rotate 180
      *out_x = width - 1 - x;
      *out_y = height - 1 - y;
      return;
    case 4:  // mirror vertical
      *out_x = x;
      *out_y = height - 1 - y;
      return;
    case 5:  // transpose
      *out_x = y;
      *out_y = x;
      return;
    case 6:  // rotate 90 clockwise
      *out_x = height - 1 - y;
      *out_y = x;
      return;
    case 7:  // transverse
      *out_x = height - 1 - y;
      *out_y = width - 1 - x;
      return;
    case 8:  // rotate 90 counter-clockwise
      *out_x = y;
      *out_y = width - 1 - x;
      return;
    default:
      *out_x = x;
      *out_y = y;
      return;
  }
}

// Rotates the decoded pixels so the result is visually upright regardless of
// whether the EXIF orientation survives into the output (requirement 8.2).
bool ApplyOrientation(jpegli::extras::PackedPixelFile* ppf, int orientation) {
  if (orientation <= pc::kOrientationIdentity || orientation > 8) {
    return true;
  }
  if (ppf->frames.size() != 1) {
    return false;
  }

  jpegli::extras::PackedImage& source = ppf->frames[0].color;
  const bool swap_axes = orientation >= 5;
  const size_t target_width = swap_axes ? source.ysize : source.xsize;
  const size_t target_height = swap_axes ? source.xsize : source.ysize;

  auto created = jpegli::extras::PackedImage::Create(target_width,
                                                     target_height,
                                                     source.format);
  if (!created.ok()) {
    return false;
  }
  jpegli::extras::PackedImage target = std::move(created).value_();

  const size_t pixel_stride = source.pixel_stride();
  for (size_t y = 0; y < source.ysize; ++y) {
    for (size_t x = 0; x < source.xsize; ++x) {
      size_t target_x = 0;
      size_t target_y = 0;
      MapOrientedCoordinate(orientation, x, y, source.xsize, source.ysize,
                            &target_x, &target_y);
      std::memcpy(target.pixels(target_y, target_x, 0),
                  source.const_pixels(y, x, 0), pixel_stride);
    }
  }

  ppf->frames[0].color = std::move(target);
  ppf->info.xsize = static_cast<uint32_t>(target_width);
  ppf->info.ysize = static_cast<uint32_t>(target_height);
  return true;
}

// Reports whether the decoded pixels already represent sRGB.
bool RepresentsSrgb(const jpegli::extras::PackedPixelFile& ppf) {
  if (ppf.icc.empty()) {
    return true;
  }
  jpegli::ColorEncoding encoding;
  jpegli::IccBytes icc = ppf.icc;
  if (!encoding.SetICC(std::move(icc), JpegliGetDefaultCms())) {
    return false;
  }
  return encoding.IsSRGB();
}

// Transforms the pixels into sRGB and retags the file accordingly.
bool ConvertToSrgb(jpegli::extras::PackedPixelFile* ppf) {
  if (ppf->icc.empty()) {
    return true;
  }

  jpegli::ColorEncoding source;
  jpegli::IccBytes icc = ppf->icc;
  if (!source.SetICC(std::move(icc), JpegliGetDefaultCms())) {
    return false;
  }

  jpegli::extras::PackedImage& image = ppf->frames[0].color;
  const size_t channels = image.format.num_channels;
  const jpegli::ColorEncoding& target = jpegli::ColorEncoding::SRGB(
      channels == 1);

  if (!source.IsSRGB()) {
    jpegli::ColorSpaceTransform transform(*JpegliGetDefaultCms());
    if (!transform.Init(source, target, 255.0f, image.xsize, 1)) {
      return false;
    }
    float* const source_row = transform.BufSrc(0);
    float* const target_row = transform.BufDst(0);
    for (size_t y = 0; y < image.ysize; ++y) {
      for (size_t x = 0; x < image.xsize; ++x) {
        for (size_t c = 0; c < channels; ++c) {
          source_row[x * channels + c] = image.GetPixelValue(y, x, c);
        }
      }
      if (!transform.Run(0, source_row, target_row, image.xsize)) {
        return false;
      }
      for (size_t x = 0; x < image.xsize; ++x) {
        for (size_t c = 0; c < channels; ++c) {
          image.SetPixelValue(y, x, c, target_row[x * channels + c]);
        }
      }
    }
  }

  ppf->icc = target.ICC();
  ppf->primary_color_representation =
      jpegli::extras::PackedPixelFile::kIccIsPrimary;
  return true;
}

// Decodes the input, flattens alpha onto the given background and rotates the
// pixels upright. This preprocessing is identical for every engine; only the
// colour-profile and metadata handling differ afterwards. The post-decode EXIF
// bytes are returned for the caller's metadata policy.
pc_status DecodeAndPrepare(const char* input_path, const int background[3],
                           const pc_cancel_handle* cancel,
                           jpegli::extras::PackedPixelFile* pixels,
                           std::vector<uint8_t>* exif, char* error_utf8,
                           size_t error_capacity) {
  std::vector<uint8_t> input;
  if (!ReadFile(input_path, &input)) {
    CopyError("Input image could not be read.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  const bool decoded =
      IsJpeg(input)
          ? static_cast<bool>(jpegli::extras::DecodeJpeg(
                input, jpegli::extras::JpegDecompressParams{}, nullptr, pixels))
          : static_cast<bool>(jpegli::extras::DecodeBytes(
                jpegli::Bytes(input), jpegli::extras::ColorHints{}, pixels));
  if (!decoded) {
    CopyError("Input image could not be decoded.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  const float normalized[3] = {background[0] / 255.0f, background[1] / 255.0f,
                               background[2] / 255.0f};
  if (!jpegli::extras::AlphaBlend(pixels, normalized)) {
    CopyError("Input alpha channel could not be flattened.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  // The orientation is applied to the pixels unconditionally: the output must be
  // upright even when the EXIF block carrying it is dropped afterwards (8.2).
  *exif = pixels->metadata.exif;
  if (!ApplyOrientation(pixels, pc::ReadExifOrientation(*exif))) {
    CopyError("Input orientation could not be applied.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }
  return PC_STATUS_OK;
}
#endif

#if defined(PC_HAVE_GUETZLI)
uint8_t ToByte(float value) {
  const float scaled = std::clamp(value, 0.0f, 1.0f) * 255.0f + 0.5f;
  return static_cast<uint8_t>(scaled);
}

// Produces interleaved 8-bit RGB for Guetzli, which only accepts three-channel
// sRGB pixels. Grayscale inputs are expanded; any alpha channel has already been
// flattened by DecodeAndPrepare.
std::vector<uint8_t> ExtractInterleavedRgb(jpegli::extras::PackedImage& image) {
  const size_t channels = image.format.num_channels;
  std::vector<uint8_t> rgb(image.xsize * image.ysize * 3);
  size_t index = 0;
  for (size_t y = 0; y < image.ysize; ++y) {
    for (size_t x = 0; x < image.xsize; ++x) {
      if (channels >= 3) {
        rgb[index++] = ToByte(image.GetPixelValue(y, x, 0));
        rgb[index++] = ToByte(image.GetPixelValue(y, x, 1));
        rgb[index++] = ToByte(image.GetPixelValue(y, x, 2));
      } else {
        const uint8_t gray = ToByte(image.GetPixelValue(y, x, 0));
        rgb[index++] = gray;
        rgb[index++] = gray;
        rgb[index++] = gray;
      }
    }
  }
  return rgb;
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
#if defined(PC_HAVE_GUETZLI)
      return 1;
#else
      return 0;
#endif
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
    case PC_ENGINE_GUETZLI:
#if defined(PC_HAVE_GUETZLI)
      return PC_GUETZLI_BUILD_VERSION;
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
#if defined(PC_HAVE_GUETZLI)
      return "";
#else
      return kGuetzliUnavailable;
#endif
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
      options->alpha_blue < 0 || options->alpha_blue > 255 ||
      options->exif_policy < PC_EXIF_KEEP ||
      options->exif_policy > PC_EXIF_REMOVE ||
      options->color_profile_policy < PC_COLOR_PROFILE_PRESERVE ||
      options->color_profile_policy > PC_COLOR_PROFILE_REMOVE) {
    CopyError("Invalid Jpegli encode request.", error_utf8, error_capacity);
    return PC_STATUS_INVALID_ARGUMENT;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

#if defined(PC_HAVE_JPEGLI)
  jpegli::extras::PackedPixelFile pixels;
  std::vector<uint8_t> exif;
  const int background[3] = {options->alpha_red, options->alpha_green,
                             options->alpha_blue};
  const pc_status prepared =
      DecodeAndPrepare(input_path_utf8, background, cancel, &pixels, &exif,
                       error_utf8, error_capacity);
  if (prepared != PC_STATUS_OK) {
    return prepared;
  }

  switch (options->color_profile_policy) {
    case PC_COLOR_PROFILE_PRESERVE:
      break;
    case PC_COLOR_PROFILE_SRGB:
      if (!ConvertToSrgb(&pixels)) {
        CopyError("Input could not be transformed to sRGB.", error_utf8,
                  error_capacity);
        return PC_STATUS_ENCODE_FAILED;
      }
      break;
    case PC_COLOR_PROFILE_REMOVE:
      if (!RepresentsSrgb(pixels)) {
        CopyError(
            "Removing the color profile would change the colors of a "
            "non-sRGB input.",
            error_utf8, error_capacity);
        return PC_STATUS_INVALID_ARGUMENT;
      }
      pixels.icc.clear();
      pixels.primary_color_representation =
          jpegli::extras::PackedPixelFile::kColorEncodingIsPrimary;
      break;
  }

  switch (options->exif_policy) {
    case PC_EXIF_KEEP:
      pc::ResetExifOrientation(exif);
      break;
    case PC_EXIF_PRIVATE:
      if (!pc::SanitizeExif(exif)) {
        CopyError("Input metadata could not be sanitized.", error_utf8,
                  error_capacity);
        return PC_STATUS_ENCODE_FAILED;
      }
      pc::ResetExifOrientation(exif);
      break;
    case PC_EXIF_REMOVE:
      exif.clear();
      break;
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

  // Jpegli writes the ICC profile itself only while app_data is empty. As soon
  // as any APP segment is supplied, it writes exactly those, so the profile has
  // to be carried explicitly.
  const std::vector<uint8_t> exif_segment = pc::BuildExifApp1(exif);
  const bool needs_explicit_icc =
      !exif_segment.empty() ||
      options->color_profile_policy == PC_COLOR_PROFILE_SRGB;
  if (!exif_segment.empty()) {
    settings.app_data.insert(settings.app_data.end(), exif_segment.begin(),
                             exif_segment.end());
  }
  if (needs_explicit_icc) {
    const std::vector<uint8_t> icc_segments = pc::BuildIccApp2(pixels.icc);
    settings.app_data.insert(settings.app_data.end(), icc_segments.begin(),
                             icc_segments.end());
  }

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
    const pc_guetzli_options* options,
    const pc_cancel_handle* cancel,
    char* error_utf8,
    size_t error_capacity) {
  if (MissingPath(input_path_utf8) || MissingPath(output_path_utf8) ||
      options == nullptr ||
      options->struct_size != sizeof(pc_guetzli_options) ||
      options->quality < 1 || options->quality > 100 ||
      options->alpha_red < 0 || options->alpha_red > 255 ||
      options->alpha_green < 0 || options->alpha_green > 255 ||
      options->alpha_blue < 0 || options->alpha_blue > 255 ||
      options->color_profile_policy < PC_COLOR_PROFILE_PRESERVE ||
      options->color_profile_policy > PC_COLOR_PROFILE_REMOVE) {
    CopyError("Invalid Guetzli encode request.", error_utf8, error_capacity);
    return PC_STATUS_INVALID_ARGUMENT;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

#if defined(PC_HAVE_GUETZLI) && defined(PC_HAVE_JPEGLI)
  jpegli::extras::PackedPixelFile pixels;
  std::vector<uint8_t> exif;
  const int background[3] = {options->alpha_red, options->alpha_green,
                             options->alpha_blue};
  const pc_status prepared =
      DecodeAndPrepare(input_path_utf8, background, cancel, &pixels, &exif,
                       error_utf8, error_capacity);
  if (prepared != PC_STATUS_OK) {
    return prepared;
  }
  // Guetzli emits no metadata; the orientation is already baked into the pixels.
  (void)exif;

  // Guetzli ignores embedded colour profiles and assumes sRGB, so the pixels
  // must represent sRGB before encoding (8.3). Preserve and sRGB both transform
  // to sRGB; Remove is only valid when the input already is sRGB.
  if (options->color_profile_policy == PC_COLOR_PROFILE_REMOVE) {
    if (!RepresentsSrgb(pixels)) {
      CopyError(
          "Removing the color profile would change the colors of a "
          "non-sRGB input.",
          error_utf8, error_capacity);
      return PC_STATUS_INVALID_ARGUMENT;
    }
  } else if (!ConvertToSrgb(&pixels)) {
    CopyError("Input could not be transformed to sRGB.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  jpegli::extras::PackedImage& image = pixels.frames[0].color;
  const int width = static_cast<int>(image.xsize);
  const int height = static_cast<int>(image.ysize);
  const std::vector<uint8_t> rgb = ExtractInterleavedRgb(image);

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

  // Guetzli offers no cooperative cancellation inside Process (10.3); the cancel
  // handle is only honoured at the safe points before and after the call.
  guetzli::Params params;
  params.butteraugli_target = static_cast<float>(
      guetzli::ButteraugliScoreForQuality(options->quality));
  guetzli::ProcessStats stats;
  std::string output;
  if (!guetzli::Process(params, &stats, rgb, width, height, &output) ||
      output.empty()) {
    CopyError("Guetzli encoding failed.", error_utf8, error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }

  if (pc_cancel_is_requested(cancel)) {
    CopyError("Encoding was canceled.", error_utf8, error_capacity);
    return PC_STATUS_CANCELED;
  }

  const std::vector<uint8_t> bytes(output.begin(), output.end());
  if (!WriteFile(output_path_utf8, bytes)) {
    CopyError("Encoded output could not be written.", error_utf8,
              error_capacity);
    return PC_STATUS_ENCODE_FAILED;
  }
  return PC_STATUS_OK;
#else
  CopyError(kGuetzliUnavailable, error_utf8, error_capacity);
  return PC_STATUS_ENGINE_UNAVAILABLE;
#endif
}
