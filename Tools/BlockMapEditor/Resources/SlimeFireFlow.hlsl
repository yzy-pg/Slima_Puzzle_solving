#ifndef SLIME_FIRE_FLOW_INCLUDED
#define SLIME_FIRE_FLOW_INCLUDED
float SlimeFireHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}
float SlimeFireNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(SlimeFireHash(cell), SlimeFireHash(cell + float2(1, 0)), f.x),
        lerp(SlimeFireHash(cell + float2(0, 1)), SlimeFireHash(cell + 1), f.x), f.y);
}
float SlimeFireFlow(float2 p, float time)
{
    p.y -= time * 0.65;
    p.x += sin(p.y * 4 + time) * 0.16;
    return SlimeFireNoise(p * float2(5, 3)) * 0.7
        + SlimeFireNoise(p * float2(11, 7) + time * 0.12) * 0.3;
}
float SlimeFireSurfaceFlow(float3 positionOS, float time)
{
    // Keep full object-space coordinates: combining X/Z or normalizing the UV position
    // compresses one side of the sphere and stretches the noise around its silhouette.
    float3 pixelPosition = floor(positionOS * 2 * 24) / 24;
    float3 weights = pow(abs(normalize(positionOS)), 4);
    weights /= max(dot(weights, float3(1, 1, 1)), 0.0001);
    float sideX = SlimeFireFlow(pixelPosition.zy, time);
    float topY = SlimeFireFlow(pixelPosition.xz, time);
    float sideZ = SlimeFireFlow(pixelPosition.xy, time);
    return dot(float3(sideX, topY, sideZ), weights);
}
#endif
