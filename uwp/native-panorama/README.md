# Native D3D11 title panorama

Draws Horizon's six title panorama faces using six ordinary 2D textures and
quad meshes, compatible with Direct3D feature level 9_1. The caller supplies
`gfx::panorama::Faces` pixel data in its +X, -X, +Y, -Y, +Z, -Z order and
invokes `draw` between the frame clear and HUD draw. The rotation and
projection follow Horizon's existing panorama renderer.

The backend currently uses point sampling rather than the original linear
filter, and this crate does not render game terrain. Both the face-mapping
and projection unit tests pass on x64. The crate also builds for ARM32 UWP
with Windows SDK 15063; those checks do not establish visual correctness on
the phone.
