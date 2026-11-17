using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace TunVision.Core.ShapeMatching.Geometry;

public struct SubPixelEdgePoint
{
    public float X;
    public float Y;
    public float Nx;
    public float Ny;
    public float Response;
    public byte State; // 0: None, 1: Weak Candidate, 2: Strong Seed
    public readonly float Magnitude => Response;
}

public static class TopologicalContourExtractor
{
    /// <summary>
    /// Extracts subpixel continuous contours based on Carsten Steger (1998) differential geometry model,
    /// 3x3 Chebyshev orthogonal polynomial facet fitting, bidirectional Hysteresis edge following,
    /// and topological connected component length filtering (MinComponentSize).
    /// Eliminates texture noise and micro-scratches while preserving sharp subpixel contours.
    /// </summary>
    public static unsafe List<List<SubPixelEdgePoint>> ExtractContours(
        Mat grayImage,
        Mat? mask,
        int contrastLow,
        int contrastHigh,
        int minComponentSize = 16,
        int minContrast = 9)
    {
        if (grayImage == null)
            throw new ArgumentNullException(nameof(grayImage));
        if (grayImage.IsDisposed || grayImage.Empty())
            return new List<List<SubPixelEdgePoint>>();

        int w = grayImage.Width;
        int h = grayImage.Height;
        if (w < 5 || h < 5)
            return new List<List<SubPixelEdgePoint>>();

        contrastLow = Math.Clamp(contrastLow, 1, 250);
        contrastHigh = Math.Clamp(Math.Max(contrastLow + 1, contrastHigh), 2, 255);
        minComponentSize = Math.Max(0, minComponentSize);
        minContrast = Math.Clamp(minContrast, 1, 250);

        // 1. Gaussian blur (3x3, sigma = 0.75) to suppress sensor quantization noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(grayImage, blurred, new Size(3, 3), 0.75);

        // 2. Sobel derivative CV_32F with 0.25 normalization scale (MWWZ industrial standard)
        using var dx = new Mat();
        using var dy = new Mat();
        Cv2.Sobel(blurred, dx, MatType.CV_32F, 1, 0, 3, scale: 0.25);
        Cv2.Sobel(blurred, dy, MatType.CV_32F, 0, 1, 3, scale: 0.25);

        float* pDx = (float*)dx.Data.ToPointer();
        float* pDy = (float*)dy.Data.ToPointer();
        byte* pMask = mask != null ? (byte*)mask.Data.ToPointer() : null;

        int stepGrad = (int)(dx.Step() / sizeof(float));
        int stepMask = mask != null ? (int)mask.Step() : 0;

        float[] mag = new float[w * h];
        SubPixelEdgePoint[] grid = new SubPixelEdgePoint[w * h];
        byte[] stateMap = new byte[w * h];
        var seeds = new List<int>(w * h / 32);

        float cLow = Math.Max((float)minContrast, (float)contrastLow);
        float cHigh = Math.Max(cLow + 1.0f, (float)contrastHigh);

        // Pass 1: Compute full gradient magnitude map
        for (int y = 0; y < h; y++)
        {
            float* rDx = pDx + y * stepGrad;
            float* rDy = pDy + y * stepGrad;
            byte* rMask = pMask != null ? pMask + y * stepMask : null;
            int rOff = y * w;

            for (int x = 0; x < w; x++)
            {
                if (rMask != null && rMask[x] == 0) continue;
                float gx = rDx[x];
                float gy = rDy[x];
                mag[rOff + x] = MathF.Sqrt(gx * gx + gy * gy);
            }
        }

        // Pass 2: Chebyshev 3x3 facet polynomial fitting & Steger subpixel extrema localization
        for (int y = 1; y < h - 1; y++)
        {
            float* rDx = pDx + y * stepGrad;
            float* rDy = pDy + y * stepGrad;
            byte* rMask = pMask != null ? pMask + y * stepMask : null;
            int rOff = y * w;
            int prev = (y - 1) * w;
            int next = (y + 1) * w;

            for (int x = 1; x < w - 1; x++)
            {
                if (rMask != null && rMask[x] == 0) continue;

                float m = mag[rOff + x];
                if (m < cLow) continue;

                float gx = rDx[x];
                float gy = rDy[x];
                float invM = 1.0f / m;
                float nx = gx * invM;
                float ny = gy * invM;

                // 3x3 gradient magnitude neighborhood:
                // [ z0  z1  z2 ]
                // [ z3  z4  z5 ]
                // [ z6  z7  z8 ]
                float z0 = mag[prev + x - 1], z1 = mag[prev + x], z2 = mag[prev + x + 1];
                float z3 = mag[rOff + x - 1], z4 = m,             z5 = mag[rOff + x + 1];
                float z6 = mag[next + x - 1], z7 = mag[next + x], z8 = mag[next + x + 1];

                // Discrete Chebyshev orthogonal polynomial 2nd-degree facet coefficients
                float a1 = ((z2 - z0) + (z5 - z3) + (z8 - z6)) * (1.0f / 6.0f);
                float a2 = ((z6 - z0) + (z7 - z1) + (z8 - z2)) * (1.0f / 6.0f);
                float a4 = (z0 - z2 - z6 + z8) * 0.25f;
                float a3 = ((z0 + z2 + z3 + z5 + z6 + z8) - 2.0f * (z1 + z4 + z7)) * (1.0f / 6.0f);
                float a5 = ((z0 + z1 + z2 + z6 + z7 + z8) - 2.0f * (z3 + z4 + z5)) * (1.0f / 6.0f);

                // First and second directional derivatives along gradient normal vector (nx, ny)
                float fn = a1 * nx + a2 * ny;
                float fnn = 2.0f * a3 * nx * nx + 2.0f * a4 * nx * ny + 2.0f * a5 * ny * ny;

                // Strict Steger Ridge & Non-Maximum Suppression (NMS) Condition:
                // 1. Curvature along gradient normal must be strictly negative (fnn < -1e-5f)
                // 2. Extrema displacement must lie within current pixel cell (|t| <= 0.6f)
                if (fnn >= -1e-5f) continue;
                float t = -fn / fnn;
                if (MathF.Abs(t) > 0.6f) continue;

                float px = nx * t;
                float py = ny * t;

                // Subpixel peak response
                float peakMag = z4 + a1 * px + a2 * py + a3 * px * px + a4 * px * py + a5 * py * py;
                if (peakMag < cLow) continue;

                byte state = (byte)(peakMag >= cHigh ? 2 : 1);
                int idx = rOff + x;
                grid[idx] = new SubPixelEdgePoint
                {
                    X = x + px,
                    Y = y + py,
                    Nx = nx,
                    Ny = ny,
                    Response = peakMag,
                    State = state
                };
                stateMap[idx] = state;

                if (state == 2) seeds.Add(idx);
            }
        }

        // If no strong seeds were found, fallback to top candidate points
        if (seeds.Count == 0)
        {
            for (int i = 0; i < stateMap.Length; i++)
            {
                if (stateMap[i] >= 1)
                    seeds.Add(i);
            }
        }

        // Sort seeds descending by peak magnitude
        seeds.Sort((a, b) => grid[b].Response.CompareTo(grid[a].Response));

        // 3. Bidirectional Hysteresis edge following + MinComponentSize filtering
        var contours = new List<List<SubPixelEdgePoint>>();
        byte[] visited = new byte[w * h];

        int[] dxN = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dyN = { 0, 1, 1, 1, 0, -1, -1, -1 };
        float[] invNorm = { 1.0f, 0.70710678f, 1.0f, 0.70710678f, 1.0f, 0.70710678f, 1.0f, 0.70710678f };

        foreach (int seedIdx in seeds)
        {
            if (visited[seedIdx] != 0) continue;

            var contour = new List<SubPixelEdgePoint>();

            // Forward direction 1: along tangent (+v = (-ny, nx))
            int curr = seedIdx;
            visited[curr] = 1;
            contour.Add(grid[curr]);

            while (true)
            {
                int nextIdx = -1;
                float bestAlign = -1.0f;
                float tx = -grid[curr].Ny;
                float ty = grid[curr].Nx;

                int cx = curr % w;
                int cy = curr / w;

                for (int n = 0; n < 8; n++)
                {
                    int nxCoord = cx + dxN[n];
                    int nyCoord = cy + dyN[n];
                    if (nxCoord < 1 || nxCoord >= w - 1 || nyCoord < 1 || nyCoord >= h - 1) continue;

                    int nIdx = nyCoord * w + nxCoord;
                    if (visited[nIdx] != 0 || stateMap[nIdx] < 1) continue;

                    // Normalize jump vector to prevent diagonal bias
                    float align = (tx * dxN[n] + ty * dyN[n]) * invNorm[n];
                    if (align > bestAlign && align > 0f)
                    {
                        bestAlign = align;
                        nextIdx = nIdx;
                    }
                }

                if (nextIdx == -1) break;
                visited[nextIdx] = 1;
                contour.Add(grid[nextIdx]);
                curr = nextIdx;
            }

            // Reverse direction 2: along negative tangent (-v = (ny, -nx))
            contour.Reverse();
            curr = seedIdx;

            while (true)
            {
                int nextIdx = -1;
                float bestAlign = -1.0f;
                float tx = grid[curr].Ny;  // -(-ny) = ny
                float ty = -grid[curr].Nx; // -nx

                int cx = curr % w;
                int cy = curr / w;

                for (int n = 0; n < 8; n++)
                {
                    int nxCoord = cx + dxN[n];
                    int nyCoord = cy + dyN[n];
                    if (nxCoord < 1 || nxCoord >= w - 1 || nyCoord < 1 || nyCoord >= h - 1) continue;

                    int nIdx = nyCoord * w + nxCoord;
                    if (visited[nIdx] != 0 || stateMap[nIdx] < 1) continue;

                    float align = (tx * dxN[n] + ty * dyN[n]) * invNorm[n];
                    if (align > bestAlign && align > 0f)
                    {
                        bestAlign = align;
                        nextIdx = nIdx;
                    }
                }

                if (nextIdx == -1) break;
                visited[nextIdx] = 1;
                contour.Add(grid[nextIdx]);
                curr = nextIdx;
            }

            // 4. MinComponentSize filter: calculate total Euclidean arc length
            float contourLength = 0f;
            for (int ci = 1; ci < contour.Count; ci++)
            {
                float cdx = contour[ci].X - contour[ci - 1].X;
                float cdy = contour[ci].Y - contour[ci - 1].Y;
                contourLength += MathF.Sqrt(cdx * cdx + cdy * cdy);
            }

            if (contourLength >= minComponentSize)
            {
                contours.Add(contour);
            }
        }

        return contours;
    }

    /// <summary>
    /// Fast contour extraction preview with default minComponentSize = 16 and minContrast = 5.
    /// Used for interactive real-time parameter tuning and previews.
    /// </summary>
    public static List<List<SubPixelEdgePoint>> ExtractContoursFast(
        Mat grayImage,
        int contrastLow,
        int contrastHigh,
        int minComponentSize = 16)
    {
        return ExtractContours(grayImage, null, contrastLow, contrastHigh, minComponentSize, minContrast: 5);
    }
}
