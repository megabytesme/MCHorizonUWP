//! Per-frame uploads through one reused staging belt. `Queue::write_buffer`
//! and `Queue::write_texture` create a fresh staging buffer for every call (on
//! Metal a kernel call each), which made the animated sprites' few hundred
//! texture writes a spike on every tick that changes a frame. The belt keeps a
//! few mapped buffers and records the copies into the frame's encoder instead.
use std::num::NonZeroU64;
use wgpu::util::DeviceExt;

// DeviceExt uploads each array layer and mip separately. D3D12 rounds every
// staging allocation, so thousands of sprites can exhaust a 32-bit process's
// address space before its first frame. Group adjacent layers of each mip.
pub(super) fn array_texture_with_data(
    device: &wgpu::Device,
    queue: &wgpu::Queue,
    desc: &wgpu::TextureDescriptor<'_>,
    data: &[u8],
    batch_layers: bool,
) -> wgpu::Texture {
    if !batch_layers {
        return device.create_texture_with_data(
            queue,
            desc,
            wgpu::util::TextureDataOrder::LayerMajor,
            data,
        );
    }
    debug_assert_eq!(desc.dimension, wgpu::TextureDimension::D2);
    debug_assert_eq!(desc.format, wgpu::TextureFormat::Rgba8Unorm);
    let mut copy_desc = desc.clone();
    copy_desc.usage |= wgpu::TextureUsages::COPY_DST;
    let texture = device.create_texture(&copy_desc);
    let layers = desc.size.depth_or_array_layers as usize;
    let sizes: Vec<usize> = (0..desc.mip_level_count)
        .map(|mip| {
            (desc.size.width >> mip).max(1) as usize * (desc.size.height >> mip).max(1) as usize * 4
        })
        .collect();
    let layer_stride: usize = sizes.iter().sum();
    assert_eq!(data.len(), layer_stride * layers);
    let mut mip_offset = 0;
    for (mip, &mip_bytes) in sizes.iter().enumerate() {
        let width = (desc.size.width >> mip).max(1);
        let height = (desc.size.height >> mip).max(1);
        for first in (0..layers).step_by(64) {
            let count = (layers - first).min(64);
            let bytes = gather_mip_layers(data, layer_stride, mip_offset, mip_bytes, first, count);
            queue.write_texture(
                wgpu::TexelCopyTextureInfo {
                    texture: &texture,
                    mip_level: mip as u32,
                    origin: wgpu::Origin3d {
                        x: 0,
                        y: 0,
                        z: first as u32,
                    },
                    aspect: wgpu::TextureAspect::All,
                },
                &bytes,
                wgpu::TexelCopyBufferLayout {
                    offset: 0,
                    bytes_per_row: Some(width * 4),
                    rows_per_image: Some(height),
                },
                wgpu::Extent3d {
                    width,
                    height,
                    depth_or_array_layers: count as u32,
                },
            );
        }
        mip_offset += mip_bytes;
    }
    texture
}

fn gather_mip_layers(
    data: &[u8],
    stride: usize,
    offset: usize,
    len: usize,
    first: usize,
    count: usize,
) -> Vec<u8> {
    let mut result = Vec::with_capacity(len * count);
    for layer in first..first + count {
        let start = layer * stride + offset;
        result.extend_from_slice(&data[start..start + len]);
    }
    result
}

#[cfg(test)]
mod array_upload_tests {
    use super::gather_mip_layers;

    #[test]
    fn extracts_each_mip_from_only_the_requested_layers() {
        // Each layer has a 4-byte first mip followed by a 1-byte second mip.
        let data = [0, 1, 2, 3, 4, 10, 11, 12, 13, 14, 20, 21, 22, 23, 24];
        assert_eq!(
            gather_mip_layers(&data, 5, 0, 4, 1, 2),
            [10, 11, 12, 13, 20, 21, 22, 23]
        );
        assert_eq!(gather_mip_layers(&data, 5, 4, 1, 0, 3), [4, 14, 24]);
    }
}

/// Large enough for a tick of animated sprites at 16x16 with every mip level.
const CHUNK: u64 = 1 << 20;

pub struct Uploads {
    belt: wgpu::util::StagingBelt,
    chunk: u64,
}

