using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenCvSharp;
using TunVision.Core.Common;
using TunVision.Core.Models;

namespace TunVision.Core.ShapeMatching;

public class TargetPyramidLevel
{
    public int Level;
    public int Width;
    public int Height;
    public float[] Vx; // Normalized gradient X
    public float[] Vy; // Normalized gradient Y

    public TargetPyramidLevel(int level, int width, int height)
    {
        Level = level;
        Width = width;
        Height = height;
        Vx = new float[width * height];
        Vy = new float[width * height];
    }
}

public class ShapeMatcher
{
    private struct Candidate
    {
        public float X;
        public float Y;
        public float AngleRad;
        public float Scale;
        public float Score;
        public float Coverage;

        public Candidate(float x, float y, float angleRad, float scale, float score, float coverage = 0)
        {
            X = x;
            Y = y;
            AngleRad = angleRad;
            Scale = scale;
            Score = score;
            Coverage = coverage;
        }
    }

    private struct TransformedFeature
    {
        public int RelX;
        public int RelY;
        public int LinearOffset;
        public float Dx;
        public float Dy;
    }

    public static List<MatchResult> Match(
        Mat searchImage,
        ShapeModel model,
        float angleMinDeg = -180f,
        float angleMaxDeg = 180f,
        float angleStepDeg = 1.0f,
        float scaleMin = 0.9f,
        float scaleMax = 1.1f,
        float scaleStep = 0.05f,
        float minScore = 0.5f,
        float greediness = 0.8f,
        int maxInstances = 5,
        float maxOverlap = 0.5f,
        bool subpixel = true,
        Rect? searchRoi = null,
        int lowestPyramid = 1)
    {
        if (model == null || searchImage == null || searchImage.Empty())
            return new List<MatchResult>();

        var levels = model.Levels.ToArray();
        if (levels.Length == 0)
            return new List<MatchResult>();

        // Clamp parameters to safe bounds to prevent invalid/extreme inputs from freezing or crashing
        minScore = Math.Clamp(minScore, 0.05f, 1.0f);
        greediness = Math.Clamp(greediness, 0.0f, 1.0f);
        scaleMin = Math.Clamp(scaleMin, 0.2f, 3.0f);
        scaleMax = Math.Clamp(scaleMax, scaleMin, 3.0f);
        scaleStep = Math.Clamp(scaleStep, 0.01f, 0.5f);
        angleMinDeg = Math.Clamp(angleMinDeg, -360f, 360f);
        angleMaxDeg = Math.Clamp(angleMaxDeg, angleMinDeg, 360f);
        angleStepDeg = Math.Clamp(angleStepDeg, 0.2f, 45f);
        // maxInstances <= 0 means Unlimited (0 = all matches, industry standard like Halcon NumMatches=0)
        maxOverlap = Math.Clamp(maxOverlap, 0.0f, 0.99f);

        using var gray = searchImage.Channels() == 1 ? searchImage.Clone() : searchImage.CvtColor(ColorConversionCodes.BGR2GRAY);

        // 1. Build Target Image Pyramid
        var targetPyramid = BuildTargetPyramid(gray, levels.Length, model.ContrastLow);
        int numLevels = Math.Min(levels.Length, targetPyramid.Count);
        if (numLevels == 0)
            return new List<MatchResult>();

        // 2. Coarse Search at coarsest level
        int coarseLevelIdx = numLevels - 1;
        var coarseLevel = targetPyramid[coarseLevelIdx];
        var coarseModel = levels[coarseLevelIdx];

        float coarseScaleFactor = MathF.Pow(2.0f, coarseLevelIdx);
        float coarseAngleStep = MathF.Max(angleStepDeg * coarseScaleFactor, 2.0f);
        float coarseScaleStep = MathF.Max(scaleStep * 2f, 0.05f);

        float angleMinRad = VisionTransform.DegToRad(angleMinDeg);
        float angleMaxRad = VisionTransform.DegToRad(angleMaxDeg);
        float coarseAngleStepRad = VisionTransform.DegToRad(coarseAngleStep);

        var searchParams = new List<(float Scale, float AngleRad)>();
        for (float s = scaleMin; s <= scaleMax + 1e-4f; s += coarseScaleStep)
        {
            for (float a = angleMinRad; a <= angleMaxRad + 1e-4f; a += coarseAngleStepRad)
            {
                searchParams.Add((s, a));
            }
        }

        var coarseCandidates = new List<Candidate>(200);
        var syncLock = new object();

        // Parallel coarse search across angle-scale parameter space
        Parallel.ForEach(
            searchParams,
            () => new List<Candidate>(),
            (param, loopState, localList) =>
            {
                var tFeatures = TransformFeatures(coarseModel.Features, param.AngleRad, param.Scale, coarseLevel.Width);
                if (tFeatures.Length > 0)
                {
                    int startX = 2;
                    int endX = coarseLevel.Width - 2;
                    int startY = 2;
                    int endY = coarseLevel.Height - 2;

                    if (searchRoi.HasValue)
                    {
                        int roiStartX = (int)(searchRoi.Value.X / coarseScaleFactor);
                        int roiEndX = (int)((searchRoi.Value.X + searchRoi.Value.Width) / coarseScaleFactor);
                        int roiStartY = (int)(searchRoi.Value.Y / coarseScaleFactor);
                        int roiEndY = (int)((searchRoi.Value.Y + searchRoi.Value.Height) / coarseScaleFactor);

                        startX = Math.Max(startX, roiStartX);
                        endX = Math.Min(endX, roiEndX);
                        startY = Math.Max(startY, roiStartY);
                        endY = Math.Min(endY, roiEndY);
                    }

                    if (startX < endX && startY < endY)
                    {
                        int stride = coarseLevelIdx >= 2 ? 1 : 2;
                        EvaluateCoarseGrid(
                            coarseLevel, tFeatures, startX, endX, startY, endY, stride,
                            param.AngleRad, param.Scale, minScore * 0.85f, greediness, localList);
                    }
                }
                return localList;
            },
            localList =>
            {
                if (localList.Count > 0)
                {
                    lock (syncLock)
                    {
                        coarseCandidates.AddRange(localList);
                    }
                }
            });

        if (coarseCandidates.Count == 0)
            return new List<MatchResult>();

        // Coarse NMS: Keep candidates well-dispersed (dynamically sized based on maxInstances)
        int coarseKeep = maxInstances <= 0 ? 1000 : Math.Max(maxInstances * 4, 100);
        var activeCandidates = CoarseNms(coarseCandidates, MathF.Max(4.0f, coarseModel.Width / 8f), coarseKeep);

        int stopLevel = Math.Clamp(lowestPyramid - 1, 0, numLevels - 1);

        // 3. Coarse-to-Fine Refinement
        for (int l = coarseLevelIdx - 1; l >= stopLevel; l--)
        {
            var curTarget = targetPyramid[l];
            var curModel = levels[l];
            float curAngleStep = MathF.Max(angleStepDeg * MathF.Pow(2.0f, l), 0.5f);
            float curAngleStepRad = VisionTransform.DegToRad(curAngleStep);
            float curScaleStep = MathF.Max(scaleStep, 0.02f);

            var refinedCandidates = new List<Candidate>(activeCandidates.Count);

            foreach (var cand in activeCandidates)
            {
                float projX = cand.X * 2.0f;
                float projY = cand.Y * 2.0f;

                Candidate bestSub = new Candidate(projX, projY, cand.AngleRad, cand.Scale, 0);

                // Small local window search
                for (float ds = -curScaleStep; ds <= curScaleStep + 1e-4f; ds += curScaleStep)
                {
                    float testS = cand.Scale + ds;
                    if (testS < scaleMin || testS > scaleMax) continue;

                    for (float da = -curAngleStepRad; da <= curAngleStepRad + 1e-4f; da += curAngleStepRad)
                    {
                        float testA = cand.AngleRad + da;
                        var tFeats = TransformFeatures(curModel.Features, testA, testS, curTarget.Width);
                        if (tFeats.Length == 0) continue;

                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int cy = (int)MathF.Round(projY + dy);
                            if (cy < 2 || cy >= curTarget.Height - 2) continue;

                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int cx = (int)MathF.Round(projX + dx);
                                if (cx < 2 || cx >= curTarget.Width - 2) continue;

                                float score = ScorePosition(curTarget, tFeats, cx, cy, 0f, 0f, toleranceRadius: l <= 1 ? 1 : 0);
                                if (score > bestSub.Score)
                                {
                                    bestSub = new Candidate(cx, cy, testA, testS, score);
                                }
                            }
                        }
                    }
                }

                if (bestSub.Score >= minScore * 0.9f)
                {
                    refinedCandidates.Add(bestSub);
                }
            }

