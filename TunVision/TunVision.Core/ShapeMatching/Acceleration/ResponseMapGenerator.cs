using System;
using System.Runtime.CompilerServices;
using OpenCvSharp;

namespace TunVision.Core.ShapeMatching.Acceleration;

public sealed class ResponseMapGenerator : IDisposable
{
    // Cosine lookup table scaled by 127 for 16 sectors (22.5° per sector)
    private static readonly sbyte[] CosTable = new sbyte[16]
    {
        127, 118, 90, 48, 0, -48, -90, -118, -127, -118, -90, -48, 0, 48, 90, 118
    };

    // Fixed-point tangent thresholds (scale 4096 = 12-bit shift)
    private const int Tan11_25 = 815;   // tan(11.25°) * 4096
    private const int Tan33_75 = 2736;  // tan(33.75°) * 4096
    private const int Tan56_25 = 6128;  // tan(56.25°) * 4096
    private const int Tan78_75 = 19267; // tan(78.75°) * 4096

    public sbyte[][] ResponseMaps { get; private set; } = Array.Empty<sbyte[]>();
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>
    /// Build 16 cosine response maps from a grayscale image.
    /// Pixels below contrastThreshold receive 0 across all maps.
    /// </summary>
    public unsafe void Build(Mat grayImage, bool ignorePolarity, float contrastThreshold = 10f)
    {
        Width = grayImage.Cols;
        Height = grayImage.Rows;
        int totalPixels = Width * Height;

        ResponseMaps = new sbyte[16][];
        for (int k = 0; k < 16; k++)
            ResponseMaps[k] = new sbyte[totalPixels];

        using var sx = new Mat();
        using var sy = new Mat();
        Cv2.Sobel(grayImage, sx, MatType.CV_16S, 1, 0, 3);
        Cv2.Sobel(grayImage, sy, MatType.CV_16S, 0, 1, 3);

        short* pGx = (short*)sx.Data.ToPointer();
        short* pGy = (short*)sy.Data.ToPointer();
        int step = (int)(sx.Step() / sizeof(short));

        float threshSq = contrastThreshold * contrastThreshold;

        for (int y = 0; y < Height; y++)
        {
            int rowOff = y * step;
            int pixOff = y * Width;

            for (int x = 0; x < Width; x++)
            {
                short gx = pGx[rowOff + x];
                short gy = pGy[rowOff + x];
                int magSq = gx * gx + gy * gy;

                if (magSq < threshSq)
                {
                    continue; // Map buffers are zero-initialized
                }

                int sector = QuantizeOrientation(gx, gy);

                for (int k = 0; k < 16; k++)
                {
                    int diff = (k - sector) & 15;
                    sbyte val = CosTable[diff];

                    if (ignorePolarity)
                    {
                        // Branchless absolute value
                        int mask = val >> 7;
                        val = (sbyte)((val ^ mask) - mask);
                    }

                    ResponseMaps[k][pixOff + x] = val;
                }
            }
        }
    }

    /// <summary>
    /// Spreads response maps by taking max in local cross neighborhood (radius = 1: center + 4 neighbors).
    /// Eliminates integer grid quantization loss and makes matching robust against slight deformation.
    /// </summary>
    public void Spread(int radius = 1)
    {
        if (radius <= 0 || ResponseMaps.Length == 0) return;
        int w = Width;
        int h = Height;
        int total = w * h;

        for (int k = 0; k < 16; k++)
        {
            var src = ResponseMaps[k];
            var dst = new sbyte[total];

            for (int y = 1; y < h - 1; y++)
            {
                int rowOff = y * w;
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = rowOff + x;
                    sbyte max = src[idx];
                    sbyte v1 = src[idx - 1];
                    sbyte v2 = src[idx + 1];
                    sbyte v3 = src[idx - w];
                    sbyte v4 = src[idx + w];

                    if (v1 > max) max = v1;
                    if (v2 > max) max = v2;
                    if (v3 > max) max = v3;
                    if (v4 > max) max = v4;

                    dst[idx] = max;
                }
            }
            ResponseMaps[k] = dst;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int QuantizeOrientation(short gx, short gy)
    {
        int absX = Math.Abs((int)gx);
        int absY = Math.Abs((int)gy);
        if (absX == 0) absX = 1;

        long ratio = ((long)absY << 12) / absX;
        int octant;

        if (ratio < Tan11_25) octant = 0;
        else if (ratio < Tan33_75) octant = 1;
        else if (ratio < Tan56_25) octant = 2;
        else if (ratio < Tan78_75) octant = 3;
        else octant = 4;

        if (gx >= 0 && gy >= 0) return octant;                // Quadrant 1
        if (gx < 0 && gy >= 0)  return (8 - octant) & 15;     // Quadrant 2
        if (gx < 0 && gy < 0)   return (8 + octant) & 15;     // Quadrant 3
        return (16 - octant) & 15;                             // Quadrant 4
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetFeatureSector(float nx, float ny)
    {
        short gx = (short)Math.Clamp((int)(nx * 4096f), short.MinValue, short.MaxValue);
        short gy = (short)Math.Clamp((int)(ny * 4096f), short.MinValue, short.MaxValue);
        return QuantizeOrientation(gx, gy);
    }

    public void Dispose()
    {
        ResponseMaps = Array.Empty<sbyte[]>();
    }
}
