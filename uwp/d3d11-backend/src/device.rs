use std::ffi::c_void;
use windows::{
    Win32::{
        Foundation::{E_INVALIDARG, HMODULE},
        Graphics::{
            Direct3D::{
                D3D_DRIVER_TYPE_HARDWARE, D3D_FEATURE_LEVEL_9_1,
                D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST,
            },
            Direct3D11::*,
            Dxgi::{
                Common::*, DXGI_MATRIX_3X2_F, DXGI_PRESENT, DXGI_SCALING_NONE,
                DXGI_SCALING_STRETCH, DXGI_SWAP_CHAIN_DESC1, DXGI_SWAP_CHAIN_FLAG,
                DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL, DXGI_USAGE_RENDER_TARGET_OUTPUT, IDXGIAdapter,
                IDXGIDevice, IDXGIFactory2, IDXGISwapChain1, IDXGISwapChain2,
            },
        },
    },
    core::{Error, IUnknown, Interface, Result, s},
};

mod dynamic_mesh;
pub use dynamic_mesh::DynamicMesh;

const VS: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/terrain-vs.cso"));
const PS: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/terrain-ps.cso"));
const PS_OPAQUE: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/terrain-opaque-ps.cso"));
const PS_ENTITY: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/entity-ps.cso"));
const PS_ALPHA: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/terrain-alpha-ps.cso"));
const PS_INVERT: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/terrain-invert-ps.cso"));

/// Expanded vertex format used by the feature-level 9_1 terrain pipeline.
#[repr(C)]
#[derive(Clone, Copy, Debug, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Vertex {
    pub position: [f32; 3],
    pub uv: [f32; 2],
    pub color: [f32; 4],
}

/// An immutable indexed mesh; indices are 16-bit for feature level 9_1.
pub struct Mesh {
    vertices: ID3D11Buffer,
    indices: ID3D11Buffer,
    index_count: u32,
}

/// A single 2D RGBA texture atlas. Array textures are unavailable at 9_1.
pub struct Texture {
    view: ID3D11ShaderResourceView,
    texture: ID3D11Texture2D,
    width: u32,
    height: u32,
    mipmapped: bool,
}

/// Rendering state for solid/cutout terrain, translucent geometry, or UI.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum DrawPass {
    Opaque,
    Entity,
    Cutout,
    Translucent,
    Overlay,
    OverlayInvert,
    OverlayAdditive,
    OverlayVignette,
}

/// Depth convention used by the projection matrix submitted with each draw.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum DepthConvention {
    Forward,
    Reverse,
}

struct Pipeline {
    vertex_shader: ID3D11VertexShader,
    pixel_shader: ID3D11PixelShader,
    opaque_shader: ID3D11PixelShader,
    entity_shader: ID3D11PixelShader,
    alpha_shader: ID3D11PixelShader,
    invert_shader: ID3D11PixelShader,
    input_layout: ID3D11InputLayout,
    constants: ID3D11Buffer,
    sampler: ID3D11SamplerState,
    mip_sampler: ID3D11SamplerState,
    rasterizer: ID3D11RasterizerState,
    depth_write: ID3D11DepthStencilState,
    depth_read: ID3D11DepthStencilState,
    reverse_write: ID3D11DepthStencilState,
    reverse_read: ID3D11DepthStencilState,
    depth_off: ID3D11DepthStencilState,
    alpha_blend: ID3D11BlendState,
    invert_blend: ID3D11BlendState,
    additive_blend: ID3D11BlendState,
    vignette_blend: ID3D11BlendState,
}

/// A hardware Direct3D 11 device deliberately restricted to feature level 9_1.
pub struct Renderer {
    device: ID3D11Device,
    context: ID3D11DeviceContext,
    swap_chain: IDXGISwapChain1,
    target: Option<ID3D11RenderTargetView>,
    depth: Option<ID3D11DepthStencilView>,
    pipeline: Pipeline,
    depth_convention: DepthConvention,
}

impl Renderer {
    /// Identifies the adapter backing this device for the in-game debug overlay.
    pub fn adapter_info(&self) -> Result<(String, u32)> {
        let dxgi: IDXGIDevice = self.device.cast()?;
        let desc = unsafe { dxgi.GetAdapter()?.GetDesc()? };
        let end = desc
            .Description
            .iter()
            .position(|&c| c == 0)
            .unwrap_or(desc.Description.len());
        Ok((
            String::from_utf16_lossy(&desc.Description[..end]),
            desc.VendorId,
        ))
    }

