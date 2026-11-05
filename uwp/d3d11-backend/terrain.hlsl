// A small terrain pipeline using only feature-level 9_1 capabilities.
cbuffer Frame : register(b0) { row_major float4x4 view_projection; };
Texture2D atlas : register(t0);
SamplerState atlas_sampler : register(s0);

struct VertexInput {
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};
struct VertexOutput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};
VertexOutput VSMain(VertexInput input) {
    VertexOutput output;
    output.position = mul(float4(input.position, 1.0), view_projection);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}
float4 PSMain(VertexOutput input) : SV_TARGET {
    float4 color = atlas.Sample(atlas_sampler, input.uv) * input.color;
    clip(color.a - 0.5);
    return color;
}
float4 PSOpaque(VertexOutput input) : SV_TARGET {
    float4 color = atlas.Sample(atlas_sampler, input.uv) * input.color;
    return float4(color.rgb, 1.0);
}
float4 PSEntity(VertexOutput input) : SV_TARGET {
    float4 color = atlas.Sample(atlas_sampler, input.uv) * input.color;
    clip(color.a - 0.1);
    return color;
}
float4 PSAlpha(VertexOutput input) : SV_TARGET {
    return atlas.Sample(atlas_sampler, input.uv) * input.color;
}
float4 PSInvert(VertexOutput input) : SV_TARGET {
    float4 color = atlas.Sample(atlas_sampler, input.uv) * input.color;
    clip(color.a - 0.001);
    return color;
}
