fn post(input: PostInput) -> vec4<f32> { return vec4<f32>(input.color.rgb * vec3<f32>(0.05, 0.60, 1.0), 1.0); }
