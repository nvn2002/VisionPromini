using System;
using OpenCvSharp;

namespace TunVision.Core.Common;

public static class VisionTransform
{
    public static Point2f RotatePoint(Point2f pt, Point2f center, float angleRad)
    {
        float cosA = MathF.Cos(angleRad);
        float sinA = MathF.Sin(angleRad);
        float dx = pt.X - center.X;
        float dy = pt.Y - center.Y;
        return new Point2f(
            center.X + dx * cosA - dy * sinA,
            center.Y + dx * sinA + dy * cosA);
    }

    public static Point2f TransformPoint(Point2f pt, Point2f translation, float angleRad, float scale)
    {
        float cosA = MathF.Cos(angleRad) * scale;
        float sinA = MathF.Sin(angleRad) * scale;
        return new Point2f(
            translation.X + pt.X * cosA - pt.Y * sinA,
            translation.Y + pt.X * sinA + pt.Y * cosA);
    }

    public static Mat GetAffineTransform(Point2f center, float angleDeg, float scale, Point2f translation)
    {
        // 2x3 affine matrix
        var rotMat = Cv2.GetRotationMatrix2D(center, angleDeg, scale);
        // adjust translation
        rotMat.Set(0, 2, rotMat.At<double>(0, 2) + translation.X - center.X);
        rotMat.Set(1, 2, rotMat.At<double>(1, 2) + translation.Y - center.Y);
        return rotMat;
    }

    public static float DegToRad(float deg) => deg * (MathF.PI / 180f);
    public static float RadToDeg(float rad) => rad * (180f / MathF.PI);
}
