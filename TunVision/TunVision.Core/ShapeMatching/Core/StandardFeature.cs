using System;
using System.Runtime.InteropServices;

namespace TunVision.Core.ShapeMatching.Core;

/// <summary>
/// Standard 16-byte unmanaged feature struct representing a contour point with unit normal vector.
/// Perfectly matches MWWZ / Halcon industrial shape model binary specification.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
public readonly struct StandardFeature
{
    public readonly float X;  // Position X relative to model origin
    public readonly float Y;  // Position Y relative to model origin
    public readonly float Nx; // Unit normal vector X (gradient direction cos theta)
    public readonly float Ny; // Unit normal vector Y (gradient direction sin theta)

    public StandardFeature(float x, float y, float nx, float ny)
    {
        X = x;
        Y = y;
        Nx = nx;
        Ny = ny;
    }

    public ShapeFeature ToShapeFeature()
    {
        float angle = MathF.Atan2(Ny, Nx);
        return new ShapeFeature(X, Y, Nx, Ny, angle, 1.0f);
    }

    public static StandardFeature FromShapeFeature(ShapeFeature f)
    {
        return new StandardFeature(f.X, f.Y, f.Dx, f.Dy);
    }
}
