using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace TunVision.Core.ShapeMatching;

public enum ContourExtractionMode
{
    Auto,
    Silhouette, // Clean closed boundaries (outer + inner holes) for high-contrast objects (retainers, rings, nuts)
    Edges,      // Filtered long continuous structural curves for grayscale/machined parts
    StegerSubpixel // Steger Facet 3x3 differential geometry subpixel extractor (MWWZ standard)
}

public readonly record struct ContrastEstimationResult(
    int MinContrast,
    int ContrastLow,
    int ContrastHigh,
    int MinComponentSize,
    int NumLevels,
    float NoiseStd);

public static class ShapeTrainer
{
    public static ContrastEstimationResult AutoEstimateContrast(Mat image, Mat? mask = null)
    {
        if (image.Empty())
        {
            return new ContrastEstimationResult(9, 39, 52, 16, 4, 1.5f);
        }

        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        int w = gray.Width;
        int h = gray.Height;

        // 1. Estimate physical sensor noise level via Immerkaer 3x3 Laplacian operator
        // Kernel K = [ 1 -2  1; -2  4 -2;  1 -2  1 ]
        float noiseSum = 0f;
        long noiseCount = 0;

        unsafe
        {
            byte* pGray = (byte*)gray.Data.ToPointer();
            int step = (int)gray.Step();
            byte* pMask = mask != null ? (byte*)mask.Data.ToPointer() : null;
            int stepMask = mask != null ? (int)mask.Step() : 0;

            for (int y = 1; y < h - 1; y++)
            {
                byte* rPrev = pGray + (y - 1) * step;
                byte* rCurr = pGray + y * step;
                byte* rNext = pGray + (y + 1) * step;
                byte* rMask = pMask != null ? pMask + y * stepMask : null;

                for (int x = 1; x < w - 1; x++)
                {
                    if (rMask != null && rMask[x] == 0) continue;

                    int lap = (rPrev[x - 1] + rPrev[x + 1] + rNext[x - 1] + rNext[x + 1])
                            - 2 * (rPrev[x] + rCurr[x - 1] + rCurr[x + 1] + rNext[x])
                            + 4 * rCurr[x];

                    noiseSum += Math.Abs(lap);
                    noiseCount++;
                }
            }
        }

        float sigmaNoise = 1.0f;
        if (noiseCount > 100)
        {
            // Immerkaer formula: sigma = sqrt(pi / 2) * (1 / (6 * N)) * sum(|I * K|)
            sigmaNoise = MathF.Sqrt(MathF.PI * 0.5f) * (noiseSum / (6.0f * noiseCount));
        }

        // 2. Sobel derivative CV_32F with 0.25 scale (MWWZ industrial standard)
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0.75);

        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(blurred, dx, MatType.CV_32F, 1, 0, 3, scale: 0.25);
        Cv2.Sobel(blurred, dy, MatType.CV_32F, 0, 1, 3, scale: 0.25);

        // 3. Gradient magnitude histogram (256 bins [0..255])
        int[] hist = new int[256];
        long totalGradCount = 0;

        unsafe
        {
            float* pDx = (float*)dx.Data.ToPointer();
            float* pDy = (float*)dy.Data.ToPointer();
            int stepGrad = (int)(dx.Step() / sizeof(float));
            byte* pMask = mask != null ? (byte*)mask.Data.ToPointer() : null;
            int stepMask = mask != null ? (int)mask.Step() : 0;

            for (int y = 1; y < h - 1; y++)
            {
                float* rDx = pDx + y * stepGrad;
                float* rDy = pDy + y * stepGrad;
                byte* rMask = pMask != null ? pMask + y * stepMask : null;

                for (int x = 1; x < w - 1; x++)
                {
                    if (rMask != null && rMask[x] == 0) continue;

                    float gx = rDx[x];
                    float gy = rDy[x];
                    float mag = MathF.Sqrt(gx * gx + gy * gy);

                    int bin = Math.Clamp((int)mag, 0, 255);
                    hist[bin]++;
                    totalGradCount++;
                }
            }
        }

        if (totalGradCount == 0)
        {
            return new ContrastEstimationResult(9, 39, 52, 16, 4, sigmaNoise);
        }

