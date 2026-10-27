// 연출 셰이더 공용 잡음 (2026-10-27): 진화 모델 FX 메시는 UV가 없어 물체 공간 위치로 잡음을 만든다.
#ifndef STATION_NOISE_INCLUDED
#define STATION_NOISE_INCLUDED

float StationHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// 값 잡음 0 ~ 1 (부드러운 보간)
float StationNoise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(StationHash3(i + float3(0, 0, 0)), StationHash3(i + float3(1, 0, 0)), f.x),
                     lerp(StationHash3(i + float3(0, 1, 0)), StationHash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(StationHash3(i + float3(0, 0, 1)), StationHash3(i + float3(1, 0, 1)), f.x),
                     lerp(StationHash3(i + float3(0, 1, 1)), StationHash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float StationFbm3(float3 p)
{
    return 0.5 * StationNoise3(p) + 0.3 * StationNoise3(p * 2.03 + 1.7) + 0.2 * StationNoise3(p * 4.01 + 3.1);
}

#endif
