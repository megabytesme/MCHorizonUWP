/// Downsample the centred square of a mapped BGRA backbuffer to opaque RGBA.
pub(crate) fn square_rgba(
    bytes: &[u8],
    width: usize,
    height: usize,
    stride: usize,
    size: usize,
) -> Option<Vec<u8>> {
    if width == 0
        || height == 0
        || size == 0
        || stride < width.checked_mul(4)?
        || bytes.len() < stride.checked_mul(height)?
    {
        return None;
    }
    let side = width.min(height);
    let x0 = (width - side) / 2;
    let y0 = (height - side) / 2;
    let mut output = vec![0; size.checked_mul(size)?.checked_mul(4)?];
    for y in 0..size {
        for x in 0..size {
            let source = (y0 + y * side / size) * stride + (x0 + x * side / size) * 4;
            let dest = (y * size + x) * 4;
            output[dest..dest + 4].copy_from_slice(&[
                bytes[source + 2],
                bytes[source + 1],
                bytes[source],
                255,
            ]);
        }
    }
    Some(output)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn crops_the_middle_and_swaps_bgra() {
        let pixels = [10, 20, 30, 1, 11, 21, 31, 2, 12, 22, 32, 3, 13, 23, 33, 4];
        assert_eq!(
            square_rgba(&pixels, 4, 1, 16, 1),
            Some(vec![31, 21, 11, 255])
        );
    }
    #[test]
    fn handles_stride_and_rejects_invalid_sizes() {
        let pixels = [1, 2, 3, 4, 0, 0, 0, 0, 5, 6, 7, 8, 0, 0, 0, 0];
        assert_eq!(square_rgba(&pixels, 1, 2, 8, 1), Some(vec![3, 2, 1, 255]));
        assert!(square_rgba(&pixels, 2, 2, 4, 1).is_none());
        assert!(square_rgba(&pixels, 1, 2, 8, 0).is_none());
    }
}
