// Board T2 / T1: slime coat drawn over a one-shot copied power (SingleUseSlimeCoat.mat, Shader Graph
// "Corruption/SingleUseSlime"). The material is appended as an extra pass on the power model, so this coat sits just
// above its surface: a lumpy jelly shell that slowly breathes, with flowing thicker streaks, holes, wet bumps and a
// glowing rim. Every look parameter is a material property (placeholder look, design-owned).
//
// Space: the model is authored tiny and scaled (FBX), so positions are taken in object space then multiplied by the
// object's scale: the pattern sticks to the object (no swimming when it moves) but its sizes are in world units.

#ifndef SINGLE_USE_SLIME_INCLUDED
#define SINGLE_USE_SLIME_INCLUDED

// Per-object seed from the object's world position, so identical models side by side don't wear the same stamp.
float3 SlimeObjectSeed()
{
    float3 origin = UNITY_MATRIX_M._m03_m13_m23;
    return frac(origin * float3(0.137, 0.271, 0.193)) * 37.0;
}

float3 SlimeObjectScale()
{
    return float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
}

float SlimeHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Smooth value noise in [0, 1].
float SlimeNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(SlimeHash(i + float3(0, 0, 0)), SlimeHash(i + float3(1, 0, 0)), f.x),
                     lerp(SlimeHash(i + float3(0, 1, 0)), SlimeHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(SlimeHash(i + float3(0, 0, 1)), SlimeHash(i + float3(1, 0, 1)), f.x),
                     lerp(SlimeHash(i + float3(0, 1, 1)), SlimeHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float SlimeFbm(float3 p)
{
    float v = 0.5 * SlimeNoise(p);
    v += 0.25 * SlimeNoise(p * 2.03 + 11.7);
    v += 0.125 * SlimeNoise(p * 4.01 + 23.1);
    return v / 0.875;
}

// Thickness of the slime layer at a (world-sized, object-anchored) point: domain-warped fbm flowing downward (world
// gravity expressed in the object's frame) so the streaks visibly creep over the object.
float SlimeThickness(float3 p, float3 downOS, float t)
{
    float3 flow = downOS * t * _FlowSpeed;
    float3 warp = float3(SlimeNoise(p * 0.7 + t * 0.11), SlimeNoise(p * 0.7 + 5.2 - t * 0.09), SlimeNoise(p * 0.7 + 9.4)) - 0.5;
    return SlimeFbm((p + warp * 1.5) * _NoiseScale - flow * _NoiseScale + SlimeObjectSeed());
}

// Hanging drips: on faces that look sideways or down (world frame), narrow columns of the shell are pulled down along
// gravity, each column growing then snapping back on its own cycle, so tongues of slime slowly creep off the edges.
float SlimeDrip(float3 p, float3 downOS, float sideness, float t)
{
    // Column id from the position projected on the plane perpendicular to gravity.
    float3 flat = p - downOS * dot(p, downOS) + SlimeObjectSeed();
    float column = SlimeNoise(flat * _DripDensity);
    float mask = smoothstep(0.62, 0.86, column);
    float seed = SlimeHash(floor(flat * _DripDensity) + 3.1);
    float grow = frac(t * 0.12 * (0.6 + seed) + seed);
    grow = grow * grow * (3.0 - 2.0 * grow);
    return mask * sideness * grow * _DripLength;
}

void SlimeVertex_float(float3 PositionOS, float3 NormalOS, out float3 Out)
{
    float3 scale = SlimeObjectScale();
    float3 p = PositionOS * scale;
    float t = _TimeParameters.x;
    float3 downOS = normalize(TransformWorldToObjectDir(float3(0, -1, 0)) * scale);

    float thick = SlimeThickness(p, downOS, t);
    // Jelly: a lumpy shell (thicker where the slime is thicker) that slowly breathes.
    float breathe = 0.5 + 0.5 * sin(t * 1.7 + dot(p, float3(1.3, 0.7, 1.1)) * 2.0);
    float offset = _Inflate * (0.2 + thick * 1.6) + _Wobble * breathe;
    float3 shell = NormalOS * (offset / max(dot(abs(NormalOS), scale), 1e-5));

    float3 normalWS = TransformObjectToWorldNormal(NormalOS);
    float sideness = saturate(1.0 - normalWS.y * 1.4);
    float drip = SlimeDrip(p, downOS, sideness, t);
    Out = PositionOS + shell + downOS * (drip / max(dot(abs(downOS), scale), 1e-5));
}

void SlimeSurface_float(float3 PositionOS, float3 NormalWS, float3 ViewWS,
                        out float3 BaseColor, out float3 Normal, out float Smoothness, out float3 Emission, out float Alpha)
{
    float3 scale = SlimeObjectScale();
    float3 p = PositionOS * scale;
    float t = _TimeParameters.x;
    float3 downOS = normalize(TransformWorldToObjectDir(float3(0, -1, 0)) * scale);

    float thick = SlimeThickness(p, downOS, t);

    // Wet bumps: normal bent by the thickness gradient (finite differences, object frame → world).
    float e = 0.04;
    float3 grad = float3(SlimeThickness(p + float3(e, 0, 0), downOS, t) - thick,
                         SlimeThickness(p + float3(0, e, 0), downOS, t) - thick,
                         SlimeThickness(p + float3(0, 0, e), downOS, t) - thick) / e;
    float3 gradWS = TransformObjectToWorldDir(grad / scale, false);
    float3 n = normalize(NormalWS - gradWS * _Bumpiness * 0.18);

    // Patchy cover: holes where the layer is thin, so the copied power still shows through.
    float cover = smoothstep(_Coverage - 0.16, _Coverage + 0.12, thick);
    // Sparse trapped bubbles.
    float bubbleCell = SlimeNoise(p * _NoiseScale * 3.5 + float3(0, t * 0.25, 0));
    float bubble = smoothstep(0.86, 0.9, bubbleCell) * cover;

    float3 view = normalize(ViewWS);
    float fresnel = pow(1.0 - saturate(dot(n, view)), 3.0);
    // 0 at the edge of a patch, 1 deep inside: thin film is clear and pale, thick gel is deep and dense.
    float depth = saturate((thick - _Coverage) * 3.0);

    BaseColor = lerp(_SlimeColor.rgb, _DeepColor.rgb, depth) + bubble * 0.3;
    Normal = n;
    Smoothness = _Smoothness;
    Emission = (_SlimeColor.rgb * (fresnel * _RimGlow + depth * _InnerGlow)) * cover;
    Alpha = saturate(cover * lerp(_Opacity * 0.2, _Opacity, depth) + fresnel * 0.3 * cover + bubble * 0.25);
}

#endif
