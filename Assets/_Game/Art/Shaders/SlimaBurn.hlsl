#ifndef SLIMA_BURN_INCLUDED
#define SLIMA_BURN_INCLUDED
TEXTURE2D(_BurnNoiseTex);
SAMPLER(sampler_BurnNoiseTex);

float BurnDistance(float3 positionWS)
{
    float height = (positionWS.y - _BurnHeight.x) / max(.001, _BurnHeight.y);
    float2 noiseUV = positionWS.xz * 3 + positionWS.y * float2(.67, .37);
    float noise = SAMPLE_TEXTURE2D_LOD(_BurnNoiseTex, sampler_BurnNoiseTex, noiseUV, 0).r;
    // At 0 the full model survives; at 1 even the bottom is gone. The irregular edge moves downward.
    return 1.13 - _BurnProgress * 1.26 - height - (noise - .5) * .16;
}

void ClipBurn(float3 positionWS)
{
    if (_BurnEnabled > .5) clip(BurnDistance(positionWS));
}

half3 BurnColor(half3 color, float3 positionWS)
{
    if (_BurnEnabled < .5) return color;
    float distance = BurnDistance(positionWS);
    float charred = 1 - smoothstep(.07, .21, distance);
    float ember = 1 - smoothstep(.015, .085, distance);
    float flicker = SAMPLE_TEXTURE2D(_BurnNoiseTex, sampler_BurnNoiseTex,
        positionWS.xz * 5 + float2(_Time.y * .8, -_Time.y * 1.3)).r;
    half3 fire = lerp(half3(1, .055, .003), half3(1, .72, .065), flicker);
    return lerp(color, half3(.045, .018, .008), charred) + fire * ember * 2.4;
}
#endif
