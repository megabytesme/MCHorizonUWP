fn entity(input: EntityInput) -> Surface { return Surface(vec4<f32>(0,1,0,input.albedo.a * input.tint.a)); }
