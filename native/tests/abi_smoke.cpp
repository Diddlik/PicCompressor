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
  Check(pc_engine_available(PC_ENGINE_GUETZLI) == 0, "Guetzli is unavailable");
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

  pc_preview_options preview_options{sizeof(pc_preview_options), 512, 255, 255,
                                     255};
  pc_preview preview{sizeof(pc_preview), 0, 0, nullptr, 0, 0, 0};
  char preview_error[128]{};
  Check(pc_render_preview("input.png", &preview_options, &preview, cancel,
                          preview_error, sizeof(preview_error)) ==
            PC_STATUS_CANCELED,
        "a canceled preview reports Canceled");
  Check(preview.rgb == nullptr, "a canceled preview owns no buffer");

  pc_preview_options invalid_preview = preview_options;
  invalid_preview.max_edge = 0;
  char invalid_preview_error[128]{};
  Check(pc_render_preview("input.png", &invalid_preview, &preview, nullptr,
                          invalid_preview_error,
                          sizeof(invalid_preview_error)) ==
            PC_STATUS_INVALID_ARGUMENT,
        "a preview without a target size is rejected");

  // Releasing a preview that was never filled must stay safe.
  pc_preview_release(&preview);
  pc_preview_release(nullptr);

  pc_cancel_destroy(cancel);

  char guetzli_error[128]{};
  const pc_status guetzli_status = pc_encode_guetzli(
      "input.png", "output.jpg", 90, nullptr, guetzli_error,
      sizeof(guetzli_error));
  Check(guetzli_status == PC_STATUS_ENGINE_UNAVAILABLE,
        "Guetzli reports EngineUnavailable");
  Check(std::strlen(guetzli_error) > 0, "Guetzli reports a reason");

  if (g_failures != 0) {
    std::fprintf(stderr, "%d check(s) failed\n", g_failures);
    return 1;
  }
  return 0;
}
