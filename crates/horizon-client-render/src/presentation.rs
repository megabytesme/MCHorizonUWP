//! The window-facing part of a renderer, independent of its graphics API.
//!
//! `Frame` holds the CPU-side inputs for a single frame. Surface presentation
//! and terrain mesh lifecycle pass through backend contracts; device creation,
//! LOD meshes and renderer diagnostics still use `gfx::Renderer` directly.
//! These also need interfaces before the entire game can use another backend.

use crate::{
    Result,
    chunk::ColumnLayout,
    gfx::{Frame, Renderer, SectionKey},
    graphics::PresentMode,
    mesher::SectionMesh,
};

/// Presentation and surface lifecycle operations shared by game renderers.
pub trait PresentationBackend {
    /// Size of the active drawing surface in physical pixels.
    fn viewport_size(&self) -> [u32; 2];
    /// Returns whether the frame was actually presented.
    fn present_frame(&mut self, frame: &Frame<'_>) -> Result<bool>;
    fn resize(&mut self, width: u32, height: u32);
    fn release_surface(&mut self);
    fn recreate_surface(&mut self) -> Result<()>;
    fn set_present_mode(&mut self, vsync: bool, mode: PresentMode);
}

impl PresentationBackend for Renderer {
    fn viewport_size(&self) -> [u32; 2] {
        Renderer::viewport_size(self)
    }
    fn present_frame(&mut self, frame: &Frame<'_>) -> Result<bool> {
        self.render(frame)
    }
    fn resize(&mut self, width: u32, height: u32) {
        Renderer::resize(self, width, height);
    }
    fn release_surface(&mut self) {
        Renderer::release_surface(self);
    }
    fn recreate_surface(&mut self) -> Result<()> {
        Renderer::recreate_surface(self)
    }
    fn set_present_mode(&mut self, vsync: bool, mode: PresentMode) {
        Renderer::set_present_mode(self, vsync, mode);
    }
}

/// The CPU mesh lifecycle, independent of the GPU representation.
///
/// Upload replaces the previous mesh for a section. A failed upload returns an
/// error, allowing the game to report it rather than silently discard it.
pub trait TerrainBackend {
    fn upload_section(&mut self, key: SectionKey, mesh: &SectionMesh) -> Result<()>;
    fn clear_sections(&mut self, layout: ColumnLayout);
    fn remove_columns(&mut self, columns: &mut dyn Iterator<Item = (i32, i32)>);
    fn has_section(&self, key: SectionKey) -> bool;
    fn section_count(&self) -> usize;
}

impl TerrainBackend for Renderer {
    fn upload_section(&mut self, key: SectionKey, mesh: &SectionMesh) -> Result<()> {
        Renderer::upload(self, key, mesh);
        Ok(())
    }
    fn clear_sections(&mut self, layout: ColumnLayout) {
        Renderer::clear_sections(self, layout);
    }
    fn remove_columns(&mut self, columns: &mut dyn Iterator<Item = (i32, i32)>) {
        Renderer::remove_columns(self, columns);
    }
    fn has_section(&self, key: SectionKey) -> bool {
        Renderer::has_section(self, key)
    }
    fn section_count(&self) -> usize {
        Renderer::section_count(self)
    }
}

/// Upload one mesh through the shared contract; failures leave it available for retry.
pub fn upload_section(
    backend: &mut dyn TerrainBackend,
    key: SectionKey,
    mesh: &SectionMesh,
) -> Result<()> {
    backend.upload_section(key, mesh)
}

pub fn clear_sections(backend: &mut dyn TerrainBackend, layout: ColumnLayout) {
    backend.clear_sections(layout);
}

pub fn remove_columns(
    backend: &mut dyn TerrainBackend,
    columns: impl IntoIterator<Item = (i32, i32)>,
) {
    backend.remove_columns(&mut columns.into_iter());
}

pub fn has_section(backend: &dyn TerrainBackend, key: SectionKey) -> bool {
    backend.has_section(key)
}

/// Present through the shared contract, including loading and save screens.
pub fn present(backend: &mut dyn PresentationBackend, frame: &Frame<'_>) -> Result<bool> {
    backend.present_frame(frame)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[derive(Default)]
    struct Fake {
        size: [u32; 2],
        released: bool,
    }

    impl PresentationBackend for Fake {
        fn viewport_size(&self) -> [u32; 2] {
            self.size
        }
        fn present_frame(&mut self, _: &Frame<'_>) -> Result<bool> {
            Ok(true)
        }
        fn resize(&mut self, width: u32, height: u32) {
            self.size = [width, height];
        }
        fn release_surface(&mut self) {
            self.released = true;
        }
        fn recreate_surface(&mut self) -> Result<()> {
            self.released = false;
            Ok(())
        }
        fn set_present_mode(&mut self, _: bool, _: PresentMode) {}
    }

    #[derive(Default)]
    struct FakeTerrain {
        sections: std::collections::HashSet<SectionKey>,
        fail_next: bool,
    }

    impl TerrainBackend for FakeTerrain {
        fn upload_section(&mut self, key: SectionKey, _: &SectionMesh) -> Result<()> {
            if std::mem::take(&mut self.fail_next) {
                return Err("simulated allocation failure".into());
            }
            self.sections.insert(key);
            Ok(())
        }
        fn clear_sections(&mut self, _: ColumnLayout) {
            self.sections.clear();
        }
        fn remove_columns(&mut self, columns: &mut dyn Iterator<Item = (i32, i32)>) {
            let gone: std::collections::HashSet<_> = columns.collect();
            self.sections.retain(|key| !gone.contains(&(key.0, key.2)));
        }
        fn has_section(&self, key: SectionKey) -> bool {
            self.sections.contains(&key)
        }
        fn section_count(&self) -> usize {
            self.sections.len()
        }
    }

    #[test]
    fn terrain_upload_failure_is_returned_to_caller_for_retry() {
        let mut fake = FakeTerrain {
            fail_next: true,
            ..Default::default()
        };
        let terrain: &mut dyn TerrainBackend = &mut fake;
        let mesh = SectionMesh::default();
        assert!(upload_section(terrain, (1, 0, 2), &mesh).is_err());
        assert!(!has_section(terrain, (1, 0, 2)));
        upload_section(terrain, (1, 0, 2), &mesh).unwrap();
        assert!(has_section(terrain, (1, 0, 2)));
    }

    #[test]
    fn terrain_lifecycle_routes_cpu_meshes_and_column_removal() {
        let mut fake = FakeTerrain::default();
        let terrain: &mut dyn TerrainBackend = &mut fake;
        let mesh = SectionMesh::default();
        upload_section(terrain, (1, 0, 2), &mesh).unwrap();
        upload_section(terrain, (1, 1, 2), &mesh).unwrap();
        upload_section(terrain, (3, 0, 4), &mesh).unwrap();
        assert_eq!(terrain.section_count(), 3);
        remove_columns(terrain, [(1, 2)]);
        assert!(!has_section(terrain, (1, 0, 2)));
        assert!(!has_section(terrain, (1, 1, 2)));
        assert!(has_section(terrain, (3, 0, 4)));
        clear_sections(terrain, ColumnLayout::VANILLA);
        assert_eq!(terrain.section_count(), 0);
    }

    #[test]
    fn surface_lifecycle_is_callable_through_the_backend_contract() {
        let mut fake = Fake::default();
        let backend: &mut dyn PresentationBackend = &mut fake;
        backend.resize(1280, 720);
        assert_eq!(backend.viewport_size(), [1280, 720]);
        backend.release_surface();
        assert!(fake.released);
        PresentationBackend::recreate_surface(&mut fake).unwrap();
        assert!(!fake.released);
    }
}
