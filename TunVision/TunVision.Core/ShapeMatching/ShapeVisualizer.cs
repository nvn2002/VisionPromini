using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;

namespace TunVision.Core.ShapeMatching;

/// <summary>
/// High-performance subpixel visualizer for shape model contours and match overlays.
/// Follows industrial standard (MWWZ / Halcon) subpixel rendering:
/// - Tangent segments with length L = 1.2px (half-length 0.6px) along T = (-Ny, Nx)
/// - Subpixel anti-aliased fixed-point rendering with shift: 4
/// - Green (#00FF00) for Inliers / Active template features
/// - Red (#FF0000) for Outliers / Masked/Excluded features
/// - Magenta (#FF00FF) for Origin crosshairs
/// </summary>
public static class ShapeVisualizer
{
    // Industrial Standard BGR Colors:
    // Green (#00FF00 = B:0, G:255, R:0)
    public static readonly Scalar ColorInlierGreen    = new(0, 255, 0);
    // Red (#FF0000 = B:0, G:0, R:255)
    public static readonly Scalar ColorOutlierRed     = new(0, 0, 255);
    // Magenta (#FF00FF = B:255, G:0, R:255) - Legacy support
    public static readonly Scalar ColorOriginMagenta  = new(255, 0, 255);
    // Coordinate Axes Colors
    public static readonly Scalar ColorAxisX          = new(0, 0, 255);   // Red (#FF0000) for X-axis
    public static readonly Scalar ColorAxisY          = new(0, 255, 0);   // Green (#00FF00) for Y-axis
    public static readonly Scalar ColorOriginDot      = new(0, 255, 255); // Yellow origin dot
    // Bounding Box Colors
    public static readonly Scalar ColorBoundingBoxPrimary   = new(0, 230, 255); // Cyan-Yellow
    public static readonly Scalar ColorBoundingBoxSecondary = new(0, 165, 255); // Orange
    public static readonly Scalar ColorRoiBorder            = new(0, 165, 255); // Orange

