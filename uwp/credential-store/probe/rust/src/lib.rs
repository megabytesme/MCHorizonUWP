use horizon_uwp_credential_store as vault;
#[unsafe(no_mangle)]
pub extern "C" fn horizon_vault_probe() -> i32 {
    let key = format!("synthetic-rust-{}", std::process::id());
    if !matches!(vault::get(&key), Ok(None)) {
        return 1;
    }
    let original = "X".repeat(8192);
    if vault::set(&key, &original).is_err() {
        return 2;
    }
    let check = matches!(vault::get(&key),Ok(Some(s)) if s.expose()==original && format!("{s:?}")=="Secret(<hidden>)");
    let replaced = vault::set(&key, "replacement").is_ok();
    let check2 = matches!(vault::get(&key),Ok(Some(s)) if s.expose()=="replacement");
    let removed = matches!(vault::remove(&key), Ok(true));
    let missing = matches!(vault::get(&key), Ok(None));
    if check && replaced && check2 && removed && missing {
        0
    } else {
        3
    }
}
