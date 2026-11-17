using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace TunVision.Core.Barcode;

public enum CodeKind
{
    AutoDetect,
    QrCode,
    DataMatrix,
    Code128,
    Code39,
    Ean13,
    UpcA,
    Pdf417,
    Aztec
}

public class CodeReadResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public string FormatName { get; set; } = string.Empty;
    public BarcodeFormat Format { get; set; }
    public List<Point2f> Points { get; set; } = new();
    public Rect2f BoundingBox { get; set; }
    public Point2f Center { get; set; }
    public float AngleDeg { get; set; }
    public double ExecutionTimeMs { get; set; }
    public List<CodeReadResult> AdditionalResults { get; set; } = new();
}

public static class BarcodeReadingEngine
{
    public static CodeReadResult ReadCode(
        Mat mat,
        Rect? roi = null,
        CodeKind kind = CodeKind.AutoDetect,
        bool tryHarder = true,
        bool tryInverted = true)
    {
        var result = new CodeReadResult();
        if (mat == null || mat.Empty()) return result;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Crop to ROI if specified
        Mat workMat = mat;
        int offsetX = 0;
        int offsetY = 0;
        bool needDisposeWork = false;

        if (roi.HasValue)
        {
            var r = roi.Value;
            r.X = Math.Max(0, Math.Min(mat.Width - 1, r.X));
            r.Y = Math.Max(0, Math.Min(mat.Height - 1, r.Y));
            r.Width = Math.Max(1, Math.Min(mat.Width - r.X, r.Width));
            r.Height = Math.Max(1, Math.Min(mat.Height - r.Y, r.Height));
            workMat = new Mat(mat, r);
            offsetX = r.X;
            offsetY = r.Y;
            needDisposeWork = true;
        }

        // 2. Convert to Grayscale
        Mat grayMat;
        bool needDisposeGray = false;
        if (workMat.Channels() == 3)
        {
            grayMat = workMat.CvtColor(ColorConversionCodes.BGR2GRAY);
            needDisposeGray = true;
        }
        else if (workMat.Channels() == 4)
        {
            grayMat = workMat.CvtColor(ColorConversionCodes.BGRA2GRAY);
            needDisposeGray = true;
        }
        else
        {
            grayMat = workMat;
        }

        try
        {
            int w = grayMat.Width;
            int h = grayMat.Height;
            byte[] rawBytes = new byte[w * h];

            if (grayMat.IsContinuous())
            {
                Marshal.Copy(grayMat.Data, rawBytes, 0, rawBytes.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(grayMat.Ptr(y), rawBytes, y * w, w);
                }
            }

            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = tryHarder,
                    TryInverted = tryInverted,
                    PossibleFormats = GetTargetFormats(kind)
                }
            };

            var luminanceSource = new RGBLuminanceSource(rawBytes, w, h, RGBLuminanceSource.BitmapFormat.Gray8);
            var zxResults = reader.DecodeMultiple(luminanceSource);
            var zxResult = zxResults?.FirstOrDefault() ?? reader.Decode(luminanceSource);

            if (zxResult == null && tryInverted)
            {
                // If standard decode failed, try inverting luminance directly
                var invertedSource = luminanceSource.invert();
                zxResults = reader.DecodeMultiple(invertedSource);
                zxResult = zxResults?.FirstOrDefault() ?? reader.Decode(invertedSource);
            }

            sw.Stop();
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

            var itemsToProcess = zxResults != null && zxResults.Length > 0
                ? zxResults
                : (zxResult != null ? new[] { zxResult } : Array.Empty<ZXing.Result>());

            if (itemsToProcess.Length > 0)
            {
                var processedList = new List<CodeReadResult>();
                foreach (var zx in itemsToProcess)
                {
                    var item = new CodeReadResult
                    {
                        Success = true,
                        Text = zx.Text ?? string.Empty,
                        Format = zx.BarcodeFormat,
                        FormatName = zx.BarcodeFormat.ToString(),
                        ExecutionTimeMs = result.ExecutionTimeMs
                    };

                    var pts = new List<Point2f>();
                    if (zx.ResultPoints != null)
                    {
                        foreach (var p in zx.ResultPoints)
                        {
                            if (p != null)
                            {
                                pts.Add(new Point2f(p.X + offsetX, p.Y + offsetY));
                            }
                        }
                    }

                    item.Points = pts;

                    if (pts.Count > 0)
                    {
                        float minX = float.MaxValue, minY = float.MaxValue;
                        float maxX = float.MinValue, maxY = float.MinValue;
                        float sumX = 0, sumY = 0;

                        foreach (var p in pts)
                        {
                            minX = Math.Min(minX, p.X);
                            minY = Math.Min(minY, p.Y);
                            maxX = Math.Max(maxX, p.X);
                            maxY = Math.Max(maxY, p.Y);
                            sumX += p.X;
                            sumY += p.Y;
                        }

                        item.Center = new Point2f(sumX / pts.Count, sumY / pts.Count);
                        item.BoundingBox = new Rect2f(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));

                        if (pts.Count >= 2)
                        {
                            var dx = pts[1].X - pts[0].X;
                            var dy = pts[1].Y - pts[0].Y;
                            item.AngleDeg = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                        }
                    }
                    else if (roi.HasValue)
                    {
                        item.Center = new Point2f(roi.Value.X + roi.Value.Width * 0.5f, roi.Value.Y + roi.Value.Height * 0.5f);
                        item.BoundingBox = new Rect2f(roi.Value.X, roi.Value.Y, roi.Value.Width, roi.Value.Height);
                    }

                    processedList.Add(item);
                }

                var primary = processedList[0];
                result.Success = primary.Success;
                result.Text = primary.Text;
                result.Format = primary.Format;
                result.FormatName = primary.FormatName;
                result.Points = primary.Points;
                result.BoundingBox = primary.BoundingBox;
                result.Center = primary.Center;
                result.AngleDeg = primary.AngleDeg;
                if (processedList.Count > 1)
                {
                    result.AdditionalResults = processedList.Skip(1).ToList();
                }
            }
        }
        finally
        {
            if (needDisposeGray) grayMat.Dispose();
            if (needDisposeWork) workMat.Dispose();
        }

        return result;
    }

    public static IList<BarcodeFormat>? GetTargetFormats(CodeKind kind)
    {
        return kind switch
        {
            CodeKind.QrCode => new[] { BarcodeFormat.QR_CODE },
            CodeKind.DataMatrix => new[] { BarcodeFormat.DATA_MATRIX },
            CodeKind.Code128 => new[] { BarcodeFormat.CODE_128 },
            CodeKind.Code39 => new[] { BarcodeFormat.CODE_39 },
            CodeKind.Ean13 => new[] { BarcodeFormat.EAN_13 },
            CodeKind.UpcA => new[] { BarcodeFormat.UPC_A },
            CodeKind.Pdf417 => new[] { BarcodeFormat.PDF_417 },
            CodeKind.Aztec => new[] { BarcodeFormat.AZTEC },
            _ => null // All formats
        };
    }

    public static Mat CreateSampleCodeMat(BarcodeFormat format, string text, int width = 300, int height = 300)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 2
            }
        };

        var pixelData = writer.Write(text);
        var mat = new Mat(pixelData.Height, pixelData.Width, MatType.CV_8UC4);
        Marshal.Copy(pixelData.Pixels, 0, mat.Data, pixelData.Pixels.Length);
        var bgr = mat.CvtColor(ColorConversionCodes.BGRA2BGR);
        mat.Dispose();
        return bgr;
    }
}
