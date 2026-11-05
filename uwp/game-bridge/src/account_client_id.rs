/// The Horizon launcher application ID is public. An explicitly empty build
/// setting disables sign-in; an absent setting uses Horizon's default.
pub fn client_id(configured: Option<&str>) -> Option<&str> {
    let id = configured
        .unwrap_or(include_str!("../../shared/horizon-client-id.txt"))
        .trim();
    (!id.is_empty()).then_some(id)
}

#[cfg(test)]
mod tests {
    use super::client_id;

    #[test]
    fn uses_default_or_explicit_setting() {
        assert!(client_id(None).is_some_and(|id| id.len() == 36));
        assert_eq!(client_id(Some("  test-id  ")), Some("test-id"));
        assert_eq!(client_id(Some("  ")), None);
    }
}