impl Uploads {
    pub fn new(device: &wgpu::Device) -> Self {
        Self::with_chunk(device, CHUNK)
    }
    /// A belt whose staging buffers hold `chunk` bytes (a larger write gets
    /// a buffer of its own, rounded up to a power of two).
    pub fn with_chunk(device: &wgpu::Device, chunk: u64) -> Self {
        Self {
            belt: wgpu::util::StagingBelt::new(device.clone(), chunk),
            chunk,
        }
    }
    /// A mapped staging slice of at least `size` bytes. The belt gives a
    /// write larger than its chunk a buffer of exactly that size, keeps it for
    /// good and only reuses it for writes that fit: an upload that grows a
    /// little every frame (the entity batch while mobs appear) took a new
    /// buffer each frame until the game ran out of memory (#2826). Rounded up
    /// to a power of two, the large buffers it keeps are reused, one per
    /// doubling.
    fn allocate(&mut self, size: NonZeroU64, alignment: NonZeroU64) -> wgpu::BufferSlice<'_> {
        let size = if size.get() > self.chunk {
            size.checked_next_power_of_two().unwrap_or(size)
        } else {
            size
        };
        self.belt.allocate(size, alignment)
    }
    /// Copy tightly packed RGBA8 `pixels` (`width` by `height`) into one mip
    /// level of one array layer, padding rows to the copy alignment.
    pub fn texture(
        &mut self,
        encoder: &mut wgpu::CommandEncoder,
        destination: wgpu::TexelCopyTextureInfo<'_>,
        width: u32,
        height: u32,
        pixels: &[u8],
    ) {
        let row = width as usize * 4;
        debug_assert_eq!(pixels.len(), row * height as usize);
        let padded = row.next_multiple_of(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT as usize);
        let Some(size) = NonZeroU64::new((padded * height as usize) as u64) else {
            return;
        };
        let alignment = NonZeroU64::new(u64::from(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT))
            .expect("non-zero alignment");
        let slice = self.allocate(size, alignment);
        {
            let mut view = slice
                .get_mapped_range_mut()
                .expect("staging belt slices are mapped");
            for (i, src) in pixels.chunks(row).enumerate() {
                view.slice(i * padded..i * padded + row)
                    .copy_from_slice(src);
            }
        }
        encoder.copy_buffer_to_texture(
            wgpu::TexelCopyBufferInfo {
                buffer: slice.buffer(),
                layout: wgpu::TexelCopyBufferLayout {
                    offset: slice.offset(),
                    bytes_per_row: Some(padded as u32),
                    rows_per_image: Some(height),
                },
            },
            destination,
            wgpu::Extent3d {
                width,
                height,
                depth_or_array_layers: 1,
            },
        );
    }
    /// Copy `bytes` into `target` at `offset`, as `Queue::write_buffer`
    /// would, in the encoder's order.
    pub fn buffer(
        &mut self,
        encoder: &mut wgpu::CommandEncoder,
        target: &wgpu::Buffer,
        offset: u64,
        bytes: &[u8],
    ) {
        let Some(size) = NonZeroU64::new(bytes.len() as u64) else {
            return;
        };
        // The belt's own `write_buffer` checks, kept now that a large write's
        // slice is rounded up past them.
        assert!(
            offset.is_multiple_of(wgpu::COPY_BUFFER_ALIGNMENT)
                && size.get().is_multiple_of(wgpu::COPY_BUFFER_ALIGNMENT),
            "buffer upload at {offset} of {size} bytes must be 4-byte aligned"
        );
        let alignment = NonZeroU64::new(wgpu::COPY_BUFFER_ALIGNMENT).expect("non-zero alignment");
        let slice = self.allocate(size, alignment).slice(..size.get());
        encoder.copy_buffer_to_buffer(slice.buffer(), slice.offset(), target, offset, size.get());
        slice
            .get_mapped_range_mut()
            .expect("staging belt slices are mapped")
            .slice(..)
            .copy_from_slice(bytes);
    }
    /// Close this frame's staging buffers: they come back for reuse once the
    /// encoder's submission finishes on the GPU.
    pub fn finish(&mut self, encoder: &wgpu::CommandEncoder) {
        self.belt.finish_and_recall_on_submit(encoder);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn device() -> Option<(wgpu::Device, wgpu::Queue)> {
        let instance = crate::gfx::test_gpu::instance();
        let adapter = pollster::block_on(instance.request_adapter(&Default::default())).ok()?;
        pollster::block_on(adapter.request_device(&Default::default())).ok()
    }

    /// Every mip level of one layer, read back as tightly packed RGBA8.
    fn read_back(device: &wgpu::Device, queue: &wgpu::Queue, texture: &wgpu::Texture) -> Vec<u8> {
        read_back_layer(device, queue, texture, 1)
    }

    fn read_back_layer(
        device: &wgpu::Device,
        queue: &wgpu::Queue,
        texture: &wgpu::Texture,
        layer: u32,
    ) -> Vec<u8> {
        let mut out = Vec::new();
        for level in 0..texture.mip_level_count() {
            let edge = texture.width() >> level;
            let padded = (edge * 4).next_multiple_of(wgpu::COPY_BYTES_PER_ROW_ALIGNMENT);
            let buffer = device.create_buffer(&wgpu::BufferDescriptor {
                label: None,
                size: u64::from(padded * edge),
                usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            });
            let mut encoder = device.create_command_encoder(&Default::default());
            encoder.copy_texture_to_buffer(
                wgpu::TexelCopyTextureInfo {
                    texture,
                    mip_level: level,
                    origin: wgpu::Origin3d {
                        x: 0,
                        y: 0,
                        z: layer,
                    },
                    aspect: wgpu::TextureAspect::All,
                },
                wgpu::TexelCopyBufferInfo {
                    buffer: &buffer,
                    layout: wgpu::TexelCopyBufferLayout {
                        offset: 0,
                        bytes_per_row: Some(padded),
                        rows_per_image: Some(edge),
                    },
                },
                wgpu::Extent3d {
                    width: edge,
                    height: edge,
                    depth_or_array_layers: 1,
                },
            );
            queue.submit([encoder.finish()]);
            buffer
                .slice(..)
                .map_async(wgpu::MapMode::Read, |r| r.unwrap());
            device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
            let data = buffer.slice(..).get_mapped_range().unwrap();
            for row in data.chunks(padded as usize) {
                out.extend_from_slice(&row[..edge as usize * 4]);
            }
        }
        out
    }

    #[test]
    fn batched_array_upload_matches_layer_major_upload() {
        let Some((device, queue)) = device() else {
            return;
        };
        let desc = wgpu::TextureDescriptor {
            label: Some("array upload test"),
            size: wgpu::Extent3d {
                width: 4,
                height: 4,
                depth_or_array_layers: 70,
            },
            mip_level_count: 3,
            sample_count: 1,
            dimension: wgpu::TextureDimension::D2,
            format: wgpu::TextureFormat::Rgba8Unorm,
            usage: wgpu::TextureUsages::TEXTURE_BINDING | wgpu::TextureUsages::COPY_SRC,
            view_formats: &[],
        };
        let data: Vec<u8> = (0..70 * (4 * 4 * 4 + 2 * 2 * 4 + 4))
            .map(|i| ((i * 37 + i / 11) % 251) as u8)
            .collect();
        let expected = array_texture_with_data(&device, &queue, &desc, &data, false);
        let actual = array_texture_with_data(&device, &queue, &desc, &data, true);
        for layer in [0, 1, 63, 64, 69] {
            assert_eq!(
                read_back_layer(&device, &queue, &actual, layer),
                read_back_layer(&device, &queue, &expected, layer),
                "layer {layer}"
            );
        }
    }

    #[test]
    fn belt_uploads_match_queue_writes_on_every_mip_level() {
        let Some((device, queue)) = device() else {
            eprintln!("no GPU adapter: skipped");
            return;
        };
        let texture = || {
            device.create_texture(&wgpu::TextureDescriptor {
                label: None,
                size: wgpu::Extent3d {
                    width: 16,
                    height: 16,
                    depth_or_array_layers: 3,
                },
                mip_level_count: 5,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: wgpu::TextureFormat::Rgba8Unorm,
                usage: wgpu::TextureUsages::COPY_DST | wgpu::TextureUsages::COPY_SRC,
                view_formats: &[],
            })
        };
        let (expected, actual) = (texture(), texture());
        let mut uploads = Uploads::new(&device);
        // Two frames, so the second reuses the belt's recalled buffers.
        for frame in 0..2u8 {
            let mut encoder = device.create_command_encoder(&Default::default());
            for level in 0..5 {
                let edge = 16 >> level;
                let pixels: Vec<u8> = (0..edge * edge * 4)
                    .map(|i| {
                        (i as u8)
                            .wrapping_mul(31)
                            .wrapping_add(level as u8 * 7 + frame)
                    })
                    .collect();
                let at = |texture| wgpu::TexelCopyTextureInfo {
                    texture,
                    mip_level: level,
                    origin: wgpu::Origin3d { x: 0, y: 0, z: 1 },
                    aspect: wgpu::TextureAspect::All,
                };
                queue.write_texture(
                    at(&expected),
                    &pixels,
                    wgpu::TexelCopyBufferLayout {
                        offset: 0,
                        bytes_per_row: Some(edge * 4),
                        rows_per_image: Some(edge),
                    },
                    wgpu::Extent3d {
                        width: edge,
                        height: edge,
                        depth_or_array_layers: 1,
                    },
                );
                uploads.texture(&mut encoder, at(&actual), edge, edge, &pixels);
            }
            uploads.finish(&encoder);
            queue.submit([encoder.finish()]);
            device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
            assert_eq!(
                read_back(&device, &queue, &actual),
                read_back(&device, &queue, &expected),
                "frame {frame}"
            );
        }
    }

    /// Staging buffers the belt holds, once every submitted frame is done
    /// with them. The belt only reports its buffers through `Debug`.
    fn kept_buffers(device: &wgpu::Device, uploads: &mut Uploads) -> usize {
        device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
        // Finishing an empty frame takes back every recalled buffer.
        uploads.finish(&device.create_command_encoder(&Default::default()));
        let belt = format!("{:?}", uploads.belt);
        let count = |field: &str| -> usize {
            let at = belt.find(&format!("{field}: ")).expect(&belt) + field.len() + 2;
            let digits = belt[at..].split(|c: char| !c.is_ascii_digit()).next();
            digits.and_then(|d| d.parse().ok()).expect(&belt)
        };
        count("active_chunks") + count("closed_chunks") + count("free_chunks")
    }

    /// #2826: a write larger than the chunk that grows a little every frame
    /// (the entity batch while mobs appear) got a new exact-size buffer each
    /// frame, all of them kept, until the game ran out of memory.
    #[test]
    fn growing_large_writes_reuse_the_belt_buffers() {
        let Some((device, queue)) = device() else {
            eprintln!("no GPU adapter: skipped");
            return;
        };
        const CHUNK: u64 = 1 << 16;
        const TARGET: usize = 4 * CHUNK as usize;
        let target = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: TARGET as u64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC,
            mapped_at_creation: false,
        });
        let readback = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: TARGET as u64,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut uploads = Uploads::with_chunk(&device, CHUNK);
        let mut expected = vec![0u8; TARGET];
        let frames = 64;
        for frame in 0..frames {
            let mut encoder = device.create_command_encoder(&Default::default());
            // One small write, then the large one, 1.5 to 3.5 chunks long.
            uploads.buffer(&mut encoder, &target, TARGET as u64 - 4, &[9; 4]);
            expected[TARGET - 4..].fill(9);
            let len = (3 * CHUNK as usize / 2 + frame * 2048).next_multiple_of(4);
            let bytes: Vec<u8> = (0..len).map(|i| (i + frame) as u8).collect();
            uploads.buffer(&mut encoder, &target, 0, &bytes);
            expected[..len].copy_from_slice(&bytes);
            encoder.copy_buffer_to_buffer(&target, 0, &readback, 0, TARGET as u64);
            uploads.finish(&encoder);
            queue.submit([encoder.finish()]);
            readback
                .slice(..)
                .map_async(wgpu::MapMode::Read, |r| r.unwrap());
            device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
            // Only the written bytes land: the rest of the target is untouched.
            assert!(
                *readback.slice(..).get_mapped_range().unwrap() == expected[..],
                "frame {frame}"
            );
            readback.unmap();
        }
        // A chunk for the small writes and a buffer per doubling of the large
        // one (2 and 4 chunks), maybe one more where a small write took the
        // start of a buffer the large one needed whole: not one per frame.
        let kept = kept_buffers(&device, &mut uploads);
        assert!(
            kept <= 4,
            "the belt kept {kept} buffers over {frames} frames"
        );
    }

    #[test]
    fn belt_buffer_writes_land_in_order_at_their_offsets() {
        let Some((device, queue)) = device() else {
            eprintln!("no GPU adapter: skipped");
            return;
        };
        let target = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: 64,
            usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC,
            mapped_at_creation: false,
        });
        let readback = device.create_buffer(&wgpu::BufferDescriptor {
            label: None,
            size: 64,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
            mapped_at_creation: false,
        });
        let mut uploads = Uploads::new(&device);
        let mut expected = [0u8; 64];
        for frame in 0..3u8 {
            let mut encoder = device.create_command_encoder(&Default::default());
            // Overlapping writes: the later one wins, as with the queue.
            for (offset, len, value) in [(0, 32, frame + 1), (16, 32, frame + 10), (60, 4, 7)] {
                let bytes = vec![value; len];
                uploads.buffer(&mut encoder, &target, offset as u64, &bytes);
                expected[offset..offset + len].copy_from_slice(&bytes);
            }
            uploads.buffer(&mut encoder, &target, 0, &[]);
            encoder.copy_buffer_to_buffer(&target, 0, &readback, 0, 64);
            uploads.finish(&encoder);
            queue.submit([encoder.finish()]);
            readback
                .slice(..)
                .map_async(wgpu::MapMode::Read, |r| r.unwrap());
            device.poll(wgpu::PollType::wait_indefinitely()).unwrap();
            assert_eq!(
                *readback.slice(..).get_mapped_range().unwrap(),
                expected[..]
            );
            readback.unmap();
        }
    }
}