    /// Creates a renderer for an existing UWP CoreWindow.
    ///
    /// # Safety
    /// `window` must be a live, non-null CoreWindow COM interface pointer on
    /// its owning UI thread. The host must keep the CoreWindow alive during
    /// swap-chain creation. After construction, the renderer owns its D3D11
    /// resources and may be transferred to one render thread; the immediate
    /// context and swap chain must only be used by one thread at a time.
    pub unsafe fn new(window: *mut c_void) -> Result<Self> {
        let window = unsafe { IUnknown::from_raw_borrowed(&window) }
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let (device, context, factory) = Self::create_device()?;
        let desc = DXGI_SWAP_CHAIN_DESC1 {
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            BufferUsage: DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount: 2,
            Scaling: DXGI_SCALING_NONE,
            SwapEffect: DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
            AlphaMode: DXGI_ALPHA_MODE_IGNORE,
            ..Default::default()
        };
        let swap_chain =
            unsafe { factory.CreateSwapChainForCoreWindow(&device, window, &desc, None)? };
        Self::from_swap_chain(device, context, swap_chain)
    }

    /// Creates a XAML composition swap chain for a `SwapChainPanel`.
    pub fn new_for_composition(
        width: u32,
        height: u32,
        scale_x: f32,
        scale_y: f32,
    ) -> Result<Self> {
        if width == 0
            || height == 0
            || !scale_x.is_finite()
            || !scale_y.is_finite()
            || scale_x <= 0.0
            || scale_y <= 0.0
        {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let (device, context, factory) = Self::create_device()?;
        let desc = DXGI_SWAP_CHAIN_DESC1 {
            Width: width,
            Height: height,
            Format: DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            BufferUsage: DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount: 2,
            Scaling: DXGI_SCALING_STRETCH,
            SwapEffect: DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL,
            AlphaMode: DXGI_ALPHA_MODE_IGNORE,
            ..Default::default()
        };
        let swap_chain = unsafe { factory.CreateSwapChainForComposition(&device, &desc, None)? };
        let renderer = Self::from_swap_chain(device, context, swap_chain)?;
        renderer.set_composition_scale(scale_x, scale_y)?;
        Ok(renderer)
    }

    /// Cancels the scale that XAML applies to a composition swap chain.
    ///
    /// SwapChainPanel buffers are sized in physical pixels, while XAML applies
    /// its composition scale when presenting them. Without this inverse
    /// transform, a high-DPI panel magnifies the already pixel-sized image.
    pub fn set_composition_scale(&self, scale_x: f32, scale_y: f32) -> Result<()> {
        if !scale_x.is_finite() || !scale_y.is_finite() || scale_x <= 0.0 || scale_y <= 0.0 {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let swap_chain: IDXGISwapChain2 = self.swap_chain.cast()?;
        let inverse_scale = DXGI_MATRIX_3X2_F {
            _11: 1.0 / scale_x,
            _22: 1.0 / scale_y,
            ..Default::default()
        };
        unsafe { swap_chain.SetMatrixTransform(&inverse_scale)? };
        Ok(())
    }

    fn create_device() -> Result<(ID3D11Device, ID3D11DeviceContext, IDXGIFactory2)> {
        let mut device = None;
        let mut context = None;
        unsafe {
            D3D11CreateDevice(
                None::<&IDXGIAdapter>,
                D3D_DRIVER_TYPE_HARDWARE,
                HMODULE::default(),
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                Some(&[D3D_FEATURE_LEVEL_9_1]),
                D3D11_SDK_VERSION,
                Some(&mut device),
                None,
                Some(&mut context),
            )?;
        }
        let device = device.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let context = context.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let dxgi: IDXGIDevice = device.cast()?;
        let factory: IDXGIFactory2 = unsafe { dxgi.GetAdapter()?.GetParent()? };
        Ok((device, context, factory))
    }

    fn from_swap_chain(
        device: ID3D11Device,
        context: ID3D11DeviceContext,
        swap_chain: IDXGISwapChain1,
    ) -> Result<Self> {
        let (target, depth) = Self::create_targets(&device, &swap_chain)?;
        let pipeline = Pipeline::new(&device)?;
        Ok(Self {
            device,
            context,
            swap_chain,
            target: Some(target),
            depth: Some(depth),
            pipeline,
            depth_convention: DepthConvention::Forward,
        })
    }

    /// Returns the borrowed swap-chain interface for binding to a XAML panel.
    pub fn swap_chain_ptr(&self) -> *mut c_void {
        self.swap_chain.as_raw()
    }

    /// Returns the composition surface's current physical pixel dimensions.
    pub fn swap_chain_size(&self) -> Result<[u32; 2]> {
        let desc = unsafe { self.swap_chain.GetDesc1()? };
        Ok([desc.Width, desc.Height])
    }

    /// Selects the depth convention of subsequently submitted frames.
    pub fn set_depth_convention(&mut self, convention: DepthConvention) {
        self.depth_convention = convention;
    }

    fn create_targets(
        device: &ID3D11Device,
        swap_chain: &IDXGISwapChain1,
    ) -> Result<(ID3D11RenderTargetView, ID3D11DepthStencilView)> {
        unsafe {
            let back_buffer: ID3D11Texture2D = swap_chain.GetBuffer(0)?;
            let mut target = None;
            device.CreateRenderTargetView(&back_buffer, None, Some(&mut target))?;
            let mut back_desc = D3D11_TEXTURE2D_DESC::default();
            back_buffer.GetDesc(&mut back_desc);
            let depth_desc = D3D11_TEXTURE2D_DESC {
                Width: back_desc.Width,
                Height: back_desc.Height,
                MipLevels: 1,
                ArraySize: 1,
                Format: DXGI_FORMAT_D16_UNORM,
                SampleDesc: DXGI_SAMPLE_DESC {
                    Count: 1,
                    Quality: 0,
                },
                Usage: D3D11_USAGE_DEFAULT,
                BindFlags: D3D11_BIND_DEPTH_STENCIL.0 as u32,
                ..Default::default()
            };
            let mut texture = None;
            device.CreateTexture2D(&depth_desc, None, Some(&mut texture))?;
            let texture = texture.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
            let mut depth = None;
            device.CreateDepthStencilView(&texture, None, Some(&mut depth))?;
            Ok((
                target.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                depth.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
            ))
        }
    }

    /// Recreates the swap-chain target using the CoreWindow's current size.
    pub fn resize(&mut self) -> Result<()> {
        self.resize_to(0, 0)
    }

    /// Recreates the swap-chain target at the requested pixel dimensions.
    pub fn resize_to(&mut self, width: u32, height: u32) -> Result<()> {
        unsafe {
            self.context
                .OMSetRenderTargets(None, None::<&ID3D11DepthStencilView>);
            self.target = None;
            self.depth = None;
            self.swap_chain.ResizeBuffers(
                2,
                width,
                height,
                DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_SWAP_CHAIN_FLAG(0),
            )?;
        }
        let (target, depth) = Self::create_targets(&self.device, &self.swap_chain)?;
        self.target = Some(target);
        self.depth = Some(depth);
        Ok(())
    }

    /// Uploads an immutable triangle mesh, checking every 16-bit index.
    pub fn create_mesh(&self, vertices: &[Vertex], indices: &[u16]) -> Result<Mesh> {
        if vertices.is_empty()
            || indices.is_empty()
            || indices.iter().any(|&i| usize::from(i) >= vertices.len())
        {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let index_count =
            u32::try_from(indices.len()).map_err(|_| Error::from_hresult(E_INVALIDARG))?;
        Ok(Mesh {
            vertices: immutable_buffer(
                &self.device,
                bytemuck::cast_slice(vertices),
                D3D11_BIND_VERTEX_BUFFER,
            )?,
            indices: immutable_buffer(
                &self.device,
                bytemuck::cast_slice(indices),
                D3D11_BIND_INDEX_BUFFER,
            )?,
            index_count,
        })
    }

    /// Uploads a tightly packed RGBA8 atlas within the 9_1 2048-pixel limit.
    pub fn create_atlas(&self, width: u32, height: u32, rgba: &[u8]) -> Result<Texture> {
        self.create_atlas_mips(width, height, &[rgba])
    }

    /// Uploads explicitly authored mip levels; each level is tightly packed.
    pub fn create_atlas_mips(&self, width: u32, height: u32, levels: &[&[u8]]) -> Result<Texture> {
        if width == 0
            || height == 0
            || width > 2048
            || height > 2048
            || levels.is_empty()
            || levels.len() > (width.min(height).ilog2() + 1) as usize
        {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let mut data = Vec::with_capacity(levels.len());
        for (level, rgba) in levels.iter().enumerate() {
            let w = width >> level;
            let h = height >> level;
            if (w as usize)
                .checked_mul(h as usize)
                .and_then(|n| n.checked_mul(4))
                != Some(rgba.len())
            {
                return Err(Error::from_hresult(E_INVALIDARG));
            }
            data.push(D3D11_SUBRESOURCE_DATA {
                pSysMem: rgba.as_ptr().cast(),
                SysMemPitch: w * 4,
                ..Default::default()
            });
        }
        let desc = D3D11_TEXTURE2D_DESC {
            Width: width,
            Height: height,
            MipLevels: levels.len() as u32,
            ArraySize: 1,
            Format: DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleDesc: DXGI_SAMPLE_DESC {
                Count: 1,
                Quality: 0,
            },
            Usage: D3D11_USAGE_DEFAULT,
            BindFlags: D3D11_BIND_SHADER_RESOURCE.0 as u32,
            ..Default::default()
        };
        unsafe {
            let mut texture = None;
            self.device
                .CreateTexture2D(&desc, Some(data.as_ptr()), Some(&mut texture))?;
            let texture = texture.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
            let mut view = None;
            self.device
                .CreateShaderResourceView(&texture, None, Some(&mut view))?;
            Ok(Texture {
                view: view.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                texture,
                width,
                height,
                mipmapped: levels.len() > 1,
            })
        }
    }

    /// Replaces a tightly packed atlas rectangle, including font glyph patches.
    pub fn update_atlas(
        &self,
        atlas: &Texture,
        x: u32,
        y: u32,
        width: u32,
        height: u32,
        rgba: &[u8],
    ) -> Result<()> {
        let right = x
            .checked_add(width)
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let bottom = y
            .checked_add(height)
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let expected = (width as usize)
            .checked_mul(height as usize)
            .and_then(|n| n.checked_mul(4));
        if atlas.mipmapped
            || width == 0
            || height == 0
            || right > atlas.width
            || bottom > atlas.height
            || expected != Some(rgba.len())
        {
            return Err(Error::from_hresult(E_INVALIDARG));
        }
        let bounds = D3D11_BOX {
            left: x,
            top: y,
            front: 0,
            right,
            bottom,
            back: 1,
        };
        unsafe {
            self.context.UpdateSubresource(
                &atlas.texture,
                0,
                Some(&bounds),
                rgba.as_ptr().cast(),
                width * 4,
                0,
            );
        }
        Ok(())
    }

    /// Starts a frame using the selected depth convention.
    pub fn begin_frame(&self, color: [f32; 4]) -> Result<()> {
        let target = self
            .target
            .as_ref()
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let depth = self
            .depth
            .as_ref()
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        unsafe {
            let desc = self.swap_chain.GetDesc1()?;
            self.context.ClearRenderTargetView(target, &color);
            let clear_depth = match self.depth_convention {
                DepthConvention::Forward => 1.0,
                DepthConvention::Reverse => 0.0,
            };
            self.context
                .ClearDepthStencilView(depth, D3D11_CLEAR_DEPTH.0 as u32, clear_depth, 0);
            self.context
                .OMSetRenderTargets(Some(&[Some(target.clone())]), depth);
            self.context.RSSetViewports(Some(&[D3D11_VIEWPORT {
                TopLeftX: 0.0,
                TopLeftY: 0.0,
                Width: desc.Width as f32,
                Height: desc.Height as f32,
                MinDepth: 0.0,
                MaxDepth: 1.0,
            }]));
        }
        Ok(())
    }

    /// Clears depth without changing the color target, for the player's hand.
    pub fn clear_depth(&self) -> Result<()> {
        let depth = self
            .depth
            .as_ref()
            .ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
        let clear = match self.depth_convention {
            DepthConvention::Forward => 1.0,
            DepthConvention::Reverse => 0.0,
        };
        unsafe {
            self.context
                .ClearDepthStencilView(depth, D3D11_CLEAR_DEPTH.0 as u32, clear, 0);
        }
        Ok(())
    }

    /// Draws one mesh with a row-major world-to-clip matrix and its atlas.
    pub fn draw(&self, mesh: &Mesh, atlas: &Texture, view_projection: [[f32; 4]; 4]) {
        self.draw_pass(mesh, atlas, view_projection, DrawPass::Cutout);
    }

    /// Draws cutout terrain, depth-tested translucent geometry, or UI.
    pub fn draw_pass(
        &self,
        mesh: &Mesh,
        atlas: &Texture,
        view_projection: [[f32; 4]; 4],
        pass: DrawPass,
    ) {
        let p = &self.pipeline;
        unsafe {
            self.context.UpdateSubresource(
                &p.constants,
                0,
                None,
                view_projection.as_ptr().cast(),
                0,
                0,
            );
            self.context.IASetInputLayout(&p.input_layout);
            self.context
                .IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            let vertex = Some(mesh.vertices.clone());
            let stride = std::mem::size_of::<Vertex>() as u32;
            let offset = 0;
            self.context
                .IASetVertexBuffers(0, 1, Some(&vertex), Some(&stride), Some(&offset));
            self.context
                .IASetIndexBuffer(&mesh.indices, DXGI_FORMAT_R16_UINT, 0);
            self.context.RSSetState(&p.rasterizer);
            let (depth, blend, pixel) = match (pass, self.depth_convention) {
                (DrawPass::Opaque, DepthConvention::Forward) => {
                    (&p.depth_write, None, &p.opaque_shader)
                }
                (DrawPass::Opaque, DepthConvention::Reverse) => {
                    (&p.reverse_write, None, &p.opaque_shader)
                }
                (DrawPass::Entity, DepthConvention::Forward) => {
                    (&p.depth_write, Some(&p.alpha_blend), &p.entity_shader)
                }
                (DrawPass::Entity, DepthConvention::Reverse) => {
                    (&p.reverse_write, Some(&p.alpha_blend), &p.entity_shader)
                }
                (DrawPass::Cutout, DepthConvention::Forward) => {
                    (&p.depth_write, None, &p.pixel_shader)
                }
                (DrawPass::Cutout, DepthConvention::Reverse) => {
                    (&p.reverse_write, None, &p.pixel_shader)
                }
                (DrawPass::Translucent, DepthConvention::Forward) => {
                    (&p.depth_read, Some(&p.alpha_blend), &p.alpha_shader)
                }
                (DrawPass::Translucent, DepthConvention::Reverse) => {
                    (&p.reverse_read, Some(&p.alpha_blend), &p.alpha_shader)
                }
                (DrawPass::Overlay, _) => (&p.depth_off, Some(&p.alpha_blend), &p.alpha_shader),
                (DrawPass::OverlayInvert, _) => {
                    (&p.depth_off, Some(&p.invert_blend), &p.invert_shader)
                }
                (DrawPass::OverlayAdditive, _) => {
                    (&p.depth_off, Some(&p.additive_blend), &p.alpha_shader)
                }
                (DrawPass::OverlayVignette, _) => {
                    (&p.depth_off, Some(&p.vignette_blend), &p.alpha_shader)
                }
            };
            self.context.OMSetDepthStencilState(depth, 0);
            self.context.OMSetBlendState(blend, None, u32::MAX);
            self.context.VSSetShader(&p.vertex_shader, None);
            self.context
                .VSSetConstantBuffers(0, Some(&[Some(p.constants.clone())]));
            self.context.PSSetShader(pixel, None);
            self.context.PSSetSamplers(
                0,
                Some(&[Some(if atlas.mipmapped {
                    p.mip_sampler.clone()
                } else {
                    p.sampler.clone()
                })]),
            );
            self.context
                .PSSetShaderResources(0, Some(&[Some(atlas.view.clone())]));
            self.context.DrawIndexed(mesh.index_count, 0, 0);
        }
    }

    /// Presents the current frame.
    pub fn present(&self) -> Result<()> {
        unsafe { self.swap_chain.Present(1, DXGI_PRESENT(0)).ok() }
    }

    /// Count pixels differing from a black RGB clear before presentation.
    /// This blocking readback is intended only for one-off diagnostics.
    #[cfg(feature = "readback-probe")]
    pub fn probe_non_black_pixels(&self) -> Result<(u32, u32, u64)> {
        self.probe_pixels_different_from([0, 0, 0], 0)
    }

    /// Count backbuffer pixels differing from a known RGB clear by more than
    /// `tolerance` per channel. Call before presenting the frame.
    #[cfg(feature = "readback-probe")]
    pub fn probe_pixels_different_from(
        &self,
        clear: [u8; 3],
        tolerance: u8,
    ) -> Result<(u32, u32, u64)> {
        unsafe {
            let back: ID3D11Texture2D = self.swap_chain.GetBuffer(0)?;
            let mut desc = D3D11_TEXTURE2D_DESC::default();
            back.GetDesc(&mut desc);
            if desc.Width == 0 || desc.Height == 0 {
                return Err(Error::from_hresult(E_INVALIDARG));
            }
            let (width, height) = (desc.Width, desc.Height);
            desc.Usage = D3D11_USAGE_STAGING;
            desc.BindFlags = 0;
            desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ.0 as u32;
            desc.MiscFlags = 0;
            let mut staging = None;
            self.device
                .CreateTexture2D(&desc, None, Some(&mut staging))?;
            let staging = staging.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
            self.context.CopyResource(&staging, &back);
            let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
            self.context
                .Map(&staging, 0, D3D11_MAP_READ, 0, Some(&mut mapped))?;
            if mapped.pData.is_null() || mapped.RowPitch < width * 4 {
                self.context.Unmap(&staging, 0);
                return Err(Error::from_hresult(E_INVALIDARG));
            }
            let mut non_black = 0u64;
            for y in 0..height as usize {
                let row = (mapped.pData as *const u8).add(y * mapped.RowPitch as usize);
                for pixel in std::slice::from_raw_parts(row, width as usize * 4).chunks_exact(4) {
                    // The swap chain is BGRA while `clear` is RGB.
                    if pixel[2].abs_diff(clear[0]) > tolerance
                        || pixel[1].abs_diff(clear[1]) > tolerance
                        || pixel[0].abs_diff(clear[2]) > tolerance
                    {
                        non_black += 1;
                    }
                }
            }
            self.context.Unmap(&staging, 0);
            Ok((width, height, non_black))
        }
    }

    /// Samples four points from the current back buffer before presentation.
    /// Used only by the isolated renderer regression probe.
    #[cfg(feature = "readback-probe")]
    pub fn probe_pixels(&self) -> Result<[[u8; 4]; 4]> {
        unsafe {
            let back: ID3D11Texture2D = self.swap_chain.GetBuffer(0)?;
            let mut desc = D3D11_TEXTURE2D_DESC::default();
            back.GetDesc(&mut desc);
            if desc.Width < 4 || desc.Height < 4 {
                return Err(Error::from_hresult(E_INVALIDARG));
            }
            let (width, height) = (desc.Width, desc.Height);
            desc.Usage = D3D11_USAGE_STAGING;
            desc.BindFlags = 0;
            desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ.0 as u32;
            desc.MiscFlags = 0;
            let mut staging = None;
            self.device
                .CreateTexture2D(&desc, None, Some(&mut staging))?;
            let staging = staging.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?;
            self.context.CopyResource(&staging, &back);
            let mut mapped = D3D11_MAPPED_SUBRESOURCE::default();
            self.context
                .Map(&staging, 0, D3D11_MAP_READ, 0, Some(&mut mapped))?;
            if mapped.pData.is_null() || mapped.RowPitch < width * 4 {
                self.context.Unmap(&staging, 0);
                return Err(Error::from_hresult(E_INVALIDARG));
            }
            let positions = [
                (width / 4, height / 4),
                (width * 3 / 4, height / 4),
                (width / 4, height * 3 / 4),
                (width * 3 / 4, height * 3 / 4),
            ];
            let mut pixels = [[0; 4]; 4];
            for (pixel, (x, y)) in pixels.iter_mut().zip(positions) {
                let offset = y as usize * mapped.RowPitch as usize + x as usize * 4;
                let source = (mapped.pData as *const u8).add(offset);
                std::ptr::copy_nonoverlapping(source, pixel.as_mut_ptr(), 4);
                pixel.swap(0, 2); // swap-chain pixels are BGRA; report RGBA.
            }
            self.context.Unmap(&staging, 0);
            Ok(pixels)
        }
    }
}

fn immutable_buffer(
    device: &ID3D11Device,
    bytes: &[u8],
    bind: D3D11_BIND_FLAG,
) -> Result<ID3D11Buffer> {
    let size = u32::try_from(bytes.len()).map_err(|_| Error::from_hresult(E_INVALIDARG))?;
    let desc = D3D11_BUFFER_DESC {
        ByteWidth: size,
        Usage: D3D11_USAGE_IMMUTABLE,
        BindFlags: bind.0 as u32,
        ..Default::default()
    };
    let data = D3D11_SUBRESOURCE_DATA {
        pSysMem: bytes.as_ptr().cast(),
        ..Default::default()
    };
    let mut buffer = None;
    unsafe {
        device.CreateBuffer(&desc, Some(&data), Some(&mut buffer))?;
    }
    buffer.ok_or_else(|| Error::from_hresult(E_INVALIDARG))
}

impl Pipeline {
    fn new(device: &ID3D11Device) -> Result<Self> {
        unsafe {
            let mut vertex_shader = None;
            device.CreateVertexShader(VS, None::<&ID3D11ClassLinkage>, Some(&mut vertex_shader))?;
            let mut pixel_shader = None;
            device.CreatePixelShader(PS, None::<&ID3D11ClassLinkage>, Some(&mut pixel_shader))?;
            let mut opaque_shader = None;
            device.CreatePixelShader(
                PS_OPAQUE,
                None::<&ID3D11ClassLinkage>,
                Some(&mut opaque_shader),
            )?;
            let mut entity_shader = None;
            device.CreatePixelShader(
                PS_ENTITY,
                None::<&ID3D11ClassLinkage>,
                Some(&mut entity_shader),
            )?;
            let mut alpha_shader = None;
            device.CreatePixelShader(
                PS_ALPHA,
                None::<&ID3D11ClassLinkage>,
                Some(&mut alpha_shader),
            )?;
            let mut invert_shader = None;
            device.CreatePixelShader(
                PS_INVERT,
                None::<&ID3D11ClassLinkage>,
                Some(&mut invert_shader),
            )?;
            let elements = [
                D3D11_INPUT_ELEMENT_DESC {
                    SemanticName: s!("POSITION"),
                    Format: DXGI_FORMAT_R32G32B32_FLOAT,
                    AlignedByteOffset: 0,
                    InputSlotClass: D3D11_INPUT_PER_VERTEX_DATA,
                    ..Default::default()
                },
                D3D11_INPUT_ELEMENT_DESC {
                    SemanticName: s!("TEXCOORD"),
                    Format: DXGI_FORMAT_R32G32_FLOAT,
                    AlignedByteOffset: 12,
                    InputSlotClass: D3D11_INPUT_PER_VERTEX_DATA,
                    ..Default::default()
                },
                D3D11_INPUT_ELEMENT_DESC {
                    SemanticName: s!("COLOR"),
                    Format: DXGI_FORMAT_R32G32B32A32_FLOAT,
                    AlignedByteOffset: 20,
                    InputSlotClass: D3D11_INPUT_PER_VERTEX_DATA,
                    ..Default::default()
                },
            ];
            let mut input_layout = None;
            device.CreateInputLayout(&elements, VS, Some(&mut input_layout))?;
            let desc = D3D11_BUFFER_DESC {
                ByteWidth: 64,
                Usage: D3D11_USAGE_DEFAULT,
                BindFlags: D3D11_BIND_CONSTANT_BUFFER.0 as u32,
                ..Default::default()
            };
            let mut constants = None;
            device.CreateBuffer(&desc, None, Some(&mut constants))?;
            let desc = D3D11_SAMPLER_DESC {
                Filter: D3D11_FILTER_MIN_MAG_MIP_POINT,
                AddressU: D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressV: D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressW: D3D11_TEXTURE_ADDRESS_CLAMP,
                MaxLOD: f32::MAX,
                ..Default::default()
            };
            let mut sampler = None;
            device.CreateSamplerState(&desc, Some(&mut sampler))?;
            let mip_desc = D3D11_SAMPLER_DESC {
                Filter: D3D11_FILTER_MIN_MAG_POINT_MIP_LINEAR,
                ..desc
            };
            let mut mip_sampler = None;
            device.CreateSamplerState(&mip_desc, Some(&mut mip_sampler))?;
            let desc = D3D11_RASTERIZER_DESC {
                FillMode: D3D11_FILL_SOLID,
                CullMode: D3D11_CULL_NONE,
                DepthClipEnable: true.into(),
                ..Default::default()
            };
            let mut rasterizer = None;
            device.CreateRasterizerState(&desc, Some(&mut rasterizer))?;
            let depth_write = create_depth_state(device, true, true, D3D11_COMPARISON_LESS_EQUAL)?;
            let depth_read = create_depth_state(device, true, false, D3D11_COMPARISON_LESS_EQUAL)?;
            let reverse_write =
                create_depth_state(device, true, true, D3D11_COMPARISON_GREATER_EQUAL)?;
            let reverse_read =
                create_depth_state(device, true, false, D3D11_COMPARISON_GREATER_EQUAL)?;
            let depth_off = create_depth_state(device, false, false, D3D11_COMPARISON_ALWAYS)?;
            let mut desc = D3D11_BLEND_DESC::default();
            desc.RenderTarget[0] = D3D11_RENDER_TARGET_BLEND_DESC {
                BlendEnable: true.into(),
                SrcBlend: D3D11_BLEND_SRC_ALPHA,
                DestBlend: D3D11_BLEND_INV_SRC_ALPHA,
                BlendOp: D3D11_BLEND_OP_ADD,
                SrcBlendAlpha: D3D11_BLEND_ONE,
                DestBlendAlpha: D3D11_BLEND_INV_SRC_ALPHA,
                BlendOpAlpha: D3D11_BLEND_OP_ADD,
                RenderTargetWriteMask: D3D11_COLOR_WRITE_ENABLE_ALL.0 as u8,
            };
            let mut alpha_blend = None;
            device.CreateBlendState(&desc, Some(&mut alpha_blend))?;
            desc.RenderTarget[0].SrcBlend = D3D11_BLEND_INV_DEST_COLOR;
            desc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_COLOR;
            desc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
            desc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
            let mut invert_blend = None;
            device.CreateBlendState(&desc, Some(&mut invert_blend))?;
            desc.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
            desc.RenderTarget[0].DestBlend = D3D11_BLEND_ONE;
            desc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
            desc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
            let mut additive_blend = None;
            device.CreateBlendState(&desc, Some(&mut additive_blend))?;
            desc.RenderTarget[0].SrcBlend = D3D11_BLEND_ZERO;
            desc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_COLOR;
            desc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ZERO;
            desc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
            let mut vignette_blend = None;
            device.CreateBlendState(&desc, Some(&mut vignette_blend))?;
            Ok(Self {
                vertex_shader: vertex_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                pixel_shader: pixel_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                opaque_shader: opaque_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                entity_shader: entity_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                alpha_shader: alpha_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                invert_shader: invert_shader.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                input_layout: input_layout.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                constants: constants.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                sampler: sampler.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                mip_sampler: mip_sampler.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                rasterizer: rasterizer.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                depth_write,
                depth_read,
                reverse_write,
                reverse_read,
                depth_off,
                alpha_blend: alpha_blend.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                invert_blend: invert_blend.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                additive_blend: additive_blend.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
                vignette_blend: vignette_blend.ok_or_else(|| Error::from_hresult(E_INVALIDARG))?,
            })
        }
    }
}

fn create_depth_state(
    device: &ID3D11Device,
    enabled: bool,
    write: bool,
    comparison: D3D11_COMPARISON_FUNC,
) -> Result<ID3D11DepthStencilState> {
    let stencil = D3D11_DEPTH_STENCILOP_DESC {
        StencilFailOp: D3D11_STENCIL_OP_KEEP,
        StencilDepthFailOp: D3D11_STENCIL_OP_KEEP,
        StencilPassOp: D3D11_STENCIL_OP_KEEP,
        StencilFunc: D3D11_COMPARISON_ALWAYS,
    };
    let desc = D3D11_DEPTH_STENCIL_DESC {
        DepthEnable: enabled.into(),
        DepthWriteMask: if write {
            D3D11_DEPTH_WRITE_MASK_ALL
        } else {
            D3D11_DEPTH_WRITE_MASK_ZERO
        },
        DepthFunc: comparison,
        StencilEnable: false.into(),
        StencilReadMask: 0xff,
        StencilWriteMask: 0xff,
        FrontFace: stencil,
        BackFace: stencil,
    };
    let mut state = None;
    unsafe {
        device.CreateDepthStencilState(&desc, Some(&mut state))?;
    }
    state.ok_or_else(|| Error::from_hresult(E_INVALIDARG))
}
