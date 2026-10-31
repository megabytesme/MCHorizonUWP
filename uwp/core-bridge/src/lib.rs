//! Minimal C ABI for checking that the UWP host can link Horizon Rust code.
//! This bridge is a build and integration probe, not the game client.

#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_protocol_version() -> i32 {
    horizon_core::version::PROTOCOL_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_data_version() -> i32 {
    horizon_core::version::DATA_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn horizon_uwp_legacy_random_first(seed: i64) -> i32 {
    use horizon_core::random::{LegacyRandom, RandomSource};
    LegacyRandom::new(seed).next_int()
}
