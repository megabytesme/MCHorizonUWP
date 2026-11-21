"""Stage an opt-in fault diagnostic overlay from the exact rollback assembly."""

import argparse
import hashlib
import json
from pathlib import Path


DX12 = "third_party/wgpu-dx11/wgpu-hal/src/dx12/mod.rs"
XAML = "uwp/pregame/host/XamlShell.cpp"
EXPORTS = "uwp/host/host-exports.def"
HEADER = "uwp/pregame/host/Dx12PanelFaultProbe.h"
ROLLBACK_DX12_SHA256 = "60fb37e54d6fbf04152decee7aa2018497d55bd69f288c39baab4fcb2f69bffa"


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one diagnostic anchor: {old}")
    return source.replace(old, new)


def instrument_dx12(source):
    source = replace_once(source, "    fn horizon_uwp_detach_game_swapchain() -> i32;",
                          "    fn horizon_uwp_detach_game_swapchain() -> i32;\n"
                          "    fn horizon_uwp_hdr_panel_fault(site: u32) -> i32;")
    source = replace_once(source, "            detach_uwp_panel()?;\n            pending.take();",
                          "            detach_uwp_panel()?;\n            pending.take();\n"
                          '            log::info!("UWP DX12 SwapChainPanel pending detach retry succeeded");')
    source = replace_once(source, "            if detach_uwp_panel().is_err() {", """            let detached = if unsafe { horizon_uwp_hdr_panel_fault(4) } != 0 {
                log::error!("Injected one-shot UWP DX12 SwapChainPanel rollback detach failure");
                Err(crate::SurfaceError::Other("UWP DX12 SwapChainPanel detach"))
            } else {
                detach_uwp_panel()
            };
            if detached.is_err() {""")
    source = replace_once(source, "            let support = unsafe { swap_chain.CheckColorSpaceSupport(color_space) };", """            let site = u32::from(rollback.attached_chain.is_some());
            let query_space = if unsafe { horizon_uwp_hdr_panel_fault(site) } != 0 {
                Dxgi::Common::DXGI_COLOR_SPACE_CUSTOM
            } else {
                color_space
            };
            let support = unsafe { swap_chain.CheckColorSpaceSupport(query_space) };""")
    source = replace_once(source, "        let color_result = unsafe { swap_chain.SetColorSpace1(color_space) };", """        #[cfg(target_vendor = "uwp")]
        let set_space = if uwp_panel && config.color_space != wgt::SurfaceColorSpace::Srgb
            && unsafe { horizon_uwp_hdr_panel_fault(2 + u32::from(rollback.attached_chain.is_some())) } != 0
        {
            Dxgi::Common::DXGI_COLOR_SPACE_CUSTOM
        } else {
            color_space
        };
        #[cfg(not(target_vendor = "uwp"))]
        let set_space = color_space;
        let color_result = unsafe { swap_chain.SetColorSpace1(set_space) };""")
    return replace_once(source,
                        "horizon_uwp_record_swapchain_color(swap_chain.as_raw(), color_space.0, status);",
                        "horizon_uwp_record_swapchain_color(swap_chain.as_raw(), set_space.0, status);")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", type=Path, help="Read-only 4ca69dcd assembled candidate root")
    parser.add_argument("output", type=Path, help="New scratch directory for the diagnostic overlay")
    args = parser.parse_args()
    assembly, output = args.assembly.resolve(), args.output.resolve()
    if output.exists() or output.is_relative_to(assembly):
        parser.error("Output must be a new directory outside the input assembly")
    try:
        inputs = {filename: (assembly / filename).read_bytes() for filename in (DX12, XAML, EXPORTS)}
        if hashlib.sha256(inputs[DX12]).hexdigest() != ROLLBACK_DX12_SHA256:
            raise ValueError("DX12 source differs from the exact 4ca69dcd rollback revision")
        dx12, shell, exports = (inputs[filename].decode("utf-8").replace("\r\n", "\n")
                                for filename in (DX12, XAML, EXPORTS))
        files = {
            DX12: instrument_dx12(dx12),
            XAML: replace_once(shell, 'extern "C" void __cdecl horizon_uwp_record_swapchain_color(',
                               '#include "Dx12PanelFaultProbe.h"\n\n'
                               'extern "C" void __cdecl horizon_uwp_record_swapchain_color('),
            EXPORTS: replace_once(exports, "horizon_uwp_get_advanced_color\n",
                                  "horizon_uwp_get_advanced_color\nhorizon_uwp_hdr_panel_fault\n"),
            HEADER: Path(__file__).with_name("Dx12PanelFaultProbe.h").read_text(encoding="utf-8"),
        }
        hashes = {}
        for filename, content in files.items():
            target = output / filename
            target.parent.mkdir(parents=True, exist_ok=True)
            if b"\r\n" in inputs.get(filename, b""):
                content = content.replace("\n", "\r\n")
            data = content.encode("utf-8")
            target.write_bytes(data)
            hashes[filename] = hashlib.sha256(data).hexdigest()
        (output / "panel-fault-overlay.json").write_text(json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
    except (OSError, ValueError) as exc:
        parser.exit(1, f"Cannot stage panel fault diagnostic: {exc}\n")


if __name__ == "__main__":
    main()
