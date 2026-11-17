using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.Abstractions;

namespace TunVision.Core.Models;

public class BlobItem
{
    public int Id { get; set; }
    public double Area { get; set; }
    public Point2f Centroid { get; set; }
    public Rect BoundingBox { get; set; }
    public RotatedRect MinAreaRect { get; set; }
    public double Circularity { get; set; }
    public Point[] Contour { get; set; } = Array.Empty<Point>();
    public string Summary => $"#{Id}: Area={Area:F0}, Center=({Centroid.X:F1}, {Centroid.Y:F1})";

    public override string ToString()
    {
        return $"Blob #{Id}: Area={Area:F0}, Center=({Centroid.X:F1}, {Centroid.Y:F1}), Circ={Circularity:F2}";
    }
}

public class BlobResult : BaseVisionResult
{
    public List<BlobItem> Blobs { get; set; } = new();
    public int BlobCount => Blobs.Count;

    public BlobResult()
    {
        Data = Blobs;
    }
}
