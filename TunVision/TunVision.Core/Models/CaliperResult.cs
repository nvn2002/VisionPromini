using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.Abstractions;

namespace TunVision.Core.Models;

public enum EdgePolarity
{
    All,
    Positive, // Dark to bright (Dark -> Light)
    Negative  // Bright to dark (Light -> Dark)
}

public enum EdgeSelection
{
    All,
    First,
    Last,
    Strongest
}

public enum CircleScanDirection
{
    Outward, // Inside -> Outside
    Inward   // Outside -> Inside
}

public struct EdgePoint
{
    public float X;
    public float Y;
    public float Magnitude;
    public int Direction; // +1 = pos, -1 = neg
    public float Offset;
    public bool IsInlier;

    public EdgePoint(float x, float y, float magnitude, int direction, float offset, bool isInlier = true)
    {
        X = x;
        Y = y;
        Magnitude = magnitude;
        Direction = direction;
        Offset = offset;
        IsInlier = isInlier;
    }
}

public class CaliperResult : BaseVisionResult
{
    public List<EdgePoint> Edges { get; set; } = new();
    public double Distance { get; set; }

    // Line / Rake Fit Results
    public Point2f? FittedPoint1 { get; set; }
    public Point2f? FittedPoint2 { get; set; }
    public Point2f? FittedMidPoint { get; set; }
    public double FittedAngleDeg { get; set; }
    public double StraightnessError { get; set; }

    // Circle Fit Results
    public Point2f? FittedCircleCenter { get; set; }
    public double? FittedRadius { get; set; }
    public double? FittedDiameter => FittedRadius.HasValue ? FittedRadius.Value * 2.0 : null;
    public double RoundnessError { get; set; }
    public double RmsError { get; set; }
    public int InlierCount { get; set; }
    public int OutlierCount { get; set; }

    // Corner Fit Results (Intersection of 2 Caliper Lines)
    public Point2f? CornerPoint { get; set; }
    public double IncludedAngleDeg { get; set; }
    public Point2f? Line2Point1 { get; set; }
    public Point2f? Line2Point2 { get; set; }

    // Ellipse Fit Results
    public RotatedRect? FittedEllipse { get; set; }

    public CaliperResult()
    {
        Data = Edges;
    }
}
