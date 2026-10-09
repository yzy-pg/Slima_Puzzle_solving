#ifndef SLIMA_TOON_LIGHTING_INCLUDED
#define SLIMA_TOON_LIGHTING_INCLUDED

// Written per camera by SlimaToonLightingFeature. Never stored in a material.
float _SlimaLightSettingsEnabled;
float4 _SlimaMainLightSettings;
float4 _SlimaMainLightNoise;
float4 _SlimaAdditionalLightSettings[MAX_VISIBLE_LIGHTS];
float4 _SlimaAdditionalLightNoise[MAX_VISIBLE_LIGHTS];

void ResolveLightSettings(inout float4 settings, inout float4 noiseSettings)
{
    if (_SlimaLightSettingsEnabled < 0.5 || settings.x < 0.0)
        settings.x = _LightingMode;
    if (_SlimaLightSettingsEnabled < 0.5 || settings.y < 2.0)
    {
        settings.yzw = float3(_Bands, _NoiseStrength, _Brightness);
        noiseSettings = float4(_NoiseWorldSize, _NoisePixelSize, _NoiseSpace, _NoiseSeed);
    }
}

float2 SurfaceNoisePlane(float3 positionWS, float3 normalWS)
{
    float3 n = abs(normalWS);
    return n.y >= n.x && n.y >= n.z ? positionWS.xz :
        (n.x >= n.z ? positionWS.zy : positionWS.xy);
}

// Sample lighting at a cell's center, not separately inside every fragment.
// The screen cell ray intersects the fragment's surface plane; flat BlockMap
// faces then have one distance/shadow sample per square, even in perspective.
float3 PixelLightSamplePosition(float3 positionWS, float3 normalWS, float2 screenPx,
    float4 settings, float4 noiseSettings)
{
    ResolveLightSettings(settings, noiseSettings);
    if (settings.x > 0.5) return positionWS;
    if (noiseSettings.z > 0.5)
    {
        float size = max(1.0, floor(noiseSettings.y + 0.5));
        float2 uv = (floor(screenPx / size) + 0.5) * size / _ScaledScreenParams.xy;
        #if UNITY_REVERSED_Z
            float nearDepth = 1.0;
            float farDepth = 0.0;
        #else
            float nearDepth = UNITY_NEAR_CLIP_VALUE;
            float farDepth = 1.0;
        #endif
        float3 origin = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
        float3 end = ComputeWorldSpacePosition(uv, farDepth, UNITY_MATRIX_I_VP);
        float3 ray = end - origin;
        float denominator = dot(ray, normalWS);
        if (abs(denominator) < 0.00001) return positionWS;
        return origin + ray * (dot(positionWS - origin, normalWS) / denominator);
    }
    float size = max(0.002, noiseSettings.x);
    float2 plane = SurfaceNoisePlane(positionWS, normalWS);
    float2 delta = (floor(plane / size) + 0.5) * size - plane;
    float3 n = abs(normalWS);
    float3 offset = n.y >= n.x && n.y >= n.z ? float3(delta.x, 0, delta.y) :
        (n.x >= n.z ? float3(0, delta.y, delta.x) : float3(delta.x, delta.y, 0));
    // Preserve the surface plane for non-axis-aligned geometry too.
    float3 axis = n.y >= n.x && n.y >= n.z ? float3(0, 1, 0) :
        (n.x >= n.z ? float3(1, 0, 0) : float3(0, 0, 1));
    offset -= axis * dot(offset, normalWS) / dot(axis, normalWS);
    return positionWS + offset;
}

float SurfacePixelNoise(float3 positionWS, float3 normalWS, float2 screenPx, float4 noiseSettings)
{
    float2 cell;
    if (noiseSettings.z > 0.5)
        cell = floor(screenPx / max(1.0, floor(noiseSettings.y + 0.5)));
    else
    {
        // Project onto the dominant surface plane: square cells on floors AND walls.
        cell = floor(SurfaceNoisePlane(positionWS, normalWS) / max(0.002, noiseSettings.x));
    }
    return Hash21(cell + noiseSettings.w * float2(17.17, 43.71));
}

float MapBandBrightness(float t)
{
    float p = saturate(t) * 4.0;
    if (p < 1.0) return lerp(_Band0, _Band1, p);
    if (p < 2.0) return lerp(_Band1, _Band2, p - 1.0);
    if (p < 3.0) return lerp(_Band2, _Band3, p - 2.0);
    return lerp(_Band3, _Band4, p - 3.0);
}

float EvaluateToonLight(float raw, float3 positionWS, float3 normalWS, float2 screenPx,
    float4 settings, float4 noiseSettings)
{
    ResolveLightSettings(settings, noiseSettings);
    // A zero contribution stays zero even when the artistic black point is changed.
    if (raw <= 0.0 || settings.w <= 0.0) return 0.0;
    float t = saturate((raw * max(0.0, settings.w) - _LightInputMin) /
        max(0.0001, _LightInputMax - _LightInputMin));
    t = pow(t, max(0.1, _LightContrast));
    if (settings.x < 0.5)
    {
        float bands = clamp(floor(settings.y + 0.5), 2.0, 8.0);
        float noise = SurfacePixelNoise(positionWS, normalWS, screenPx, noiseSettings);
        // Jitter spans at most ONE bin. Away from a boundary it cannot change the band.
        float jitter = (noise - 0.5) * saturate(settings.z) / bands;
        t = min(floor(saturate(t + jitter) * bands), bands - 1.0) / (bands - 1.0);
    }
    return MapBandBrightness(t);
}

float LightBrightness(half3 color)
{
    return max(0.0, dot(color, float3(0.2126, 0.7152, 0.0722)));
}

#endif
