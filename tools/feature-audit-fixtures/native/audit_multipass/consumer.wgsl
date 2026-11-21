fn post(input: PostInput) -> vec4<f32> {
    let marker = all(abs(input.color.rg - vec2<f32>(0.25, 0.5)) < vec2<f32>(0.015));
    return select(vec4<f32>(1.0, 0.08, 0.0, 1.0),
                  vec4<f32>(0.02, 0.7, 1.0, 1.0), marker);
}
