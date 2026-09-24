Texture2D backdrop : register(t0);
Texture2D softened : register(t1);
SamplerState linearClamp : register(s0);
cbuffer Parameters : register(b0) { float2 size; float pad; float dark; float dpi; float2 blurDirection; float unused; };

float4 VS(uint id : SV_VertexID) : SV_POSITION
{
    return float4(id == 2 ? 3 : -1, id == 1 ? 3 : -1, 0, 1);
}

// Two separable passes cover every neighboring physical pixel, rather than
// spreading a sparse sample grid farther apart as the blur grows.
float4 BlurPS(float4 position : SV_POSITION) : SV_TARGET
{
    float2 uv = position.xy / (size + 2 * pad);
    float2 step = blurDirection / (size + 2 * pad);
    float sigma = clamp(2.5 * dpi, 1.0, 6.0);
    int radius = (int)ceil(3 * sigma);
    float3 color = backdrop.SampleLevel(linearClamp, uv, 0).rgb;
    float total = 1;
    [loop] for (int i = 1; i <= radius; ++i)
    {
        float weight = exp(-float(i * i) / (2 * sigma * sigma));
        color += (backdrop.SampleLevel(linearClamp, uv + step * i, 0).rgb
            + backdrop.SampleLevel(linearClamp, uv - step * i, 0).rgb) * weight;
        total += 2 * weight;
    }
    return float4(color / total, 1);
}

float3 GlassColor(float2 uv)
{
    return lerp(backdrop.Sample(linearClamp, uv).rgb,
        softened.Sample(linearClamp, uv).rgb, .60);
}

float4 PS(float4 position : SV_POSITION) : SV_TARGET
{
    float2 p = position.xy;
    float radius = min(size.x, size.y) * .5;
    float2 q = p - size * .5;
    float2 axis = clamp(q, -size * .5 + radius, size * .5 - radius);
    float2 radial = q - axis;
    float lengthR = length(radial);
    float distance = radius - lengthR;
    float alpha = saturate(distance + .5);
    if (alpha <= 0) return 0;
    float2 normal = radial / max(lengthR, .001);
    float bevel = min(13 * dpi, radius * .45) * (2.0 / 3.0);
    float edge = saturate(1 - distance / bevel);
    // Sample farther inward at the curved lip while preserving the background's colors.
    float2 bend = -normal * (sin(edge * 1.5707963) * 7 * dpi);
    float2 uv = (p + pad + bend) / (size + 2 * pad);
    float2 texel = 1 / (size + 2 * pad);
    float3 color = GlassColor(uv);
    float2 dispersion = normal * .55 * dpi * edge * texel;
    color.r = lerp(color.r, GlassColor(uv + dispersion).r, edge * .6);
    color.b = lerp(color.b, GlassColor(uv - dispersion).b, edge * .6);
    color = lerp(color, dark > .5 ? float3(.06,.085,.12) : float3(.95,.98,1), .10);
    float rim = 1 - smoothstep(.45 * dpi, 1.55 * dpi, distance);
    float light = saturate(dot(normal, normalize(float2(-.5,-1))));
    float shade = saturate(dot(normal, normalize(float2(.5,1))));
    // Soft shading inside the curved lip adds depth without another outline.
    color *= 1 - pow(edge, 2) * .075 * shade;
    color = lerp(color, float3(.94,.98,1), rim * (.14 + .52 * light));
    color += pow(edge, 3) * .09 * light;
    return float4(saturate(color) * alpha, alpha);
}
