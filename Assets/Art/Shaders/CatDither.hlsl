#ifndef CAT_DITHER_INCLUDED
#define CAT_DITHER_INCLUDED

// Per-renderer fade amount. Declared OUTSIDE the UnityPerMaterial CBUFFER on purpose: a
// MaterialPropertyBlock override cannot live in the SRP-batched material CBUFFER. It IS a shader
// Property, so an untouched material uploads its default (1 = fully opaque) and the body renders
// normally when nothing drives the fade.
float _CatFadeAmount;

// 4x4 ordered (Bayer) screen-door dither.
//
// WHY DITHER AND NOT ALPHA: this CLIPS pixels instead of blending them, so the avatar stays in the
// OPAQUE queue — no per-object transparency sorting, no self-sorting artefacts through the cat's own
// ears/tail/cape, and shadows/depth stay correct. It is the standard technique for fading a character.
//
// _CatFadeAmount: 1 = fully visible (nothing clipped), 0 = fully clipped (invisible).
void CatDitherClip(float4 positionCS)
{
    const float kBayer4x4[16] =
    {
         0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
        12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
         3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
        15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
    };

    // positionCS.xy in the fragment stage is the pixel coordinate — tile the 4x4 matrix over the screen.
    uint2 _pixel = uint2(positionCS.xy) & 3;
    clip(_CatFadeAmount - kBayer4x4[_pixel.y * 4 + _pixel.x] - 1e-5);
}

#endif
