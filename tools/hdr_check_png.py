"""Verify the structure and color metadata of Horizon's ordinary or HDR F2 PNG."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import zlib


def require(condition, message):
    if not condition:
        raise ValueError(message)


def chunks(data):
    require(data[:8] == b"\x89PNG\r\n\x1a\n", "Wrong PNG signature")
    pos = 8
    while pos < len(data):
        require(pos + 12 <= len(data), "Truncated chunk header")
        size, tag = struct.unpack_from(">I4s", data, pos)
        end = pos + 8 + size
        require(end + 4 <= len(data), f"Truncated {tag!r}")
        payload = memoryview(data)[pos + 8:end]
        expected_crc, = struct.unpack_from(">I", data, end)
        require(zlib.crc32(payload, zlib.crc32(tag)) == expected_crc, f"Bad {tag!r} CRC")
        yield tag, payload
        pos = end + 4


def hdr_pixels(filtered, width, height, paper):
    """Reconstruct RGB16 and verify Horizon's MaxRGB content-light statistics."""
    histogram = [0] * 65536
    stride = width * 6
    above = bytearray(stride)
    for y in range(height):
        start = y * (stride + 1)
        mode = filtered[start]
        row = bytearray(filtered[start + 1:start + 1 + stride])
        for i in range(stride) if mode else ():
            left = row[i - 6] if i >= 6 else 0
            top = above[i]
            if mode == 1:
                predictor = left
            elif mode == 2:
                predictor = top
            elif mode == 3:
                predictor = (left + top) // 2
            else:
                corner = above[i - 6] if i >= 6 else 0
                base = left + top - corner
                dl, dt, dc = abs(base - left), abs(base - top), abs(base - corner)
                predictor = left if dl <= dt and dl <= dc else (top if dt <= dc else corner)
            row[i] = (row[i] + predictor) & 255
        for rgb in struct.iter_unpack(">3H", row):
            histogram[max(rgb)] += 1
        above = row
    levels = []
    for code, count in enumerate(histogram):
        if count:
            p = (code / 65535) ** (32 / 2523)
            nits = 10_000 * (max(0, p - 3424 / 4096) / (2413 / 128 - 2392 / 128 * p)) ** (16384 / 2610)
            levels.append((nits, count))
    total = width * height
    over = sum(count for nits, count in levels if nits > paper)
    return dict(max_rgb_nits=levels[-1][0], average_max_rgb_nits=math.fsum(n * c for n, c in levels) / total,
                paper_white_nits=paper, pixels_above_paper_white=over, total_pixels=total)


def inspect(path, kind, paper_white=None):
    data = path.read_bytes()
    tags = []
    meta = {}
    compressed = bytearray()
    unique = {b"IHDR", b"IEND", b"iCCP", b"cICP", b"cLLI", b"mDCV"}
    for tag, payload in chunks(data):
        if tag in unique:
            require(tag not in meta, f"Duplicate {tag!r}")
            require(tag == b"IEND" or not compressed, f"{tag!r} follows IDAT")
            meta[tag] = bytes(payload)
        if tag == b"IDAT":
            require(not compressed or tags[-1] == tag, "Nonconsecutive IDAT chunks")
            compressed.extend(payload)
        tags.append(tag)
    require(tags and tags[0] == b"IHDR" and tags[-1] == b"IEND", "Missing first IHDR or final IEND")
    require(compressed and not meta[b"IEND"], "Missing image or malformed IEND")
    width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", meta[b"IHDR"])
    require((depth, color) == ((16, 2) if kind == "hdr" else (8, 6)), f"Unexpected depth/color {depth}/{color}")
    require(width and height and (compression, filtering, interlace) == (0, 0, 0), "Unexpected IHDR")
    row_size = 1 + width * (6 if kind == "hdr" else 4)
    expected = row_size * height
    require(expected <= 512 * 1024 * 1024, "Image exceeds the checker memory limit")
    decoder = zlib.decompressobj()
    pixels = decoder.decompress(compressed, expected + 1)
    require(decoder.eof and not decoder.unused_data and len(pixels) == expected, "Invalid image stream length")
    require(all(mode <= 4 for mode in pixels[::row_size]), "Invalid scanline filter")
    report = dict(file=path.name, sha256=hashlib.sha256(data).hexdigest(), kind=kind,
                  size=[width, height], bits_per_channel=depth, png_color_type=color)
    if kind == "sdr":
        require(not {b"cICP", b"cLLI", b"mDCV"}.intersection(tags), "Unexpected metadata on Horizon SDR capture")
        return report

    require(meta.get(b"cICP") == bytes([9, 16, 0, 1]), "Expected BT.2020, PQ, identity matrix, full range")
    require(b"cLLI" in meta and b"iCCP" in meta, "Missing Horizon cLLI or ICC profile")
    max_cll, max_fall = struct.unpack(">II", meta[b"cLLI"])
    require(max_cll <= 100_000_000 and max_fall <= max_cll, "Invalid PQ content light levels")
    name, icc = meta[b"iCCP"].split(b"\0", 1)
    require(1 <= len(name) <= 79 and icc[:1] == b"\0", "Invalid ICC name or compression method")
    profile_decoder = zlib.decompressobj()
    profile = profile_decoder.decompress(icc[1:], 4 * 1024 * 1024)
    require(profile_decoder.eof and not profile_decoder.unused_data and len(profile) >= 132, "Invalid ICC stream")
    require(int.from_bytes(profile[:4], "big") == len(profile) and profile[36:40] == b"acsp"
            and profile[16:24] == b"RGB XYZ " and b"BT.2020 PQ, 203 nits = white" in profile,
            "Unexpected Horizon ICC profile")
    report.update(cICP=list(meta[b"cICP"]), max_cll_nits=max_cll / 10_000, max_fall_nits=max_fall / 10_000,
                  icc_sha256=hashlib.sha256(profile).hexdigest(), mastering_display=None)
    if paper_white is not None:
        require(math.isfinite(paper_white) and paper_white > 0, "Invalid paper white")
        stats = hdr_pixels(pixels, width, height, paper_white)
        for nits, encoded in [(stats["max_rgb_nits"], max_cll), (stats["average_max_rgb_nits"], max_fall)]:
            require(abs(nits * 10_000 - encoded) <= 1.0, "cLLI disagrees with decoded HDR pixels")
        report["decoded_light"] = stats
    if b"mDCV" in meta:
        *chroma, maximum, minimum = struct.unpack(">8H2I", meta[b"mDCV"])
        require(max(chroma) <= 50_000 and minimum <= maximum, "Invalid mastering display")
        report["mastering_display"] = dict(chromaticity=[n / 50_000 for n in chroma],
                                           max_nits=maximum / 10_000, min_nits=minimum / 10_000)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=("sdr", "hdr"))
    parser.add_argument("png", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--paper-white", type=float, help="Decode HDR pixels and compare cLLI against measured codes")
    args = parser.parse_args()
    if args.paper_white is not None and args.kind != "hdr":
        parser.error("--paper-white requires an HDR capture")
    try:
        result = inspect(args.png, args.kind, args.paper_white)
    except (KeyError, ValueError, struct.error, zlib.error) as exc:
        parser.exit(1, f"PNG check failed: {exc}\n")
    text = json.dumps(result, indent=2)
    if args.output:
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
