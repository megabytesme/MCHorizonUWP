fn tonemap(input: TonemapInput) -> vec3<f32> { let x = select(select(1.0,2.0,input.uv.x >= 0.666667),0.25,input.uv.x < 0.333333); return vec3<f32>(x / (1.0 + x)); }
