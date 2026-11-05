//! The Horizon title panorama, drawn with six FL9_1-compatible 2D textures.
//!
//! Input faces follow Horizon's native cube order (+X, -X, +Y, -Y, +Z, -Z).
//! Each face has already been vertically flipped by `panorama::Faces::load`.

use glam::Mat4;
use horizon_uwp_d3d11::{DrawPass, Mesh, Renderer, Texture, Vertex};
use std::error::Error;

type Result<T> = std::result::Result<T, Box<dyn Error>>;

const CORNERS: [[f32; 3]; 8] = [
    [-1., -1., -1.],
    [1., -1., -1.],
    [1., 1., -1.],
    [-1., 1., -1.],
    [-1., -1., 1.],
    [1., -1., 1.],
    [1., 1., 1.],
    [-1., 1., 1.],
];

// The exact quad order of `gfx/panorama.wgsl`, with each quad's corresponding
// layer in `Faces`. Culling is disabled for the interior of the cube.
const QUADS: [([usize; 4], usize); 6] = [
    ([4, 7, 6, 5], 4),
    ([5, 6, 2, 1], 0),
    ([1, 2, 3, 0], 5),
    ([0, 3, 7, 4], 1),
    ([7, 3, 2, 6], 2),
    ([0, 4, 5, 1], 3),
];

/// Map a cube direction to the relevant face's ordinary 2D coordinates.
fn face_uv(face: usize, [x, y, z]: [f32; 3]) -> [f32; 2] {
    let (s, t) = match face {
        0 => (-z, -y),
        1 => (z, -y),
        2 => (x, z),
        3 => (x, -z),
        4 => (x, -y),
        5 => (-x, -y),
        _ => unreachable!("cube face index"),
    };
    [(s + 1.) * 0.5, (t + 1.) * 0.5]
}

/// Returns the projection used by Horizon's wgpu panorama renderer. The
/// returned nested arrays can be passed directly to the native backend: each
/// is a row of the transposed matrix expected by its row-vector HLSL shader.
pub fn projection(viewport: [u32; 2], yaw: f32, pitch: f32) -> Result<[[f32; 4]; 4]> {
    if viewport.contains(&0) || !yaw.is_finite() || !pitch.is_finite() {
        return Err("invalid panorama viewport or rotation".into());
    }
    let m = Mat4::perspective_rh(
        85.0f32.to_radians(),
        viewport[0] as f32 / viewport[1] as f32,
        0.05,
        10.,
    ) * Mat4::from_rotation_x(std::f32::consts::PI)
        * Mat4::from_rotation_x(pitch.to_radians())
        * Mat4::from_rotation_y(yaw.to_radians());
    Ok(m.to_cols_array_2d())
}

/// GPU resources for the six faces of a title panorama.
pub struct Panorama {
    textures: Vec<Texture>,
    meshes: Vec<(usize, Mesh)>,
}

impl Panorama {
    /// Upload finalized Horizon `Faces` pixels; they are not retained on CPU.
    pub fn new(renderer: &Renderer, size: u32, pixels: &[u8]) -> Result<Self> {
        let face_bytes = (size as usize)
            .checked_mul(size as usize)
            .and_then(|n| n.checked_mul(4))
            .ok_or("panorama size overflow")?;
        if size == 0 || size > 2048 || face_bytes.checked_mul(6) != Some(pixels.len()) {
            return Err("invalid panorama face size or pixels".into());
        }
        let textures: Vec<_> = pixels
            .chunks_exact(face_bytes)
            .map(|face| renderer.create_atlas(size, size, face))
            .collect::<std::result::Result<_, _>>()?;
        let mut meshes = Vec::with_capacity(6);
        for (corners, face) in QUADS {
            let vertices: Vec<_> = corners
                .into_iter()
                .map(|index| {
                    let position = CORNERS[index];
                    Vertex {
                        position,
                        uv: face_uv(face, position),
                        color: [1.; 4],
                    }
                })
                .collect();
            meshes.push((face, renderer.create_mesh(&vertices, &[0, 1, 2, 0, 2, 3])?));
        }
        Ok(Self { textures, meshes })
    }

    /// Draw before the HUD after `Renderer::begin_frame` on the UI thread.
    pub fn draw(
        &self,
        renderer: &Renderer,
        viewport: [u32; 2],
        yaw: f32,
        pitch: f32,
    ) -> Result<()> {
        let matrix = projection(viewport, yaw, pitch)?;
        for (face, mesh) in &self.meshes {
            renderer.draw_pass(mesh, &self.textures[*face], matrix, DrawPass::Overlay);
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_quad_covers_its_own_face() {
        let mut faces = [false; 6];
        for (corners, face) in QUADS {
            assert!(!faces[face]);
            faces[face] = true;
            let mut uvs = corners.map(|i| face_uv(face, CORNERS[i]));
            uvs.sort_by(|a, b| a[0].total_cmp(&b[0]).then(a[1].total_cmp(&b[1])));
            assert_eq!(uvs, [[0., 0.], [0., 1.], [1., 0.], [1., 1.]]);
        }
        assert!(faces.into_iter().all(|v| v));
    }

    #[test]
    fn projection_is_valid_and_rejects_empty_viewports() {
        assert!(projection([0, 720], 0., 0.).is_err());
        assert!(projection([1280, 720], f32::NAN, 0.).is_err());
        assert!(
            projection([1280, 720], 15., 10.)
                .unwrap()
                .iter()
                .flatten()
                .all(|v| v.is_finite())
        );
    }
}
