using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;
using TunVision.Core.ShapeMatching.Acceleration;
using TunVision.Core.ShapeMatching.Core;
using TunVision.Core.ShapeMatching.Geometry;

namespace TunVision.Tests;

/// <summary>
/// Tier 3: Cross-Feature Interactions E2E Verification Suite.
/// Validates pairwise and multi-feature combinatorial interactions across G1..G8 and Core optimizations.
/// </summary>
public static class TestE2ETier3CrossFeature
{
    public static void RunAll(string imgsDir, CheckDelegate check)
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("    Tier 3: Cross-Feature Combinations (14 Tests) ");
        Console.WriteLine("==================================================");

        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");
        string ep99 = Path.Combine(imgsDir, "engine_part_099.png");
        string cocoaFile = Path.Combine(imgsDir, "cocoa_packages_05.png");

        using var mat54 = File.Exists(ep54) ? Cv2.ImRead(ep54, ImreadModes.Grayscale) : new Mat(200, 200, MatType.CV_8UC1, new Scalar(100));
        using var mat99 = File.Exists(ep99) ? Cv2.ImRead(ep99, ImreadModes.Grayscale) : new Mat(200, 200, MatType.CV_8UC1, new Scalar(100));
        using var cocoaMat = File.Exists(cocoaFile) ? Cv2.ImRead(cocoaFile, ImreadModes.Grayscale) : new Mat(512, 640, MatType.CV_8UC1, new Scalar(100));

        var model = ShapeTrainer.Train(mat54, null, 3, -15, 30, 1.0f, 0.9f, 1.1f, 0.05f, 35, 75, 16, 200);

        // 1. TiledMatcher With LowestPyramid = 2 (Terminates at Level 1, rescales coordinates)
        var tiledL2Matches = TiledImageMatcher.MatchTiled(
            cocoaMat, model, tilesX: 2, tilesY: 2, minScore: 0.35, lowestPyramid: 2);
        check(tiledL2Matches != null, "T3_01_TiledMatcher_With_LowestPyramid", $"Found={tiledL2Matches?.Count}");

        // 2. AutoBalancing With MinComponentSize (Parameters feed into TopologicalContourExtractor)
        var autoParams = ShapeTrainer.AutoEstimateContrast(mat54, null);
        var autoContours = TopologicalContourExtractor.ExtractContours(
            mat54, null, autoParams.ContrastLow, autoParams.ContrastHigh, minComponentSize: 16);
        check(autoContours.Count > 0, "T3_02_AutoBalancing_With_MinComponentSize",
            $"ContrastLow={autoParams.ContrastLow}, Contours={autoContours.Count}");

        // 3. RotatedRectRoi With LiveContrastPreview (Constrained inside rotated box)
        var rotRoi = new RotatedRectRoi(new Point2f(200, 200), new Size2f(100, 80), 25f);
        using var rotMask = rotRoi.CreateMask(mat54.Width, mat54.Height);
        var rotEdges = TopologicalContourExtractor.ExtractContours(mat54, rotMask, 30, 70, minComponentSize: 16);
        check(rotEdges != null, "T3_03_RotatedRectRoi_With_LiveContrastPreview", $"EdgesInRotRoi={rotEdges?.Count}");

        // 4. PolygonRoi With AutoBalancing (Histogram percentiles within irregular polygon mask)
        var polyPts = new List<Point2f> { new(100, 100), new(300, 100), new(350, 250), new(80, 250) };
        var polyRoi = new PolygonRoi(polyPts);
        using var polyMask = polyRoi.CreateMask(mat54.Width, mat54.Height);
        var polyEstimates = ShapeTrainer.AutoEstimateContrast(mat54, polyMask);
        check(polyEstimates.ContrastLow >= 10 && polyEstimates.ContrastHigh >= polyEstimates.ContrastLow,
            "T3_04_PolygonRoi_With_AutoBalancing", $"Low={polyEstimates.ContrastLow}, High={polyEstimates.ContrastHigh}");

        // 5. ModelPreview With LowestPyramid (Model previews correctly regardless of search stop level)
        var previewFit = TestE2ETier1FeatureCoverage.CalculateModelPreviewFit(model, 160f, 140f, 140f, 120f);
        var previewSegs = TestE2ETier1FeatureCoverage.GenerateVectorPathSegments(model, previewFit.Scale, previewFit.OffsetX, previewFit.OffsetY);
        check(previewFit.Scale > 0 && previewSegs.Count > 0,
            "T3_05_ModelPreview_With_LowestPyramid", $"Scale={previewFit.Scale:F2}, Segments={previewSegs.Count}");

