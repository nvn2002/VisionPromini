using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using OpenCvSharp;
using TunVision.Core.Common;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching.Acceleration;

namespace TunVision.Core.ShapeMatching.Engine;

public static class TunVisionShapeMatcher
{
    private struct Candidate
    {
        public float X;
        public float Y;
        public float AngleRad;
        public float Scale;
        public float Score;

        public Candidate(float x, float y, float angleRad, float scale, float score)
        {
            X = x;
            Y = y;
            AngleRad = angleRad;
            Scale = scale;
            Score = score;
        }
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
        Rect? searchRoi = null)
    {
        if (model.Levels.Count == 0 || searchImage.Empty())
            return new List<MatchResult>();

        // Safe bounds clamping
        minScore = Math.Clamp(minScore, 0.05f, 1.0f);
        greediness = Math.Clamp(greediness, 0.0f, 1.0f);
        scaleMin = Math.Clamp(scaleMin, 0.2f, 3.0f);
        scaleMax = Math.Clamp(scaleMax, scaleMin, 3.0f);
        scaleStep = Math.Clamp(scaleStep, 0.01f, 0.5f);
        angleMinDeg = Math.Clamp(angleMinDeg, -360f, 360f);
        angleMaxDeg = Math.Clamp(angleMaxDeg, angleMinDeg, 360f);
        angleStepDeg = Math.Clamp(angleStepDeg, 0.2f, 45f);
        // maxInstances <= 0 means Unlimited (0 = all matches)
        maxOverlap = Math.Clamp(maxOverlap, 0.0f, 0.99f);

        using var gray = searchImage.Channels() == 1 ? searchImage.Clone() : searchImage.CvtColor(ColorConversionCodes.BGR2GRAY);

        int numLevels = model.Levels.Count;
        bool ignorePolarity = (model.Metric != 0);

        // 1. Build Multi-Level Response Maps Pyramid with Dynamic Contrast Halving
        var pyramidMaps = new ResponseMapGenerator[numLevels];
        Mat curImg = gray.Clone();

        for (int l = 0; l < numLevels; l++)
        {
            float dynamicContrast = MathF.Max(5f, (float)model.ContrastLow / (1 << l));
            var respGen = new ResponseMapGenerator();
            respGen.Build(curImg, ignorePolarity, dynamicContrast);
            if (l > 0)
            {
                respGen.Spread(1); // Spread coarse levels to prevent subsampling grid misses
            }
            pyramidMaps[l] = respGen;

            if (l < numLevels - 1)
            {
                var next = new Mat();
                Cv2.PyrDown(curImg, next);
                curImg.Dispose();
                curImg = next;
            }
        }
        curImg.Dispose();

        try
        {
            // 2. Coarse Search on Coarsest Level
            int coarseIdx = numLevels - 1;
            var coarseMap = pyramidMaps[coarseIdx];
            var coarseModel = model.Levels[coarseIdx];
            int featCount = coarseModel.Features.Count;

            if (featCount == 0)
                return new List<MatchResult>();

            float coarseScaleFactor = MathF.Pow(2.0f, coarseIdx);
            float coarseAngleStep = MathF.Max(angleStepDeg, 2.0f);
            float coarseScaleStep = MathF.Max(scaleStep, 0.05f);

            float angleMinRad = VisionTransform.DegToRad(angleMinDeg);
            float angleMaxRad = VisionTransform.DegToRad(angleMaxDeg);
            float coarseAngleStepRad = VisionTransform.DegToRad(coarseAngleStep);

            // Calculate search angle & scale combinations
            var searchParams = new List<(float Scale, float AngleRad)>();
            for (float s = scaleMin; s <= scaleMax + 1e-4f; s += coarseScaleStep)
            {
                for (float a = angleMinRad; a <= angleMaxRad + 1e-4f; a += coarseAngleStepRad)
                {
                    searchParams.Add((s, a));
                }
            }

            // Search bounding box on coarse level
            int startX = 2;
            int endX = coarseMap.Width - 2;
            int startY = 2;
            int endY = coarseMap.Height - 2;

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

            var coarseCandidates = new List<Candidate>(200);
            var syncLock = new object();
            int stride = coarseIdx >= 2 ? 1 : 2;
            float coarseThreshold = minScore * 0.80f; // Relaxed coarse threshold for robust candidate detection

            // Parallel coarse grid search
            Parallel.ForEach(
                searchParams,
                () => new List<Candidate>(),
                (param, loopState, localList) =>
                {
                    Avx2ScoreAccumulator.PrecomputeOffsets(
                        coarseModel.Features,
                        param.AngleRad,
                        param.Scale,
                        coarseMap.Width,
                        out int[] offsets,
                        out int[] sectors);

                    for (int y = startY; y < endY; y += stride)
                    {
                        for (int x = startX; x < endX; x += stride)
                        {
                            float score = Avx2ScoreAccumulator.ScoreAtPosition(
                                coarseMap.ResponseMaps,
                                coarseMap.Width,
                                coarseMap.Height,
                                x,
                                y,
                                offsets,
                                sectors,
                                featCount,
                                coarseThreshold,
                                greediness);

                            if (score >= coarseThreshold)
                            {
                                localList.Add(new Candidate(x, y, param.AngleRad, param.Scale, score));
                            }
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

            // Coarse NMS: Keep candidates well-dispersed
            int coarseKeep = maxInstances <= 0 ? 1000 : Math.Max(maxInstances * 4, 100);
            var activeCandidates = NmsFilter(coarseCandidates, MathF.Max(4.0f, coarseMap.Width / 16f), coarseKeep);

            // 3. Coarse-to-Fine Multilevel Refinement
            for (int l = coarseIdx - 1; l >= 0; l--)
            {
                var curMap = pyramidMaps[l];
                var curModel = model.Levels[l];
                int curFeatCount = curModel.Features.Count;
                if (curFeatCount == 0) continue;

                float curAngleStep = MathF.Max(angleStepDeg, 0.5f);
                float curAngleStepRad = VisionTransform.DegToRad(curAngleStep);
                float curScaleStep = MathF.Max(scaleStep, 0.02f);
                float curThreshold = minScore * 0.70f;

                var refined = new List<Candidate>(activeCandidates.Count);

                foreach (var cand in activeCandidates)
                {
                    float projX = cand.X * 2.0f;
                    float projY = cand.Y * 2.0f;

                    Candidate best = new Candidate(projX, projY, cand.AngleRad, cand.Scale, 0f);

                    for (float ds = -curScaleStep; ds <= curScaleStep + 1e-4f; ds += curScaleStep)
                    {
                        float testS = cand.Scale + ds;
                        if (testS < scaleMin || testS > scaleMax) continue;

                        for (float da = -curAngleStepRad * 2; da <= curAngleStepRad * 2 + 1e-4f; da += curAngleStepRad)
                        {
                            float testA = cand.AngleRad + da;

                            Avx2ScoreAccumulator.PrecomputeOffsets(
                                curModel.Features,
                                testA,
                                testS,
                                curMap.Width,
                                out int[] offsets,
                                out int[] sectors);

                            for (int dy = -2; dy <= 2; dy++)
                            {
                                int cy = (int)MathF.Round(projY + dy);
                                if (cy < 2 || cy >= curMap.Height - 2) continue;

                                for (int dx = -2; dx <= 2; dx++)
                                {
                                    int cx = (int)MathF.Round(projX + dx);
                                    if (cx < 2 || cx >= curMap.Width - 2) continue;

                                    float score = Avx2ScoreAccumulator.ScoreAtPosition(
                                        curMap.ResponseMaps,
                                        curMap.Width,
                                        curMap.Height,
                                        cx,
                                        cy,
                                        offsets,
                                        sectors,
                                        curFeatCount,
                                        curThreshold,
                                        greediness);

                                    if (score > best.Score)
                                    {
                                        best = new Candidate(cx, cy, testA, testS, score);
                                    }
                                }
                            }
                        }
                    }

                    if (best.Score >= curThreshold)
                    {
                        refined.Add(best);
                    }
                }

                int refineKeep = maxInstances <= 0 ? 500 : Math.Max((int)(maxInstances * 2), 60);
                activeCandidates = NmsFilter(refined, 4.0f, refineKeep);
            }

            // 4. Level 0 Subpixel Refinement & Verification
            var lvl0Map = pyramidMaps[0];
            var lvl0Model = model.Levels[0];
            int lvl0FeatCount = lvl0Model.Features.Count;
            var finalMatches = new List<MatchResult>();

            foreach (var cand in activeCandidates)
            {
                if (cand.Score < minScore * 0.90f) continue;

                float subX = cand.X;
                float subY = cand.Y;
                float subA = cand.AngleRad;
                float subS = cand.Scale;
                float finalScore = cand.Score;

                if (subpixel && lvl0FeatCount > 0)
                {
                    int ix = (int)MathF.Round(subX);
                    int iy = (int)MathF.Round(subY);

                    Avx2ScoreAccumulator.PrecomputeOffsets(
                        lvl0Model.Features,
                        subA,
                        subS,
                        lvl0Map.Width,
                        out int[] offsets,
                        out int[] sectors);

                    float Eval(int x, int y) => Avx2ScoreAccumulator.ScoreAtPosition(
                        lvl0Map.ResponseMaps, lvl0Map.Width, lvl0Map.Height, x, y, offsets, sectors, lvl0FeatCount, 0f, 0f);

                    // Ensure (ix, iy) is the true local discrete maximum before parabolic Taylor fit
                    int bestIx = ix;
                    int bestIy = iy;
                    float bestS00 = Eval(ix, iy);

                    for (int oy = -1; oy <= 1; oy++)
                    {
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            if (ox == 0 && oy == 0) continue;
                            float s = Eval(ix + ox, iy + oy);
                            if (s > bestS00)
                            {
                                bestS00 = s;
                                bestIx = ix + ox;
                                bestIy = iy + oy;
                            }
                        }
                    }

                    ix = bestIx;
                    iy = bestIy;

                    float s00   = bestS00;
                    float sm1_0 = Eval(ix - 1, iy);
                    float sp1_0 = Eval(ix + 1, iy);
                    float s0_m1 = Eval(ix, iy - 1);
                    float s0_p1 = Eval(ix, iy + 1);
                    float sm1_m1 = Eval(ix - 1, iy - 1);
                    float sp1_m1 = Eval(ix + 1, iy - 1);
                    float sm1_p1 = Eval(ix - 1, iy + 1);
                    float sp1_p1 = Eval(ix + 1, iy + 1);

                    if (SubpixelRefiner.Refine2D(s00, sm1_0, sp1_0, s0_m1, s0_p1, sm1_m1, sp1_m1, sm1_p1, sp1_p1,
                        out float dx, out float dy, out float refined))
                    {
                        subX = ix + dx;
                        subY = iy + dy;
                        finalScore = MathF.Max(s00, refined);
                    }

                    // 1D angle subpixel refinement
                    float fineAngleStep = VisionTransform.DegToRad(Math.Min(angleStepDeg, 0.5f));
                    Avx2ScoreAccumulator.PrecomputeOffsets(lvl0Model.Features, subA - fineAngleStep, subS, lvl0Map.Width, out var offMinus, out var secMinus);
                    Avx2ScoreAccumulator.PrecomputeOffsets(lvl0Model.Features, subA + fineAngleStep, subS, lvl0Map.Width, out var offPlus, out var secPlus);

                    float sMinus = Avx2ScoreAccumulator.ScoreAtPosition(lvl0Map.ResponseMaps, lvl0Map.Width, lvl0Map.Height, ix, iy, offMinus, secMinus, lvl0FeatCount, 0f, 0f);
                    float sPlus  = Avx2ScoreAccumulator.ScoreAtPosition(lvl0Map.ResponseMaps, lvl0Map.Width, lvl0Map.Height, ix, iy, offPlus, secPlus, lvl0FeatCount, 0f, 0f);

                    float dAngle = SubpixelRefiner.RefineAngle1D(sMinus, s00, sPlus, fineAngleStep);
                    subA += dAngle;
                }

                if (finalScore >= minScore)
                {
                    float coverage = Math.Clamp(finalScore / 0.95f, 0f, 1.0f);

                    var match = new MatchResult
                    {
                        X = subX,
                        Y = subY,
                        AngleDeg = VisionTransform.RadToDeg(subA),
                        Scale = subS,
                        Score = finalScore,
                        Coverage = coverage
                    };

                    float w = model.TemplateWidth * subS;
                    float h = model.TemplateHeight * subS;
                    match.BoundingBox = new RotatedRect(new Point2f(subX, subY), new Size2f(w, h), match.AngleDeg);

                    // Transform model contours for visual overlay
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

            // 5. Final Overlap NMS
            return FilterMatchesByOverlap(finalMatches, maxInstances, maxOverlap);
        }
        finally
        {
            for (int i = 0; i < pyramidMaps.Length; i++)
                pyramidMaps[i]?.Dispose();
        }
    }

    private static List<Candidate> NmsFilter(List<Candidate> candidates, float distanceThreshold, int maxKeep)
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

    private static List<MatchResult> FilterMatchesByOverlap(
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
