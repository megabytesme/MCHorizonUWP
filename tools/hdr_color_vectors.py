"""Check pinned Horizon fragment shaders by reading their offscreen render targets.

Install wgpu==0.33.0 in a disposable Python environment and pass a Horizon
source tree. This test requests a CPU adapter and does not open a window.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct

import wgpu


PIN = "0a875da91dbb0fdcb7ba8c42c11341aa229af51a"
SHADERS = {
    "crates/horizon-shader-api/packs/horizon_hdr/hdr_look.wgsl": "5674f4daeb4d49039b838519b650083770ac3cb8",
    "crates/horizon-client-render/src/hdr_light.wgsl": "784e6e44988db3dd8efb8e1c257d1f7543fbf2d1",
    "crates/horizon-client-render/src/hdr.wgsl": "d18f6cd305ef13561ec29624e5a56cf6865a70ca",
}


def shader_source(root):
    sources = []
    for path, blob in SHADERS.items():
        data = (root / path).read_bytes().replace(b"\r\n", b"\n")
        actual = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
        if actual != blob:
            raise ValueError(f"{path} differs from the pinned shader at {PIN}")
        sources.append(data.decode("utf-8"))
    return "".join(sources)


def pq(nits):
    # Inverse of the reference EOTF, ITU-R BT.2100 Table 4.
    y = (max(0.0, min(10000.0, nits)) / 10000.0) ** (2610 / 16384)
    return ((3424 / 4096 + 2413 / 128 * y) / (1 + 2392 / 128 * y)) ** (2523 / 32)


def to_bt2020(rgb):
    # ITU-R BT.2087-0, linear-light conversion.
    rows = ((.6274, .3293, .0433), (.0691, .9195, .0114), (.0164, .0880, .8956))
    return [sum(c * v for c, v in zip(row, rgb)) for row in rows]


def half(value):
    return struct.unpack("<e", struct.pack("<e", value))[0]


class Pass:
    def __init__(self, device, source):
        self.device = device
        self.shader = device.create_shader_module(code=source)
        entries = []
        for i in range(6):
            entry = {"binding": i, "visibility": wgpu.ShaderStage.FRAGMENT}
            if i == 2:
                entry["buffer"] = {"type": "uniform"}
            else:
                entry["texture"] = {"sample_type": "float", "view_dimension": "2d"}
            entries.append(entry)
        self.bindings = device.create_bind_group_layout(entries=entries)
        self.layout = device.create_pipeline_layout(bind_group_layouts=[self.bindings])
        self.pipelines = {}

    def texture(self, width, fmt, data):
        size = (width, 1, 1)
        usage = wgpu.TextureUsage.TEXTURE_BINDING | wgpu.TextureUsage.COPY_DST
        texture = self.device.create_texture(size=size, format=fmt, usage=usage)
        self.device.queue.write_texture({"texture": texture}, data, {"bytes_per_row": len(data)}, size)
        return texture.create_view()

    def render(self, samples, entry, fmt, paper, peak, black=0, ui=1, calibration=(0, 0), look=1,
               boost_to_2020=False):
        width = len(samples)
        gamma = b"".join(struct.pack("<4e", *world, 1) for world, _, _ in samples)
        frame = self.texture(width, "rgba16float", gamma)
        hud = self.texture(width, "rgba8unorm", bytes(c for _, pixel, _ in samples for c in (*pixel, 255)))
        coverage = self.texture(width, "r8unorm", bytes(c for _, _, c in samples))
        logo = self.texture(1, "r8unorm", bytes([255]))
        params = [paper / 80, peak / paper, fmt == "rgb10a2unorm", black, ui, look, 1, 0,
                  *calibration, 1, 0] + [0] * 16
        if boost_to_2020:
            # The primaries are stretched all the way to the BT.2020 triangle:
            # red, green and blue map to BT.2020's unit basis vectors.
            for i in (12, 17, 22, 24):
                params[i] = 1
        uniform = self.device.create_buffer_with_data(data=struct.pack("<28f", *params), usage=wgpu.BufferUsage.UNIFORM)
        resources = [frame, frame, {"buffer": uniform}, logo, coverage, hud]
        group = self.device.create_bind_group(layout=self.bindings,
            entries=[{"binding": i, "resource": resource} for i, resource in enumerate(resources)])
        if (entry, fmt) not in self.pipelines:
            self.pipelines[entry, fmt] = self.device.create_render_pipeline(
                layout=self.layout, vertex={"module": self.shader, "entry_point": "vs_hdr"},
                fragment={"module": self.shader, "entry_point": entry, "targets": [{"format": fmt}]})
        size = (width, 1, 1)
        target = self.device.create_texture(size=size, format=fmt,
            usage=wgpu.TextureUsage.RENDER_ATTACHMENT | wgpu.TextureUsage.COPY_SRC)
        encoder = self.device.create_command_encoder()
        render = encoder.begin_render_pass(color_attachments=[{
            "view": target.create_view(), "clear_value": (0, 0, 0, 0), "load_op": "clear", "store_op": "store"}])
        render.set_pipeline(self.pipelines[entry, fmt])
        render.set_bind_group(0, group)
        render.draw(3)
        render.end()
        self.device.queue.submit([encoder.finish()])
        stride = 8 if fmt == "rgba16float" else 4
        data = self.device.queue.read_texture({"texture": target}, {"bytes_per_row": width * stride}, size)
        if fmt == "rgb10a2unorm":
            return [tuple((p >> shift) & 1023 for shift in (0, 10, 20)) for p, in struct.iter_unpack("<I", data)]
        code = "<4e" if fmt == "rgba16float" else "<4B"
        return [pixel[:3] for pixel in struct.iter_unpack(code, data)]


def check(runner):
    results = []

    def record(case, fmt, expected, actual, tolerance):
        errors = [abs(want - got) for want, got in zip(expected, actual, strict=True)]
        results.append(dict(case=case, target=fmt, expected=expected, actual=actual, tolerance=tolerance,
                            passed=all(math.isfinite(error) and error <= tolerance for error in errors)))

    paper, peak = 203, 1000
    black = (0, 0, 0)
    vectors = [("black", black), ("near black", (.01,) * 3), ("80 nit grey", (80,) * 3),
               ("paper white", (paper,) * 3), ("400 nit grey", (400,) * 3),
               ("peak", (peak,) * 3), ("above peak", (1200,) * 3),
               ("red", (paper, 0, 0)), ("green", (0, paper, 0)), ("blue", (0, 0, paper))]
    samples = []
    references = []
    for _, rgb in vectors:
        world = tuple(half((channel / paper) ** (1 / 2.2)) for channel in rgb)
        # The frame entering the final composite has already been stored at FP16.
        samples.append((world, tuple(round(min(1, c) * 255) for c in world), 0))
        references.append(tuple(min(peak, (channel ** 2.2) * paper) for channel in world))
    samples.append((black, (255, 255, 255), 255))
    references.append((paper / 2,) * 3)
    names = [name for name, _ in vectors] + ["opaque HUD at 50%"]
    for fmt in ("rgba16float", "rgb10a2unorm"):
        pixels = runner.render(samples, "fs_hdr_layered", fmt, paper, peak, ui=.5)
        for name, nits, actual in zip(names, references, pixels, strict=True):
            if fmt == "rgba16float":
                expected = [half(n / 80) for n in nits]
                tolerance = max(0.00001, max(expected) / 1024)
            else:
                expected = [round(1023 * pq(n)) for n in to_bt2020(nits)]
                tolerance = 1
            record(name, fmt, expected, actual, tolerance)
        for name, black_nits, calibration, expected_nits in [
                ("calibrated black", .05, (0, 0), .05), ("black slider", 0, (1, .1), .1),
                ("peak slider", 0, (2, 450), 450), ("paper slider", 0, (3, 185), 185)]:
            actual, = runner.render([(black, black, 0)], "fs_hdr_layered", fmt, paper, peak,
                                    black=black_nits, calibration=calibration)
            expected = half(expected_nits / 80) if fmt == "rgba16float" else round(1023 * pq(expected_nits))
            record(name, fmt, [expected] * 3, actual, max(.00001, expected / 1024) if fmt == "rgba16float" else 1)

    # Extended-range scRGB represents a BT.2020 primary using negative BT.709 channels.
    primary = [((1, 0, 0), (255, 0, 0), 0)]
    actual, = runner.render(primary, "fs_hdr_layered", "rgba16float", paper, peak, boost_to_2020=True)
    extended_red = [half(c * paper / 80) for c in (1.6605, -.1246, -.0182)]
    record("Color Boost BT.2020 red", "rgba16float",
           extended_red, actual, math.ldexp(1, math.floor(math.log2(max(extended_red))) - 10))
    actual, = runner.render(primary, "fs_hdr_layered", "rgb10a2unorm", paper, peak, boost_to_2020=True)
    record("Color Boost BT.2020 red", "rgb10a2unorm", (round(pq(paper) * 1023), 0, 0), actual, 1)

    # Horizon's own authored 1000-nit scene peak is mapped to the configured display peak.
    top = (1000 / paper) ** (1 / 2.2)
    actual, = runner.render([((top,) * 3, black, 0)], "fs_tonemap", "rgba16float", paper, 450)
    record("Horizon look 1000 nit scene on 450 nit display", "rgba16float",
           [half((450 / paper) ** (1 / 2.2))] * 3, actual, .003)
    # The classic pack pass accepts an SDR picture; full white is presented at paper white.
    actual, = runner.render([((1, 1, 1), black, 0)], "fs_hdr", "rgba16float", paper, peak)
    record("classic pack output pass at paper white", "rgba16float", [half(paper / 80)] * 3, actual, .002)
    # With 1x headroom and the look disabled, the tone pass clips at paper white.
    actual, = runner.render([((2., .5, .25), black, 0)], "fs_tonemap", "rgba16float", paper, paper, look=0)
    record("vanilla tone curve at 1x headroom", "rgba16float", (1, .5, .25), actual, .0005)
    # The world's screenshot uses the brightest channel as its SDR white and preserves hue.
    actual, = runner.render([((2., 1., .5), black, 0)], "fs_sdr_screenshot", "rgba8unorm", paper, peak)
    record("SDR screenshot highlight", "rgba8unorm", (255, 128, 64), actual, 1)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    if os.name == "nt":
        os.environ.setdefault("WGPU_BACKEND_TYPE", "D3D12")
    adapter = wgpu.gpu.request_adapter_sync(force_fallback_adapter=True)
    if adapter.info["adapter_type"] != "CPU":
        raise RuntimeError(f"Expected a software adapter, got {adapter.info}")
    rows = check(Pass(adapter.request_device_sync(), shader_source(args.source)))
    result = dict(engine_commit=PIN, wgsl_git_blobs=SHADERS, test_backend=dict(adapter.info),
                  wgpu_python=wgpu.__version__, scope="offscreen software shader readback",
                  references=["https://www.itu.int/rec/R-REC-BT.2100", "https://www.itu.int/rec/R-REC-BT.2087"],
                  passed=all(row["passed"] for row in rows), vectors=rows)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"{sum(row['passed'] for row in rows)}/{len(rows)} checks passed")
    for row in rows:
        if not row["passed"]:
            print(row)
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
