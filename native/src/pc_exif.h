#ifndef PICCOMPRESSOR_PC_EXIF_H_
#define PICCOMPRESSOR_PC_EXIF_H_

#include <cstdint>
#include <vector>

// TIFF/EXIF handling for the PicCompressor native wrapper.
//
// All functions operate on a raw TIFF blob as produced by the Jpegli decoder,
// i.e. starting at the TIFF header ("II*\0" or "MM\0*") without the JPEG APP1
// "Exif\0\0" signature. Every function treats the blob as untrusted input and
// never reads outside it.

namespace pc {

inline constexpr int kOrientationIdentity = 1;

// Reads EXIF tag 0x0112 from IFD0. Returns kOrientationIdentity when the blob
// is empty, malformed or carries no usable orientation.
int ReadExifOrientation(const std::vector<uint8_t>& exif);

// Sets EXIF tag 0x0112 in IFD0 to the identity value. Does nothing when the
// tag is absent or the blob is malformed.
void ResetExifOrientation(std::vector<uint8_t>& exif);

// Rebuilds the blob, keeping only an allowlist of non-identifying tags from
// IFD0 and the Exif sub-IFD. Everything else is dropped, including the GPS
// IFD, MakerNote, serial numbers, owner and camera identity fields and the
// thumbnail IFD. Returns false and clears the blob when it is malformed.
bool SanitizeExif(std::vector<uint8_t>& exif);

// Wraps a TIFF blob into a JPEG APP1 segment. Returns an empty vector when the
// blob is empty or does not fit into a single segment.
std::vector<uint8_t> BuildExifApp1(const std::vector<uint8_t>& exif);

// Wraps an ICC profile into consecutive JPEG APP2 segments. Returns an empty
// vector when the profile is empty or needs more than 255 segments.
std::vector<uint8_t> BuildIccApp2(const std::vector<uint8_t>& icc);

}  // namespace pc

#endif  // PICCOMPRESSOR_PC_EXIF_H_
