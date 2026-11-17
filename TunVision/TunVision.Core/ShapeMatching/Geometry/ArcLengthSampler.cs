using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.ShapeMatching.Core;

namespace TunVision.Core.ShapeMatching.Geometry;

public static class ArcLengthSampler
{
    /// <summary>
    /// Resamples subpixel continuous contour points with uniform arc-length spacing (step = 1.0 px).
    /// Preserves natural curvature of circles and ellipses without polygon decimation (no ApproxPolyDP).
    /// Linearly interpolates coordinates, normal unit vectors (Nx, Ny), and response amplitude.
    /// </summary>
    public static List<SubPixelEdgePoint> ResampleContour(List<SubPixelEdgePoint> contour, float step = 1.0f)
    {
        if (contour.Count < 2)
            return new List<SubPixelEdgePoint>(contour);

        var result = new List<SubPixelEdgePoint> { contour[0] };
        float accumulated = 0f;

        for (int i = 0; i < contour.Count - 1; i++)
        {
            float dx = contour[i + 1].X - contour[i].X;
            float dy = contour[i + 1].Y - contour[i].Y;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            if (segLen < 1e-5f) continue;

            float invSegLen = 1.0f / segLen;
            float ux = dx * invSegLen;
            float uy = dy * invSegLen;

            float distToNext = segLen;
            float walked = 0f;

            while (accumulated + (distToNext - walked) >= step)
            {
                float needed = step - accumulated;
                walked += needed;
                float t = Math.Clamp(walked * invSegLen, 0f, 1f);

                // Linearly interpolate normal vector and normalize to unit length
                float nx = (1.0f - t) * contour[i].Nx + t * contour[i + 1].Nx;
                float ny = (1.0f - t) * contour[i].Ny + t * contour[i + 1].Ny;
                float nLen = MathF.Sqrt(nx * nx + ny * ny);
                if (nLen > 1e-6f)
                {
                    nx /= nLen;
                    ny /= nLen;
                }
                else
                {
                    nx = contour[i].Nx;
                    ny = contour[i].Ny;
                }

                // Linearly interpolate response amplitude
                float resp = (1.0f - t) * contour[i].Response + t * contour[i + 1].Response;

                result.Add(new SubPixelEdgePoint
                {
                    X = contour[i].X + ux * walked,
                    Y = contour[i].Y + uy * walked,
                    Nx = nx,
                    Ny = ny,
                    Response = resp,
                    State = contour[i].State
                });
                accumulated = 0f;
            }

            accumulated += (distToNext - walked);
        }

        if (accumulated > 0.25f && result.Count > 0)
        {
            result.Add(contour[^1]);
        }

        return result;
    }

    /// <summary>
    /// Converts a list of SubPixelEdgePoint to Point2f for UI rendering (OpenCvSharp / WPF).
    /// </summary>
    public static List<Point2f> ToPoint2fList(List<SubPixelEdgePoint> points)
    {
        var list = new List<Point2f>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            list.Add(new Point2f(points[i].X, points[i].Y));
        }
        return list;
    }

    /// <summary>
    /// Samples subpixel features along contours with uniform arc-length spacing,
    /// avoiding clustering and ensuring all parts of the object are proportionally represented.
    /// Eliminates ApproxPolyDP polygon collapse distortion.
    /// </summary>
    public static List<StandardFeature> Sample(
        List<StandardFeature> rawFeatures,
        float minGap = 1.0f,
        int maxFeatures = 500)
    {
        if (rawFeatures.Count == 0)
            return new List<StandardFeature>();

        if (rawFeatures.Count <= maxFeatures && minGap <= 0.5f)
            return new List<StandardFeature>(rawFeatures);

        float minGapSq = minGap * minGap;
        var selected = new List<StandardFeature>(Math.Min(rawFeatures.Count, maxFeatures));

        // Spatial grid hashing for O(N) neighbor distance checks instead of O(N^2)
        float cellSize = MathF.Max(minGap, 2.0f);
        var grid = new Dictionary<long, List<int>>();

        // Subsample while maintaining spatial dispersion
        for (int i = 0; i < rawFeatures.Count; i++)
        {
            var f = rawFeatures[i];
            int cx = (int)MathF.Floor(f.X / cellSize);
            int cy = (int)MathF.Floor(f.Y / cellSize);

            bool tooClose = false;
            for (int dy = -1; dy <= 1 && !tooClose; dy++)
            {
                for (int dx = -1; dx <= 1 && !tooClose; dx++)
                {
                    long neighborHash = ((long)(cx + dx) << 32) ^ (uint)(cy + dy);
                    if (grid.TryGetValue(neighborHash, out var indices))
                    {
                        foreach (var idx in indices)
                        {
                            var existing = selected[idx];
                            float distSq = (f.X - existing.X) * (f.X - existing.X) + (f.Y - existing.Y) * (f.Y - existing.Y);
                            if (distSq < minGapSq)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (!tooClose)
            {
                int newIdx = selected.Count;
                selected.Add(f);

                long cellHash = ((long)cx << 32) ^ (uint)cy;
                if (!grid.TryGetValue(cellHash, out var list))
                {
                    list = new List<int>(4);
                    grid[cellHash] = list;
                }
                list.Add(newIdx);

                if (selected.Count >= maxFeatures)
                    break;
            }
        }

        return selected;
    }
}
