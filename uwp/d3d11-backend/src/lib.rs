//! Native UWP Direct3D 11 presentation for the feature-level 9_1 path.
//!
//! This module is an initial backend building block; the Horizon renderer
//! still needs to pass it terrain, sprites and UI before it can run the game.

#[cfg(windows)]
mod device;
#[cfg(windows)]
mod thumbnail;

#[cfg(windows)]
pub use device::{DepthConvention, DrawPass, DynamicMesh, Mesh, Renderer, Texture, Vertex};