    /// <summary>
    /// R3.1: Draw template contour image (TrainShapeModelNode - "Contour Img"):
    /// - Green subpixel tangent segments (#00FF00) for active model features (shift: 4, AntiAlias, thickness: 1)
    /// - Red subpixel tangent segments (#FF0000) for masked/excluded points
    /// - Object coordinate frame (Red X-axis, Green Y-axis arrow, scaled to object size) at (OriginX, OriginY)
    /// - ROI bounding box if provided
    /// </summary>
    public static void DrawTemplateContourImage(
        Mat overlay,
        ShapeModel model,
        BaseRoi? roi = null,
        float tangentLength = 1.2f)
    {
        if (overlay == null || overlay.Empty() || model == null) return;

        float ox = model.OriginX;
        float oy = model.OriginY;
        float halfLen = tangentLength * 0.5f;
        var lvl0 = model.Levels.Count > 0 ? model.Levels[0] : null;

        // 1. Draw continuous vector contours from ModelContours in vibrant green (#00FF00)
        if (model.ModelContours != null && model.ModelContours.Count > 0)
        {
            foreach (var poly in model.ModelContours)
            {
                if (poly.Count < 2) continue;
                var pts = new Point[poly.Count];
                for (int i = 0; i < poly.Count; i++)
                {
                    pts[i] = new Point(
                        (int)MathF.Round((poly[i].X + ox) * 16f),
                        (int)MathF.Round((poly[i].Y + oy) * 16f));
                }

                // Check if loop is closed (endpoints within 3 pixels)
                float dCloseX = poly[0].X - poly[^1].X;
                float dCloseY = poly[0].Y - poly[^1].Y;
                bool isClosed = (dCloseX * dCloseX + dCloseY * dCloseY) < 9.0f;

                Cv2.Polylines(overlay, new[] { pts }, isClosed, ColorInlierGreen, 2, LineTypes.AntiAlias, shift: 4);
            }
        }
        else
        {
            // Fallback for models without ModelContours (draw from Level 0 Features)
            if (lvl0 != null && lvl0.Features.Count > 0)
            {
                foreach (var f in lvl0.Features)
                {
                    float px = ox + f.X;
                    float py = oy + f.Y;

                    // Unit normal N = (Dx, Dy), unit tangent T = (-Dy, Dx)
                    float tx = -f.Dy;
                    float ty = f.Dx;

                    float x1 = px - halfLen * tx;
                    float y1 = py - halfLen * ty;
                    float x2 = px + halfLen * tx;
                    float y2 = py + halfLen * ty;

                    Cv2.Line(
                        overlay,
                        new Point((int)MathF.Round(x1 * 16f), (int)MathF.Round(y1 * 16f)),
                        new Point((int)MathF.Round(x2 * 16f), (int)MathF.Round(y2 * 16f)),
                        ColorInlierGreen,
                        thickness: 2,
                        lineType: LineTypes.AntiAlias,
                        shift: 4);
                }
            }
        }

        // 2. Draw masked/excluded features as red subpixel tangent segments (#FF0000)
        if (model.MaskedFeatures.Count > 0)
        {
            foreach (var mf in model.MaskedFeatures)
            {
                float px = ox + mf.X;
                float py = oy + mf.Y;

                float tx = -mf.Dy;
                float ty = mf.Dx;

                float x1 = px - halfLen * tx;
                float y1 = py - halfLen * ty;
                float x2 = px + halfLen * tx;
                float y2 = py + halfLen * ty;

                Cv2.Line(
                    overlay,
                    new Point((int)MathF.Round(x1 * 16f), (int)MathF.Round(y1 * 16f)),
                    new Point((int)MathF.Round(x2 * 16f), (int)MathF.Round(y2 * 16f)),
                    ColorOutlierRed,
                    thickness: 1,
                    lineType: LineTypes.AntiAlias,
                    shift: 4);
            }
        }

        // 3. Draw object coordinate frame (Red X-axis, Green Y-axis, proportional to object size)
        float objDim = Math.Max(model.TemplateWidth, model.TemplateHeight);
        if (objDim <= 0 && lvl0 != null && lvl0.Features.Count > 0)
        {
            float minFx = float.MaxValue, maxFx = float.MinValue;
            float minFy = float.MaxValue, maxFy = float.MinValue;
            foreach (var f in lvl0.Features)
            {
                if (f.X < minFx) minFx = f.X;
                if (f.X > maxFx) maxFx = f.X;
                if (f.Y < minFy) minFy = f.Y;
                if (f.Y > maxFy) maxFy = f.Y;
            }
            objDim = Math.Max(maxFx - minFx, maxFy - minFy);
        }
        if (objDim <= 0) objDim = 100f;
        DrawObjectCoordinateFrame(overlay, ox, oy, angleDeg: 0f, objectSize: objDim);

        // 4. Draw ROI box if provided
        if (roi != null)
        {
            var rBox = roi.GetBoundingBox();
            Cv2.Rectangle(overlay, rBox, ColorRoiBorder, 2);
        }
    }

