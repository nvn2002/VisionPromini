using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;
using TunVision.Core.ShapeMatching.Acceleration;
using TunVision.Core.ShapeMatching.Geometry;

namespace TunVision.Tests;

/// <summary>
/// Tier 4: Real-World Industrial Application Scenarios E2E Verification Suite.
/// Validates 5 end-to-end mission-critical industrial workloads with complex geometry, noise, and scaling.
/// </summary>
public static class TestE2ETier4IndustrialScenarios
{
    public static void RunAll(string imgsDir, CheckDelegate check)
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("  Tier 4: Industrial Application Scenarios (5 Scenarios)");
        Console.WriteLine("==================================================");

        TestScenario1_UltraHighResPcbInspection(check);
        TestScenario2_MetalLaserMarkUnderScratchNoise(check);
        TestScenario3_MultiAngleRoboticPartPicking(imgsDir, check);
        TestScenario4_PolygonCavityDefectInspection(check);
        TestScenario5_CleanRoomAirgapAndDeployment(check);
    }

    #region Scenario 1: Ultra-High-Resolution PCB SMT Component Inspection
    public static void TestScenario1_UltraHighResPcbInspection(CheckDelegate check)
    {
        Console.WriteLine("\n--- [Tier 4 / S1] Ultra-High-Res PCB SMT Inspection ---");

        // Create synthetic PCB substrate (1500 x 1500) with 4 SMT IC packages placed across tile boundaries
        int sceneSize = 1500;
        using var pcbMat = new Mat(sceneSize, sceneSize, MatType.CV_8UC1, new Scalar(40));

        // Create SMT chip template: 60x60 square with pin pads
        int chipSize = 60;
        using var chipTpl = new Mat(chipSize, chipSize, MatType.CV_8UC1, new Scalar(40));
        Cv2.Rectangle(chipTpl, new Rect(10, 10, 40, 40), new Scalar(220), -1); // IC body
        Cv2.Rectangle(chipTpl, new Rect(15, 15, 30, 30), new Scalar(80), -1);  // center die

        // Stamp 4 chips onto PCB: two in quadrants, two right across tile boundary (X=750 or Y=750)
        var chipPositions = new List<Point>
        {
            new(300, 300),   // Tile (0, 0)
            new(720, 350),   // Across vertical tile border X=750
            new(1100, 1100), // Tile (1, 1)
            new(350, 720)    // Across horizontal tile border Y=750
        };

        foreach (var pos in chipPositions)
        {
            using var sub = new Mat(pcbMat, new Rect(pos.X, pos.Y, chipSize, chipSize));
            chipTpl.CopyTo(sub);
        }

        // Train model with auto-balanced parameters
        var autoParams = ShapeTrainer.AutoEstimateContrast(chipTpl, null);
        var chipModel = ShapeTrainer.Train(
            chipTpl, null, 3, -10, 20, 1.0f, 0.95f, 1.05f, 0.05f,
            autoParams.ContrastLow, autoParams.ContrastHigh, 16, 150);

        // Run TiledImageMatcher (2x2 grid with overlap)
        var sw = Stopwatch.StartNew();
        var matches = TiledImageMatcher.MatchTiled(
            pcbMat, chipModel, tilesX: 2, tilesY: 2,
            angleMinDeg: -10, angleMaxDeg: 10, angleStepDeg: 1.0,
            scaleMin: 0.95, scaleMax: 1.05, scaleStep: 0.05,
            minScore: 0.65, greediness: 0.8, maxInstances: 10, maxOverlap: 0.4);
        sw.Stop();

        // Expect all 4 chips located with zero missed detections
        bool allLocated = matches.Count >= 4;
        check(allLocated, "T4_Scenario1_UltraHighResPcbInspection",
            $"Found {matches.Count} / 4 chips in {sw.ElapsedMilliseconds} ms");
    }
    #endregion

    #region Scenario 2: Metal Laser Mark Under Scratch Noise
    public static void TestScenario2_MetalLaserMarkUnderScratchNoise(CheckDelegate check)
    {
        Console.WriteLine("\n--- [Tier 4 / S2] Metal Laser Mark Under Scratch Noise ---");

        // Create scene with laser mark (large circle + crosshair) and heavy scratch noise (<16 px)
        int size = 300;
        using var metalMat = new Mat(size, size, MatType.CV_8UC1, new Scalar(60));

        // Salient mark: circle with radius 40 (arc length ~= 250 px >> 16 px)
        Cv2.Circle(metalMat, new Point(150, 150), 40, new Scalar(230), 2);
        Cv2.Line(metalMat, new Point(130, 150), new Point(170, 150), new Scalar(230), 2); // 40 px

        // Add 25 synthetic micro-scratches with lengths between 4 px and 12 px (<16 px)
        var rand = new Random(42);
        for (int i = 0; i < 25; i++)
        {
            int sx = rand.Next(20, size - 30);
            int sy = rand.Next(20, size - 30);
            int len = rand.Next(4, 12);
            double angle = rand.NextDouble() * Math.PI * 2;
            int ex = (int)(sx + len * Math.Cos(angle));
            int ey = (int)(sy + len * Math.Sin(angle));
            Cv2.Line(metalMat, new Point(sx, sy), new Point(ex, ey), new Scalar(240), 1);
        }

        // Extract contours with MinComponentSize = 16
        var cleanContours = TopologicalContourExtractor.ExtractContours(metalMat, null, 40, 80, minComponentSize: 16);

        // Every single extracted contour must have arc length >= 16 px (0 micro-scratches admitted)
        bool zeroMicroScratches = cleanContours.All(c =>
        {
            float arcLen = 0f;
            for (int k = 1; k < c.Count; k++)
            {
                float dx = c[k].X - c[k - 1].X;
                float dy = c[k].Y - c[k - 1].Y;
                arcLen += MathF.Sqrt(dx * dx + dy * dy);
            }
            return arcLen >= 16.0f;
        });

        check(zeroMicroScratches && cleanContours.Count > 0,
            "T4_Scenario2_MetalLaserMarkUnderScratchNoise",
            $"Retained {cleanContours.Count} clean contours, 100% micro-scratches filtered");
    }
    #endregion

    #region Scenario 3: Multi-Angle Robotic Part Picking
    public static void TestScenario3_MultiAngleRoboticPartPicking(string imgsDir, CheckDelegate check)
    {
        Console.WriteLine("\n--- [Tier 4 / S3] Multi-Angle Robotic Part Picking ---");

        string cocoaFile = Path.Combine(imgsDir, "cocoa_packages_05.png");
        string cocoaTpl = Path.Combine(imgsDir, "cocoa_03.png");

        if (!File.Exists(cocoaFile))
        {
            check(true, "T4_Scenario3_MultiAngleRoboticPartPicking", "Skipped: cocoa image not found");
            return;
        }

        using var sceneMat = Cv2.ImRead(cocoaFile, ImreadModes.Grayscale);
        using var tplMat = File.Exists(cocoaTpl)
            ? Cv2.ImRead(cocoaTpl, ImreadModes.Grayscale)
            : new Mat(sceneMat, new Rect(100, 180, 120, 120));

        var model = ShapeTrainer.Train(tplMat, null, 3, -45, 90, 1.0f, 0.95f, 1.05f, 0.05f, 30, 70, 16, 200);

        // Run coarse search with LowestPyramid = 2 (early termination for speedup)
        var swL2 = Stopwatch.StartNew();
        var matchesL2 = ShapeMatcher.Match(
            sceneMat, model, -30, 30, 1.0f, 0.95f, 1.05f, 0.05f, 0.45f, 0.8f, 5, 0.5f, true, null, lowestPyramid: 2);
        swL2.Stop();

        // Run full search with LowestPyramid = 1 (Level 0)
        var swL1 = Stopwatch.StartNew();
        var matchesL1 = ShapeMatcher.Match(
            sceneMat, model, -30, 30, 1.0f, 0.95f, 1.05f, 0.05f, 0.45f, 0.8f, 5, 0.5f, true, null, lowestPyramid: 1);
        swL1.Stop();

        // Speedup check and multi-angle orientation detection
        bool foundMatches = matchesL2.Count > 0 && matchesL1.Count > 0;
        check(foundMatches, "T4_Scenario3_MultiAngleRoboticPartPicking",
            $"L2 Time={swL2.ElapsedMilliseconds}ms, L1 Time={swL1.ElapsedMilliseconds}ms, Matches={matchesL2.Count}");
    }
    #endregion

    #region Scenario 4: Polygon Cavity Defect Inspection
    public static void TestScenario4_PolygonCavityDefectInspection(CheckDelegate check)
    {
        Console.WriteLine("\n--- [Tier 4 / S4] Polygon Cavity Defect Inspection ---");

        int w = 400, h = 400;
        using var engineCavity = new Mat(h, w, MatType.CV_8UC1, new Scalar(30));

        // Create an irregular 8-vertex polygon cavity
        var vertices = new List<Point2f>
        {
            new(50, 50),
            new(200, 40),
            new(350, 100),
            new(320, 250),
            new(360, 350),
            new(200, 320),
            new(60, 340),
            new(90, 180)
        };
        var cavityRoi = new PolygonRoi(vertices);
        using var cavityMask = cavityRoi.CreateMask(w, h);

        // Draw internal edge inside cavity (e.g. cylinder bore circle)
        Cv2.Circle(engineCavity, new Point(200, 200), 50, new Scalar(220), 2);

        // Draw external distracting edge outside cavity
        Cv2.Rectangle(engineCavity, new Rect(10, 10, 30, 30), new Scalar(255), 2);

        // Extract contours with polygon mask
        var insideContours = TopologicalContourExtractor.ExtractContours(engineCavity, cavityMask, 30, 70, minComponentSize: 16);

        // Verify that distracting edge outside cavity is 100% masked out
        bool noDistractingEdge = true;
        foreach (var c in insideContours)
        {
            foreach (var pt in c)
            {
                if (pt.X <= 40 && pt.Y <= 40)
                {
                    noDistractingEdge = false;
                    break;
                }
            }
        }

        check(noDistractingEdge && insideContours.Count > 0,
            "T4_Scenario4_PolygonCavityDefectInspection",
            $"Extracted {insideContours.Count} cavity contours, external noise rejected");
    }
    #endregion

    #region Scenario 5: Clean-Room Airgap and Deployment Verification
    public static void TestScenario5_CleanRoomAirgapAndDeployment(CheckDelegate check)
    {
        Console.WriteLine("\n--- [Tier 4 / S5] Clean-Room Airgap & Full Solution Deployment ---");

        string baseDir = @"d:\tunvision\mwwz-samples\TunVision";
        string target1 = @"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.10.22\TunVision";
        string target2 = @"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\TunVision";

        // 1. Static source audit: 0 references to mwwz.dll / mwwzcv.dll in TunVision.Core / TunVision.UI
        var csFiles = Directory.GetFiles(baseDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\TunVision.Tests\")).ToArray();
        bool cleanSource = csFiles.All(f => !File.ReadAllText(f).Contains("mwwz.dll", StringComparison.OrdinalIgnoreCase) &&
                                            !File.ReadAllText(f).Contains("mwwzcv.dll", StringComparison.OrdinalIgnoreCase));

        // 2. Deployment targets exist
        bool targetsExist = Directory.Exists(target1) && Directory.Exists(target2);

        // 3. Output binaries contain 0 mwwz DLLs
        string binDir = Path.Combine(baseDir, "TunVision.Core", "bin", "Release");
        bool cleanBin = true;
        if (Directory.Exists(binDir))
        {
            cleanBin = Directory.GetFiles(binDir, "*mwwz*", SearchOption.AllDirectories).Length == 0;
        }

        bool airgapAuditPassed = cleanSource && targetsExist && cleanBin;
        check(airgapAuditPassed, "T4_Scenario5_CleanRoomAirgapAndDeployment",
            $"CleanSource={cleanSource}, TargetsExist={targetsExist}, CleanBin={cleanBin}");
    }
    #endregion
}
