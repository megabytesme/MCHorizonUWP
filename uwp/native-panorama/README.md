# Native D3D11 title panorama

Draws Horizon's six title panorama faces using six ordinary 2D textures and
quad meshes, compatible with Direct3D feature level 9_1. The caller supplies
`gfx::panorama::Faces` pixel data in its +X, -X, +Y, -Y, +Z, -Z order and
invokes `draw` between the frame clear and HUD draw. The rotation and
projection follow Horizon's existing panorama renderer.

The backend currently uses point sampling rather than the original linear
filter, and this crate does not render game terrain. Both the face-mapping
and projection unit tests pass on x64. An isolated x64 UWP GPU probe created
an FL9_1 device, drew six distinct-color faces, read the back buffer, verified
full coverage at two rotations and checked that the sampled face changed.
The crate also builds for ARM32 UWP with Windows SDK 15063; these checks do
not establish visual correctness on the phone.

The isolated GPU probe lives in `probe/`. Build its static library for the
x64 UWP target with patched `rust-src` as described in `../rust-std/README.md`,
then set `HORIZON_PANORAMA_PROBE_LIB` to that library and run its
`build-host.cmd`. Register its host in a separate UWP package identity. It
writes `LocalState/panorama-probe.txt` and exits after GPU readback; a zero
result indicates both coverage and rotation checks passed.
