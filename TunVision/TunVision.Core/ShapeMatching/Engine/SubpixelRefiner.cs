using System;

namespace TunVision.Core.ShapeMatching.Engine;

public static class SubpixelRefiner
{
    /// <summary>
    /// 2D parabolic subpixel refinement using full 2x2 Hessian matrix with cross-derivative Sxy.
    /// Solves: H * [dx, dy]^T = -g
    /// </summary>
    public static bool Refine2D(
        float s00,
        float sm1_0,
        float sp1_0,
        float s0_m1,
        float s0_p1,
        float sm1_m1,
        float sp1_m1,
        float sm1_p1,
        float sp1_p1,
        out float deltaX,
        out float deltaY,
        out float refinedScore)
    {
        deltaX = 0f;
        deltaY = 0f;
        refinedScore = s00;

        float gx = (sp1_0 - sm1_0) * 0.5f;
        float gy = (s0_p1 - s0_m1) * 0.5f;

        float Sxx = sp1_0 - 2f * s00 + sm1_0;
        float Syy = s0_p1 - 2f * s00 + s0_m1;
        float Sxy = (sp1_p1 - sm1_p1 - sp1_m1 + sm1_m1) * 0.25f;

        float det = Sxx * Syy - Sxy * Sxy;
        if (MathF.Abs(det) < 1e-6f || Sxx >= 0f)
        {
            // Fallback to independent 1D parabolas if Hessian is singular or not negative definite
            if (MathF.Abs(Sxx) > 1e-5f)
                deltaX = Math.Clamp(-gx / Sxx, -0.9f, 0.9f);
            if (MathF.Abs(Syy) > 1e-5f)
                deltaY = Math.Clamp(-gy / Syy, -0.9f, 0.9f);
            refinedScore = s00 + 0.5f * (gx * deltaX + gy * deltaY);
            return true;
        }

        float invDet = 1f / det;
        deltaX = -(Syy * gx - Sxy * gy) * invDet;
        deltaY = -(Sxx * gy - Sxy * gx) * invDet;

        deltaX = Math.Clamp(deltaX, -0.9f, 0.9f);
        deltaY = Math.Clamp(deltaY, -0.9f, 0.9f);

        refinedScore = s00 + 0.5f * (gx * deltaX + gy * deltaY);
        return true;
    }

    /// <summary>
    /// 1D parabolic subpixel refinement for angle.
    /// </summary>
    public static float RefineAngle1D(float sMinus, float sCenter, float sPlus, float angleStep)
    {
        float denom = 2f * sCenter - sMinus - sPlus;
        if (MathF.Abs(denom) < 1e-6f)
            return 0f;

        float offset = (sMinus - sPlus) / (2f * denom);
        return Math.Clamp(offset, -1f, 1f) * angleStep;
    }
}
