using System;

namespace TunVision.Core.ShapeMatching;

public struct ShapeFeature
{
    public float X;
    public float Y;
    public float Dx;
    public float Dy;
    public float Angle;
    public float Magnitude;

    public ShapeFeature(float x, float y, float dx, float dy, float angle, float magnitude)
    {
        X = x;
        Y = y;
        Dx = dx;
        Dy = dy;
        Angle = angle;
        Magnitude = magnitude;
    }
}