            int refineKeep = maxInstances <= 0 ? 500 : Math.Max((int)(maxInstances * 2), 60);
            activeCandidates = CoarseNms(refinedCandidates, 4.0f, refineKeep);
        }

        // 4. Subpixel Refinement & Final Scoring at stopLevel
        var evalTarget = targetPyramid[stopLevel];
        var evalModel = levels[stopLevel];
        float scaleUp = MathF.Pow(2.0f, stopLevel);
        var finalMatches = new List<MatchResult>();

        foreach (var cand in activeCandidates)
        {
            if (cand.Score < minScore) continue;

            float subX = cand.X;
            float subY = cand.Y;
            float subA = cand.AngleRad;
            float subS = cand.Scale;
            float finalScore = cand.Score;
            float coverage = 0f;

            var tFeats = TransformFeatures(evalModel.Features, subA, subS, evalTarget.Width);

            if (subpixel && tFeats.Length > 0)
            {
                // Quadratic 2D fit for (x, y)
                int ix = (int)MathF.Round(subX);
                int iy = (int)MathF.Round(subY);

                float s00 = ScorePosition(evalTarget, tFeats, ix, iy, 0f, 0f);
                float sm1_0 = ScorePosition(evalTarget, tFeats, ix - 1, iy, 0f, 0f);
                float sp1_0 = ScorePosition(evalTarget, tFeats, ix + 1, iy, 0f, 0f);
                float s0_m1 = ScorePosition(evalTarget, tFeats, ix, iy - 1, 0f, 0f);
                float s0_p1 = ScorePosition(evalTarget, tFeats, ix, iy + 1, 0f, 0f);
                float sm1_m1 = ScorePosition(evalTarget, tFeats, ix - 1, iy - 1, 0f, 0f);
                float sp1_m1 = ScorePosition(evalTarget, tFeats, ix + 1, iy - 1, 0f, 0f);
                float sm1_p1 = ScorePosition(evalTarget, tFeats, ix - 1, iy + 1, 0f, 0f);
                float sp1_p1 = ScorePosition(evalTarget, tFeats, ix + 1, iy + 1, 0f, 0f);

                if (Engine.SubpixelRefiner.Refine2D(s00, sm1_0, sp1_0, s0_m1, s0_p1, sm1_m1, sp1_m1, sm1_p1, sp1_p1,
                    out float offsetX, out float offsetY, out _))
                {
                    subX = ix + offsetX;
                    subY = iy + offsetY;
                }

                // 1D parabolic fit for angle
                float dAngleStep = VisionTransform.DegToRad(Math.Min(angleStepDeg, 0.5f));
                var tFeatsMinus = TransformFeatures(evalModel.Features, subA - dAngleStep, subS, evalTarget.Width);
                var tFeatsPlus = TransformFeatures(evalModel.Features, subA + dAngleStep, subS, evalTarget.Width);
                float sAngMinus = ScorePosition(evalTarget, tFeatsMinus, ix, iy, 0f, 0f);
                float sAngPlus = ScorePosition(evalTarget, tFeatsPlus, ix, iy, 0f, 0f);

                float dAng = (sAngPlus - sAngMinus) * 0.5f;
                float dAngAng = sAngPlus - 2 * s00 + sAngMinus;
                if (MathF.Abs(dAngAng) > 1e-5f)
                {
                    float offsetA = Math.Clamp(-dAng / dAngAng, -1.0f, 1.0f);
                    subA += offsetA * dAngleStep;
                }

                var optFeats = TransformFeatures(evalModel.Features, subA, subS, evalTarget.Width);
                finalScore = MathF.Max(s00, ScorePosition(evalTarget, optFeats, (int)MathF.Round(subX), (int)MathF.Round(subY), 0f, 0f, toleranceRadius: 1));
                tFeats = optFeats;
            }

            // Compute coverage
            coverage = ComputeCoverage(evalTarget, tFeats, (int)MathF.Round(subX), (int)MathF.Round(subY));

            if (finalScore >= minScore)
            {
                if (stopLevel > 0)
                {
                    subX *= scaleUp;
                    subY *= scaleUp;
                }

                var match = new MatchResult
                {
                    X = subX,
                    Y = subY,
                    AngleDeg = VisionTransform.RadToDeg(subA),
                    Scale = subS,
                    Score = finalScore,
                    Coverage = coverage
                };

                // Bounding box
                float w = model.TemplateWidth * subS;
                float h = model.TemplateHeight * subS;
                match.BoundingBox = new RotatedRect(new Point2f(subX, subY), new Size2f(w, h), match.AngleDeg);

                // Transform model contours for overlay (snapshot reference for thread safety)
                var contoursRef = model.ModelContours;
                for (int cIdx = 0; cIdx < contoursRef.Count; cIdx++)
                {
                    var poly = contoursRef[cIdx];
                    var transformedPoly = new List<Point2f>(poly.Count);
                    for (int p = 0; p < poly.Count; p++)
                    {
                        var pt = VisionTransform.TransformPoint(poly[p], new Point2f(subX, subY), subA, subS);
                        transformedPoly.Add(pt);
                    }
                    match.TransformedContours.Add(transformedPoly);
                }

                finalMatches.Add(match);
            }
        }

        // 5. Final NMS across matches
        return FilterMatchesByOverlap(finalMatches, maxInstances, maxOverlap);
    }

    private static unsafe List<TargetPyramidLevel> BuildTargetPyramid(Mat gray, int numLevels, int contrastLow)
    {
        var pyramid = new List<TargetPyramidLevel>(numLevels);
        Mat cur = gray.Clone();

        for (int l = 0; l < numLevels; l++)
        {
            var level = new TargetPyramidLevel(l, cur.Width, cur.Height);

            using var blurred = new Mat();
            Cv2.GaussianBlur(cur, blurred, new Size(3, 3), 0.5);

            using var dxMat = new Mat();
            using var dyMat = new Mat();
            Cv2.Sobel(blurred, dxMat, MatType.CV_16S, 1, 0, 3);
            Cv2.Sobel(blurred, dyMat, MatType.CV_16S, 0, 1, 3);

            short* pDx = (short*)dxMat.Data.ToPointer();
            short* pDy = (short*)dyMat.Data.ToPointer();
            int step = (int)(dxMat.Step() / sizeof(short));

            int w = cur.Width;
            int h = cur.Height;

            for (int y = 0; y < h; y++)
            {
                short* rowDx = pDx + y * step;
                short* rowDy = pDy + y * step;
                int rowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    short dx = rowDx[x];
                    short dy = rowDy[x];
                    float mag = MathF.Sqrt(dx * dx + dy * dy);

                    float dynamicContrast = MathF.Max(5f, (float)contrastLow / (1 << l));
                    if (mag >= dynamicContrast)
                    {
                        float inv = 1.0f / mag;
                        level.Vx[rowOffset + x] = dx * inv;
                        level.Vy[rowOffset + x] = dy * inv;
                    }
                    else
                    {
                        level.Vx[rowOffset + x] = 0f;
                        level.Vy[rowOffset + x] = 0f;
                    }
                }
            }

            pyramid.Add(level);

            if (l < numLevels - 1)
            {
                if (cur.Width < 8 || cur.Height < 8)
                    break;

                var next = new Mat();
                Cv2.PyrDown(cur, next);
                cur.Dispose();
                cur = next;
            }
        }

        cur.Dispose();
        return pyramid;
    }

    private static TransformedFeature[] TransformFeatures(List<ShapeFeature> features, float angleRad, float scale, int stride = 0)
    {
        ShapeFeature[] snapshot;
        try
        {
            snapshot = features.ToArray();
        }
        catch
        {
            return Array.Empty<TransformedFeature>();
        }

        if (snapshot.Length == 0)
            return Array.Empty<TransformedFeature>();

        float cosA = MathF.Cos(angleRad);
        float sinA = MathF.Sin(angleRad);

        var res = new TransformedFeature[snapshot.Length];
        for (int i = 0; i < snapshot.Length; i++)
        {
            var f = snapshot[i];
            float rx = scale * (f.X * cosA - f.Y * sinA);
            float ry = scale * (f.X * sinA + f.Y * cosA);
            int relX = (int)MathF.Round(rx);
            int relY = (int)MathF.Round(ry);

            res[i] = new TransformedFeature
            {
                RelX = relX,
                RelY = relY,
                LinearOffset = relY * stride + relX,
                Dx = f.Dx * cosA - f.Dy * sinA,
                Dy = f.Dx * sinA + f.Dy * cosA
            };
        }
        return res;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void EvaluateCoarseGrid(
        TargetPyramidLevel level,
        TransformedFeature[] features,
        int startX, int endX, int startY, int endY, int stride,
        float angleRad, float scale, float minScore, float greediness,
        List<Candidate> candidates)
    {
        int count = features.Length;
        float invCount = 1.0f / count;
        int w = level.Width;
        int h = level.Height;

        int checkInterval = Math.Max(8, count / 4);

        fixed (float* pVx = level.Vx, pVy = level.Vy)
        {
            for (int y = startY; y < endY; y += stride)
            {
                int rowBase = y * w;
                for (int x = startX; x < endX; x += stride)
                {
                    int baseIdx = rowBase + x;
                    float sum = 0f;
                    bool pruned = false;

                    for (int i = 0; i < count; i++)
                    {
                        ref var f = ref features[i];
                        int fx = x + f.RelX;
                        int fy = y + f.RelY;

                        if ((uint)fx < (uint)w && (uint)fy < (uint)h)
                        {
                            int idx = baseIdx + f.LinearOffset;
                            float dot = f.Dx * pVx[idx] + f.Dy * pVy[idx];
                            sum += MathF.Max(0f, dot);
                        }

                        if (i == checkInterval && greediness > 0)
                        {
                            float maxPossible = (sum + (count - i)) * invCount;
                            if (maxPossible < minScore * greediness)
                            {
                                pruned = true;
                                break;
                            }
                        }
                    }

                    if (!pruned)
                    {
                        float score = sum * invCount;
                        if (score >= minScore)
                        {
                            candidates.Add(new Candidate(x, y, angleRad, scale, score));
                        }
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float ScorePosition(
        TargetPyramidLevel level,
        TransformedFeature[] features,
        int x, int y, float minScore, float greediness, int toleranceRadius = 1)
    {
        int count = features.Length;
        float invCount = 1.0f / count;
        int w = level.Width;
        int h = level.Height;
        int baseIdx = y * w + x;

        float sum = 0f;

        fixed (float* pVx = level.Vx, pVy = level.Vy)
        {
            for (int i = 0; i < count; i++)
            {
                ref var f = ref features[i];
                int fx = x + f.RelX;
                int fy = y + f.RelY;

                if (fx < 1 || fx >= w - 1 || fy < 1 || fy >= h - 1)
                    continue;

                int idx = baseIdx + f.LinearOffset;
                float dot = f.Dx * pVx[idx] + f.Dy * pVy[idx];

                if (toleranceRadius >= 1 && dot < 0.5f)
                {
                    // Check local neighbors to handle subpixel shifts and deformation
                    float d1 = f.Dx * pVx[idx - 1] + f.Dy * pVy[idx - 1];
                    float d2 = f.Dx * pVx[idx + 1] + f.Dy * pVy[idx + 1];
                    float d3 = f.Dx * pVx[idx - w] + f.Dy * pVy[idx - w];
                    float d4 = f.Dx * pVx[idx + w] + f.Dy * pVy[idx + w];

                    float maxD = MathF.Max(MathF.Max(d1, d2), MathF.Max(d3, d4));
                    dot = MathF.Max(dot, maxD);

                    if (toleranceRadius >= 2 && dot < 0.5f)
                    {
                        // Diagonals
                        float d5 = f.Dx * pVx[idx - w - 1] + f.Dy * pVy[idx - w - 1];
                        float d6 = f.Dx * pVx[idx - w + 1] + f.Dy * pVy[idx - w + 1];
                        float d7 = f.Dx * pVx[idx + w - 1] + f.Dy * pVy[idx + w - 1];
                        float d8 = f.Dx * pVx[idx + w + 1] + f.Dy * pVy[idx + w + 1];
                        float maxDiag = MathF.Max(MathF.Max(d5, d6), MathF.Max(d7, d8));
                        dot = MathF.Max(dot, maxDiag);
                    }
                }

                sum += MathF.Max(0f, dot);
            }
        }

        return Math.Max(0f, sum * invCount);
    }

    private static unsafe float ComputeCoverage(
        TargetPyramidLevel level,
        TransformedFeature[] features,
        int x, int y)
    {
        int count = features.Length;
        if (count == 0) return 0f;

        int w = level.Width;
        int h = level.Height;
        int baseIdx = y * w + x;
        int matched = 0;

        fixed (float* pVx = level.Vx, pVy = level.Vy)
        {
            for (int i = 0; i < count; i++)
            {
                ref var f = ref features[i];
                int fx = x + f.RelX;
                int fy = y + f.RelY;

                if (fx < 1 || fx >= w - 1 || fy < 1 || fy >= h - 1)
                    continue;

                int idx = baseIdx + f.LinearOffset;
                float dot = f.Dx * pVx[idx] + f.Dy * pVy[idx];
                if (dot < 0.4f)
                {
                    float d1 = f.Dx * pVx[idx - 1] + f.Dy * pVy[idx - 1];
                    float d2 = f.Dx * pVx[idx + 1] + f.Dy * pVy[idx + 1];
                    float d3 = f.Dx * pVx[idx - w] + f.Dy * pVy[idx - w];
                    float d4 = f.Dx * pVx[idx + w] + f.Dy * pVy[idx + w];
                    dot = MathF.Max(dot, MathF.Max(MathF.Max(d1, d2), MathF.Max(d3, d4)));
                }

                if (dot >= 0.4f)
                {
                    matched++;
                }
            }
        }

        return (float)matched / count;
    }

    private static List<Candidate> CoarseNms(List<Candidate> candidates, float distanceThreshold, int maxKeep)
    {
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        float distSq = distanceThreshold * distanceThreshold;

        var result = new List<Candidate>(Math.Min(candidates.Count, maxKeep));
        foreach (var c in candidates)
        {
            bool suppress = false;
            for (int i = 0; i < result.Count; i++)
            {
                float dx = c.X - result[i].X;
                float dy = c.Y - result[i].Y;
                if (dx * dx + dy * dy < distSq)
                {
                    suppress = true;
                    break;
                }
            }

            if (!suppress)
            {
                result.Add(c);
                if (result.Count >= maxKeep)
                    break;
            }
        }

        return result;
    }

    public static List<MatchResult> FilterMatchesByOverlap(
        List<MatchResult> matches, int maxInstances, float maxOverlap)
    {
        matches.Sort((a, b) => b.Score.CompareTo(a.Score));
        int limit = maxInstances <= 0 ? int.MaxValue : maxInstances;
        var result = new List<MatchResult>(limit == int.MaxValue ? matches.Count : Math.Min(matches.Count, limit));

        foreach (var m in matches)
        {
            bool suppress = false;
            for (int i = 0; i < result.Count; i++)
            {
                float dx = m.X - result[i].X;
                float dy = m.Y - result[i].Y;
                float avgDim = (m.BoundingBox.Size.Width + m.BoundingBox.Size.Height) * 0.25f;

                // Simple fast Euclidean distance check
                if (dx * dx + dy * dy < avgDim * avgDim * (1.0f - maxOverlap))
                {
                    suppress = true;
                    break;
                }
            }

            if (!suppress)
            {
                result.Add(m);
                if (result.Count >= limit)
                    break;
            }
        }

        return result;
    }
}
