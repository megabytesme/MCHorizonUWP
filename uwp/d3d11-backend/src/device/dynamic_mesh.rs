//! Bounded, reusable vertex buffers for ordered, changing entity geometry.
use super::*;

/// A dynamic vertex buffer with an immutable sequential 16-bit index buffer.
/// Update it at most once per draw; WRITE_DISCARD allows the driver to retain
/// the previous contents until queued draws have finished.
pub struct DynamicMesh {
    mesh: Mesh,
    capacity: usize,
}

impl DynamicMesh {
    pub fn capacity(&self) -> usize {
        self.capacity
    }
    pub fn mesh(&self) -> &Mesh {
        &self.mesh
    }
}

impl Renderer {
    /// Allocate a reusable mesh for up to `capacity` sequential vertices.
    pub fn create_dynamic_mesh(&self, capacity: usize) -> Result<DynamicMesh> {
        if capacity == 0 || capacity > u16::MAX as usize {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let byte_width = capacity
            .checked_mul(std::mem::size_of::<Vertex>())
            .and_then(|n| u32::try_from(n).ok())
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let desc = D3D11_BUFFER_DESC {
            ByteWidth: byte_width,
            Usage: D3D11_USAGE_DYNAMIC,
            BindFlags: D3D11_BIND_VERTEX_BUFFER.0 as u32,
            CPUAccessFlags: D3D11_CPU_ACCESS_WRITE.0 as u32,
            ..Default::default()
        };
        let mut vertices = None;
        unsafe {
            self.device.CreateBuffer(&desc, None, Some(&mut vertices))?;
        }
        let indices: Vec<u16> = (0..capacity).map(|i| i as u16).collect();
        Ok(DynamicMesh {
            mesh: Mesh {
                vertices: vertices.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                indices: immutable_buffer(
                    &self.device,
                    bytemuck::cast_slice(&indices),
                    D3D11_BIND_INDEX_BUFFER,
                )?,
                index_count: 0,
            },
            capacity,
        })
    }

    /// Replace the mesh contents without overwriting data used by prior draws.
    pub fn update_dynamic_mesh(&self, mesh: &mut DynamicMesh, vertices: &[Vertex]) -> Result<()> {
        if vertices.is_empty() || vertices.len() > mesh.capacity {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
        unsafe {
            self.context.Map(
                &mesh.mesh.vertices,
                0,
                D3D11_MAP_WRITE_DISCARD,
                0,
                Some(&mut mapped),
            )?;
            std::ptr::copy_nonoverlapping(
                vertices.as_ptr().cast::<u8>(),
                mapped.pData.cast::<u8>(),
                std::mem::size_of_val(vertices),
            );
            self.context.Unmap(&mesh.mesh.vertices, 0);
        }
        mesh.mesh.index_count = vertices.len() as u32;
        Ok(())
    }
}
