#ifndef PARLOR_PORTAL_INCLUDED
#define PARLOR_PORTAL_INCLUDED

// The trapdoor illusion, done analytically instead of with the stencil buffer.
//
// Stage space: origin at the centre of the trapdoor on the table plane, +Y up out of the table.
// The opening is the rectangle |x| <= _ParlorHole.x, |z| <= _ParlorHole.y on the plane y = 0.
//
// A fragment below the table plane is visible only if the straight line from the eye to that fragment
// crosses the plane inside the opening. That is exactly what a stencil portal computes, but:
//   * it needs no stencil attachment (the Quest eye buffer may be created without one),
//   * it has no draw-order dependency, so there are no seams when queues or batching change,
//   * it is per eye for free, because the camera position is per eye in single-pass instanced stereo.
// Cost: one matrix multiply and a few ALU ops per fragment.

float4x4 _ParlorWorldToStage;
float4 _ParlorHole;        // xy = half extents of the opening (x, z). zw unused.
float4 _ParlorPit;         // x = y of the top of the pit (stage space), y = y of the stage floor, z = glow strength
float4 _ParlorKeyDir;      // world-space direction towards the fake key light
float4 _ParlorWarm;        // warm candle tint

// mode 0: never clipped (things that live above the table)
// mode 1: below-plane geometry, visible only through the opening
// mode 2: geometry lying on the plane, cut away where the opening is (virtual table top, contact shadows)
void ParlorPortalClip(float3 positionWS, float3 cameraWS, float mode, float pad)
{
    if (mode < 0.5) return;
    float3 p = mul(_ParlorWorldToStage, float4(positionWS, 1.0)).xyz;
    if (mode < 1.5)
    {
        if (p.y < 0.0)
        {
            float3 c = mul(_ParlorWorldToStage, float4(cameraWS, 1.0)).xyz;
            clip(c.y - 0.0005);                       // eye below the table plane: nothing to see
            float t = c.y / max(c.y - p.y, 1e-5);
            float2 hit = c.xz + (p.xz - c.xz) * t;
            float2 d = abs(hit) - (_ParlorHole.xy + pad);
            clip(-max(d.x, d.y));
        }
    }
    else
    {
        float2 d = abs(p.xz) - (_ParlorHole.xy - pad);
        clip(max(d.x, d.y));
    }
}

// 0 at the top of the pit, 1 at the stage floor: the pit gets brighter and warmer towards the stage.
float ParlorPitDepth(float3 positionWS)
{
    float y = mul(_ParlorWorldToStage, float4(positionWS, 1.0)).y;
    return saturate((_ParlorPit.x - y) / max(_ParlorPit.x - _ParlorPit.y, 1e-3));
}

#endif
