fn post(input: PostInput) -> vec4<f32> { return select(vec4<f32>(0,0,1,1), vec4<f32>(1,0,0,1), depth_at(input.uv) > 0.0); }
