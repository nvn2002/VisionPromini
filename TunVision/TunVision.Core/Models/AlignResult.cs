using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.Abstractions;

namespace TunVision.Core.Models;

public struct MatchedFeaturePoint
{
    public float X;          // Scene X (subpixel)
    public float Y;          // Scene Y (subpixel)
    public float TangentX;   // Unit tangent vector Tx = -d'_y
    public float TangentY;   // Unit tangent vector Ty = d'_x
    public bool IsInlier;    // true: Inlier (Green), false: Outlier/Defect (Red)
    public float DotProduct; // Dot product (d'_i . gradS / M)
}

public class MatchResult
{
    public float X { get; set; }
    public float Y { get; set; }
    public float AngleDeg { get; set; }
    public float Scale { get; set; } = 1.0f;
    public float Score { get; set; }
    public float Coverage { get; set; }
    public RotatedRect BoundingBox { get; set; }
    public List<List<Point2f>> TransformedContours { get; set; } = new();

    // R3: Visual Feedback - Inliers vs Outliers
    public List<MatchedFeaturePoint> FeaturePoints { get; set; } = new();
    public int InlierCount { get; set; }
    public int OutlierCount { get; set; }
    public float InlierRatio => (InlierCount + OutlierCount > 0)
        ? (float)InlierCount / (InlierCount + OutlierCount)
        : 0f;

    public string LocationText => $"Center: ({X:F1}, {Y:F1})";
    public string ScoreText => $"Score: {Score:F3} (Cov: {Coverage:P0})";
    public string AngleText => $"Angle: {AngleDeg:F1}° | Scale: {Scale:F2}";

    public override string ToString()
    {
        return $"Match at ({X:F2}, {Y:F2}), Angle: {AngleDeg:F2}°, Scale: {Scale:F2}, Score: {Score:F3}, Cov: {Coverage:P1}";
    }
}

public class AlignResult : BaseVisionResult
{
    public List<MatchResult> Matches { get; set; } = new();
    public int MatchesFound => Matches.Count;

    public AlignResult()
    {
        Data = Matches;
    }
}
