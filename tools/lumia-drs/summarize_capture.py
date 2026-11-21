"""Summarize the pinned Horizon v1 capture used by the isolated Lumia sweep."""

import argparse
import collections
import csv
import hashlib
import json
import math
import statistics
import struct
import zlib
from pathlib import Path


def varint(data, offset):
    value = 0
    for shift in range(0, 70, 7):
        if offset >= len(data):
            raise ValueError("Truncated protobuf varint")
        byte = data[offset]
        offset += 1
        value |= (byte & 0x7F) << shift
        if not (byte & 0x80):
            return value, offset
    raise ValueError("Invalid protobuf varint")


def message(data):
    fields = collections.defaultdict(list)
    offset = 0
    while offset < len(data):
        key, offset = varint(data, offset)
        number, wire = divmod(key, 8)
        if wire == 0:
            value, offset = varint(data, offset)
        else:
            if wire == 1:
                length = 8
            elif wire == 2:
                length, offset = varint(data, offset)
            elif wire == 5:
                length = 4
            else:
                raise ValueError(f"Unsupported protobuf field wire type {wire}")
            end = offset + length
            if end > len(data):
                raise ValueError(f"Truncated protobuf field {number}")
            value, offset = data[offset:end], end
        fields[number].append(value)
    return fields


def field(record, number, default=None):
    return record[number][-1] if number in record else default


def number(record, identifier):
    value = field(record, identifier)
    if value is None:
        return None
    return struct.unpack("<d" if len(value) == 8 else "<f", value)[0]


def packed_ints(record, identifier):
    for values in record[identifier]:
        if isinstance(values, int):
            yield values
            continue
        offset = 0
        while offset < len(values):
            value, offset = varint(values, offset)
            yield value


def packed_doubles(record, identifier):
    for values in record[identifier]:
        yield from (value for (value,) in struct.iter_unpack("<d", values))


def quantile(data, share):
    ordered = sorted(data)
    return ordered[max(0, math.ceil(len(ordered) * share) - 1)]


def average(data):
    return statistics.fmean(data) if data else None


def median(data):
    return statistics.median(data) if data else None


def read_metrics(path, tail_seconds, allow_incomplete=False):
    compressed = path.read_bytes()
    stream = zlib.decompressobj(wbits=31)
    raw = stream.decompress(compressed)
    if stream.unused_data:
        raise ValueError("Expected one gzip stream in the capture")
    if not stream.eof and not allow_incomplete:
        raise ValueError("The gzip stream is incomplete. Pass --allow-incomplete to inspect its records.")
    # The writer appends complete top-level protobuf fields with a sync flush.
    # Even for an incomplete gzip stream, refuse a truncated protobuf record.
    capture = message(raw)
    if field(capture, 1) != 1:
        raise ValueError("Expected the pinned v1 Horizon performance capture")

    client = 1
    strings = {}
    for value in capture[3]:
        entry = message(value)
        strings[field(entry, 1), field(entry, 2)] = field(entry, 3, b"").decode()

    main_starts = collections.defaultdict(list)
    all_spans = collections.Counter()
    for value in capture[15]:
        record = message(value)
        if field(record, 1) != client:
            continue
        thread = field(record, 2)
        for name, time in zip(packed_ints(record, 3), packed_ints(record, 4), strict=True):
            label = strings.get((client, name))
            if label:
                all_spans[label] += 1
            if label == "submit" and strings.get((client, thread)) == "Horizon game":
                main_starts[thread].append(time / 1000.0)

    health = []
    for value in capture[9]:
        record = message(value)
        if field(record, 1) == client:
            health.append(
                {
                    "time_s": number(record, 2),
                    "fps": number(record, 8),
                    "p50_ms": number(record, 9),
                    "p95_ms": number(record, 10),
                    "p99_ms": number(record, 11),
                    "cpu_frame_median_ms": number(record, 13),
                    "gpu_frame_median_ms": number(record, 14),
                    "rss_mb": number(record, 17),
                    "chunks": field(record, 24, 0),
                }
            )

    memory = []
    for value in capture[14]:
        record = message(value)
        if field(record, 1) != client:
            continue
        entries = {}
        for name, size in zip(packed_ints(record, 4), packed_doubles(record, 5), strict=True):
            label = strings.get((client, name))
            if label is not None:
                entries[label] = size
        memory.append({"capture_time_s": number(record, 2), "measurements": entries})

    result = {
        "capture": {
            "sha256": hashlib.sha256(compressed).hexdigest(),
            "gzip_complete": stream.eof,
            "compressed_bytes": len(compressed),
            "decoded_protobuf_bytes": len(raw),
            "client_stop_present": any(field(message(value), 1) == client for value in capture[23]),
        },
        "timeline_spans": all_spans,
        "health_samples": health,
        "memory_snapshots": memory,
        "selected_frame_cadence": None,
    }
    if not main_starts:
        return result, []

    thread = max(main_starts, key=lambda candidate: len(main_starts[candidate]))
    world = sorted(set(main_starts[thread]))
    tail_end = world[-1] - 1500.0
    tail_begin = tail_end - tail_seconds * 1000.0
    last_world = [ts for ts in world if tail_begin <= ts <= tail_end]
    periods = [(b / 1000.0, b - a) for a, b in zip(last_world, last_world[1:])]
    if len(periods) < 8:
        return result, periods
    times = [elapsed for _, elapsed in periods]
    # Health records use the capture clock as well. Keep each CPU value's original one-second median.
    settled = [
        item for item in health if item["time_s"] is not None
        and tail_begin / 1000.0 <= item["time_s"] <= tail_end / 1000.0
    ]
    result["selected_frame_cadence"] = {
        "source": "intervals between game-thread submit spans of successive recorded rendered frames",
        "thread": strings.get((client, thread), thread),
        "frames": len(times),
        "duration_s": sum(times) / 1000.0,
        "average_fps": 1000.0 / average(times),
        "median_fps": 1000.0 / median(times),
        "p50_ms": median(times),
        "p95_ms": quantile(times, 0.95),
        "p99_ms": quantile(times, 0.99),
        "median_of_one_second_cpu_medians_ms": median(
            [sample["cpu_frame_median_ms"] for sample in settled if sample["cpu_frame_median_ms"] is not None]
        ),
        "median_of_one_second_gpu_medians_ms": median(
            [sample["gpu_frame_median_ms"] for sample in settled if sample["gpu_frame_median_ms"] is not None]
        ),
        "sampled_max_rss_mb": max(
            [sample["rss_mb"] for sample in settled if sample["rss_mb"] is not None], default=None
        ),
    }
    return result, periods


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--seconds", type=float, default=30.0)
    parser.add_argument("--allow-incomplete", action="store_true")
    parser.add_argument("--output", type=Path, default=Path.cwd())
    args = parser.parse_args()
    if not 0 < args.seconds <= 300:
        parser.error("--seconds must be between 0 and 300")
    result, frames = read_metrics(args.capture, args.seconds, args.allow_incomplete)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "capture-summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    with (args.output / "cadence.csv").open("w", newline="", encoding="utf-8") as file:
        writer = csv.writer(file)
        writer.writerow(("capture_time_s", "rendered_frame_interval_ms"))
        writer.writerows(frames)
    print(json.dumps(result["selected_frame_cadence"], indent=2))


if __name__ == "__main__":
    main()