        float GetPercentile(float p)
        {
            long target = (long)(totalGradCount * p);
            long accum = 0;
            for (int b = 0; b < 256; b++)
            {
                accum += hist[b];
                if (accum >= target) return b;
            }
            return 255;
        }

        float p25 = GetPercentile(0.25f);
        float p70 = GetPercentile(0.70f);
        float p85 = GetPercentile(0.85f);

        // MinContrast: 25th percentile or 3 * sigmaNoise, clamped [5, 30]
        int minContrast = Math.Clamp(Math.Max((int)MathF.Round(3.0f * sigmaNoise), (int)MathF.Round(p25)), 5, 30);

        // ContrastLow: 70th percentile, at least minContrast + 5
        int contrastLow = Math.Clamp(Math.Max((int)MathF.Round(p70), minContrast + 5), 10, 120);

        // ContrastHigh: 85th percentile, at least contrastLow + 10
        int contrastHigh = Math.Clamp(Math.Max((int)MathF.Round(p85), contrastLow + 10), 20, 200);

        // MinComponentSize: 16 px (5 px if small template min(W, H) < 64)
        float minDim = MathF.Min(w, h);
        int minComponentSize = minDim < 64 ? 5 : 16;

        // NumLevels: round(log2(min(W, H) / 16)) + 1
        int numLevels = Math.Clamp((int)MathF.Round(MathF.Log2(minDim / 16.0f)) + 1, 1, 6);

