#include "piccompressor_native.h"

#include <cstdio>
#include <cstring>

namespace {

int g_failures = 0;

// Release configurations define NDEBUG, which would turn assert() into a
// no-op and silently pass this smoke test.
void Check(bool condition, const char* what) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", what);
    ++g_failures;
  }
}

}  // namespace

int main() {
  Check(pc_abi_version() == PC_ABI_VERSION, "ABI version matches the header");
#if defined(PC_EXPECT_JPEGLI)
  Check(pc_engine_available(PC_ENGINE_JPEGLI) == 1, "Jpegli is available");
  Check(std::strlen(pc_engine_build_version(PC_ENGINE_JPEGLI)) > 0,
        "Jpegli reports a build version");
#else
  Check(pc_engine_available(PC_ENGINE_JPEGLI) == 0, "Jpegli is unavailable");
#endif
#if defined(PC_EXPECT_GUETZLI)
  Check(pc_engine_available(PC_ENGINE_GUETZLI) == 1, "Guetzli is available");
  Check(std::strlen(pc_engine_build_version(PC_ENGINE_GUETZLI)) > 0,
        "Guetzli reports a build version");
#else
  Check(pc_engine_available(PC_ENGINE_GUETZLI) == 0, "Guetzli is unavailable");
#endif
  Check(std::strlen(pc_engine_source_revision(PC_ENGINE_JPEGLI)) == 40,
        "Jpegli reports a pinned revision");
  Check(std::strlen(pc_engine_source_revision(PC_ENGINE_GUETZLI)) == 40,
        "Guetzli reports a pinned revision");

  pc_cancel_handle* cancel = pc_cancel_create();
  Check(cancel != nullptr, "a cancel handle is created");
  Check(pc_cancel_is_requested(cancel) == 0, "a fresh handle is not requested");
  pc_cancel_request(cancel);
  Check(pc_cancel_is_requested(cancel) == 1, "a requested handle is observed");

  pc_jpegli_options options{sizeof(pc_jpegli_options),
                            80,
                            PC_CHROMA_420,
                            2,
                            255,
                            255,
                            255,
                            PC_EXIF_REMOVE,
                            PC_COLOR_PROFILE_PRESERVE};
  char error[128]{};
  const pc_status status = pc_encode_jpegli(
      "input.png", "output.jpg", &options, cancel, error, sizeof(error));
  Check(status == PC_STATUS_CANCELED, "a canceled request reports Canceled");
  Check(std::strlen(error) > 0, "a canceled request reports a reason");

  // An unknown policy value must be rejected before any file is touched.
  pc_jpegli_options invalid = options;
  invalid.exif_policy = static_cast<pc_exif_policy>(99);
  char invalid_error[128]{};
  Check(pc_encode_jpegli("input.png", "output.jpg", &invalid, nullptr,
                         invalid_error, sizeof(invalid_error)) ==
            PC_STATUS_INVALID_ARGUMENT,
        "an unknown EXIF policy is rejected");

  // A stale option struct from an older ABI must not be accepted.
  pc_jpegli_options stale = options;
  stale.struct_size = sizeof(pc_jpegli_options) - 8;
  char stale_error[128]{};
  Check(pc_encode_jpegli("input.png", "output.jpg", &stale, nullptr,
                         stale_error, sizeof(stale_error)) ==
            PC_STATUS_INVALID_ARGUMENT,
        "a mismatched struct size is rejected");

  pc_cancel_destroy(cancel);

  // A pre-requested cancel returns before Guetzli's heavy encode runs, so this
  // stays a fast smoke test; the real round-trip is covered by the managed
  // interop tests.
  pc_guetzli_options guetzli_options{
      sizeof(pc_guetzli_options), 90, 255, 255, 255, PC_COLOR_PROFILE_PRESERVE};
  pc_cancel_handle* guetzli_cancel = pc_cancel_create();
  pc_cancel_request(guetzli_cancel);
  char guetzli_error[128]{};
  Check(pc_encode_guetzli("input.png", "output.jpg", &guetzli_options,
                          guetzli_cancel, guetzli_error,
                          sizeof(guetzli_error)) == PC_STATUS_CANCELED,
        "a canceled Guetzli request reports Canceled");
  Check(std::strlen(guetzli_error) > 0, "a canceled Guetzli request reports a reason");
  pc_cancel_destroy(guetzli_cancel);

  pc_guetzli_options guetzli_stale = guetzli_options;
  guetzli_stale.struct_size = sizeof(pc_guetzli_options) - 4;
  char guetzli_stale_error[128]{};
  Check(pc_encode_guetzli("input.png", "output.jpg", &guetzli_stale, nullptr,
                          guetzli_stale_error, sizeof(guetzli_stale_error)) ==
            PC_STATUS_INVALID_ARGUMENT,
        "a mismatched Guetzli struct size is rejected");

  if (g_failures != 0) {
    std::fprintf(stderr, "%d check(s) failed\n", g_failures);
    return 1;
  }
  return 0;
}
