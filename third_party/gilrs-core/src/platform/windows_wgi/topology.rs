/// Indices of disappeared and newly observed controller identities.
pub(crate) fn changes<T: Eq>(before: &[T], after: &[T]) -> (Vec<usize>, Vec<usize>) {
    let removed = before
        .iter()
        .enumerate()
        .filter_map(|(i, v)| (!after.contains(v)).then_some(i))
        .collect();
    let added = after
        .iter()
        .enumerate()
        .filter_map(|(i, v)| (!before.contains(v)).then_some(i))
        .collect();
    (removed, added)
}
#[cfg(test)]
mod tests {
    use super::changes;
    #[test]
    fn reconciles_initial_appearance_unplug_and_replug() {
        assert_eq!(changes::<u8>(&[], &[7]), (vec![], vec![0]));
        assert_eq!(changes(&[7], &[7]), (vec![], vec![]));
        assert_eq!(changes(&[7], &[]), (vec![0], vec![]));
        assert_eq!(changes(&[], &[7]), (vec![], vec![0]));
        assert_eq!(changes(&[1, 2], &[2, 3]), (vec![0], vec![1]));
    }
}

