using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using TunVision.Core.ShapeMatching.Core;

namespace TunVision.Core.ShapeMatching.Acceleration;

public static class Avx2ScoreAccumulator
{
    /// <summary>
    /// Computes normalized matching score [0.0, 1.0] at target position (posX, posY).
    /// Uses pre-computed feature linear offsets and sectors.
    /// Implements branch-and-bound pruning (Greediness) at intervals.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static float ScoreAtPosition(
        sbyte[][] responseMaps,
        int mapWidth,
        int mapHeight,
        int posX,
        int posY,
        int[] featureOffsets,
        int[] featureSectors,
        int featureCount,
        float minScore,
        float greediness)
    {
        if (featureCount == 0) return 0f;

        int stride = mapWidth;
        int baseOffset = posY * stride + posX;
        int totalPixels = mapWidth * mapHeight;

        int sum = 0;
        int checkInterval = Math.Max(8, featureCount / 4);
        int maxFeatureScore = 127;
        int maxPossibleTotal = maxFeatureScore * featureCount;

        for (int i = 0; i < featureCount; i++)
        {
            int mapOffset = baseOffset + featureOffsets[i];
            if (mapOffset >= 0 && mapOffset < totalPixels)
            {
                int sector = featureSectors[i];
                sum += responseMaps[sector][mapOffset];
            }

            // Greediness branch-and-bound upper bound check:
            // Stop early if best possible remaining score cannot meet threshold
            if (i == checkInterval && greediness > 0f)
            {
                int remainingFeatures = featureCount - 1 - i;
                int maxPossible = sum + maxFeatureScore * remainingFeatures;
                if (maxPossible < (int)(minScore * greediness * maxPossibleTotal))
                {
                    return 0f;
                }
            }
        }

        return Math.Max(0f, (float)sum / maxPossibleTotal);
    }

    /// <summary>
    /// Pre-computes 1D linear stride offsets and feature sectors for all features
    /// at a given angle (radians) and scale.
    /// </summary>
    public static void PrecomputeOffsets(
        IReadOnlyList<StandardFeature> features,
        float angleRad,
        float scale,
        int stride,
        out int[] offsets,
        out int[] sectors)
    {
        int n = features.Count;
        offsets = new int[n];
        sectors = new int[n];

        float cosA = MathF.Cos(angleRad);
        float sinA = MathF.Sin(angleRad);

        for (int i = 0; i < n; i++)
        {
            var f = features[i];
            float rx = scale * (f.X * cosA - f.Y * sinA);
            float ry = scale * (f.X * sinA + f.Y * cosA);
            offsets[i] = (int)MathF.Round(ry) * stride + (int)MathF.Round(rx);

            // Rotate normal vector
            float rnx = f.Nx * cosA - f.Ny * sinA;
            float rny = f.Nx * sinA + f.Ny * cosA;
            sectors[i] = ResponseMapGenerator.GetFeatureSector(rnx, rny);
        }
    }

    /// <summary>
    /// Overload for legacy ShapeFeature list.
    /// </summary>
    public static void PrecomputeOffsets(
        IReadOnlyList<ShapeFeature> features,
        float angleRad,
        float scale,
        int stride,
        out int[] offsets,
        out int[] sectors)
    {
        int n = features.Count;
        offsets = new int[n];
        sectors = new int[n];

        float cosA = MathF.Cos(angleRad);
        float sinA = MathF.Sin(angleRad);

        for (int i = 0; i < n; i++)
        {
            var f = features[i];
            float rx = scale * (f.X * cosA - f.Y * sinA);
            float ry = scale * (f.X * sinA + f.Y * cosA);
            offsets[i] = (int)MathF.Round(ry) * stride + (int)MathF.Round(rx);

            float rdx = f.Dx * cosA - f.Dy * sinA;
            float rdy = f.Dx * sinA + f.Dy * cosA;
            sectors[i] = ResponseMapGenerator.GetFeatureSector(rdx, rdy);
        }
    }

    /// <summary>
    /// Accumulates 32 int8 values into 32 int16 accumulators using AVX2 SIMD (vpmovsxbw + vpaddsw).
    /// Safe against unaligned memory access via Vector256 unaligned load/store.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe void AccumulateBlock32(
        sbyte* pResponseMap,
        int linearOffset,
        short* pAccumulator)
    {
        if (Avx2.IsSupported)
        {
            Vector128<sbyte> raw0 = Sse2.LoadVector128(pResponseMap + linearOffset);
            Vector256<short> wide0 = Avx2.ConvertToVector256Int16(raw0);

            Vector128<sbyte> raw1 = Sse2.LoadVector128(pResponseMap + linearOffset + 16);
            Vector256<short> wide1 = Avx2.ConvertToVector256Int16(raw1);

            Vector256<short> acc0 = Avx.LoadVector256(pAccumulator);
            Vector256<short> acc1 = Avx.LoadVector256(pAccumulator + 16);

            acc0 = Avx2.AddSaturate(acc0, wide0);
            acc1 = Avx2.AddSaturate(acc1, wide1);

            Avx.Store(pAccumulator, acc0);
            Avx.Store(pAccumulator + 16, acc1);
        }
        else
        {
            sbyte* pSrc = pResponseMap + linearOffset;
            for (int i = 0; i < 32; i++)
            {
                int val = pAccumulator[i] + pSrc[i];
                pAccumulator[i] = (short)Math.Clamp(val, short.MinValue, short.MaxValue);
            }
        }
    }

    /// <summary>
    /// Managed array overload for AccumulateBlock32 using fixed pointer pinning.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe void AccumulateBlock32(
        sbyte[] responseMap,
        int linearOffset,
        short[] accumulator,
        int accOffset = 0)
    {
        fixed (sbyte* pMap = responseMap)
        fixed (short* pAcc = accumulator)
        {
            AccumulateBlock32(pMap, linearOffset, pAcc + accOffset);
        }
    }
}