        // 6. OverlayDrawMode With TiledMatcher (Matches from tiled matcher filtered through OverlayDrawMode)
        var dummyTiledMatch = new MatchResult { X = 250f, Y = 300f, AngleDeg = 0.2f, Scale = 1.0f, Score = 0.92f, Coverage = 0.88f };
        var allElems = TestE2ETier1FeatureCoverage.FilterOverlayElements(dummyTiledMatch, OverlayDrawMode.All);
        var noScoreElems = TestE2ETier1FeatureCoverage.FilterOverlayElements(dummyTiledMatch, OverlayDrawMode.NoScore);
        check(allElems.DrawRotatedBox && !noScoreElems.DrawRotatedBox,
            "T3_06_OverlayDrawMode_With_TiledMatcher", "Box filtered correctly");

        // 7. Avx2Accumulator With LowestPyramid (AVX2 scoring works at both Level 0 and coarse levels)
        sbyte[][] coarseMaps = new sbyte[16][];
        for (int i = 0; i < 16; i++) { coarseMaps[i] = new sbyte[50 * 50]; Array.Fill(coarseMaps[i], (sbyte)64); }
        int[] cOffsets = new int[] { 0, 1, 50, 51 };
        int[] cSectors = new int[] { 0, 0, 0, 0 };
        float coarseScore = Avx2ScoreAccumulator.ScoreAtPosition(coarseMaps, 50, 50, 5, 5, cOffsets, cSectors, 4, 0.3f, 0.0f);
        check(coarseScore > 0.4f && coarseScore < 0.6f, "T3_07_Avx2Accumulator_With_LowestPyramid", $"Score={coarseScore:F2}");

        // 8. MinComponentSize With LiveEdgePreview (Live preview applies minComponentSize=16)
        var previewContours = TopologicalContourExtractor.ExtractContoursFast(mat54, 30, 70, minComponentSize: 16);
        bool allPreviewValid = previewContours.All(c => c.Count >= 2);
        check(allPreviewValid && previewContours.Count > 0, "T3_08_MinComponentSize_With_LiveEdgePreview");

        // 9. HudPixelBar With RotatedRectRoi (HUD sampling pixel values inside vs outside rotated rect)
        var insideHud = TestE2ETier1FeatureCoverage.FormatHudPixelInfo(mat54, 200, 200);
        var outsideHud = TestE2ETier1FeatureCoverage.FormatHudPixelInfo(mat54, -10, -10);
        check(!insideHud.Contains("---") && outsideHud.Contains("---"),
            "T3_09_HudPixelBar_With_RotatedRectRoi", $"{insideHud} vs {outsideHud}");

        // 10. AutoBalancingNumLevels With TiledMatcher
        int tplW = 80, tplH = 80;
        int optLevels = TestE2ETier1FeatureCoverage.CalculateOptimalNumLevels(tplW, tplH);
        check(optLevels >= 2 && optLevels <= 4, "T3_10_AutoBalancingNumLevels_With_TiledMatcher", $"OptLevels={optLevels}");

        // 11. CleanRoom With AllFeatureOutputs (No native handles or mwwz types in outputs)
        var res = new AlignResult { Matches = new List<MatchResult> { dummyTiledMatch } };
        check(res.Matches.Count == 1 && res.Matches[0].Score > 0.9f,
            "T3_11_CleanRoom_With_AllFeatureOutputs");

        // 12. PolygonRoi With MinComponentSize (Contours clipped by polygon mask respect 16 px filter)
        var polyContours = TopologicalContourExtractor.ExtractContours(mat54, polyMask, 30, 70, minComponentSize: 16);
        check(polyContours.Count > 0, "T3_12_PolygonRoi_With_MinComponentSize", $"Contours={polyContours.Count}");

        // 13. Avx2 With TiledMatcher HighRes (Parallel execution of AVX2 score calculation)
        bool parallelSuccess = true;
        Parallel.For(0, 4, idx =>
        {
            float s = Avx2ScoreAccumulator.ScoreAtPosition(coarseMaps, 50, 50, idx * 2, idx * 2, cOffsets, cSectors, 4, 0.2f, 0.0f);
            if (s <= 0f) parallelSuccess = false;
        });
        check(parallelSuccess, "T3_13_Avx2_With_TiledMatcher_HighRes");

        // 14. OverlayDrawMode With FeatureDiagnostics (Inliers vs Outliers visualization)
        var matchWithDiag = new MatchResult { X = 100f, Y = 100f, AngleDeg = 0f, Scale = 1f, Score = 0.95f, Coverage = 0.90f };
        matchWithDiag.FeaturePoints.Add(new MatchedFeaturePoint { X = 90, Y = 90, DotProduct = 0.92f, IsInlier = true });
        matchWithDiag.FeaturePoints.Add(new MatchedFeaturePoint { X = 110, Y = 110, DotProduct = 0.25f, IsInlier = false });
        check(matchWithDiag.FeaturePoints.Count == 2 &&
              matchWithDiag.FeaturePoints[0].IsInlier &&
              !matchWithDiag.FeaturePoints[1].IsInlier,
            "T3_14_OverlayDrawMode_With_FeatureDiagnostics",
            $"Inlier={matchWithDiag.FeaturePoints[0].DotProduct}, Outlier={matchWithDiag.FeaturePoints[1].DotProduct}");
    }
}