        return new ContrastEstimationResult(
            MinContrast: minContrast,
            ContrastLow: contrastLow,
            ContrastHigh: contrastHigh,
            MinComponentSize: minComponentSize,
            NumLevels: numLevels,
            NoiseStd: sigmaNoise);
    }

    public static ShapeModel Train(
        Mat image,
        Mat? mask = null,
        int numLevels = 4,
        float angleStartDeg = -180f,
        float angleExtentDeg = 360f,
        float angleStepDeg = 1.0f,
        float scaleMin = 0.9f,
        float scaleMax = 1.1f,
        float scaleStep = 0.05f,
        int contrastLow = 35,
        int contrastHigh = 75,
        int minLength = 60,
        int maxFeatures = 400,
        ContourExtractionMode contourMode = ContourExtractionMode.StegerSubpixel,
        int minContrast = 9,
        int? minComponentSize = null)
    {
        if (image == null || image.Empty())
        {
            return new ShapeModel();
        }

        contrastLow = Math.Clamp(contrastLow, 1, 250);
        contrastHigh = Math.Clamp(Math.Max(contrastLow + 1, contrastHigh), 2, 255);
        minContrast = Math.Clamp(minContrast, 1, 250);
        minLength = Math.Clamp(minLength, 1, 5000);
        maxFeatures = Math.Clamp(maxFeatures, 10, 5000);

        int maxSafeLevels = Math.Clamp((int)MathF.Floor(MathF.Log2(Math.Max(8, Math.Min(image.Width, image.Height)) / 8.0f)) + 1, 1, 6);
        if (numLevels <= 0)
        {
            float minDim = MathF.Min(image.Width, image.Height);
            numLevels = Math.Clamp((int)MathF.Round(MathF.Log2(minDim / 16.0f)) + 1, 1, maxSafeLevels);
        }
        else
        {
            numLevels = Math.Clamp(numLevels, 1, maxSafeLevels);
        }

        if (contourMode == ContourExtractionMode.StegerSubpixel || contourMode == ContourExtractionMode.Auto)
        {
            return TrainSteger(image, mask, numLevels, angleStartDeg, angleExtentDeg, angleStepDeg,
                scaleMin, scaleMax, scaleStep, contrastLow, contrastHigh, minLength, maxFeatures,
                minContrast, minComponentSize);
        }

        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat curImg = gray.Clone();
        Mat? curMask = mask?.Clone();

        var model = new ShapeModel
        {
            NumLevels = numLevels,
            AngleStartDeg = angleStartDeg,
            AngleExtentDeg = angleExtentDeg,
            AngleStepDeg = angleStepDeg,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            ScaleStep = scaleStep,
            ContrastLow = contrastLow,
            ContrastHigh = contrastHigh,
            MinLength = minLength,
            TemplateWidth = gray.Width,
            TemplateHeight = gray.Height
        };

        // 1. Extract Clean Structural Contours (eliminates texture noise and scribbles)
        var cleanContours = ExtractStructuralContours(gray, mask, contrastLow, contrastHigh, minLength, contourMode);

        // Determine true geometric center (centroid of object contours, NOT arbitrary image center)
        float sumX = 0f, sumY = 0f;
        int totalPts = 0;
        foreach (var poly in cleanContours)
        {
            foreach (var pt in poly)
            {
                sumX += pt.X;
                sumY += pt.Y;
                totalPts++;
            }
        }

        if (totalPts > 0)
        {
            model.OriginX = sumX / totalPts;
            model.OriginY = sumY / totalPts;
        }
        else if (mask != null)
        {
            var moments = Cv2.Moments(mask, true);
            if (moments.M00 > 0)
            {
                model.OriginX = (float)(moments.M10 / moments.M00);
                model.OriginY = (float)(moments.M01 / moments.M00);
            }
            else
            {
                model.OriginX = gray.Width / 2f;
                model.OriginY = gray.Height / 2f;
            }
        }
        else
        {
            model.OriginX = gray.Width / 2f;
            model.OriginY = gray.Height / 2f;
        }

        // Store clean contours for visualization (relative to Origin)
        float minRelX = float.MaxValue, maxRelX = float.MinValue;
        float minRelY = float.MaxValue, maxRelY = float.MinValue;
        foreach (var poly in cleanContours)
        {
            if (poly.Count >= 2)
            {
                var relPoly = new List<Point2f>(poly.Count);
                for (int i = 0; i < poly.Count; i++)
                {
                    float rx = poly[i].X - model.OriginX;
                    float ry = poly[i].Y - model.OriginY;
                    if (rx < minRelX) minRelX = rx;
                    if (rx > maxRelX) maxRelX = rx;
                    if (ry < minRelY) minRelY = ry;
                    if (ry > maxRelY) maxRelY = ry;
                    relPoly.Add(new Point2f(rx, ry));
                }
                model.ModelContours.Add(relPoly);
            }
        }

        if (mask != null && maxRelX > minRelX && maxRelY > minRelY)
        {
            model.TemplateWidth = Math.Clamp((int)MathF.Ceiling(maxRelX - minRelX + 12f), 16, gray.Width);
            model.TemplateHeight = Math.Clamp((int)MathF.Ceiling(maxRelY - minRelY + 12f), 16, gray.Height);
        }

        // 2. Build pyramid levels with features sampled STRICTLY along clean contours
        float originScale = 1.0f;
        int targetFeatures = maxFeatures;

        for (int l = 0; l < numLevels; l++)
        {
            float curOriginX = model.OriginX * originScale;
            float curOriginY = model.OriginY * originScale;

            var levelData = ExtractFeaturesAlongContours(
                curImg, curMask, l, curOriginX, curOriginY,
                model.ModelContours, originScale, contrastLow, targetFeatures);

            model.Levels.Add(levelData);

            if (l < numLevels - 1)
            {
                if (curImg.Width < 8 || curImg.Height < 8)
                    break;

                var nextImg = new Mat();
                Cv2.PyrDown(curImg, nextImg);
                curImg.Dispose();
                curImg = nextImg;

                if (curMask != null)
                {
                    var nextMask = new Mat();
                    Cv2.PyrDown(curMask, nextMask);
                    Cv2.Threshold(nextMask, nextMask, 127, 255, ThresholdTypes.Binary);
                    curMask.Dispose();
                    curMask = nextMask;
                }

                originScale *= 0.5f;
                targetFeatures = Math.Max(30, targetFeatures / 2);
            }
        }

        model.NumLevels = model.Levels.Count;
        curImg.Dispose();
        curMask?.Dispose();

        model.CaptureInitialSnapshot();
        return model;
    }

    /// <summary>
    /// Extracts clean, smooth object boundary contours, filtering out surface texture, machining grain, and noise.
    /// </summary>
    public static List<List<Point2f>> ExtractStructuralContours(
        Mat gray,
        Mat? mask,
        int contrastLow,
        int contrastHigh,
        int minLength,
        ContourExtractionMode mode)
    {
        var result = new List<List<Point2f>>();

        // Pre-filter with Gaussian blur to suppress metal surface texture/grain
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 1.5);

        bool trySilhouette = mode == ContourExtractionMode.Silhouette || mode == ContourExtractionMode.Auto;

        if (trySilhouette)
        {
            // Test Otsu silhouette extraction for high-contrast objects (e.g. retainers, rings, nuts, chips)
            using var binMat = new Mat();
            Cv2.Threshold(blurred, binMat, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
            if (mask != null)
            {
                Cv2.BitwiseAnd(binMat, mask, binMat);
            }

            Cv2.FindContours(binMat, out var otsuContours, out _, RetrievalModes.Tree, ContourApproximationModes.ApproxNone);

            var validOtsu = new List<List<Point2f>>();
            foreach (var c in otsuContours)
            {
                double arcLen = Cv2.ArcLength(c, true);
                double area = Cv2.ContourArea(c);

                // Filter out tiny noise and border frames
                if (arcLen >= minLength && area >= 120 && area < (gray.Width * gray.Height * 0.95))
                {
                    var smooth = Cv2.ApproxPolyDP(c, epsilon: 1.2, closed: true);
                    if (smooth.Length >= 3)
                    {
                        var poly = smooth.Select(p => new Point2f(p.X, p.Y)).ToList();
                        validOtsu.Add(poly);
                    }
                }
            }

            if (mode == ContourExtractionMode.Silhouette || (mode == ContourExtractionMode.Auto && validOtsu.Count >= 1 && validOtsu.Count <= 25))
            {
                return validOtsu;
            }
        }

        // Canny edge extraction with strict continuous length filtering
        using (var canny = new Mat())
        {
            Cv2.Canny(blurred, canny, contrastLow, contrastHigh);
            if (mask != null)
            {
                Cv2.BitwiseAnd(canny, mask, canny);
            }

            Cv2.FindContours(canny, out var cannyContours, out _, RetrievalModes.List, ContourApproximationModes.ApproxNone);

            foreach (var c in cannyContours)
            {
                double arcLen = Cv2.ArcLength(c, false);
                // Real physical boundaries are long continuous curves; texture noise is short (< minLength)
                if (arcLen >= minLength)
                {
                    // Simplify polygon to remove single-pixel staircase zigzags
                    var smooth = Cv2.ApproxPolyDP(c, epsilon: 1.2, closed: false);
                    if (smooth.Length >= 2)
                    {
                        var poly = smooth.Select(p => new Point2f(p.X, p.Y)).ToList();
                        result.Add(poly);
                    }
                }
            }
        }

        // If strict filtering returned nothing, relax threshold once
        if (result.Count == 0)
        {
            using var cannyRelaxed = new Mat();
            Cv2.Canny(blurred, cannyRelaxed, Math.Max(5, contrastLow / 2), Math.Max(15, contrastHigh / 2));
            if (mask != null)
            {
                Cv2.BitwiseAnd(cannyRelaxed, mask, cannyRelaxed);
            }
            Cv2.FindContours(cannyRelaxed, out var fallbackCnts, out _, RetrievalModes.List, ContourApproximationModes.ApproxNone);
            foreach (var c in fallbackCnts)
            {
                if (Cv2.ArcLength(c, false) >= Math.Max(20, minLength / 2))
                {
                    var smooth = Cv2.ApproxPolyDP(c, epsilon: 1.2, closed: false);
                    if (smooth.Length >= 2)
                    {
                        result.Add(smooth.Select(p => new Point2f(p.X, p.Y)).ToList());
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Samples shape features STRICTLY along the clean structural contours.
    /// This guarantees zero features on surface texture, flat areas, or background noise.
    /// </summary>
    private static unsafe PyramidLevelData ExtractFeaturesAlongContours(
        Mat img,
        Mat? mask,
        int level,
        float originX,
        float originY,
        List<List<Point2f>> baseContours,
        float levelScale,
        int contrastLow,
        int maxFeatures)
    {
        var levelData = new PyramidLevelData
        {
            Level = level,
            Width = img.Width,
            Height = img.Height
        };

        using var blurred = new Mat();
        Cv2.GaussianBlur(img, blurred, new Size(3, 3), 0.5);

        using var dxMat = new Mat();
        using var dyMat = new Mat();
        Cv2.Sobel(blurred, dxMat, MatType.CV_16S, 1, 0, 3);
        Cv2.Sobel(blurred, dyMat, MatType.CV_16S, 0, 1, 3);

        int w = img.Width;
        int h = img.Height;

        short* pDx = (short*)dxMat.Data.ToPointer();
        short* pDy = (short*)dyMat.Data.ToPointer();
        int step = (int)(dxMat.Step() / sizeof(short));

        var candidates = new List<ShapeFeature>(maxFeatures * 2);

        // Sample points along each structural contour with step spacing
        foreach (var poly in baseContours)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                // Rescale contour points for current pyramid level
                float px = (poly[i].X * levelScale) + originX;
                float py = (poly[i].Y * levelScale) + originY;

                int ix = (int)MathF.Round(px);
                int iy = (int)MathF.Round(py);

                if (ix < 1 || ix >= w - 1 || iy < 1 || iy >= h - 1)
                    continue;

                short dx = pDx[iy * step + ix];
                short dy = pDy[iy * step + ix];
                float mag = MathF.Sqrt(dx * dx + dy * dy);

                if (mag >= Math.Max(10, contrastLow / 2))
                {
                    float invMag = 1.0f / mag;
                    float udx = dx * invMag;
                    float udy = dy * invMag;
                    float angle = MathF.Atan2(dy, dx);

                    candidates.Add(new ShapeFeature(
                        px - originX,
                        py - originY,
                        udx,
                        udy,
                        angle,
                        mag));
                }
            }
        }

        if (candidates.Count == 0)
        {
            // Fallback: if contours had no valid points at this level, sample points with gradient
            return FallbackExtractLevelFeatures(img, mask, level, originX, originY, contrastLow, maxFeatures);
        }

        // Subsample features uniformly along contours so they don't bunch up
        float minDistance = MathF.Max(2.0f, MathF.Sqrt((float)(w * h) / (maxFeatures * 3)));
        float minDistSq = minDistance * minDistance;

        var selected = new List<ShapeFeature>(maxFeatures);
        foreach (var cand in candidates)
        {
            bool tooClose = false;
            for (int i = 0; i < selected.Count; i++)
            {
                float dX = cand.X - selected[i].X;
                float dY = cand.Y - selected[i].Y;
                if (dX * dX + dY * dY < minDistSq)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                selected.Add(cand);
                if (selected.Count >= maxFeatures)
                    break;
            }
        }

        // If too few due to distance threshold, fill up to maxFeatures
        if (selected.Count < Math.Min(30, maxFeatures / 2))
        {
            foreach (var cand in candidates)
            {
                if (!selected.Contains(cand))
                {
                    selected.Add(cand);
                    if (selected.Count >= maxFeatures)
                        break;
                }
            }
        }

        levelData.Features = selected;
        return levelData;
    }

    private static unsafe PyramidLevelData FallbackExtractLevelFeatures(
        Mat img, Mat? mask, int level, float originX, float originY,
        int contrastLow, int maxFeatures)
    {
        var levelData = new PyramidLevelData
        {
            Level = level,
            Width = img.Width,
            Height = img.Height
        };

        using var blurred = new Mat();
        Cv2.GaussianBlur(img, blurred, new Size(3, 3), 0.5);
        using var dxMat = new Mat();
        using var dyMat = new Mat();
        Cv2.Sobel(blurred, dxMat, MatType.CV_16S, 1, 0, 3);
        Cv2.Sobel(blurred, dyMat, MatType.CV_16S, 0, 1, 3);

        int w = img.Width;
        int h = img.Height;
        short* pDx = (short*)dxMat.Data.ToPointer();
        short* pDy = (short*)dyMat.Data.ToPointer();
        int step = (int)(dxMat.Step() / sizeof(short));

        var candidates = new List<ShapeFeature>();
        for (int y = 2; y < h - 2; y += 2)
        {
            for (int x = 2; x < w - 2; x += 2)
            {
                if (mask != null && x < mask.Width && y < mask.Height && mask.At<byte>(y, x) == 0)
                    continue;
                short dx = pDx[y * step + x];
                short dy = pDy[y * step + x];
                float mag = MathF.Sqrt(dx * dx + dy * dy);
                if (mag >= contrastLow)
                {
                    float inv = 1.0f / mag;
                    candidates.Add(new ShapeFeature(x - originX, y - originY, dx * inv, dy * inv, MathF.Atan2(dy, dx), mag));
                }
            }
        }

        candidates.Sort((a, b) => b.Magnitude.CompareTo(a.Magnitude));
        levelData.Features = candidates.Take(maxFeatures).ToList();
        return levelData;
    }

    private static ShapeModel TrainSteger(
        Mat image,
        Mat? mask,
        int numLevels,
        float angleStartDeg,
        float angleExtentDeg,
        float angleStepDeg,
        float scaleMin,
        float scaleMax,
        float scaleStep,
        int contrastLow,
        int contrastHigh,
        int minLength,
        int maxFeatures,
        int minContrast = 9,
        int? minComponentSize = null)
    {
        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);

        contrastLow = Math.Clamp(contrastLow, 1, 250);
        contrastHigh = Math.Clamp(Math.Max(contrastLow + 1, contrastHigh), 2, 255);
        minContrast = Math.Clamp(minContrast, 1, 250);
        int minComp = Math.Clamp(minComponentSize ?? (minLength > 0 ? minLength : 16), 1, 5000);
        maxFeatures = Math.Clamp(maxFeatures, 10, 5000);

        int maxSafeLevels = Math.Clamp((int)MathF.Floor(MathF.Log2(Math.Max(8, Math.Min(gray.Width, gray.Height)) / 8.0f)) + 1, 1, 6);
        if (numLevels <= 0)
        {
            float minDim = MathF.Min(gray.Width, gray.Height);
            numLevels = Math.Clamp((int)MathF.Round(MathF.Log2(minDim / 16.0f)) + 1, 1, maxSafeLevels);
        }
        else
        {
            numLevels = Math.Clamp(numLevels, 1, maxSafeLevels);
        }

        var model = new ShapeModel
        {
            NumLevels = numLevels,
            AngleStartDeg = angleStartDeg,
            AngleExtentDeg = angleExtentDeg,
            AngleStepDeg = angleStepDeg,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            ScaleStep = scaleStep,
            ContrastLow = contrastLow,
            ContrastHigh = contrastHigh,
            MinContrast = minContrast,
            MinComponentSize = minComp,
            MinLength = minComp,
            TemplateWidth = gray.Width,
            TemplateHeight = gray.Height
        };

        // 1. Extract Level 0 raw subpixel contours first to determine true geometric centroid & dimensions of the object
        var lvl0RawContours = Geometry.TopologicalContourExtractor.ExtractContours(
            gray, mask, contrastLow, contrastHigh, minComp, minContrast);

        if (lvl0RawContours.Count == 0 && contrastLow > minContrast)
        {
            lvl0RawContours = Geometry.TopologicalContourExtractor.ExtractContours(
                gray, mask, minContrast, Math.Max(minContrast + 2, contrastHigh / 2), Math.Max(2, minComp / 2), minContrast);
        }

        // Determine true geometric center (centroid of object contours, NOT arbitrary image center)
        float sumX = 0f, sumY = 0f;
        int totalPts = 0;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var c in lvl0RawContours)
        {
            foreach (var pt in c)
            {
                sumX += pt.X;
                sumY += pt.Y;
                totalPts++;
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }
        }

        if (totalPts > 0)
        {
            model.OriginX = sumX / totalPts;
            model.OriginY = sumY / totalPts;
            model.TemplateWidth = Math.Clamp((int)MathF.Ceiling(maxX - minX + 8f), 16, gray.Width);
            model.TemplateHeight = Math.Clamp((int)MathF.Ceiling(maxY - minY + 8f), 16, gray.Height);
        }
        else if (mask != null)
        {
            var moments = Cv2.Moments(mask, true);
            if (moments.M00 > 0)
            {
                model.OriginX = (float)(moments.M10 / moments.M00);
                model.OriginY = (float)(moments.M01 / moments.M00);
            }
            else
            {
                model.OriginX = gray.Width * 0.5f;
                model.OriginY = gray.Height * 0.5f;
            }
        }
        else
        {
            model.OriginX = gray.Width * 0.5f;
            model.OriginY = gray.Height * 0.5f;
        }

        // Build pyramid levels with dynamic contrast halving
        Mat curImg = gray.Clone();
        Mat? curMask = mask?.Clone();
        int targetFeatures = maxFeatures;

        for (int l = 0; l < numLevels; l++)
        {
            int curLow = Math.Max(minContrast, contrastLow >> l);
            int curHigh = Math.Max(curLow + 2, contrastHigh >> l);
            int curMinComp = Math.Max(4, minComp >> l);

            var rawContours = (l == 0)
                ? lvl0RawContours
                : Geometry.TopologicalContourExtractor.ExtractContours(curImg, curMask, curLow, curHigh, curMinComp, minContrast);

            // Fallback: if no contours found on coarse level, relax contrast threshold
            if (l > 0 && rawContours.Count == 0 && curLow > minContrast)
            {
                rawContours = Geometry.TopologicalContourExtractor.ExtractContours(
                    curImg, curMask, minContrast, Math.Max(minContrast + 2, curHigh / 2), Math.Max(2, curMinComp / 2), minContrast);
            }

            var lvlData = new PyramidLevelData
            {
                Level = l,
                Width = curImg.Width,
                Height = curImg.Height
            };

            float curOriginX = model.OriginX / (1 << l);
            float curOriginY = model.OriginY / (1 << l);

            foreach (var c in rawContours)
            {
                var resampled = Geometry.ArcLengthSampler.ResampleContour(c, step: 1.0f);

                if (l == 0)
                {
                    // Store discrete contours for WPF UI visualization (preserves separate components without criss-cross lines)
                    model.ModelContours.Add(resampled.Select(p =>
                        new Point2f(p.X - model.OriginX, p.Y - model.OriginY)).ToList());
                }

                foreach (var pt in resampled)
                {
                    float angle = MathF.Atan2(pt.Ny, pt.Nx);
                    lvlData.Features.Add(new ShapeFeature(
                        pt.X - curOriginX,
                        pt.Y - curOriginY,
                        pt.Nx,
                        pt.Ny,
                        angle,
                        pt.Response));
                }
            }

            // Downsample features uniformly if count exceeds targetFeatures
            if (lvlData.Features.Count > targetFeatures)
            {
                float stepF = (float)lvlData.Features.Count / targetFeatures;
                var downsampled = new List<ShapeFeature>(targetFeatures);
                for (int i = 0; i < targetFeatures; i++)
                {
                    downsampled.Add(lvlData.Features[(int)(i * stepF)]);
                }
                lvlData.Features = downsampled;
            }

            // Fallback for coarse levels if empty: downsample from previous level
            if (lvlData.Features.Count == 0 && l > 0 && model.Levels.Count > 0)
            {
                foreach (var prevFeat in model.Levels[l - 1].Features)
                {
                    lvlData.Features.Add(new ShapeFeature(
                        prevFeat.X * 0.5f,
                        prevFeat.Y * 0.5f,
                        prevFeat.Dx,
                        prevFeat.Dy,
                        prevFeat.Angle,
                        prevFeat.Magnitude));
                }
            }

            model.Levels.Add(lvlData);

            if (l < numLevels - 1)
            {
                if (curImg.Width < 8 || curImg.Height < 8)
                    break;

                var nextImg = new Mat();
                Cv2.PyrDown(curImg, nextImg);
                curImg.Dispose();
                curImg = nextImg;

                if (curMask != null)
                {
                    var nextMask = new Mat();
                    Cv2.PyrDown(curMask, nextMask);
                    Cv2.Threshold(nextMask, nextMask, 127, 255, ThresholdTypes.Binary);
                    curMask.Dispose();
                    curMask = nextMask;
                }

                targetFeatures = Math.Max(30, targetFeatures / 2);
            }
        }

        model.NumLevels = model.Levels.Count;
        curImg.Dispose();
        curMask?.Dispose();

        model.CaptureInitialSnapshot();
        return model;
    }
}
