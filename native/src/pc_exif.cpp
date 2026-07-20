#include "pc_exif.h"

#include <algorithm>
#include <cstring>
#include <utility>

namespace pc {
namespace {

constexpr uint16_t kTagOrientation = 0x0112;
constexpr uint16_t kTagExifIfd = 0x8769;
constexpr uint16_t kTypeShort = 3;
constexpr uint16_t kTypeLong = 4;
constexpr size_t kTiffHeaderSize = 8;
constexpr size_t kEntrySize = 12;
constexpr size_t kInlineValueSize = 4;

// Bounds-checked view over an untrusted TIFF blob.
class ByteView {
 public:
  ByteView(const uint8_t* data, size_t size, bool big_endian)
      : data_(data), size_(size), big_endian_(big_endian) {}

  bool Has(size_t offset, size_t length) const {
    return offset <= size_ && length <= size_ - offset;
  }

  uint16_t U16(size_t offset) const {
    const uint16_t high = data_[big_endian_ ? offset : offset + 1];
    const uint16_t low = data_[big_endian_ ? offset + 1 : offset];
    return static_cast<uint16_t>((high << 8) | low);
  }

  uint32_t U32(size_t offset) const {
    uint32_t value = 0;
    for (size_t i = 0; i < 4; ++i) {
      const size_t index = big_endian_ ? offset + i : offset + 3 - i;
      value = (value << 8) | data_[index];
    }
    return value;
  }

  const uint8_t* At(size_t offset) const { return data_ + offset; }

