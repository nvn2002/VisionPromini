using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;

namespace TunVision.Core.Caliper;

public enum CaliperMode
{
    Line,
    SearchLine,
    Circle,
    SearchCorner,
    SearchEllipse
}

public class CaliperTool : VisionToolBase
{
    public CaliperMode Mode { get; set; } = CaliperMode.Line;

    // Line / Rake caliper parameters
    public Point2f LineStart { get; set; } = new Point2f(80, 120);
    public Point2f LineEnd { get; set; } = new Point2f(280, 120);
    public float ProjectionLength { get; set; } = 40f; // Half-length or strip width

    // Second line for SearchCorner
    public Point2f Line2Start { get; set; } = new Point2f(120, 80);
    public Point2f Line2End { get; set; } = new Point2f(120, 280);
    public float Line2ProjectionLength { get; set; } = 40f;

    // Circle / Ellipse caliper parameters
    public Point2f CircleCenter { get; set; } = new Point2f(200, 200);
    public float CircleRadius { get; set; } = 80f;
    public float EllipseRadiusB { get; set; } = 50f;
    public float EllipseAngleDeg { get; set; } = 0f;
    public float SearchRange { get; set; } = 25f;
    public int NumSpokes { get; set; } = 36;
    public CircleScanDirection ScanDirection { get; set; } = CircleScanDirection.Outward;

    // Edge & RANSAC Fit parameters (matching mwwz measure_pos_d & do_fit2)
    public float GaussianSigma { get; set; } = 1.0f;
    public float Threshold { get; set; } = 15f;
    public float OutlierDistanceThreshold { get; set; } = 2.5f;
    public EdgePolarity Polarity { get; set; } = EdgePolarity.All;
    public EdgeSelection Selection { get; set; } = EdgeSelection.Strongest;

    public CaliperTool(string name = "CaliperTool") : base(name)
    {
    }

    /// <summary>
    /// Equivalent to mwwz::ppw_to_rr (0x1000B63C):
    /// Converts point1, point2, width into a RotatedRect.
    /// </summary>
    public static RotatedRect PpwToRotatedRect(Point2f p1, Point2f p2, float width)
    {
        float dx = p2.X - p1.X;
        float dy = p2.Y - p1.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float angleDeg = MathF.Atan2(dy, dx) * (180f / MathF.PI);
        var center = new Point2f((p1.X + p2.X) * 0.5f, (p1.Y + p2.Y) * 0.5f);
        return new RotatedRect(center, new Size2f(Math.Max(2f, len), Math.Max(2f, width)), angleDeg);
    }

    /// <summary>
    /// Equivalent to mwwz::rr_to_pts (0x1000B4F9):
    /// Returns the 4 corner vertices of a RotatedRect.
    /// </summary>
    public static Point2f[] RotatedRectToPoints(RotatedRect rr) => rr.Points();

    /// <summary>
    /// Computes Point-to-Point distance, |dX|, |dY|, and angle in degrees.
    /// </summary>
    public static void ComputePointToPoint(Point2f pA, Point2f pB, out double dist, out double dx, out double dy, out double angleDeg)
    {
        double rawDx = pB.X - pA.X;
        double rawDy = pB.Y - pA.Y;
        dx = Math.Abs(rawDx);
        dy = Math.Abs(rawDy);
        dist = Math.Sqrt(rawDx * rawDx + rawDy * rawDy);
        angleDeg = Math.Atan2(rawDy, rawDx) * (180.0 / Math.PI);
    }

    protected override IVisionResult Process(Mat image)
    {
        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);