    /// <summary>
    /// Draws the object coordinate frame (X/Y orientation axes) at (cx, cy) with orientation angle:
    /// - Red arrow (#FF0000) for X-axis (direction: angleDeg)
    /// - Green arrow (#00FF00) for Y-axis (direction: angleDeg + 90 deg clockwise)
    /// - Arrow length scales proportionally to the object's physical dimension (15-25% of object diameter)
    /// - Small, crisp center origin dot (radius 2px)
    /// - Replaces large obstructive crosshair with clean industrial CAD/robotics coordinate frame.
    /// </summary>
    public static void DrawObjectCoordinateFrame(
        Mat overlay,
        float cx,
        float cy,
        float angleDeg = 0f,
        float objectSize = 100f)
    {
        if (overlay == null || overlay.Empty()) return;

        // Proportional arrow length: ~20% of object size, clamped between [12px, 45px]
        float arrowLen = Math.Clamp(objectSize * 0.20f, 12f, 45f);
        float headLen = Math.Clamp(arrowLen * 0.35f, 4f, 12f);
        float headWidth = headLen * 0.5f;

        float radX = angleDeg * (MathF.PI / 180f);
        float cosX = MathF.Cos(radX);
        float sinX = MathF.Sin(radX);

        // X-axis unit vector = (cosX, sinX)
        // Y-axis unit vector (90 deg clockwise in image coordinates) = (-sinX, cosX)
        float uxX = cosX, uyX = sinX;
        float uxY = -sinX, uyY = cosX;

        // End points for X and Y axes
        float tipXx = cx + arrowLen * uxX;
        float tipXy = cy + arrowLen * uyX;

        float tipYx = cx + arrowLen * uxY;
        float tipYy = cy + arrowLen * uyY;

        // 1. Draw X-axis arrow (RED #FF0000)
        DrawArrowSubpixel(overlay, cx, cy, tipXx, tipXy, uxX, uyX, headLen, headWidth, ColorAxisX);

        // 2. Draw Y-axis arrow (GREEN #00FF00)
        DrawArrowSubpixel(overlay, cx, cy, tipYx, tipYy, uxY, uyY, headLen, headWidth, ColorAxisY);

        // 3. Draw clean, subtle center origin dot (radius 3px)
        Cv2.Circle(overlay,
            new Point((int)MathF.Round(cx * 16f), (int)MathF.Round(cy * 16f)),
            (int)MathF.Round(3f * 16f),
            ColorOriginDot,
            -1, LineTypes.AntiAlias, shift: 4);
        Cv2.Circle(overlay,
            new Point((int)MathF.Round(cx * 16f), (int)MathF.Round(cy * 16f)),
            (int)MathF.Round(3f * 16f),
            new Scalar(0, 0, 0),
            1, LineTypes.AntiAlias, shift: 4);

        // 4. Labels 'X' and 'Y' at arrow tips
        var ptLabelX = new Point((int)MathF.Round(tipXx + uxX * 5f), (int)MathF.Round(tipXy + uyX * 5f + 3f));
        var ptLabelY = new Point((int)MathF.Round(tipYx + uxY * 5f), (int)MathF.Round(tipYy + uyY * 5f + 3f));
        Cv2.PutText(overlay, "X", ptLabelX, HersheyFonts.HersheySimplex, 0.35, ColorAxisX, 1, LineTypes.AntiAlias);
        Cv2.PutText(overlay, "Y", ptLabelY, HersheyFonts.HersheySimplex, 0.35, ColorAxisY, 1, LineTypes.AntiAlias);
    }

    private static void DrawArrowSubpixel(
        Mat overlay,
        float startX, float startY,
        float endX, float endY,
        float ux, float uy,
        float headLen, float headWidth,
        Scalar color)
    {
        // Main line shaft
        Cv2.Line(overlay,
            new Point((int)MathF.Round(startX * 16f), (int)MathF.Round(startY * 16f)),
            new Point((int)MathF.Round(endX * 16f), (int)MathF.Round(endY * 16f)),
            color, thickness: 2, lineType: LineTypes.AntiAlias, shift: 4);

        // Orthogonal unit vector vx, vy = (-uy, ux)
        float vx = -uy;
        float vy = ux;

        // Base of arrowhead
        float baseX = endX - headLen * ux;
        float baseY = endY - headLen * uy;

        // Head corner 1 & 2
        float c1X = baseX + headWidth * vx;
        float c1Y = baseY + headWidth * vy;
        float c2X = baseX - headWidth * vx;
        float c2Y = baseY - headWidth * vy;

        // Fill triangular arrowhead
        var triangle = new[]
        {
            new Point((int)MathF.Round(endX * 16f), (int)MathF.Round(endY * 16f)),
            new Point((int)MathF.Round(c1X * 16f), (int)MathF.Round(c1Y * 16f)),
            new Point((int)MathF.Round(c2X * 16f), (int)MathF.Round(c2Y * 16f))
        };
        Cv2.FillPoly(overlay, new[] { triangle }, color, LineTypes.AntiAlias, shift: 4);
    }

    /// <summary>
    /// Legacy crosshair drawing - delegates to clean coordinate frame
    /// </summary>
    public static void DrawOriginCrosshair(Mat overlay, float cx, float cy, float armLength = 15f)
    {
        if (overlay == null || overlay.Empty()) return;
        DrawObjectCoordinateFrame(overlay, cx, cy, 0f, armLength * 4f);
    }