 private:
  const uint8_t* data_;
  size_t size_;
  bool big_endian_;
};

void Store16(std::vector<uint8_t>* out, uint16_t value, bool big_endian) {
  const uint8_t high = static_cast<uint8_t>(value >> 8);
  const uint8_t low = static_cast<uint8_t>(value & 0xff);
  out->push_back(big_endian ? high : low);
  out->push_back(big_endian ? low : high);
}

void Store32(std::vector<uint8_t>* out, uint32_t value, bool big_endian) {
  for (size_t i = 0; i < 4; ++i) {
    const size_t shift = big_endian ? (24 - 8 * i) : (8 * i);
    out->push_back(static_cast<uint8_t>((value >> shift) & 0xff));
  }
}

size_t ComponentSize(uint16_t type) {
  switch (type) {
    case 1:   // BYTE
    case 2:   // ASCII
    case 6:   // SBYTE
    case 7:   // UNDEFINED
      return 1;
    case 3:   // SHORT
    case 8:   // SSHORT
      return 2;
    case 4:   // LONG
    case 9:   // SLONG
    case 11:  // FLOAT
      return 4;
    case 5:   // RATIONAL
    case 10:  // SRATIONAL
    case 12:  // DOUBLE
      return 8;
    default:
      return 0;
  }
}

// Allowlists rather than denylists: an unknown tag is assumed to be
// identifying. This drops MakerNote, GPS pointers, serial numbers, owner and
// lens identity fields and any vendor extension without further inspection.
bool IsAllowedPrimaryTag(uint16_t tag) {
  switch (tag) {
    case 0x0100:  // ImageWidth
    case 0x0101:  // ImageLength
    case 0x0102:  // BitsPerSample
    case 0x0103:  // Compression
    case 0x0106:  // PhotometricInterpretation
    case 0x010f:  // Make
    case 0x0110:  // Model
    case 0x0112:  // Orientation
    case 0x0115:  // SamplesPerPixel
    case 0x011a:  // XResolution
    case 0x011b:  // YResolution
    case 0x0128:  // ResolutionUnit
    case 0x0131:  // Software
    case 0x0132:  // DateTime
    case 0x0213:  // YCbCrPositioning
      return true;
    default:
      return false;
  }
}

bool IsAllowedSubIfdTag(uint16_t tag) {
  switch (tag) {
    case 0x829a:  // ExposureTime
    case 0x829d:  // FNumber
    case 0x8822:  // ExposureProgram
    case 0x8827:  // PhotographicSensitivity
    case 0x9000:  // ExifVersion
    case 0x9003:  // DateTimeOriginal
    case 0x9004:  // DateTimeDigitized
    case 0x9201:  // ShutterSpeedValue
    case 0x9202:  // ApertureValue
    case 0x9204:  // ExposureBiasValue
    case 0x9205:  // MaxApertureValue
    case 0x9207:  // MeteringMode
    case 0x9208:  // LightSource
    case 0x9209:  // Flash
    case 0x920a:  // FocalLength
    case 0xa001:  // ColorSpace
    case 0xa002:  // PixelXDimension
    case 0xa003:  // PixelYDimension
    case 0xa402:  // ExposureMode
    case 0xa403:  // WhiteBalance
    case 0xa405:  // FocalLengthIn35mmFilm
    case 0xa406:  // SceneCaptureType
      return true;
    default:
      return false;
  }
}

bool ParseHeader(const std::vector<uint8_t>& exif, bool* big_endian,
                 uint32_t* ifd0_offset) {
  if (exif.size() < kTiffHeaderSize) {
    return false;
  }
  if (exif[0] == 0x49 && exif[1] == 0x49) {
    *big_endian = false;
  } else if (exif[0] == 0x4d && exif[1] == 0x4d) {
    *big_endian = true;
  } else {
    return false;
  }

  const ByteView view(exif.data(), exif.size(), *big_endian);
  if (view.U16(2) != 42) {
    return false;
  }
  *ifd0_offset = view.U32(4);
  return *ifd0_offset >= kTiffHeaderSize;
}

bool ReadIfdEntryCount(const ByteView& view, uint32_t ifd_offset,
                       uint16_t* count) {
  const size_t offset = ifd_offset;
  if (!view.Has(offset, 2)) {
    return false;
  }
  const uint16_t entries = view.U16(offset);
  if (!view.Has(offset + 2, static_cast<size_t>(entries) * kEntrySize + 4)) {
    return false;
  }
  *count = entries;
  return true;
}

size_t EntryOffset(uint32_t ifd_offset, uint16_t index) {
  return static_cast<size_t>(ifd_offset) + 2 +
         static_cast<size_t>(index) * kEntrySize;
}

struct Entry {
  uint16_t tag;
  uint16_t type;
  uint32_t count;
  // Raw value bytes in the source byte order. The rebuilt blob keeps that
  // order, so payloads never need to be swapped component by component.
  std::vector<uint8_t> value;
};

bool CollectIfd(const ByteView& view, uint32_t ifd_offset, bool is_primary_ifd,
                std::vector<Entry>* entries, uint32_t* sub_ifd_offset) {
  uint16_t count = 0;
  if (!ReadIfdEntryCount(view, ifd_offset, &count)) {
    return false;
  }

  for (uint16_t index = 0; index < count; ++index) {
    const size_t entry = EntryOffset(ifd_offset, index);
    const uint16_t tag = view.U16(entry);
    const uint16_t type = view.U16(entry + 2);
    const uint32_t items = view.U32(entry + 4);

    if (is_primary_ifd && tag == kTagExifIfd) {
      if (type == kTypeLong && items == 1) {
        *sub_ifd_offset = view.U32(entry + 8);
      }
      continue;
    }
    if (!(is_primary_ifd ? IsAllowedPrimaryTag(tag) : IsAllowedSubIfdTag(tag))) {
      continue;
    }

    const size_t component = ComponentSize(type);
    if (component == 0) {
      continue;
    }
    const uint64_t length = static_cast<uint64_t>(component) * items;
    if (length == 0 || length > 0xffffffffull) {
      continue;
    }

    const size_t size = static_cast<size_t>(length);
    Entry collected;
    collected.tag = tag;
    collected.type = type;
    collected.count = items;
    if (size <= kInlineValueSize) {
      collected.value.assign(view.At(entry + 8), view.At(entry + 8) + size);
    } else {
      const uint32_t value_offset = view.U32(entry + 8);
      if (!view.Has(value_offset, size)) {
        continue;
      }
      collected.value.assign(view.At(value_offset),
                             view.At(value_offset) + size);
    }
    entries->push_back(std::move(collected));
  }
  return true;
}

std::vector<uint8_t> Serialize(bool big_endian, std::vector<Entry> primary,
                               const std::vector<Entry>& sub_ifd) {
  const bool has_sub_ifd = !sub_ifd.empty();
  if (has_sub_ifd) {
    Entry pointer;
    pointer.tag = kTagExifIfd;
    pointer.type = kTypeLong;
    pointer.count = 1;
    pointer.value.assign(kInlineValueSize, 0);
    primary.push_back(std::move(pointer));
  }

  const auto by_tag = [](const Entry& left, const Entry& right) {
    return left.tag < right.tag;
  };
  std::stable_sort(primary.begin(), primary.end(), by_tag);

  const size_t primary_size = 2 + primary.size() * kEntrySize + 4;
  const size_t sub_ifd_offset = kTiffHeaderSize + primary_size;
  const size_t sub_ifd_size =
      has_sub_ifd ? 2 + sub_ifd.size() * kEntrySize + 4 : 0;
  const size_t data_offset = sub_ifd_offset + sub_ifd_size;

  std::vector<uint8_t> out;
  std::vector<uint8_t> data;
  out.push_back(big_endian ? 0x4d : 0x49);
  out.push_back(big_endian ? 0x4d : 0x49);
  Store16(&out, 42, big_endian);
  Store32(&out, static_cast<uint32_t>(kTiffHeaderSize), big_endian);

  const auto write_ifd = [&](const std::vector<Entry>& entries) {
    Store16(&out, static_cast<uint16_t>(entries.size()), big_endian);
    for (const Entry& entry : entries) {
      Store16(&out, entry.tag, big_endian);
      Store16(&out, entry.type, big_endian);
      Store32(&out, entry.count, big_endian);
      if (entry.tag == kTagExifIfd && has_sub_ifd) {
        Store32(&out, static_cast<uint32_t>(sub_ifd_offset), big_endian);
      } else if (entry.value.size() <= kInlineValueSize) {
        out.insert(out.end(), entry.value.begin(), entry.value.end());
        out.insert(out.end(), kInlineValueSize - entry.value.size(), 0);
      } else {
        Store32(&out, static_cast<uint32_t>(data_offset + data.size()),
                big_endian);
        data.insert(data.end(), entry.value.begin(), entry.value.end());
        if (data.size() % 2 != 0) {
          data.push_back(0);
        }
      }
    }
    // The thumbnail IFD is deliberately dropped: it can carry an unredacted
    // copy of the original image.
    Store32(&out, 0, big_endian);
  };

  write_ifd(primary);
  if (has_sub_ifd) {
    std::vector<Entry> sorted = sub_ifd;
    std::stable_sort(sorted.begin(), sorted.end(), by_tag);
    write_ifd(sorted);
  }
  out.insert(out.end(), data.begin(), data.end());
  return out;
}

}  // namespace

int ReadExifOrientation(const std::vector<uint8_t>& exif) {
  bool big_endian = false;
  uint32_t ifd0_offset = 0;
  if (!ParseHeader(exif, &big_endian, &ifd0_offset)) {
    return kOrientationIdentity;
  }

  const ByteView view(exif.data(), exif.size(), big_endian);
  uint16_t count = 0;
  if (!ReadIfdEntryCount(view, ifd0_offset, &count)) {
    return kOrientationIdentity;
  }

  for (uint16_t index = 0; index < count; ++index) {
    const size_t entry = EntryOffset(ifd0_offset, index);
    if (view.U16(entry) != kTagOrientation) {
      continue;
    }
    if (view.U16(entry + 2) != kTypeShort || view.U32(entry + 4) != 1) {
      break;
    }
    const uint16_t value = view.U16(entry + 8);
    return (value >= 1 && value <= 8) ? static_cast<int>(value)
                                      : kOrientationIdentity;
  }
  return kOrientationIdentity;
}

void ResetExifOrientation(std::vector<uint8_t>& exif) {
  bool big_endian = false;
  uint32_t ifd0_offset = 0;
  if (!ParseHeader(exif, &big_endian, &ifd0_offset)) {
    return;
  }

  const ByteView view(exif.data(), exif.size(), big_endian);
  uint16_t count = 0;
  if (!ReadIfdEntryCount(view, ifd0_offset, &count)) {
    return;
  }

  for (uint16_t index = 0; index < count; ++index) {
    const size_t entry = EntryOffset(ifd0_offset, index);
    if (view.U16(entry) != kTagOrientation) {
      continue;
    }
    if (view.U16(entry + 2) != kTypeShort || view.U32(entry + 4) != 1) {
      return;
    }
    uint8_t* target = exif.data() + entry + 8;
    target[0] = big_endian ? 0 : 1;
    target[1] = big_endian ? 1 : 0;
    return;
  }
}

bool SanitizeExif(std::vector<uint8_t>& exif) {
  if (exif.empty()) {
    return true;
  }

  bool big_endian = false;
  uint32_t ifd0_offset = 0;
  if (!ParseHeader(exif, &big_endian, &ifd0_offset)) {
    exif.clear();
    return false;
  }

  const ByteView view(exif.data(), exif.size(), big_endian);
  std::vector<Entry> primary;
  std::vector<Entry> sub_ifd;
  uint32_t sub_ifd_offset = 0;
  if (!CollectIfd(view, ifd0_offset, true, &primary, &sub_ifd_offset)) {
    exif.clear();
    return false;
  }
  if (sub_ifd_offset >= kTiffHeaderSize) {
    uint32_t nested = 0;
    if (!CollectIfd(view, sub_ifd_offset, false, &sub_ifd, &nested)) {
      // A malformed sub-IFD is dropped; the primary IFD stays usable.
      sub_ifd.clear();
    }
  }

  if (primary.empty() && sub_ifd.empty()) {
    exif.clear();
    return true;
  }
  exif = Serialize(big_endian, std::move(primary), sub_ifd);
  return true;
}

std::vector<uint8_t> BuildExifApp1(const std::vector<uint8_t>& exif) {
  static constexpr uint8_t kSignature[] = {'E', 'x', 'i', 'f', 0, 0};
  constexpr size_t kSignatureSize = sizeof kSignature;
  constexpr size_t kMaxPayload = 65533;

  if (exif.empty() || exif.size() + kSignatureSize > kMaxPayload) {
    return {};
  }

  const size_t length = exif.size() + kSignatureSize + 2;
  std::vector<uint8_t> out;
  out.reserve(length + 2);
  out.push_back(0xff);
  out.push_back(0xe1);
  out.push_back(static_cast<uint8_t>(length >> 8));
  out.push_back(static_cast<uint8_t>(length & 0xff));
  out.insert(out.end(), kSignature, kSignature + kSignatureSize);
  out.insert(out.end(), exif.begin(), exif.end());
  return out;
}

std::vector<uint8_t> BuildIccApp2(const std::vector<uint8_t>& icc) {
  static constexpr uint8_t kSignature[] = {'I', 'C', 'C', '_', 'P', 'R',
                                           'O', 'F', 'I', 'L', 'E', 0};
  constexpr size_t kSignatureSize = sizeof kSignature;
  // 65535 length limit minus the length field, the signature and the two
  // sequence bytes.
  constexpr size_t kMaxChunk = 65535 - 2 - kSignatureSize - 2;
  constexpr size_t kMaxChunks = 255;

  if (icc.empty()) {
    return {};
  }
  const size_t chunks = (icc.size() + kMaxChunk - 1) / kMaxChunk;
  if (chunks > kMaxChunks) {
    return {};
  }

  std::vector<uint8_t> out;
  for (size_t index = 0; index < chunks; ++index) {
    const size_t start = index * kMaxChunk;
    const size_t size = std::min(kMaxChunk, icc.size() - start);
    const size_t length = size + kSignatureSize + 2 + 2;
    out.push_back(0xff);
    out.push_back(0xe2);
    out.push_back(static_cast<uint8_t>(length >> 8));
    out.push_back(static_cast<uint8_t>(length & 0xff));
    out.insert(out.end(), kSignature, kSignature + kSignatureSize);
    out.push_back(static_cast<uint8_t>(index + 1));
    out.push_back(static_cast<uint8_t>(chunks));
    out.insert(out.end(), icc.begin() + static_cast<ptrdiff_t>(start),
               icc.begin() + static_cast<ptrdiff_t>(start + size));
  }
  return out;
}

}  // namespace pc