        return Mode switch
        {
            CaliperMode.Line => Measure1DLine(gray),
            CaliperMode.SearchLine => Measure2DRakeLine(gray, LineStart, LineEnd, ProjectionLength, NumSpokes),
            CaliperMode.Circle => MeasureCircle(gray),
            CaliperMode.SearchCorner => MeasureCorner(gray),
            CaliperMode.SearchEllipse => MeasureEllipse(gray),
            _ => MeasureCircle(gray)
        };
    }

    private CaliperResult Measure1DLine(Mat gray)
    {
        float dx = LineEnd.X - LineStart.X;
        float dy = LineEnd.Y - LineStart.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);

        if (len < 2.0f)
        {
            return new CaliperResult { Success = false, Message = "Line segment too short" };
        }

        float ux = dx / len;
        float uy = dy / len;
        float nx = -uy;
        float ny = ux;

        int numSteps = (int)MathF.Ceiling(len);
        int halfProj = (int)MathF.Ceiling(ProjectionLength * 0.5f);
        int projSamples = Math.Max(1, halfProj * 2 + 1);

        float[] profile = new float[numSteps];
        for (int i = 0; i < numSteps; i++)
        {
            float cx = LineStart.X + ux * i;
            float cy = LineStart.Y + uy * i;

            float sum = 0f;
            for (int p = -halfProj; p <= halfProj; p++)
            {
                float px = cx + nx * p;
                float py = cy + ny * p;
                sum += SampleBilinear(gray, px, py);
            }
            profile[i] = sum / projSamples;
        }

        var edges = Find1DEdgesGaussian(profile, Threshold, Polarity, GaussianSigma);
        var filteredEdges = FilterEdges(edges, Selection == EdgeSelection.Strongest ? EdgeSelection.All : Selection);

        var edgePoints = new List<EdgePoint>(filteredEdges.Count);
        foreach (var e in filteredEdges)
        {
            float posX = LineStart.X + ux * e.Offset;
            float posY = LineStart.Y + uy * e.Offset;
            edgePoints.Add(new EdgePoint(posX, posY, e.Magnitude, e.Direction, e.Offset, true));
        }

        double distance = 0.0;
        if (edgePoints.Count >= 2)
        {
            float ex = edgePoints.Last().X - edgePoints.First().X;
            float ey = edgePoints.Last().Y - edgePoints.First().Y;
            distance = Math.Sqrt(ex * ex + ey * ey);
        }

        return new CaliperResult
        {
            Success = edgePoints.Count > 0,
            Edges = edgePoints,
            Distance = distance,
            InlierCount = edgePoints.Count,
            FittedPoint1 = edgePoints.Count > 0 ? new Point2f(edgePoints.First().X, edgePoints.First().Y) : null,
            FittedPoint2 = edgePoints.Count > 1 ? new Point2f(edgePoints.Last().X, edgePoints.Last().Y) : null,
            FittedMidPoint = edgePoints.Count >= 2
                ? new Point2f((edgePoints.First().X + edgePoints.Last().X) * 0.5f, (edgePoints.First().Y + edgePoints.Last().Y) * 0.5f)
                : (edgePoints.Count == 1 ? new Point2f(edgePoints[0].X, edgePoints[0].Y) : null),
            Message = $"Detected {edgePoints.Count} edge(s), Dist={distance:F2}px"
        };
    }

    /// <summary>
    /// Equivalent to mwwz::gen_measure_line (0x1000B6DF) + do_measure (0x1000BDBB) + do_fit2 (0x1000EFC7):
    /// Projects N perpendicular 1D calipers along the baseline (pStart -> pEnd) and fits a robust line.
    /// </summary>
    public CaliperResult Measure2DRakeLine(Mat gray, Point2f pStart, Point2f pEnd, float searchHalfWidth, int numCalipers)
    {
        float dx = pEnd.X - pStart.X;
        float dy = pEnd.Y - pStart.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 4.0f)
            return new CaliperResult { Success = false, Message = "Baseline too short" };

        float ux = dx / len;
        float uy = dy / len;
        // Normal vector (search direction perpendicular to line)
        float nx = -uy;
        float ny = ux;

        int count = Math.Clamp(numCalipers, 3, 200);
        float halfW = Math.Clamp(searchHalfWidth, 4f, 400f);
        int stripLen = (int)MathF.Ceiling(halfW * 2f);
        float thresh = Math.Clamp(Threshold, 1f, 255f);
        var rawEdges = new List<EdgePoint>(count);

        for (int c = 0; c < count; c++)
        {
            float t = count == 1 ? 0.5f : (float)c / (count - 1);
            float baseX = pStart.X + dx * t;
            float baseY = pStart.Y + dy * t;

            float[] profile = new float[stripLen];
            for (int i = 0; i < stripLen; i++)
            {
                float offset = -halfW + i;
                float px = baseX + nx * offset;
                float py = baseY + ny * offset;
                profile[i] = SampleBilinear(gray, px, py);
            }

            var edges = Find1DEdgesGaussian(profile, thresh, Polarity, GaussianSigma);
            var selected = FilterEdges(edges, Selection == EdgeSelection.All ? EdgeSelection.Strongest : Selection);
            if (selected.Count > 0)
            {
                var best = selected[0];
                float exactOffset = -halfW + best.Offset;
                float ex = baseX + nx * exactOffset;
                float ey = baseY + ny * exactOffset;
                rawEdges.Add(new EdgePoint(ex, ey, best.Magnitude, best.Direction, exactOffset, true));
            }
        }

        if (rawEdges.Count < 2)
        {
            return new CaliperResult
            {
                Success = false,
                Edges = rawEdges,
                Message = $"Insufficient edge points ({rawEdges.Count}) for line fit"
            };
        }

        FitLineRansac(rawEdges, OutlierDistanceThreshold, pStart, pEnd,
            out Point2f fitP1, out Point2f fitP2, out double angleDeg, out double rms, out int inliers, out int outliers);

        var mid = new Point2f((fitP1.X + fitP2.X) * 0.5f, (fitP1.Y + fitP2.Y) * 0.5f);
        double segLen = Math.Sqrt((fitP2.X - fitP1.X) * (fitP2.X - fitP1.X) + (fitP2.Y - fitP1.Y) * (fitP2.Y - fitP1.Y));

        return new CaliperResult
        {
            Success = true,
            Edges = rawEdges,
            FittedPoint1 = fitP1,
            FittedPoint2 = fitP2,
            FittedMidPoint = mid,
            FittedAngleDeg = angleDeg,
            Distance = segLen,
            RmsError = rms,
            StraightnessError = rms,
            InlierCount = inliers,
            OutlierCount = outliers,
            Message = $"Line: θ={angleDeg:F2}°, Inliers={inliers}/{rawEdges.Count}, RMS={rms:F3}px"
        };
    }

    private CaliperResult MeasureCorner(Mat gray)
    {
        int nCalipers = Math.Clamp(NumSpokes, 5, 100);
        var res1 = Measure2DRakeLine(gray, LineStart, LineEnd, ProjectionLength, nCalipers);
        var res2 = Measure2DRakeLine(gray, Line2Start, Line2End, Line2ProjectionLength, nCalipers);

        var combinedEdges = new List<EdgePoint>();
        combinedEdges.AddRange(res1.Edges);
        combinedEdges.AddRange(res2.Edges);

        if (!res1.Success || !res2.Success || !res1.FittedPoint1.HasValue || !res2.FittedPoint1.HasValue)
        {
            return new CaliperResult
            {
                Success = false,
                Edges = combinedEdges,
                Message = "Corner detection failed: one or both lines not found"
            };
        }

        var a1 = res1.FittedPoint1.Value;
        var a2 = res1.FittedPoint2!.Value;
        var b1 = res2.FittedPoint1.Value;
        var b2 = res2.FittedPoint2!.Value;

        if (!IntersectLines(a1, a2, b1, b2, out Point2f corner, out double angleDeg))
        {
            return new CaliperResult
            {
                Success = false,
                Edges = combinedEdges,
                Message = "Lines are parallel; no corner intersection"
            };
        }

        return new CaliperResult
        {
            Success = true,
            Edges = combinedEdges,
            FittedPoint1 = a1,
            FittedPoint2 = a2,
            Line2Point1 = b1,
            Line2Point2 = b2,
            CornerPoint = corner,
            FittedMidPoint = corner,
            IncludedAngleDeg = angleDeg,
            FittedAngleDeg = angleDeg,
            Distance = angleDeg,
            InlierCount = res1.InlierCount + res2.InlierCount,
            OutlierCount = res1.OutlierCount + res2.OutlierCount,
            Message = $"Corner=({corner.X:F1}, {corner.Y:F1}), Angle={angleDeg:F2}°"
        };
    }

    /// <summary>
    /// Equivalent to mwwz::gen_measure_circle (0x1000BA1C) + do_measure (0x1000BDBB) + do_fit2 (0x1000EFC7):
    /// Supports Outward (Inner->Outer) and Inward (Outer->Inner) spoke scanning + RANSAC/Huber Circle Fit.
    /// </summary>
    private CaliperResult MeasureCircle(Mat gray)
    {
        int spokes = Math.Clamp(NumSpokes, 6, 360);
        var spokePoints = new List<EdgePoint>(spokes);
        float angleStep = 2.0f * MathF.PI / spokes;

        float safeRadius = Math.Clamp(CircleRadius, 3f, 2500f);
        float safeRange = Math.Clamp(SearchRange, 3f, 400f);
        float thresh = Math.Clamp(Threshold, 1f, 255f);

        float rInner = Math.Max(2f, safeRadius - safeRange);
        float rOuter = safeRadius + safeRange;
        int numSteps = Math.Clamp((int)MathF.Ceiling(rOuter - rInner), 5, 1000);

        for (int s = 0; s < spokes; s++)
        {
            float angle = s * angleStep;
            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);

            float[] profile = new float[numSteps];
            for (int i = 0; i < numSteps; i++)
            {
                float r = ScanDirection == CircleScanDirection.Outward
                    ? (rInner + i)
                    : (rOuter - i);
                float px = CircleCenter.X + cosA * r;
                float py = CircleCenter.Y + sinA * r;
                profile[i] = SampleBilinear(gray, px, py);
            }

            var edges = Find1DEdgesGaussian(profile, thresh, Polarity, GaussianSigma);
            var filtered = FilterEdges(edges, Selection == EdgeSelection.All ? EdgeSelection.Strongest : Selection);

            if (filtered.Count > 0)
            {
                var bestEdge = filtered[0];
                float exactR = ScanDirection == CircleScanDirection.Outward
                    ? (rInner + bestEdge.Offset)
                    : (rOuter - bestEdge.Offset);
                float px = CircleCenter.X + cosA * exactR;
                float py = CircleCenter.Y + sinA * exactR;
                spokePoints.Add(new EdgePoint(px, py, bestEdge.Magnitude, bestEdge.Direction, exactR, true));
            }
        }

        if (spokePoints.Count < 3)
        {
            return new CaliperResult
            {
                Success = false,
                Edges = spokePoints,
                Message = "Insufficient edge points for circle fit"
            };
        }

        // RANSAC + Huber IRLS Circle Fit (matching mwwz::do_fit2)
        FitCircleRansac(spokePoints, OutlierDistanceThreshold,
            out Point2f center, out double radius, out double rms, out double roundness, out int inliers, out int outliers);

        return new CaliperResult
        {
            Success = radius > 1.0,
            Edges = spokePoints,
            FittedCircleCenter = center,
            FittedMidPoint = center,
            FittedRadius = radius,
            Distance = radius * 2.0,
            RmsError = rms,
            RoundnessError = roundness,
            InlierCount = inliers,
            OutlierCount = outliers,
            Message = $"Circle: C=({center.X:F1}, {center.Y:F1}), R={radius:F2}px, Ø={radius * 2:F2}px, Inliers={inliers}/{spokePoints.Count}"
        };
    }

    private CaliperResult MeasureEllipse(Mat gray)
    {
        var spokePoints = new List<EdgePoint>(NumSpokes);
        int spokes = Math.Clamp(NumSpokes, 8, 360);
        float angleStep = 2.0f * MathF.PI / spokes;
        float phiRad = EllipseAngleDeg * (MathF.PI / 180f);
        float cosPhi = MathF.Cos(phiRad);
        float sinPhi = MathF.Sin(phiRad);

        float ra = Math.Max(5f, CircleRadius);
        float rb = Math.Max(5f, EllipseRadiusB);

        for (int s = 0; s < spokes; s++)
        {
            float t = s * angleStep;
            float exLocal = ra * MathF.Cos(t);
            float eyLocal = rb * MathF.Sin(t);
            // Outward normal on ellipse
            float nxLocal = rb * MathF.Cos(t);
            float nyLocal = ra * MathF.Sin(t);
            float nLen = MathF.Sqrt(nxLocal * nxLocal + nyLocal * nyLocal);
            if (nLen < 1e-5f) continue;
            nxLocal /= nLen;
            nyLocal /= nLen;

            float baseX = CircleCenter.X + exLocal * cosPhi - eyLocal * sinPhi;
            float baseY = CircleCenter.Y + exLocal * sinPhi + eyLocal * cosPhi;
            float nx = nxLocal * cosPhi - nyLocal * sinPhi;
            float ny = nxLocal * sinPhi + nyLocal * cosPhi;

            int numSteps = Math.Max(5, (int)MathF.Ceiling(SearchRange * 2f));
            float[] profile = new float[numSteps];
            for (int i = 0; i < numSteps; i++)
            {
                float d = -SearchRange + i;
                profile[i] = SampleBilinear(gray, baseX + nx * d, baseY + ny * d);
            }

            var edges = Find1DEdgesGaussian(profile, Threshold, Polarity, GaussianSigma);
            var filtered = FilterEdges(edges, Selection == EdgeSelection.All ? EdgeSelection.Strongest : Selection);
            if (filtered.Count > 0)
            {
                float exactD = -SearchRange + filtered[0].Offset;
                spokePoints.Add(new EdgePoint(baseX + nx * exactD, baseY + ny * exactD, filtered[0].Magnitude, filtered[0].Direction, exactD, true));
            }
        }

        if (spokePoints.Count < 5)
            return new CaliperResult { Success = false, Edges = spokePoints, Message = "Need >= 5 points for ellipse fit" };

        var pts = spokePoints.Select(p => new Point2f(p.X, p.Y)).ToArray();
        var ell = Cv2.FitEllipse(pts);
        return new CaliperResult
        {
            Success = true,
            Edges = spokePoints,
            FittedEllipse = ell,
            FittedCircleCenter = ell.Center,
            FittedMidPoint = ell.Center,
            FittedRadius = (ell.Size.Width + ell.Size.Height) * 0.25,
            Distance = Math.Max(ell.Size.Width, ell.Size.Height),
            InlierCount = spokePoints.Count,
            Message = $"Ellipse: C=({ell.Center.X:F1}, {ell.Center.Y:F1}), Size=({ell.Size.Width:F1}x{ell.Size.Height:F1}), θ={ell.Angle:F1}°"
        };
    }

    private static List<EdgePoint> Find1DEdgesGaussian(float[] profile, float threshold, EdgePolarity polarity, float sigma)
    {
        var edges = new List<EdgePoint>();
        int len = profile.Length;
        if (len < 5) return edges;

        // Smooth profile with 1D Gaussian kernel before central derivative (matching mwwz::measure_pos_d)
        float[] smoothed = Smooth1DGaussian(profile, Math.Clamp(sigma, 0.4f, 5.0f));

        float[] d = new float[len];
        for (int i = 1; i < len - 1; i++)
        {
            d[i] = (smoothed[i + 1] - smoothed[i - 1]) * 0.5f;
        }

        for (int i = 2; i < len - 2; i++)
        {
            float mag = MathF.Abs(d[i]);
            if (mag >= threshold)
            {
                bool isPeak = false;
                int dir = d[i] > 0 ? 1 : -1;

                if (dir > 0 && d[i] >= d[i - 1] && d[i] > d[i + 1])
                    isPeak = true;
                else if (dir < 0 && d[i] <= d[i - 1] && d[i] < d[i + 1])
                    isPeak = true;

                if (isPeak)
                {
                    if (polarity == EdgePolarity.Positive && dir < 0) continue;
                    if (polarity == EdgePolarity.Negative && dir > 0) continue;

                    float ym1 = MathF.Abs(d[i - 1]);
                    float y0 = mag;
                    float yp1 = MathF.Abs(d[i + 1]);

                    float denom = 2.0f * (2.0f * y0 - ym1 - yp1);
                    float subOffset = 0f;
                    if (MathF.Abs(denom) > 1e-5f)
                    {
                        subOffset = Math.Clamp((yp1 - ym1) / denom, -0.99f, 0.99f);
                    }

                    edges.Add(new EdgePoint(0, 0, y0, dir, i + subOffset, true));
                }
            }
        }

        return edges;
    }

    private static float[] Smooth1DGaussian(float[] input, float sigma)
    {
        if (sigma <= 0.45f) return input;
        int radius = Math.Clamp((int)MathF.Ceiling(sigma * 2.5f), 1, 8);
        float[] kernel = new float[radius * 2 + 1];
        float sum = 0f;
        float inv2s2 = 1.0f / (2.0f * sigma * sigma);
        for (int k = -radius; k <= radius; k++)
        {
            float w = MathF.Exp(-k * k * inv2s2);
            kernel[k + radius] = w;
            sum += w;
        }
        for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;

        float[] output = new float[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            float acc = 0f;
            for (int k = -radius; k <= radius; k++)
            {
                int idx = Math.Clamp(i + k, 0, input.Length - 1);
                acc += input[idx] * kernel[k + radius];
            }
            output[i] = acc;
        }
        return output;
    }

    private static List<EdgePoint> FilterEdges(List<EdgePoint> edges, EdgeSelection selection)
    {
        if (edges.Count <= 1 || selection == EdgeSelection.All)
            return edges;

        return selection switch
        {
            EdgeSelection.First => new List<EdgePoint> { edges.First() },
            EdgeSelection.Last => new List<EdgePoint> { edges.Last() },
            EdgeSelection.Strongest => new List<EdgePoint> { edges.OrderByDescending(e => e.Magnitude).First() },
            _ => edges
        };
    }

    private static float SampleBilinear(Mat img, float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        if (x0 < 0 || x1 >= img.Width || y0 < 0 || y1 >= img.Height)
            return 0f;

        float fx = x - x0;
        float fy = y - y0;

        byte p00 = img.At<byte>(y0, x0);
        byte p10 = img.At<byte>(y0, x1);
        byte p01 = img.At<byte>(y1, x0);
        byte p11 = img.At<byte>(y1, x1);

        float top = p00 * (1f - fx) + p10 * fx;
        float bottom = p01 * (1f - fx) + p11 * fx;

        return top * (1f - fy) + bottom * fy;
    }

    private static void FitLineRansac(
        List<EdgePoint> points,
        float outlierThreshold,
        Point2f refStart,
        Point2f refEnd,
        out Point2f fitP1,
        out Point2f fitP2,
        out double angleDeg,
        out double rms,
        out int inlierCount,
        out int outlierCount)
    {
        int n = points.Count;
        float tol = Math.Max(0.8f, outlierThreshold);

        // Best line normal (nx, ny, d) where nx*x + ny*y + d = 0
        double bestNx = 0, bestNy = 1, bestD = 0;
        int bestInliers = -1;
        double bestResidualSum = double.MaxValue;

        // Deterministic multi-pair consensus
        int step = Math.Max(1, n / 16);
        for (int i = 0; i < n - 1; i += step)
        {
            for (int j = i + 1; j < n; j += step)
            {
                double dx = points[j].X - points[i].X;
                double dy = points[j].Y - points[i].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1.0) continue;

                double nx = -dy / len;
                double ny = dx / len;
                double d = -(nx * points[i].X + ny * points[i].Y);

                int inliers = 0;
                double resSum = 0;
                for (int k = 0; k < n; k++)
                {
                    double dist = Math.Abs(nx * points[k].X + ny * points[k].Y + d);
                    if (dist <= tol)
                    {
                        inliers++;
                        resSum += dist * dist;
                    }
                }

                if (inliers > bestInliers || (inliers == bestInliers && resSum < bestResidualSum))
                {
                    bestInliers = inliers;
                    bestResidualSum = resSum;
                    bestNx = nx;
                    bestNy = ny;
                    bestD = d;
                }
            }
        }

        // Mark inliers and refine with Total Least Squares (PCA) on inliers
        var inlierPts = new List<EdgePoint>(n);
        for (int i = 0; i < n; i++)
        {
            var pt = points[i];
            double dist = Math.Abs(bestNx * pt.X + bestNy * pt.Y + bestD);
            bool isInlier = bestInliers < 2 || dist <= tol;
            points[i] = new EdgePoint(pt.X, pt.Y, pt.Magnitude, pt.Direction, pt.Offset, isInlier);
            if (isInlier) inlierPts.Add(points[i]);
        }

        if (inlierPts.Count < 2)
            inlierPts = new List<EdgePoint>(points);

        double mx = inlierPts.Average(p => (double)p.X);
        double my = inlierPts.Average(p => (double)p.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (var p in inlierPts)
        {
            double ux = p.X - mx;
            double uy = p.Y - my;
            sxx += ux * ux;
            syy += uy * uy;
            sxy += ux * uy;
        }

        double theta = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
        double dirX = Math.Cos(theta);
        double dirY = Math.Sin(theta);

        // Align direction with reference vector (refEnd - refStart)
        if (dirX * (refEnd.X - refStart.X) + dirY * (refEnd.Y - refStart.Y) < 0)
        {
            dirX = -dirX;
            dirY = -dirY;
        }

        angleDeg = Math.Atan2(dirY, dirX) * (180.0 / Math.PI);

        // Project refStart and refEnd onto fitted line
        double t1 = (refStart.X - mx) * dirX + (refStart.Y - my) * dirY;
        double t2 = (refEnd.X - mx) * dirX + (refEnd.Y - my) * dirY;
        fitP1 = new Point2f((float)(mx + t1 * dirX), (float)(my + t1 * dirY));
        fitP2 = new Point2f((float)(mx + t2 * dirX), (float)(my + t2 * dirY));

        double errSq = 0;
        double fnx = -dirY, fny = dirX;
        foreach (var p in inlierPts)
        {
            double e = (p.X - mx) * fnx + (p.Y - my) * fny;
            errSq += e * e;
        }
        rms = Math.Sqrt(errSq / inlierPts.Count);
        inlierCount = inlierPts.Count;
        outlierCount = n - inlierCount;
    }

    private static bool IntersectLines(Point2f a1, Point2f a2, Point2f b1, Point2f b2, out Point2f intersection, out double includedAngleDeg)
    {
        double dx1 = a2.X - a1.X, dy1 = a2.Y - a1.Y;
        double dx2 = b2.X - b1.X, dy2 = b2.Y - b1.Y;
        double denom = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(denom) < 1e-6)
        {
            intersection = new Point2f(0, 0);
            includedAngleDeg = 0;
            return false;
        }

        double t = ((b1.X - a1.X) * dy2 - (b1.Y - a1.Y) * dx2) / denom;
        intersection = new Point2f((float)(a1.X + t * dx1), (float)(a1.Y + t * dy1));

        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        double dot = (dx1 * dx2 + dy1 * dy2) / Math.Max(1e-6, len1 * len2);
        double rad = Math.Acos(Math.Clamp(dot, -1.0, 1.0));
        includedAngleDeg = rad * (180.0 / Math.PI);
        return true;
    }

    private static void FitCircleRansac(
        List<EdgePoint> points,
        float outlierThreshold,
        out Point2f center,
        out double radius,
        out double rms,
        out double roundnessError,
        out int inlierCount,
        out int outlierCount)
    {
        int n = points.Count;
        if (n < 3)
        {
            center = new Point2f(0, 0);
            radius = 0;
            rms = 0;
            roundnessError = 0;
            inlierCount = 0;
            outlierCount = n;
            return;
        }

        float tol = Math.Max(1.0f, outlierThreshold);

        // Initial Kasa fit on all points
        FitCircleAlgebraic(points, out Point2f initC, out double initR, out _);

        // Try RANSAC 3-point triplets to reject outliers when n >= 6
        Point2f bestC = initC;
        double bestR = initR;
        int bestInliers = 0;
        double bestErr = double.MaxValue;

        int step = Math.Max(1, n / 12);
        for (int i = 0; i < n; i += step)
        {
            int j = (i + n / 3) % n;
            int k = (i + (2 * n) / 3) % n;
            if (CircleFrom3Points(points[i], points[j], points[k], out Point2f c3, out double r3))
            {
                int inl = 0;
                double err = 0;
                for (int m = 0; m < n; m++)
                {
                    double d = Math.Sqrt((points[m].X - c3.X) * (points[m].X - c3.X) + (points[m].Y - c3.Y) * (points[m].Y - c3.Y));
                    double diff = Math.Abs(d - r3);
                    if (diff <= tol)
                    {
                        inl++;
                        err += diff * diff;
                    }
                }
                if (inl > bestInliers || (inl == bestInliers && err < bestErr))
                {
                    bestInliers = inl;
                    bestErr = err;
                    bestC = c3;
                    bestR = r3;
                }
            }
        }

        // Classify inliers vs outliers
        var inliers = new List<EdgePoint>(n);
        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            double d = Math.Sqrt((p.X - bestC.X) * (p.X - bestC.X) + (p.Y - bestC.Y) * (p.Y - bestC.Y));
            bool isIn = bestInliers < 3 || Math.Abs(d - bestR) <= tol;
            points[i] = new EdgePoint(p.X, p.Y, p.Magnitude, p.Direction, p.Offset, isIn);
            if (isIn) inliers.Add(points[i]);
        }

        if (inliers.Count < 3)
            inliers = new List<EdgePoint>(points);

        FitCircleAlgebraic(inliers, out center, out radius, out rms);

        double minR = double.MaxValue, maxR = double.MinValue;
        foreach (var p in inliers)
        {
            double d = Math.Sqrt((p.X - center.X) * (p.X - center.X) + (p.Y - center.Y) * (p.Y - center.Y));
            if (d < minR) minR = d;
            if (d > maxR) maxR = d;
        }
        roundnessError = maxR >= minR ? (maxR - minR) : 0;
        inlierCount = inliers.Count;
        outlierCount = n - inlierCount;
    }

    private static bool CircleFrom3Points(EdgePoint p1, EdgePoint p2, EdgePoint p3, out Point2f center, out double radius)
    {
        double x1 = p1.X, y1 = p1.Y;
        double x2 = p2.X, y2 = p2.Y;
        double x3 = p3.X, y3 = p3.Y;
        double d = 2.0 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
        if (Math.Abs(d) < 1e-5)
        {
            center = default;
            radius = 0;
            return false;
        }
        double s1 = x1 * x1 + y1 * y1;
        double s2 = x2 * x2 + y2 * y2;
        double s3 = x3 * x3 + y3 * y3;
        double ux = (s1 * (y2 - y3) + s2 * (y3 - y1) + s3 * (y1 - y2)) / d;
        double uy = (s1 * (x3 - x2) + s2 * (x1 - x3) + s3 * (x2 - x1)) / d;
        center = new Point2f((float)ux, (float)uy);
        radius = Math.Sqrt((x1 - ux) * (x1 - ux) + (y1 - uy) * (y1 - uy));
        return radius > 1.0 && radius < 10000.0;
    }

    private static void FitCircleAlgebraic(List<EdgePoint> points, out Point2f center, out double radius, out double rms)
    {
        int n = points.Count;
        if (n < 3)
        {
            center = new Point2f(0, 0);
            radius = 0;
            rms = 0;
            return;
        }

        double meanX = 0, meanY = 0;
        foreach (var p in points)
        {
            meanX += p.X;
            meanY += p.Y;
        }
        meanX /= n;
        meanY /= n;

        double suu = 0, svv = 0, suv = 0, suuu_uvv = 0, svvv_uuv = 0, sz = 0;
        foreach (var p in points)
        {
            double u = p.X - meanX;
            double v = p.Y - meanY;
            double z = u * u + v * v;

            suu += u * u;
            svv += v * v;
            suv += u * v;
            suuu_uvv += u * z;
            svvv_uuv += v * z;
            sz += z;
        }

        double det = suu * svv - suv * suv;
        if (Math.Abs(det) < 1e-9)
        {
            center = new Point2f((float)meanX, (float)meanY);
            radius = 0;
            rms = 0;
            return;
        }

        double uc = 0.5 * (suuu_uvv * svv - svvv_uuv * suv) / det;
        double vc = 0.5 * (svvv_uuv * suu - suuu_uvv * suv) / det;

        double cx = meanX + uc;
        double cy = meanY + vc;
        double r = Math.Sqrt(uc * uc + vc * vc + sz / n);

        center = new Point2f((float)cx, (float)cy);
        radius = r;

        double errSum = 0;
        foreach (var p in points)
        {
            double dist = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
            double diff = dist - r;
            errSum += diff * diff;
        }
        rms = Math.Sqrt(errSum / n);
    }
}