    /// <summary>
    /// Classifies each model feature against the scene gradient at match pose (x, y, angleDeg, scale).
    /// Computes scene Sobel gradient (gx, gy) and magnitude M = sqrt(gx^2 + gy^2).
    /// If M >= MinContrast and (d'_i . gradS / M) > 0: classify as INLIER.
    /// If M < MinContrast, or occluded, or negative dot product: classify as OUTLIER.
    /// Populates match.FeaturePoints, match.InlierCount, match.OutlierCount.
    /// Ultra-fast raw pointer Sobel sampling (<0.01 ms).
    /// </summary>
    public static unsafe void ClassifyMatchFeatures(
        MatchResult match,
        ShapeModel model,
        Mat sceneGray,
        float minContrast = 9f)
    {
        if (match == null || model == null || sceneGray == null || sceneGray.Empty()) return;

        var lvl0 = model.Levels.Count > 0 ? model.Levels[0] : null;
        if (lvl0 == null || lvl0.Features.Count == 0) return;

        match.FeaturePoints.Clear();
        int featCount = lvl0.Features.Count;
        if (match.FeaturePoints.Capacity < featCount)
            match.FeaturePoints.Capacity = featCount;

        float rad = match.AngleDeg * (MathF.PI / 180f);
        float cosA = MathF.Cos(rad);
        float sinA = MathF.Sin(rad);
        float scale = match.Scale;

        int w = sceneGray.Width;
        int h = sceneGray.Height;
        float minContrastSq = minContrast * minContrast;

        byte* ptr = (byte*)sceneGray.Data.ToPointer();
        int step = (int)sceneGray.Step();

        int inliers = 0;
        int outliers = 0;

        foreach (var f in lvl0.Features)
        {
            // Transform feature position to scene coordinates
            float px = match.X + (f.X * cosA - f.Y * sinA) * scale;
            float py = match.Y + (f.X * sinA + f.Y * cosA) * scale;

            // Transform unit normal d'
            float dx = f.Dx * cosA - f.Dy * sinA;
            float dy = f.Dx * sinA + f.Dy * cosA;

            // Unit tangent t' = (-dy, dx)
            float tx = -dy;
            float ty = dx;

            int ix = (int)MathF.Round(px);
            int iy = (int)MathF.Round(py);

            bool isInlier = false;
            float dotProduct = 0f;

            if (ix >= 1 && ix < w - 1 && iy >= 1 && iy < h - 1)
            {
                byte* rowPrev = ptr + (iy - 1) * step;
                byte* rowCurr = ptr + iy * step;
                byte* rowNext = ptr + (iy + 1) * step;

                int p00 = rowPrev[ix - 1];
                int p01 = rowPrev[ix];
                int p02 = rowPrev[ix + 1];

                int p10 = rowCurr[ix - 1];
                int p12 = rowCurr[ix + 1];

                int p20 = rowNext[ix - 1];
                int p21 = rowNext[ix];
                int p22 = rowNext[ix + 1];

                int gx = (p02 - p00) + 2 * (p12 - p10) + (p22 - p20);
                int gy = (p20 - p00) + 2 * (p21 - p01) + (p22 - p02);

                float magSq = (float)(gx * gx + gy * gy);
                if (magSq >= minContrastSq)
                {
                    float mag = MathF.Sqrt(magSq);
                    float dot = (dx * gx + dy * gy) / mag;
                    if (dot > 0f)
                    {
                        isInlier = true;
                        dotProduct = dot;
                    }
                }
            }

            if (isInlier) inliers++; else outliers++;

            match.FeaturePoints.Add(new MatchedFeaturePoint
            {
                X = px,
                Y = py,
                TangentX = tx,
                TangentY = ty,
                IsInlier = isInlier,
                DotProduct = dotProduct
            });
        }

        match.InlierCount = inliers;
        match.OutlierCount = outliers;
    }

    /// <summary>
    /// Helper to classify features and immediately draw them onto an overlay Mat.
    /// </summary>
    public static void ClassifyAndDrawFeaturesOnTheFly(
        Mat? overlay,
        MatchResult match,
        ShapeModel model,
        Mat sceneGray,
        float minContrast = 9f,
        float halfLen = 0.6f)
    {
        ClassifyMatchFeatures(match, model, sceneGray, minContrast);

        if (overlay != null && !overlay.Empty() && match.FeaturePoints.Count > 0)
        {
            foreach (var fp in match.FeaturePoints)
            {
                Scalar color = fp.IsInlier ? ColorInlierGreen : ColorOutlierRed;
                float x1 = fp.X - halfLen * fp.TangentX;
                float y1 = fp.Y - halfLen * fp.TangentY;
                float x2 = fp.X + halfLen * fp.TangentX;
                float y2 = fp.Y + halfLen * fp.TangentY;

                Cv2.Line(overlay,
                    new Point((int)MathF.Round(x1 * 16f), (int)MathF.Round(y1 * 16f)),
                    new Point((int)MathF.Round(x2 * 16f), (int)MathF.Round(y2 * 16f)),
                    color, thickness: 1, lineType: LineTypes.AntiAlias, shift: 4);
            }
        }
    }

