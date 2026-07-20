#include "pc_exif.h"

#include <algorithm>
#include <cstdio>
#include <cstdint>
#include <string>
#include <vector>

namespace {

int g_failures = 0;

void Check(bool condition, const char* what) {
  if (!condition) {
    std::fprintf(stderr, "FAILED: %s\n", what);
    ++g_failures;
  }
}

// A minimal little-endian TIFF header carrying only Orientation = 6.
const std::vector<uint8_t> kLittleEndianOrientation6 = {
    0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00,  // header, IFD0 at 8
    0x01, 0x00,                                      // one entry
    0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,  // tag 0x0112, SHORT, 1
    0x06, 0x00, 0x00, 0x00,                          // value 6
    0x00, 0x00, 0x00, 0x00};                         // no next IFD

// The same content in big-endian byte order.
const std::vector<uint8_t> kBigEndianOrientation6 = {
    0x4d, 0x4d, 0x00, 0x2a, 0x00, 0x00, 0x00, 0x08,
    0x00, 0x01,
    0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01,
    0x00, 0x06, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00};

struct TestEntry {
  uint16_t tag;
  uint16_t type;
  uint32_t count;
  std::vector<uint8_t> value;
};

void PushLe16(std::vector<uint8_t>* out, uint16_t value) {
  out->push_back(static_cast<uint8_t>(value & 0xff));
  out->push_back(static_cast<uint8_t>(value >> 8));
}

void PushLe32(std::vector<uint8_t>* out, uint32_t value) {
  for (int shift = 0; shift < 32; shift += 8) {
    out->push_back(static_cast<uint8_t>((value >> shift) & 0xff));
  }
}

// Builds a little-endian TIFF blob with a primary IFD and an Exif sub-IFD.
std::vector<uint8_t> BuildTiff(const std::vector<TestEntry>& primary,
                               const std::vector<TestEntry>& sub_ifd) {
  const size_t primary_count = primary.size() + (sub_ifd.empty() ? 0 : 1);
  const size_t primary_size = 2 + primary_count * 12 + 4;
  const size_t sub_offset = 8 + primary_size;
  const size_t sub_size = sub_ifd.empty() ? 0 : 2 + sub_ifd.size() * 12 + 4;
  const size_t data_offset = sub_offset + sub_size;

  std::vector<uint8_t> out;
  std::vector<uint8_t> data;
  out.push_back(0x49);
  out.push_back(0x49);
  PushLe16(&out, 42);
  PushLe32(&out, 8);

  const auto write_ifd = [&](const std::vector<TestEntry>& entries,
                             bool with_pointer) {
    PushLe16(&out, static_cast<uint16_t>(entries.size() + (with_pointer ? 1 : 0)));
    for (const TestEntry& entry : entries) {
      PushLe16(&out, entry.tag);
      PushLe16(&out, entry.type);
      PushLe32(&out, entry.count);
      if (entry.value.size() <= 4) {
        out.insert(out.end(), entry.value.begin(), entry.value.end());
        out.insert(out.end(), 4 - entry.value.size(), 0);
      } else {
        PushLe32(&out, static_cast<uint32_t>(data_offset + data.size()));
        data.insert(data.end(), entry.value.begin(), entry.value.end());
        if (data.size() % 2 != 0) {
          data.push_back(0);
        }
      }
    }
    if (with_pointer) {
      PushLe16(&out, 0x8769);
      PushLe16(&out, 4);
      PushLe32(&out, 1);
      PushLe32(&out, static_cast<uint32_t>(sub_offset));
    }
    PushLe32(&out, 0);
  };

  write_ifd(primary, !sub_ifd.empty());
  if (!sub_ifd.empty()) {
    write_ifd(sub_ifd, false);
  }
  out.insert(out.end(), data.begin(), data.end());
  return out;
}

std::vector<uint8_t> Ascii(const std::string& text) {
  std::vector<uint8_t> out(text.begin(), text.end());
  out.push_back(0);
  return out;
}

bool Contains(const std::vector<uint8_t>& haystack, const std::string& needle) {
  const std::vector<uint8_t> pattern(needle.begin(), needle.end());
  return std::search(haystack.begin(), haystack.end(), pattern.begin(),
                     pattern.end()) != haystack.end();
}

void TestReadOrientation() {
  Check(pc::ReadExifOrientation(kLittleEndianOrientation6) == 6,
        "little-endian orientation is read");
  Check(pc::ReadExifOrientation(kBigEndianOrientation6) == 6,
        "big-endian orientation is read");
  Check(pc::ReadExifOrientation({}) == pc::kOrientationIdentity,
        "empty blob falls back to identity");

  std::vector<uint8_t> truncated = kLittleEndianOrientation6;
  truncated.resize(12);
  Check(pc::ReadExifOrientation(truncated) == pc::kOrientationIdentity,
        "truncated blob falls back to identity");

  std::vector<uint8_t> bad_magic = kLittleEndianOrientation6;
  bad_magic[0] = 0x00;
  Check(pc::ReadExifOrientation(bad_magic) == pc::kOrientationIdentity,
        "invalid byte order mark falls back to identity");

  std::vector<uint8_t> out_of_range = kLittleEndianOrientation6;
  out_of_range[16] = 0x63;
  Check(pc::ReadExifOrientation(out_of_range) == pc::kOrientationIdentity,
        "out of range orientation falls back to identity");
}

void TestResetOrientation() {
  std::vector<uint8_t> little = kLittleEndianOrientation6;
  pc::ResetExifOrientation(little);
  Check(pc::ReadExifOrientation(little) == pc::kOrientationIdentity,
        "little-endian orientation is reset");
  Check(little.size() == kLittleEndianOrientation6.size(),
        "resetting does not resize the blob");

  std::vector<uint8_t> big = kBigEndianOrientation6;
  pc::ResetExifOrientation(big);
  Check(pc::ReadExifOrientation(big) == pc::kOrientationIdentity,
        "big-endian orientation is reset");
}

void TestSanitize() {
  const std::vector<TestEntry> primary = {
      {0x010f, 2, 6, Ascii("Canon")},
      {0x0112, 3, 1, {0x06, 0x00}},
      {0x013b, 2, 4, Ascii("Bob")},
      {0x8825, 4, 1, {0x00, 0x00, 0x00, 0x00}},
  };
  const std::vector<TestEntry> sub_ifd = {
      {0x829d, 5, 1, {8, 0, 0, 0, 1, 0, 0, 0}},
      {0xa431, 2, 8, Ascii("SN12345")},
      {0x927c, 7, 9, Ascii("MAKERNOTE")},
  };

  std::vector<uint8_t> exif = BuildTiff(primary, sub_ifd);
  Check(Contains(exif, "Bob"), "fixture contains the artist");
  Check(Contains(exif, "SN12345"), "fixture contains the serial number");
  Check(pc::ReadExifOrientation(exif) == 6, "fixture carries the orientation");

  Check(pc::SanitizeExif(exif), "sanitizing a valid blob succeeds");
  Check(pc::ReadExifOrientation(exif) == 6,
        "sanitizing keeps the orientation");
  Check(Contains(exif, "Canon"), "sanitizing keeps the camera make");
  Check(!Contains(exif, "Bob"), "sanitizing drops the artist");
  Check(!Contains(exif, "SN12345"), "sanitizing drops the serial number");
  Check(!Contains(exif, "MAKERNOTE"), "sanitizing drops the maker note");

  // The GPS pointer must not survive, neither as a tag nor as an offset.
  Check(pc::ReadExifOrientation(exif) == 6,
        "the sanitized blob stays parseable");

  std::vector<uint8_t> malformed = {0x00, 0x01, 0x02};
  Check(!pc::SanitizeExif(malformed), "a malformed blob is rejected");
  Check(malformed.empty(), "a rejected blob is cleared");

  std::vector<uint8_t> empty;
  Check(pc::SanitizeExif(empty), "an empty blob is accepted");
  Check(empty.empty(), "an empty blob stays empty");
}

void TestSanitizeKeepsSubIfdValues() {
  const std::vector<TestEntry> primary = {{0x0112, 3, 1, {0x01, 0x00}}};
  const std::vector<TestEntry> sub_ifd = {
      {0x9003, 2, 20, Ascii("2026:07:20 10:00:00")}};

  std::vector<uint8_t> exif = BuildTiff(primary, sub_ifd);
  Check(pc::SanitizeExif(exif), "sanitizing succeeds");
  Check(Contains(exif, "2026:07:20 10:00:00"),
        "sanitizing keeps the capture date from the sub-IFD");
}

void TestApp1() {
  const std::vector<uint8_t> segment =
      pc::BuildExifApp1(kLittleEndianOrientation6);
  Check(segment.size() == kLittleEndianOrientation6.size() + 10,
        "APP1 adds marker, length and signature");
  Check(segment[0] == 0xff && segment[1] == 0xe1, "APP1 marker is written");

  const size_t length = (static_cast<size_t>(segment[2]) << 8) | segment[3];
  Check(length == segment.size() - 2, "APP1 length covers the payload");
  Check(segment[4] == 'E' && segment[5] == 'x' && segment[6] == 'i' &&
            segment[7] == 'f' && segment[8] == 0 && segment[9] == 0,
        "APP1 carries the Exif signature");
  Check(pc::BuildExifApp1({}).empty(), "an empty blob yields no segment");

  const std::vector<uint8_t> oversized(65536, 0x00);
  Check(pc::BuildExifApp1(oversized).empty(),
        "an oversized blob yields no segment");
}

void TestApp2() {
  const std::vector<uint8_t> small(1000, 0x42);
  const std::vector<uint8_t> one_chunk = pc::BuildIccApp2(small);
  Check(one_chunk.size() == small.size() + 18,
        "a small profile fits into one segment");
  Check(one_chunk[0] == 0xff && one_chunk[1] == 0xe2, "APP2 marker is written");
  Check(one_chunk[16] == 1 && one_chunk[17] == 1,
        "the single chunk is numbered 1 of 1");

  const std::vector<uint8_t> large(70000, 0x42);
  const std::vector<uint8_t> two_chunks = pc::BuildIccApp2(large);
  Check(two_chunks[16] == 1 && two_chunks[17] == 2,
        "the first of two chunks is numbered 1 of 2");
  Check(two_chunks.size() == large.size() + 2 * 18,
        "both segments carry their own header");
  Check(pc::BuildIccApp2({}).empty(), "an empty profile yields no segment");
}

}  // namespace

int main() {
  TestReadOrientation();
  TestResetOrientation();
  TestSanitize();
  TestSanitizeKeepsSubIfdValues();
  TestApp1();
  TestApp2();

  if (g_failures != 0) {
    std::fprintf(stderr, "%d check(s) failed\n", g_failures);
    return 1;
  }
  return 0;
}
