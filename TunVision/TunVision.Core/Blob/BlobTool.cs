using System;
using System.Collections.Generic;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;

namespace TunVision.Core.Blob;

public enum ThresholdMode
{
    Manual,
    Otsu,
    Adaptive
}

public enum MorphOp
{
    None,
    Open,
    Close,
    Dilate,
    Erode,
    TopHat,
    BlackHat
}

public class BlobTool : VisionToolBase
{
    public ThresholdMode ThresholdMode { get; set; } = ThresholdMode.Manual;
    public double ThresholdLow { get; set; } = 128;
    public double ThresholdHigh { get; set; } = 255;
    public bool Invert { get; set; } = false;

    public MorphOp Morphology { get; set; } = MorphOp.None;
    public int MorphKernelSize { get; set; } = 3;
    public bool FillUpHoles { get; set; } = false; // Equivalent to mwwz::fill_up_y

    public double MinArea { get; set; } = 20.0;
    public double MaxArea { get; set; } = 1_000_000.0;
    public double MinCircularity { get; set; } = 0.0;

    public BaseRoi? SearchRoi { get; set; }

    public BlobTool(string name = "BlobTool") : base(name)
    {
    }

    protected override IVisionResult Process(Mat image)
    {
        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var binary = new Mat();

        // 1. Thresholding
        switch (ThresholdMode)
        {
            case ThresholdMode.Otsu:
                var oType = Invert ? (ThresholdTypes.BinaryInv | ThresholdTypes.Otsu) : (ThresholdTypes.Binary | ThresholdTypes.Otsu);
                Cv2.Threshold(gray, binary, 0, 255, oType);
                break;
            case ThresholdMode.Adaptive:
                var aType = Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, aType, 11, 2);
                break;
            case ThresholdMode.Manual:
            default:
                var mType = Invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary;
                Cv2.Threshold(gray, binary, ThresholdLow, ThresholdHigh, mType);
                break;
        }

        // Apply ROI Mask if provided
        if (SearchRoi != null)
        {
            using var mask = SearchRoi.CreateMask(gray.Width, gray.Height);
            Cv2.BitwiseAnd(binary, mask, binary);
        }

        // 2. Morphology (matching mwwz::opening_y, closing_y, dilation2_y, erosion2_y, top_hat_y, bottom_hat_y)
        if (Morphology != MorphOp.None && MorphKernelSize >= 3)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(MorphKernelSize, MorphKernelSize));
            switch (Morphology)
            {
                case MorphOp.Open:
                    Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);
                    break;
                case MorphOp.Close:
                    Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
                    break;
                case MorphOp.Dilate:
                    Cv2.Dilate(binary, binary, kernel);
                    break;
                case MorphOp.Erode:
                    Cv2.Erode(binary, binary, kernel);
                    break;
                case MorphOp.TopHat:
                    Cv2.MorphologyEx(binary, binary, MorphTypes.TopHat, kernel);
                    break;
                case MorphOp.BlackHat:
                    Cv2.MorphologyEx(binary, binary, MorphTypes.BlackHat, kernel);
                    break;
            }
        }

        // 2b. FillUpHoles (equivalent to mwwz::fill_up_y)
        if (FillUpHoles)
        {
            Cv2.FindContours(binary, out var extContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Cv2.DrawContours(binary, extContours, -1, Scalar.White, -1);
        }

        // 3. Find Contours
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var blobs = new List<BlobItem>();
        int id = 1;

        foreach (var c in contours)
        {
            double area = Cv2.ContourArea(c);
            if (area < MinArea || area > MaxArea)
                continue;

            double perimeter = Cv2.ArcLength(c, true);
            double circularity = 0.0;
            if (perimeter > 0)
            {
                circularity = 4.0 * Math.PI * area / (perimeter * perimeter);
            }

            if (circularity < MinCircularity)
                continue;

            var moments = Cv2.Moments(c);
            Point2f centroid = moments.M00 > 0
                ? new Point2f((float)(moments.M10 / moments.M00), (float)(moments.M01 / moments.M00))
                : new Point2f(0, 0);

            var bbox = Cv2.BoundingRect(c);
            var minRect = c.Length >= 5 ? Cv2.MinAreaRect(c) : new RotatedRect(centroid, new Size2f(bbox.Width, bbox.Height), 0);

            blobs.Add(new BlobItem
            {
                Id = id++,
                Area = area,
                Centroid = centroid,
                BoundingBox = bbox,
                MinAreaRect = minRect,
                Circularity = circularity,
                Contour = c
            });
        }

        return new BlobResult
        {
            Success = blobs.Count > 0,
            Blobs = blobs,
            Message = $"Found {blobs.Count} blob(s)"
        };
    }
}