    /// <summary>
    /// R3.2: Draw match overlay (ShapeMatchNode - "Match Overlay"):
    /// - Green subpixel tangent segments (#00FF00) for Inliers (d'_i . gradS / M > 0)
    /// - Red subpixel tangent segments (#FF0000) for Outliers/Defects (missing edge, occlusion, opposite polarity)
    /// - RotatedRect Bounding Box
    /// - Magenta Origin Crosshair at (match.X, match.Y)
    /// - Score & Inlier badge
    /// </summary>
    public static void DrawMatchOverlay(
        Mat overlay,
        List<MatchResult> matches,
        ShapeModel? model = null,
        Mat? sceneGray = null,
        float minContrast = 9f,
        float tangentLength = 1.2f)
    {
        if (overlay == null || overlay.Empty() || matches == null || matches.Count == 0) return;

        float halfLen = tangentLength * 0.5f;

        for (int idx = 0; idx < matches.Count; idx++)
        {
            var match = matches[idx];
            bool isPrimary = (idx == 0);
            Scalar boxColor = isPrimary ? ColorBoundingBoxPrimary : ColorBoundingBoxSecondary;

            // 1. If not yet classified and sceneGray + model are supplied, classify now
            if (match.FeaturePoints.Count == 0 && model != null && sceneGray != null && !sceneGray.Empty())
            {
                ClassifyMatchFeatures(match, model, sceneGray, minContrast);
            }

            // Ensure TransformedContours are available from model if not pre-populated
            if ((match.TransformedContours == null || match.TransformedContours.Count == 0) && model != null && model.ModelContours.Count > 0)
            {
                match.TransformedContours ??= new List<List<Point2f>>();
                float rad = match.AngleDeg * (MathF.PI / 180f);
                float cosA = MathF.Cos(rad) * match.Scale;
                float sinA = MathF.Sin(rad) * match.Scale;
                for (int cIdx = 0; cIdx < model.ModelContours.Count; cIdx++)
                {
                    var poly = model.ModelContours[cIdx];
                    var tPoly = new List<Point2f>(poly.Count);
                    for (int p = 0; p < poly.Count; p++)
                    {
                        tPoly.Add(new Point2f(
                            match.X + (poly[p].X * cosA - poly[p].Y * sinA),
                            match.Y + (poly[p].X * sinA + poly[p].Y * cosA)));
                    }
                    match.TransformedContours.Add(tPoly);
                }
            }

            // 2. Draw Transformed Continuous Contours as smooth green polylines (#00FF00)
            if (match.TransformedContours != null && match.TransformedContours.Count > 0)
            {
                Scalar contourColor = isPrimary ? ColorInlierGreen : new Scalar(255, 230, 0);
                foreach (var contour in match.TransformedContours)
                {
                    if (contour.Count < 2) continue;
                    var pts = new Point[contour.Count];
                    for (int i = 0; i < contour.Count; i++)
                    {
                        pts[i] = new Point(
                            (int)MathF.Round(contour[i].X * 16f),
                            (int)MathF.Round(contour[i].Y * 16f));
                    }
                    float dCloseX = contour[0].X - contour[^1].X;
                    float dCloseY = contour[0].Y - contour[^1].Y;
                    bool isClosed = (dCloseX * dCloseX + dCloseY * dCloseY) < 9.0f;

                    Cv2.Polylines(overlay, new[] { pts }, isClosed, contourColor, 2, LineTypes.AntiAlias, shift: 4);
                }

                // If there are outlier features (defect / occlusion), overlay them in Red (#FF0000)
                if (match.FeaturePoints.Count > 0 && match.OutlierCount > 0)
                {
                    foreach (var fp in match.FeaturePoints)
                    {
                        if (!fp.IsInlier)
                        {
                            float x1 = fp.X - halfLen * fp.TangentX;
                            float y1 = fp.Y - halfLen * fp.TangentY;
                            float x2 = fp.X + halfLen * fp.TangentX;
                            float y2 = fp.Y + halfLen * fp.TangentY;

                            Cv2.Line(overlay,
                                new Point((int)MathF.Round(x1 * 16f), (int)MathF.Round(y1 * 16f)),
                                new Point((int)MathF.Round(x2 * 16f), (int)MathF.Round(y2 * 16f)),
                                ColorOutlierRed, thickness: 2, lineType: LineTypes.AntiAlias, shift: 4);
                        }
                    }
                }
            }
            else if (match.FeaturePoints.Count > 0)
            {
                // Fallback for matches without TransformedContours
                foreach (var fp in match.FeaturePoints)
                {
                    Scalar color = fp.IsInlier ? ColorInlierGreen : ColorOutlierRed;
                    float x1 = fp.X - halfLen * fp.TangentX;
                    float y1 = fp.Y - halfLen * fp.TangentY;
                    float x2 = fp.X + halfLen * fp.TangentX;
                    float y2 = fp.Y + halfLen * fp.TangentY;

                    Cv2.Line(overlay,
                        new Point((int)MathF.Round(x1 * 16f), (int)MathF.Round(y1 * 16f)),
                        new Point((int)MathF.Round(x2 * 16f), (int)MathF.Round(y2 * 16f)),
                        color, thickness: 2, lineType: LineTypes.AntiAlias, shift: 4);
                }
            }

            // 3. Draw Bounding Box
            var boxPts = match.BoundingBox.Points();
            if (boxPts != null && boxPts.Length == 4)
            {
                var bPts = new Point[4];
                for (int i = 0; i < 4; i++)
                    bPts[i] = new Point((int)Math.Round(boxPts[i].X), (int)Math.Round(boxPts[i].Y));
                Cv2.Polylines(overlay, new[] { bPts }, true, boxColor, 2, LineTypes.AntiAlias);
            }

            // 4. Draw Object Coordinate Frame (Red X-axis, Green Y-axis rotated by match.AngleDeg, scaled to object size)
            float objDim = Math.Max(model?.TemplateWidth ?? 100, model?.TemplateHeight ?? 100) * match.Scale;
            DrawObjectCoordinateFrame(overlay, match.X, match.Y, angleDeg: match.AngleDeg, objectSize: objDim);

            // 5. Draw Badge
            string label = match.FeaturePoints.Count > 0
                ? $"#{idx + 1} {match.Score:F2} (In:{match.InlierRatio:P0})"
                : $"#{idx + 1} {match.Score:F2}";

            DrawBadge(overlay, label, new Point((int)Math.Round(match.X), (int)Math.Round(match.Y - 14)), isPrimary, boxColor);
        }
    }

