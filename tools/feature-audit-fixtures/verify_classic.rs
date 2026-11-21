use std::path::PathBuf;

use horizon_shaders::{GameInputs, Group, Macros, Pack, load_post, read_properties};

fn main() {
    let root = PathBuf::from(std::env::args_os().nth(1).expect("pass the classic fixture folder"));
    let pack = Pack::open(&root).expect("open classic fixture");
    let mut macros = Macros::default();
    let (properties, notes) = read_properties(&pack, &mut macros);
    assert!(notes.is_empty(), "unexpected property notes: {notes:?}");
    let post = load_post(&pack, "", &properties, &macros, &GameInputs::default());
    assert!(post.notes.is_empty(), "unexpected translation notes: {:?}", post.notes);
    assert!(post.terrain.is_none(), "post-only fixture declared terrain");
    assert_eq!(post.programs.len(), 1, "expected one classic post program");
    let program = &post.programs[0];
    assert_eq!(program.name, "composite");
    assert_eq!(program.group, Group::Composite);
    assert_eq!(program.buffers, [0]);
    assert!(program.program.is_some(), "missing translated vertex/fragment pair");
    println!("Translated exactly one composite vertex/fragment pair to colortex0.");
}
