// A deliberately small feature-level 9_1 presentation and texture test.
Texture2D atlas : register(t0);
SamplerState atlasSampler : register(s0);

struct VertexInput {
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct VertexOutput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VertexOutput VSMain(VertexInput input) {
    VertexOutput output;
    output.position = float4(input.position, 0.5, 1.0);
    output.uv = input.uv;
    return output;
}

float4 PSMain(VertexOutput input) : SV_TARGET {
    return atlas.Sample(atlasSampler, input.uv);
}