    private static void DrawBadge(Mat overlay, string text, Point pos, bool isPrimary, Scalar boxColor)
    {
        int baseline = 0;
        var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, 0.45, 1, out baseline);
        var labelPos = new Point((int)MathF.Round(pos.X - textSize.Width * 0.5f), pos.Y);
        if (labelPos.Y < 20) labelPos.Y = pos.Y + 42;
        if (labelPos.X < 8) labelPos.X = 8;
        if (labelPos.X + textSize.Width > overlay.Width - 8) labelPos.X = Math.Max(8, overlay.Width - textSize.Width - 8);

        Cv2.Rectangle(overlay,
            new Point(labelPos.X - 4, labelPos.Y - textSize.Height - 3),
            new Point(labelPos.X + textSize.Width + 4, labelPos.Y + baseline + 3),
            new Scalar(15, 15, 15), -1);
        Cv2.Rectangle(overlay,
            new Point(labelPos.X - 4, labelPos.Y - textSize.Height - 3),
            new Point(labelPos.X + textSize.Width + 4, labelPos.Y + baseline + 3),
            boxColor, 1);
        Cv2.PutText(overlay, text, labelPos, HersheyFonts.HersheySimplex, 0.45,
            isPrimary ? ColorInlierGreen : new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
    }
}
