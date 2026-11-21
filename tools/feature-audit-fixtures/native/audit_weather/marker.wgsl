fn weather(input: WeatherInput) -> vec4<f32> { return vec4<f32>(select(vec3<f32>(1,0,0), vec3<f32>(0,0,1), input.snow), input.color.a); }
