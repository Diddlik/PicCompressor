#include "piccompressor_native.h"

#include <cassert>
#include <cstring>

int main() {
  assert(pc_abi_version() == PC_ABI_VERSION);
#if defined(PC_EXPECT_JPEGLI)
  assert(pc_engine_available(PC_ENGINE_JPEGLI) == 1);
  assert(std::strlen(pc_engine_build_version(PC_ENGINE_JPEGLI)) > 0);
#else
  assert(pc_engine_available(PC_ENGINE_JPEGLI) == 0);
#endif
  assert(pc_engine_available(PC_ENGINE_GUETZLI) == 0);
  assert(std::strlen(pc_engine_source_revision(PC_ENGINE_JPEGLI)) == 40);
  assert(std::strlen(pc_engine_source_revision(PC_ENGINE_GUETZLI)) == 40);

  pc_cancel_handle* cancel = pc_cancel_create();
  assert(cancel != nullptr);
  assert(pc_cancel_is_requested(cancel) == 0);
  pc_cancel_request(cancel);
  assert(pc_cancel_is_requested(cancel) == 1);

  pc_jpegli_options options{
      sizeof(pc_jpegli_options), 80, PC_CHROMA_420, 2, 255, 255, 255};
  char error[128]{};
  const pc_status status =
      pc_encode_jpegli("input.png", "output.jpg", &options, cancel, error,
                       sizeof(error));
  assert(status == PC_STATUS_CANCELED);
  assert(std::strlen(error) > 0);

  pc_cancel_destroy(cancel);

  char guetzli_error[128]{};
  const pc_status guetzli_status =
      pc_encode_guetzli("input.png", "output.jpg", 90, nullptr, guetzli_error,
                        sizeof(guetzli_error));
  assert(guetzli_status == PC_STATUS_ENGINE_UNAVAILABLE);
  assert(std::strlen(guetzli_error) > 0);
  return 0;
}
