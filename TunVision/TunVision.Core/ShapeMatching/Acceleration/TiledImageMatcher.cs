using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using TunVision.Core.Models;

namespace TunVision.Core.ShapeMatching.Acceleration;

/// <summary>
/// TiledImageMatcher (G8): High-performance spatial grid partitioning matcher for ultra-high-resolution
/// images (20MP+). Partitions scenes into tiles with overlap margin (>= model.Diameter), executes
/// matching across tiles in parallel, remaps coordinates to global space, and applies global NMS.
/// </summary>
public static class TiledImageMatcher
{
    public static List<MatchResult> MatchTiled(
        Mat largeScene,
        ShapeModel model,
        int tilesX = 2,
        int tilesY = 2,
        double angleMinDeg = -180,
        double angleMaxDeg = 180,
        double angleStepDeg = 1.0,
        double scaleMin = 0.9,
        double scaleMax = 1.1,
        double scaleStep = 0.05,
        double minScore = 0.5,
        double greediness = 0.8,
        int maxInstances = 5,
        double maxOverlap = 0.5,
        bool subpixel = true,
        int lowestPyramid = 1)
    {
        if (largeScene == null || largeScene.Empty() || model == null || model.Levels.Count == 0)
            return new List<MatchResult>();

        tilesX = Math.Max(1, tilesX);
        tilesY = Math.Max(1, tilesY);

        int W = largeScene.Width;
        int H = largeScene.Height;
        int wt = W / tilesX;
        int ht = H / tilesY;

        // Overlap margin >= model.Diameter (where diameter is scaleMax * sqrt(Wm^2 + Hm^2) + 4)
        double modelDiag = Math.Sqrt((double)model.TemplateWidth * model.TemplateWidth + (double)model.TemplateHeight * model.TemplateHeight);
        int margin = (int)Math.Ceiling(scaleMax * modelDiag + 4.0);

        var allMatches = new ConcurrentBag<MatchResult>();

        Parallel.For(0, tilesY, ty =>
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                int x0 = Math.Max(0, tx * wt - margin);
                int y0 = Math.Max(0, ty * ht - margin);
                int x1 = Math.Min(W, (tx == tilesX - 1 ? W : (tx + 1) * wt) + margin);
                int y1 = Math.Min(H, (ty == tilesY - 1 ? H : (ty + 1) * ht) + margin);

                int tileW = x1 - x0;
                int tileH = y1 - y0;
                if (tileW <= 0 || tileH <= 0) continue;

                using var subMat = new Mat(largeScene, new Rect(x0, y0, tileW, tileH)).Clone();

                var tileMatches = ShapeMatcher.Match(
                    subMat,
                    model,
                    (float)angleMinDeg,
                    (float)angleMaxDeg,
                    (float)angleStepDeg,
                    (float)scaleMin,
                    (float)scaleMax,
                    (float)scaleStep,
                    (float)minScore,
                    (float)greediness,
                    maxInstances,
                    (float)maxOverlap,
                    subpixel,
                    searchRoi: null,
                    lowestPyramid: lowestPyramid);

                foreach (var m in tileMatches)
                {
                    m.X += x0;
                    m.Y += y0;
                    m.BoundingBox = new RotatedRect(
                        new Point2f(m.BoundingBox.Center.X + x0, m.BoundingBox.Center.Y + y0),
                        m.BoundingBox.Size,
                        m.BoundingBox.Angle);

                    for (int ci = 0; ci < m.TransformedContours.Count; ci++)
                    {
                        var poly = m.TransformedContours[ci];
                        for (int pi = 0; pi < poly.Count; pi++)
                        {
                            poly[pi] = new Point2f(poly[pi].X + x0, poly[pi].Y + y0);
                        }
                    }

                    allMatches.Add(m);
                }
            }
        });

        var combinedList = allMatches.ToList();
        return ShapeMatcher.FilterMatchesByOverlap(combinedList, maxInstances, (float)maxOverlap);
    }

    public static List<MatchResult> MatchTiled(
        Mat largeScene,
        ShapeModel model,
        int tilesX,
        int tilesY,
        float angleMinDeg,
        float angleMaxDeg,
        float angleStepDeg = 1.0f,
        float scaleMin = 0.9f,
        float scaleMax = 1.1f,
        float scaleStep = 0.05f,
        float minScore = 0.5f,
        float greediness = 0.8f,
        int maxInstances = 5,
        float maxOverlap = 0.5f,
        bool subpixel = true,
        int lowestPyramid = 1)
    {
        return MatchTiled(
            largeScene,
            model,
            tilesX,
            tilesY,
            (double)angleMinDeg,
            (double)angleMaxDeg,
            (double)angleStepDeg,
            (double)scaleMin,
            (double)scaleMax,
            (double)scaleStep,
            (double)minScore,
            (double)greediness,
            maxInstances,
            (double)maxOverlap,
            subpixel,
            lowestPyramid);
    }
}
