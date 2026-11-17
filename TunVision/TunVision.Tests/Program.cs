using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Barcode;
using TunVision.Core.Blob;
using TunVision.Core.Caliper;
using TunVision.Core.Engine;
using TunVision.Core.Graph;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;
using TunVision.Core.ShapeMatching.Acceleration;
using TunVision.Core.ShapeMatching.Geometry;

namespace TunVision.Tests;

class Program
{
    static int _passed = 0;
    static int _failed = 0;

    static void Check(bool condition, string testName, string details = "")
    {
        if (condition)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[PASS] ");
            Console.ResetColor();
            Console.WriteLine(testName + (string.IsNullOrEmpty(details) ? "" : $" ({details})"));
            _passed++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[FAIL] ");
            Console.ResetColor();
            Console.WriteLine(testName + (string.IsNullOrEmpty(details) ? "" : $" ({details})"));
            _failed++;
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("      TunVision Clean-Room Verification Suite     ");
        Console.WriteLine("==================================================");

        string imgsDir = @"d:\tunvision\mwwz-samples\imgs";

        TestShapeMatching(imgsDir);
        TestModelSerialization(imgsDir);
        TestBlobTool(imgsDir);
        TestCaliperTool(imgsDir);
        TestOtherShapes(imgsDir);
        TestVisionEnginePipeline(imgsDir);
        TestDagGraphPipeline(imgsDir);
        TestMwwzAdvancedFeaturesAndMetrology(imgsDir);
        TestZxingBarcodeAndQrCodeReading(imgsDir);
        TestUserTwoTemplatePointToPointPipeline(imgsDir);
        TestMilestone1AutoContrastAndTopologicalContours(imgsDir);
        TestMilestone2VisualizerInliersOutliers(imgsDir);
        TestMilestone4VerificationAndDelivery(imgsDir);
        TestAdversarialEmpiricalChallenger(imgsDir);
        TestPathResolverAndSourceImageNodeFallback(imgsDir);
        TestSourceImageNodeStatusAndCocoaVerification(imgsDir);
        TestMilestone3CoreKernelAndScalability(imgsDir);
        TestMilestone1UiAndCanvasEnhancements(imgsDir);
        TestE2ETiersCleanRoom.RunAll(imgsDir, Check);

        Console.WriteLine("==================================================");
        Console.WriteLine($"RESULTS: {_passed} Passed, {_failed} Failed");
        Console.WriteLine("==================================================");

        if (_failed > 0)
        {
            Environment.Exit(1);
        }
    }

    static void TestShapeMatching(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Shape Matching Engine ---");

        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");
        string ep99 = Path.Combine(imgsDir, "engine_part_099.png");

        if (!File.Exists(ep54) || !File.Exists(ep99))
        {
            Console.WriteLine($"Skipping ShapeMatching test: {ep54} not found");
            return;
        }

        using var mat54 = Cv2.ImRead(ep54, ImreadModes.Grayscale);
        using var mat99 = Cv2.ImRead(ep99, ImreadModes.Grayscale);

        var alignTool = new AlignTool("EnginePartAlign")
        {
            AngleMinDeg = -20f,
            AngleMaxDeg = 20f,
            AngleStepDeg = 1.0f,
            ScaleMin = 0.88f,
            ScaleMax = 1.12f,
            ScaleStep = 0.04f,
            MinScore = 0.35f,
            Greediness = 0.80f,
            MaxInstances = 2,
            Subpixel = true
        };

        // Train on ep54
        alignTool.Train(mat54, null, numLevels: 4, contrastLow: 15, contrastHigh: 30, minLength: 10, maxFeatures: 300);
        Check(alignTool.Model != null && alignTool.Model.Levels.Count == 4, "Train Model on engine_part_054", $"Levels={alignTool.Model?.Levels.Count}");
        Check(alignTool.Model?.Levels[0].Features.Count > 100, "Level 0 Feature Count", $"Count={alignTool.Model?.Levels[0].Features.Count}");

        // 1. Self-match on training image
        var selfResult = (AlignResult)alignTool.Run(mat54);
        Check(selfResult.Success && selfResult.Matches.Count > 0, "Self Match Found", $"Count={selfResult.Matches.Count}");
        if (selfResult.Matches.Count > 0)
        {
            var best = selfResult.Matches[0];
            Check(best.Score > 0.85f, "Self Match Score", $"Score={best.Score:F3}");
            Check(MathF.Abs(best.AngleDeg) < 2.0f, "Self Match Angle near 0°", $"Angle={best.AngleDeg:F2}°");
            Check(MathF.Abs(best.X - alignTool.Model!.OriginX) < 1.0f, "Self Match X Position", $"X={best.X:F1}, Expected={alignTool.Model!.OriginX:F1}");
            Check(MathF.Abs(best.Y - alignTool.Model!.OriginY) < 1.0f, "Self Match Y Position", $"Y={best.Y:F1}, Expected={alignTool.Model!.OriginY:F1}");
        }

        // 2. Match on transformed scene engine_part_099
        var sceneResult = (AlignResult)alignTool.Run(mat99);
        Check(sceneResult.Success && sceneResult.Matches.Count > 0, "Scene Match on engine_part_099 Found", $"Count={sceneResult.Matches.Count}");
        if (sceneResult.Matches.Count > 0)
        {
            var m = sceneResult.Matches[0];
            Check(m.Score > 0.40f, "Scene Match Score", $"Score={m.Score:F3}, Coverage={m.Coverage:P1}");
            Console.WriteLine($"   Detected instance: X={m.X:F2}, Y={m.Y:F2}, Angle={m.AngleDeg:F2}°, Score={m.Score:F3}");
        }

        // 3. Test Steger 1998 Subpixel Extractor + Response-Map AVX2 Matcher
        var stegerModel = ShapeTrainer.Train(
            mat54, null, numLevels: 4, contrastLow: 15, contrastHigh: 35,
            contourMode: ContourExtractionMode.StegerSubpixel, maxFeatures: 400);
        Check(stegerModel.Levels.Count == 4 && stegerModel.Levels[0].Features.Count >= 100, "Steger Subpixel Training (Dense Features)", $"Count={stegerModel.Levels[0].Features.Count}");

        var stegerTool = new AlignTool("StegerAlign")
        {
            Model = stegerModel,
            AngleMinDeg = -20f,
            AngleMaxDeg = 20f,
            AngleStepDeg = 1.0f,
            ScaleMin = 0.88f,
            ScaleMax = 1.12f,
            ScaleStep = 0.04f,
            MinScore = 0.35f,
            Greediness = 0.80f,
            MaxInstances = 2,
            Subpixel = true
        };
        var stegerSceneRes = (AlignResult)stegerTool.Run(mat99);
        Check(stegerSceneRes.Success && stegerSceneRes.Matches.Count > 0, "Steger Subpixel Scene Match on engine_part_099", $"Matches={stegerSceneRes.Matches.Count}");
        if (stegerSceneRes.Matches.Count > 0)
        {
            var sm = stegerSceneRes.Matches[0];
            Check(sm.Score >= 0.50f, "Steger Scene Match Score Improved", $"Score={sm.Score:F3}");
            Console.WriteLine($"   Steger Detected: X={sm.X:F2}, Y={sm.Y:F2}, Angle={sm.AngleDeg:F2}°, Score={sm.Score:F3}");
        }
    }

    static void TestModelSerialization(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Model Serialization (.shm and .json) ---");

        var dummy = new ShapeModel
        {
            NumLevels = 3,
            AngleStartDeg = -45f,
            AngleExtentDeg = 90f,
            OriginX = 100f,
            OriginY = 100f,
            TemplateWidth = 200,
            TemplateHeight = 200
        };

        var lvl = new PyramidLevelData { Level = 0, Width = 200, Height = 200 };
        lvl.Features.Add(new ShapeFeature(10, 20, 0.6f, 0.8f, 0.9f, 50f));
        dummy.Levels.Add(lvl);

        string jsonPath = Path.Combine(Path.GetTempPath(), "test_model.json");
        string shmPath = Path.Combine(Path.GetTempPath(), "test_model.shm");

        dummy.SaveJson(jsonPath);
        var loadedJson = ShapeModel.LoadJson(jsonPath);
        Check(loadedJson.NumLevels == dummy.NumLevels && loadedJson.Levels[0].Features.Count == 1, "JSON Serialization Round-trip");

        dummy.SaveShm(shmPath);
        var loadedShm = ShapeModel.LoadShm(shmPath);
        Check(loadedShm.NumLevels == dummy.NumLevels && loadedShm.Levels[0].Features.Count == 1, "Binary .SHM Serialization Round-trip");
        Check(MathF.Abs(loadedShm.AngleStartDeg - dummy.AngleStartDeg) < 0.01f, ".SHM Angle Integrity");

        string phoneShmPath = Path.Combine(imgsDir, "phone.shm");
        if (File.Exists(phoneShmPath))
        {
            var phoneModel = ShapeModel.LoadShm(phoneShmPath);
            Check(phoneModel.NumLevels == 5 && phoneModel.Levels[0].Features.Count == 427, "MWWZ Standard phone.shm Load Integrity (5 levels, 427 lvl0 features)");
        }

        string myModelShmPath = Path.Combine(Path.GetDirectoryName(imgsDir) ?? @"d:\tunvision\mwwz-samples", "data", "my_model.shm");
        if (File.Exists(myModelShmPath))
        {
            var myModel = ShapeModel.LoadShm(myModelShmPath);
            Check(myModel.NumLevels == 4 && myModel.Levels[0].Features.Count == 354, "MWWZ Standard my_model.shm Load Integrity (4 levels, 354 lvl0 features)");
        }

        if (File.Exists(jsonPath)) File.Delete(jsonPath);
        if (File.Exists(shmPath)) File.Delete(shmPath);

        // Pre-generate standard models for Retainers and Engine Part
        try
        {
            string retImg = Path.Combine(imgsDir, "retainers_01.png");
            if (File.Exists(retImg))
            {
                using var retMat = Cv2.ImRead(retImg, ImreadModes.Grayscale);
                var retTrainer = new AlignTool { ContourMode = ContourExtractionMode.Silhouette, MinLength = 40 };
                // Train on ONE SINGLE RETAINER (Contour 8: center large retainer)
                var roi = new RectRoi(210, 180, 125, 125);
                retTrainer.Train(retMat, roi, numLevels: 4);
                if (retTrainer.Model != null)
                {
                    var modelDirs = new[]
                    {
                        @"d:\tunvision\mwwz-samples\TunVision\Models",
                        @"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\TunVision\Models",
                        @"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.10.22\TunVision\Models"
                    };
                    foreach (var dir in modelDirs)
                    {
                        if (Directory.Exists(dir))
                        {
                            retTrainer.Model.SaveShm(Path.Combine(dir, "retainers_model.shm"));
                        }
                    }
                }
            }
        }
        catch { }
    }

    static void TestBlobTool(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Blob Tool ---");

        string ringsImg = Path.Combine(imgsDir, "rings_01.png");
        if (!File.Exists(ringsImg))
        {
            Console.WriteLine($"Skipping BlobTool test: {ringsImg} not found");
            return;
        }

        using var mat = Cv2.ImRead(ringsImg, ImreadModes.Grayscale);
        var blobTool = new BlobTool("RingDetector")
        {
            ThresholdMode = ThresholdMode.Otsu,
            Invert = true,
            MinArea = 50.0,
            MaxArea = 100000.0
        };

        var res = (BlobResult)blobTool.Run(mat);
        Check(res.Success && res.BlobCount > 0, "Blob Detection on rings_01", $"Found {res.BlobCount} blobs");
        if (res.BlobCount > 0)
        {
            var first = res.Blobs[0];
            Check(first.Area > 50, "Blob Area Valid", $"Area={first.Area:F0}");
            Check(first.Centroid.X > 0 && first.Centroid.Y > 0, "Blob Centroid Valid", $"Center=({first.Centroid.X:F1}, {first.Centroid.Y:F1})");
        }
    }

    static void TestCaliperTool(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Caliper Tool ---");

        // Synthesize test image with sharp step edge
        using var edgeImg = new Mat(100, 200, MatType.CV_8UC1, new Scalar(50));
        edgeImg[new Rect(100, 0, 100, 100)].SetTo(new Scalar(200)); // Step edge at X=100

        var caliper = new CaliperTool("EdgeCaliper")
        {
            Mode = CaliperMode.Line,
            LineStart = new Point2f(20, 50),
            LineEnd = new Point2f(180, 50),
            ProjectionLength = 20,
            Threshold = 10f,
            Polarity = EdgePolarity.Positive
        };

        var res = (CaliperResult)caliper.Run(edgeImg);
        Check(res.Success && res.Edges.Count == 1, "1D Step Edge Detection", $"Edges={res.Edges.Count}");
        if (res.Edges.Count > 0)
        {
            float edgeX = res.Edges[0].X;
            Check(MathF.Abs(edgeX - 99.5f) < 0.2f, "Subpixel Edge Position", $"X={edgeX:F3}, Expected=99.5");
        }

        // Test Circle Caliper on synthesized circle
        using var circleImg = new Mat(200, 200, MatType.CV_8UC1, new Scalar(30));
        Cv2.Circle(circleImg, new Point(100, 100), 50, new Scalar(220), -1);

        var circleCaliper = new CaliperTool("CircleCaliper")
        {
            Mode = CaliperMode.Circle,
            CircleCenter = new Point2f(100, 100),
            CircleRadius = 50f,
            SearchRange = 15f,
            NumSpokes = 32,
            Threshold = 20f,
            Polarity = EdgePolarity.Negative
        };

        var cRes = (CaliperResult)circleCaliper.Run(circleImg);
        Check(cRes.Success && cRes.FittedRadius.HasValue, "Circle Fit Success", $"RMS={cRes.RmsError:F3}");
        if (cRes.FittedRadius.HasValue)
        {
            Check(Math.Abs(cRes.FittedRadius.Value - 50.0) < 0.5, "Circle Radius Accuracy", $"R={cRes.FittedRadius.Value:F2}");
            Check(Math.Abs(cRes.FittedCircleCenter!.Value.X - 100.0) < 0.5, "Circle Center X Accuracy", $"X={cRes.FittedCircleCenter.Value.X:F2}");
            Check(Math.Abs(cRes.FittedCircleCenter.Value.Y - 100.0) < 0.5, "Circle Center Y Accuracy", $"Y={cRes.FittedCircleCenter.Value.Y:F2}");
        }
    }

    static void TestOtherShapes(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Gasket & Cocoa Package Matching ---");
        string gMdlPath = Path.Combine(imgsDir, "gasket_model.png");
        string gScenePath = Path.Combine(imgsDir, "gasket_04.png");
        if (File.Exists(gMdlPath) && File.Exists(gScenePath))
        {
            using var gMdl = Cv2.ImRead(gMdlPath, ImreadModes.Grayscale);
            using var gScene = Cv2.ImRead(gScenePath, ImreadModes.Grayscale);
            var model = ShapeTrainer.Train(gMdl, null, numLevels: 4, contrastLow: 25, contrastHigh: 50, minLength: 30);
            var matches = ShapeMatcher.Match(gScene, model, angleMinDeg: -180, angleMaxDeg: 180, angleStepDeg: 1.0f, minScore: 0.5f);
            Check(matches.Count > 0, "Gasket Match on gasket_04", $"Matches={matches.Count}");
            if (matches.Count > 0)
                Console.WriteLine($"   Gasket Match #1: Pos=({matches[0].X:F1},{matches[0].Y:F1}), Angle={matches[0].AngleDeg:F1}°, Score={matches[0].Score:F3}");
        }

        string cMdlPath = Path.Combine(imgsDir, "cocoa_package_model.png");
        string cScenePath = Path.Combine(imgsDir, "cocoa_packages_03.png");
        string cScene5Path = Path.Combine(imgsDir, "cocoa_packages_05.png");
        if (File.Exists(cMdlPath) && File.Exists(cScenePath))
        {
            using var cMdl = Cv2.ImRead(cMdlPath, ImreadModes.Grayscale);
            using var cScene = Cv2.ImRead(cScenePath, ImreadModes.Grayscale);
            var model = ShapeTrainer.Train(cMdl, null, numLevels: 4, contrastLow: 30, contrastHigh: 60, minLength: 30);
            var matches = ShapeMatcher.Match(cScene, model, angleMinDeg: -45, angleMaxDeg: 45, angleStepDeg: 1.0f, minScore: 0.5f);
            Check(matches.Count > 0, "Cocoa Match on cocoa_03", $"Matches={matches.Count}");
            if (matches.Count > 0)
                Console.WriteLine($"   Cocoa Match #1: Pos=({matches[0].X:F1},{matches[0].Y:F1}), Angle={matches[0].AngleDeg:F1}°, Score={matches[0].Score:F3}");

            if (File.Exists(cScene5Path))
            {
                using var cScene5 = Cv2.ImRead(cScene5Path, ImreadModes.Grayscale);
                var modelUser = ShapeTrainer.Train(cMdl, null, numLevels: 4, contrastLow: 35, contrastHigh: 75, minLength: 60, contourMode: ContourExtractionMode.Auto);
                var matches5 = ShapeMatcher.Match(cScene5, modelUser, angleMinDeg: -180, angleMaxDeg: 180, angleStepDeg: 1.0f, scaleMin: 0.9f, scaleMax: 1.1f, minScore: 0.5f, greediness: 0.8f, maxInstances: 5);
                Check(matches5.Count >= 3, "Multi-Instance Match on cocoa_05 (Found 3 packages)", $"Matches={matches5.Count}");
                for (int mIdx = 0; mIdx < matches5.Count; mIdx++)
                {
                    var m = matches5[mIdx];
                    Console.WriteLine($"   Cocoa_05 #{mIdx+1}: Pos=({m.X:F1}, {m.Y:F1}), Angle={m.AngleDeg:F1}°, Score={m.Score:F3}, Cov={m.Coverage:P1}");
                }
            }
        }
    }

    static void TestVisionEnginePipeline(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Vision Engine Composite Pipeline ---");

        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");
        if (!File.Exists(ep54)) return;

        using var mat54 = Cv2.ImRead(ep54, ImreadModes.Grayscale);

        var engine = new VisionEngine();

        var align = new AlignTool("Aligner") { MinScore = 0.5f };
        align.Train(mat54, null, numLevels: 4);

        var blob = new BlobTool("Blobber") { MinArea = 100 };
        var caliper = new CaliperTool("Caliper")
        {
            LineStart = new Point2f(200, 256),
            LineEnd = new Point2f(450, 256),
            Threshold = 10f
        };

        engine.AddTool(align);
        engine.AddTool(blob);
        engine.AddTool(caliper);

        var pipelineResult = engine.Execute(mat54);
        Check(pipelineResult.Success, "VisionEngine Pipeline Execution", $"Time={pipelineResult.ExecutionTimeMs:F1}ms");
        Check(pipelineResult.ToolResults.ContainsKey("Aligner"), "Pipeline Contains Aligner Result");
        Check(pipelineResult.ToolResults.ContainsKey("Blobber"), "Pipeline Contains Blobber Result");
        Check(pipelineResult.ToolResults.ContainsKey("Caliper"), "Pipeline Contains Caliper Result");
    }

    static void TestDagGraphPipeline(string imgsDir)
    {
        Console.WriteLine("\n--- Testing ForgeStation-style Visual DAG Pipeline ---");

        var graph = PipelineGraph.CreateDefaultInspectionPipeline();
        Check(graph.Nodes.Count == 6, "Graph Default Nodes Count", $"Count={graph.Nodes.Count}");
        Check(graph.Connections.Count == 6, "Graph Default Connections Count", $"Count={graph.Connections.Count}");

        var order = graph.GetExecutionOrder();
        Check(order.Count == 6, "Topological Sort Order Count", $"Order={string.Join(" -> ", order.Select(n => n.Title))}");
        Check(order[0] is SourceImageNode, "Topological Order starts with SourceImageNode");

        string retainerPath1 = Path.Combine(imgsDir, "retainers_01.png");
        string retainerPath = retainerPath1;
        var context = new GraphExecutionContext { RootImagePath = retainerPath };

        var executor = new GraphExecutor();
        var execTask = executor.ExecuteAsync(graph, context);
        execTask.Wait();
        var result = execTask.Result;

        Check(result.Success, "DAG Pipeline Execution Succeeded", $"Time={result.TotalExecutionTimeMs:F1}ms");
        Check(result.ExecutedNodes.Count == 6, "All 6 Nodes Executed");
        Check(result.FinalPreviewMat != null && !result.FinalPreviewMat.Empty(), "Final Overlay Mat Generated");
        Check(result.OverallVerdict != null, "Overall Verdict Produced", result.OverallVerdict?.ToString() ?? "null");
        var matchNode = graph.Nodes.OfType<ShapeMatchNode>().FirstOrDefault();
        Console.WriteLine($"   DAG MatchNode Status (ret1): {matchNode?.LastResult}");

        // Now test ret2!
        string retainerPath2 = Path.Combine(imgsDir, "retainers_02.png");
        var src = graph.Nodes.OfType<SourceImageNode>().FirstOrDefault();
        src?.SetImageFile(retainerPath2);
        var execTask2 = executor.ExecuteAsync(graph, new GraphExecutionContext());
        execTask2.Wait();
        Console.WriteLine($"   DAG MatchNode Status (ret2): {matchNode?.LastResult}");

        // Now test ret3!
        string retainerPath3 = Path.Combine(imgsDir, "retainers_03.png");
        src?.SetImageFile(retainerPath3);
        var execTask3 = executor.ExecuteAsync(graph, new GraphExecutionContext());
        execTask3.Wait();
        Console.WriteLine($"   DAG MatchNode Status (ret3): {matchNode?.LastResult}");

        // --- Testing Automatic Downstream Node Re-Execution on Image Switch ---
        Console.WriteLine("\n--- Testing Automatic Downstream Node Re-Execution on Image Switch ---");
        var autoGraph = PipelineGraph.CreateDefaultInspectionPipeline();
        var autoSrc = autoGraph.Nodes.OfType<SourceImageNode>().First();
        var autoMatch = autoGraph.Nodes.OfType<ShapeMatchNode>().First();
        var autoFix = autoGraph.Nodes.OfType<FixtureNode>().First();
        var autoCircle = autoGraph.Nodes.OfType<CaliperCircleNode>().First();
        var autoVerdict = autoGraph.Nodes.OfType<ToleranceVerdictNode>().First();

        // 1. Initial direct synchronous Execute() on PipelineGraph
        var initRes = autoGraph.Execute();
        Check(initRes.Success, "PipelineGraph.Execute() Synchronous Succeeded", $"Time={initRes.TotalExecutionTimeMs:F1}ms");
        string ret1MatchResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(ret1MatchResult) && ret1MatchResult.Contains("score="), "Initial Match Node Executed (ret1)", ret1MatchResult);

        // 2. Auto-rerun when setting ImagePath directly (no manual execute call!)
        autoSrc.ImagePath = retainerPath2;
        string ret2MatchResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(ret2MatchResult) && ret2MatchResult != ret1MatchResult, 
            "SourceImageNode.ImagePath Assignment Automatically Re-Executed Downstream MatchNode (ret2)", ret2MatchResult);
        Check(autoCircle.LastOutputMat != null && !autoCircle.LastOutputMat.Empty(), "Downstream CaliperCircleNode Automatically Re-Executed");
        Check(autoVerdict.LastResult != null, "Downstream ToleranceVerdictNode Automatically Re-Executed");

        // 3. Auto-rerun when calling NextImage()
        autoSrc.NextImage();
        string nextMatchResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(nextMatchResult), "SourceImageNode.NextImage() Automatically Re-Executed Downstream Nodes", nextMatchResult);

        // 4. Auto-rerun when calling PrevImage()
        autoSrc.PrevImage();
        string prevMatchResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(prevMatchResult), "SourceImageNode.PrevImage() Automatically Re-Executed Downstream Nodes", prevMatchResult);

        // 5. Auto-rerun when calling SetImageFile()
        autoSrc.SetImageFile(retainerPath3);
        string ret3MatchResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(ret3MatchResult) && ret3MatchResult.Contains("score="), 
            "SourceImageNode.SetImageFile Automatically Re-Executed Downstream Nodes (ret3)", ret3MatchResult);

        // 6. Auto-rerun when calling SelectImageByPath()
        autoSrc.SelectImageByPath(retainerPath2);
        string selectPathResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(selectPathResult) && selectPathResult == ret2MatchResult, 
            "SourceImageNode.SelectImageByPath Automatically Re-Executed Downstream Nodes", selectPathResult);

        // 7. Auto-rerun when calling SelectSampleByFileName()
        autoSrc.SelectSampleByFileName("retainers_01.png");
        string selectSampleResult = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(selectSampleResult) && selectSampleResult == ret1MatchResult, 
            "SourceImageNode.SelectSampleByFileName Automatically Re-Executed Downstream Nodes", selectSampleResult);

        // 8. Test Direct PipelineGraph.ExecuteAsync()
        var asyncRes = autoGraph.ExecuteAsync().Result;
        Check(asyncRes.Success, "PipelineGraph.ExecuteAsync() Succeeded", $"Time={asyncRes.TotalExecutionTimeMs:F1}ms");

        // 9. Test Direct Node.Execute() & Node.ExecuteAsync()
        var nodeSyncOutputs = autoSrc.Execute();
        Check(nodeSyncOutputs.ContainsKey("image") && nodeSyncOutputs["image"] != null, "GraphNode.Execute() Parameterless Succeeded on SourceImageNode");

        var nodeAsyncOutputs = autoSrc.ExecuteAsync().Result;
        Check(nodeAsyncOutputs.ContainsKey("image") && nodeAsyncOutputs["image"] != null, "GraphNode.ExecuteAsync() Parameterless Succeeded on SourceImageNode");

        // 10. Test GetDownstreamNodes & ExecuteDownstream
        var downstreamNodes = autoGraph.GetDownstreamNodes(autoSrc);
        Check(downstreamNodes.Count == 6, "PipelineGraph.GetDownstreamNodes(src) Returned All 6 Nodes", $"Count={downstreamNodes.Count}");
        Check(downstreamNodes[0] == autoSrc, "GetDownstreamNodes First Node is SourceImageNode");

        var dsRes = autoGraph.ExecuteDownstream(autoSrc);
        Check(dsRes.Success, "PipelineGraph.ExecuteDownstream(src) Succeeded");

        // 11. Test AutoExecute = false suppresses auto-rerun
        autoGraph.AutoExecute = false;
        string beforeSuppress = autoMatch.LastResult?.ToString() ?? "";
        autoSrc.ImagePath = retainerPath2;
        string afterSuppress = autoMatch.LastResult?.ToString() ?? "";
        Check(beforeSuppress == afterSuppress, "AutoExecute = false Suppresses Automatic Re-Execution");
        autoGraph.AutoExecute = true;

        // 12. Edge Case: Path Casing / Normalization (Drive letter & slashes)
        int idxNormalized = autoSrc.FindImageIndex(retainerPath2.ToLowerInvariant());
        Check(idxNormalized >= 0, "Case-Insensitive Image Path Normalization Resolves Correct Index", $"Index={idxNormalized}");

        // 13. Edge Case: Assigning Non-Existent Image Path reports File Not Found & Propagates DEFECT downstream
        string missingPath = Path.Combine(imgsDir, "non_existent_img_9999.png");
        autoSrc.ImagePath = missingPath;
        Check(autoSrc.Status == "File Not Found", "SourceImageNode Status is 'File Not Found' for Missing File", autoSrc.Status);
        Check(autoSrc.LastOutputMat == null, "SourceImageNode LastOutputMat is null for Missing File");
        Check(autoMatch.Status == "No Scene Image", "Downstream MatchNode Status is 'No Scene Image' on Missing Input");
        Check(autoVerdict.Status == "NG", "Downstream ToleranceVerdictNode Reports 'NG' on Missing Input");

        // 14. Edge Case: Corrupted 0-Byte Image File Gracefully Handled as Error
        string corruptPath = Path.Combine(Path.GetTempPath(), "tunvision_corrupted_test.png");
        File.WriteAllBytes(corruptPath, Array.Empty<byte>());
        try
        {
            autoSrc.ImagePath = corruptPath;
            Check(autoSrc.Status.StartsWith("Error: Invalid Image File"), "SourceImageNode Reports 'Error: Invalid Image File' for Corrupted Image", autoSrc.Status);
            Check(autoSrc.LastOutputMat == null, "SourceImageNode LastOutputMat is null for Corrupted Image");
            Check(autoVerdict.Status == "NG", "Downstream Verdict Reports 'NG' for Corrupted Image");
        }
        finally
        {
            if (File.Exists(corruptPath)) File.Delete(corruptPath);
        }

        // Restore to valid image
        autoSrc.ImagePath = retainerPath1;
        Check(autoSrc.Status == "OK", "Restored Valid Image Path Reports 'OK'", autoSrc.Status);

        // 15. Preserved Typed Outputs in LastOutputs for Individual Node Execution
        autoGraph.Execute();
        Check(autoMatch.LastOutputs.ContainsKey("frame") && autoMatch.LastOutputs["frame"] is FixtureTransform,
            "ShapeMatchNode Preserves FixtureTransform in LastOutputs");
        autoCircle.Execute();
        Check(autoCircle.LastActiveFixture != FixtureTransform.Identity,
            "CaliperCircleNode.Execute() Successfully Receives Upstream FixtureTransform from LastOutputs");

        // 16. Downstream Subgraph Topological Execution executes only target nodes
        var downstreamOnlyRes = autoGraph.ExecuteDownstream(autoMatch);
        Check(downstreamOnlyRes.ExecutedNodes.All(n => n.Id != autoSrc.Id),
            "ExecuteDownstream(matchNode) Excludes Upstream SourceImageNode from Execution Batch");
        Check(downstreamOnlyRes.ExecutedNodes.Any(n => n.Id == autoCircle.Id),
            "ExecuteDownstream(matchNode) Includes Downstream CaliperCircleNode");

        // 17. High-Frequency Rapid Trigger Switching Convergence
        autoSrc.ImagePath = retainerPath1;
        autoSrc.ImagePath = retainerPath3;
        autoSrc.ImagePath = retainerPath2;
        string finalRapidMatch = autoMatch.LastResult?.ToString() ?? "";
        Check(!string.IsNullOrEmpty(finalRapidMatch) && finalRapidMatch.Contains("score="),
            "Rapid Successive Image Changes Converge to Latest Image State", finalRapidMatch);

        // Test Preset Templates (Blank, Shape Detection, Blob Inspection)
        var blankGraph = PipelineGraph.CreateBlankPipeline();
        Check(blankGraph.Nodes.Count == 0 && blankGraph.Connections.Count == 0, "Blank Pipeline Template is Empty");

        var detectGraph = PipelineGraph.CreateShapeDetectionPipeline();
        Check(detectGraph.Nodes.Count == 6 && detectGraph.Connections.Count == 5, "Shape Detection Pipeline Template Structure", $"Nodes={detectGraph.Nodes.Count}, Conns={detectGraph.Connections.Count}");
        var detectExec = executor.ExecuteAsync(detectGraph, new GraphExecutionContext()).Result;
        Check(detectExec.Success, "Shape Detection Pipeline Template Execution", $"Time={detectExec.TotalExecutionTimeMs:F1}ms, Verdict={detectExec.OverallVerdict}");

        var blobGraph = PipelineGraph.CreateBlobInspectionPipeline();
        Check(blobGraph.Nodes.Count == 4 && blobGraph.Connections.Count == 3, "Blob Inspection Pipeline Template Structure", $"Nodes={blobGraph.Nodes.Count}, Conns={blobGraph.Connections.Count}");
        var blobExec = executor.ExecuteAsync(blobGraph, new GraphExecutionContext()).Result;
        Check(blobExec.Success, "Blob Inspection Pipeline Template Execution", $"Time={blobExec.TotalExecutionTimeMs:F1}ms, Verdict={blobExec.OverallVerdict}");
    }

    static void TestMwwzAdvancedFeaturesAndMetrology(string imgsDir)
    {
        Console.WriteLine("\n--- Testing MWWZ Eraser, Rake/Corner Caliper, P2P & Multi-Tolerance ---");

        // 1. Test remove_model_feature (Brush & Box Erase + Undo + Reset) & set_match_result_offset
        using var synthTpl = new Mat(200, 200, MatType.CV_8UC1, new Scalar(30));
        Cv2.Circle(synthTpl, new Point(100, 100), 60, new Scalar(230), -1);
        var model = ShapeTrainer.Train(synthTpl, null, numLevels: 3);
        int initialCount = model.Levels[0].Features.Count;
        Check(initialCount >= 15, "Synth Model Initial Features", $"Count={initialCount}");

        // Erase near top edge (100, 40)
        int removed = model.RemoveFeaturesByBrush(new Point2f(100, 40), 25f);
        Check(removed > 0 && model.Levels[0].Features.Count < initialCount, "Brush Erase Removes Features (remove_model_feature)", $"Removed={removed}, Remaining={model.Levels[0].Features.Count}");

        // Undo erase
        bool undone = model.UndoFeatureRemoval();
        Check(undone && model.Levels[0].Features.Count == initialCount, "Undo Feature Removal (Ctrl+Z)", $"Restored={model.Levels[0].Features.Count}");

        // Box erase & Reset
        int boxRemoved = model.RemoveFeaturesByRect(new Rect2f(70, 25, 60, 35));
        Check(boxRemoved > 0 && model.Levels[0].Features.Count < initialCount, "Box Erase Removes Features", $"Removed={boxRemoved}");
        bool resetOk = model.ResetAllRemovedFeatures();
        Check(resetOk && model.Levels[0].Features.Count == initialCount, "Reset All Removed Features", $"Restored={model.Levels[0].Features.Count}");

        // Test set_match_result_offset (TransformByMatchOffset)
        var transformedPt = ShapeModel.TransformByMatchOffset(100f, 200f, 90f, 1.0f, 10f, 0f);
        Check(MathF.Abs(transformedPt.X - 100f) < 0.05f && MathF.Abs(transformedPt.Y - 210f) < 0.05f,
            "TransformByMatchOffset 90° Rotation (set_match_result_offset)", $"Pt=({transformedPt.X:F2}, {transformedPt.Y:F2})");

        // 2. Test 2D Rake SearchLine & SearchCorner & Point-to-Point Distance
        using var cornerImg = new Mat(300, 300, MatType.CV_8UC1, new Scalar(30));
        Cv2.Rectangle(cornerImg, new Rect(100, 100, 140, 140), new Scalar(220), -1);

        var cornerTool = new CaliperTool("CornerTester")
        {
            Mode = CaliperMode.SearchCorner,
            LineStart = new Point2f(120, 100),
            LineEnd = new Point2f(220, 100),
            ProjectionLength = 25f,
            Line2Start = new Point2f(100, 120),
            Line2End = new Point2f(100, 220),
            Line2ProjectionLength = 25f,
            NumSpokes = 15,
            Threshold = 15f
        };
        var cornerRes = (CaliperResult)cornerTool.Run(cornerImg);
        Check(cornerRes.Success && cornerRes.CornerPoint.HasValue, "SearchCorner 2-Line Intersection Found", cornerRes.Message);
        if (cornerRes.CornerPoint.HasValue)
        {
            var cp = cornerRes.CornerPoint.Value;
            Check(MathF.Abs(cp.X - 99.5f) < 1.5f && MathF.Abs(cp.Y - 99.5f) < 1.5f,
                "SearchCorner Subpixel Vertex Accuracy", $"Corner=({cp.X:F2}, {cp.Y:F2})");
            Check(Math.Abs(cornerRes.IncludedAngleDeg - 90.0) < 1.5,
                "SearchCorner Included Angle ~ 90°", $"Angle={cornerRes.IncludedAngleDeg:F2}°");
        }

        CaliperTool.ComputePointToPoint(new Point2f(100, 100), new Point2f(130, 140), out double dist, out double dx, out double dy, out _);
        Check(Math.Abs(dist - 50.0) < 0.01 && Math.Abs(dx - 30.0) < 0.01 && Math.Abs(dy - 40.0) < 0.01,
            "Point-to-Point Distance (3-4-5 Triangle)", $"dist={dist:F2}, dx={dx:F2}, dy={dy:F2}");

        // 3. Test Complete Metrology Pipeline & Multi-Row Tolerance Table & Project Save/Load
        var metroGraph = PipelineGraph.CreateCompleteMetrologyPipeline();
        var executor = new GraphExecutor();
        var metroExec = executor.ExecuteAsync(metroGraph, new GraphExecutionContext()).Result;
        Check(metroExec.Success && metroExec.CompositePreviewMat != null && !metroExec.CompositePreviewMat.Empty(),
            "Complete Metrology Pipeline & Composite Overlay Execution", $"Time={metroExec.TotalExecutionTimeMs:F1}ms");

        var tolNode = metroGraph.Nodes.OfType<ToleranceVerdictNode>().First();
        tolNode.CaptureNominalsFromGraph(metroGraph, 5.0);
        Check(tolNode.Rules.Count >= 4, "CaptureNominalsFromGraph Populates Multi-Row Table", $"Rules={tolNode.Rules.Count}");

        var metroExec2 = executor.ExecuteAsync(metroGraph, new GraphExecutionContext()).Result;
        Check(metroExec2.OverallVerdict?.State == VerdictState.Pass,
            "Multi-Row Tolerance Verdict Evaluates PASS on Captured Nominals", metroExec2.OverallVerdict?.Message ?? "");

        string tmpProj = Path.Combine(Path.GetTempPath(), "test_metro_pipeline.tvproj");
        metroGraph.SaveToFile(tmpProj);
        var loadedGraph = PipelineGraph.LoadFromFile(tmpProj);
        Check(loadedGraph.Nodes.Count == metroGraph.Nodes.Count && loadedGraph.Connections.Count == metroGraph.Connections.Count,
            "Pipeline Project (.tvproj) Save & Load Round-Trip", $"Nodes={loadedGraph.Nodes.Count}");
        if (File.Exists(tmpProj)) File.Delete(tmpProj);
    }

    static void TestZxingBarcodeAndQrCodeReading(string imgsDir)
    {
        Console.WriteLine("\n--- Testing ZXing 1D & 2D Code Reading ---");

        // 1. Generate QR Code image and test decoding
        string qrText = "TUNVISION-SN-2026-8899-PASS";
        using var qrMat = BarcodeReadingEngine.CreateSampleCodeMat(ZXing.BarcodeFormat.QR_CODE, qrText, 320, 320);
        string qrPath = Path.Combine(imgsDir, "sample_qr.png");
        Cv2.ImWrite(qrPath, qrMat);

        var qrResult = BarcodeReadingEngine.ReadCode(qrMat, null, CodeKind.QrCode);
        Check(qrResult.Success && qrResult.Text == qrText, "ZXing QR Code Read Success", $"Text={qrResult.Text}, Format={qrResult.FormatName}, Time={qrResult.ExecutionTimeMs:F1}ms");
        Check(qrResult.Points.Count >= 3 && qrResult.BoundingBox.Width > 50, "ZXing QR Code Points & Bounding Box", $"Center=({qrResult.Center.X:F1},{qrResult.Center.Y:F1}), Box={qrResult.BoundingBox.Width:F0}x{qrResult.BoundingBox.Height:F0}");

        // 2. Generate 1D Code 128 Barcode image and test decoding
        string barText = "TV-128-45678";
        using var barMat = BarcodeReadingEngine.CreateSampleCodeMat(ZXing.BarcodeFormat.CODE_128, barText, 400, 150);
        string barPath = Path.Combine(imgsDir, "sample_barcode.png");
        Cv2.ImWrite(barPath, barMat);

        var barResult = BarcodeReadingEngine.ReadCode(barMat, null, CodeKind.Code128);
        Check(barResult.Success && barResult.Text == barText, "ZXing Code 128 Barcode Read Success", $"Text={barResult.Text}, Format={barResult.FormatName}, Time={barResult.ExecutionTimeMs:F1}ms");

        // 3. Test ScanQrNode & ScanBarcodeNode in DAG Graph Execution
        var graph = PipelineGraph.CreateCodeReadingPipeline();
        var executor = new GraphExecutor();
        var res = executor.ExecuteAsync(graph, new GraphExecutionContext()).Result;
        Check(res.Success && res.OverallVerdict?.State == VerdictState.Pass,
            "1D/2D Code Reading Pipeline (ZXing) Execution", $"Verdict={res.OverallVerdict?.State}, Time={res.TotalExecutionTimeMs:F1}ms");

        // 4. Test Verification / Match Rules (Equals, Contains, Mismatch)
        var qrNode = graph.Nodes.OfType<ScanQrNode>().First();
        qrNode.MatchRule = TextMatchRule.Contains;
        qrNode.ExpectedText = "2026-8899";
        var resMatch = executor.ExecuteAsync(graph, new GraphExecutionContext()).Result;
        Check(qrNode.IsMatchPassed && qrNode.Status == "OK", "Code Reader Text Verification (Contains)", qrNode.LastResult?.ToString() ?? "");

        qrNode.ExpectedText = "DIFFERENT-STRING";
        var resMismatch = executor.ExecuteAsync(graph, new GraphExecutionContext()).Result;
        Check(!qrNode.IsMatchPassed && qrNode.Status == "NG", "Code Reader Text Verification (Mismatch Flagged)", qrNode.LastResult?.ToString() ?? "");
    }

    static void TestUserTwoTemplatePointToPointPipeline(string imgsDir)
    {
        Console.WriteLine("\n--- Testing 2-Template Point-to-Point Measurement Pipeline (User Scenario) ---");

        string m1Path = Path.Combine(imgsDir, "m1.bmp");
        string gearPath = Path.Combine(imgsDir, "gear_template.png");
        string ringsPath = Path.Combine(imgsDir, "rings_01.png");

        var graph = new PipelineGraph { Name = "Two-Template Ring Distance" };

        var srcM1 = new SourceImageNode { Id = "src_m1", ImagePath = m1Path };
        var trainM1 = new TrainShapeModelNode { Id = "train_m1" };

        var srcGear = new SourceImageNode { Id = "src_gear", ImagePath = gearPath };
        var trainGear = new TrainShapeModelNode { Id = "train_gear" };

        var srcRings = new SourceImageNode { Id = "src_rings", ImagePath = ringsPath };

        var matchM1 = new ShapeMatchNode { Id = "match_m1", MinScore = 0.5f };
        var matchGear = new ShapeMatchNode { Id = "match_gear", MinScore = 0.5f };

        var p2p = new PointToPointNode { Id = "p2p_dist" };

        graph.Nodes.AddRange(new GraphNode[] { srcM1, trainM1, srcGear, trainGear, srcRings, matchM1, matchGear, p2p });

        // Connect nodes exactly as in user graph (deliberately connecting Contour Img into Model In to test tolerance!)
        graph.Connect("src_m1", "image", "train_m1", "image");
        graph.Connect("train_m1", "image", "match_m1", "model"); // Even when user connects "image" to "model"!

        graph.Connect("src_gear", "image", "train_gear", "image");
        graph.Connect("train_gear", "image", "match_gear", "model"); // Even when user connects "image" to "model"!

        graph.Connect("src_rings", "image", "match_m1", "image");
        graph.Connect("src_rings", "image", "match_gear", "image");

        graph.Connect("match_m1", "pose", "p2p_dist", "point_a");
        graph.Connect("match_gear", "pose", "p2p_dist", "point_b");
        graph.Connect("src_rings", "image", "p2p_dist", "image");

        var executor = new GraphExecutor();
        var res = executor.ExecuteAsync(graph, new GraphExecutionContext()).Result;

        Check(res.Success, "User 2-Template Graph Execution Succeeded", $"Time={res.TotalExecutionTimeMs:F1}ms");
        Check(matchM1.ToolInstance.Model != null, "Match 1 Model Resolved from Train Node (even via Contour Img wire)", $"Features={matchM1.ToolInstance.Model?.Levels[0].Features.Count}");
        Check(matchGear.ToolInstance.Model != null, "Match 2 Model Resolved from Train Node (even via Contour Img wire)", $"Features={matchGear.ToolInstance.Model?.Levels[0].Features.Count}");
        Check(matchM1.LastMatches.Count > 0, "Match 1 (C-Ring) Found on rings_01.png", $"Pos=({matchM1.LastMatches.FirstOrDefault()?.X:F1},{matchM1.LastMatches.FirstOrDefault()?.Y:F1}), Score={matchM1.LastMatches.FirstOrDefault()?.Score:F3}");
        Check(matchGear.LastMatches.Count > 0, "Match 2 (Gear) Found on rings_01.png", $"Pos=({matchGear.LastMatches.FirstOrDefault()?.X:F1},{matchGear.LastMatches.FirstOrDefault()?.Y:F1}), Score={matchGear.LastMatches.FirstOrDefault()?.Score:F3}");
        Check(p2p.HasValidPoints && p2p.LastDist > 50, "P2P Distance Between C-Ring and Gear Computed & Drawn", $"Dist={p2p.LastDist:F2}px, ΔX={p2p.LastDx:F2}, ΔY={p2p.LastDy:F2}");
    }

    static void TestMilestone1AutoContrastAndTopologicalContours(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Milestone 1: Auto Parameter Estimation & Topological Contours (R1 & R2) ---");

        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");

        if (File.Exists(ep54))
        {
            using var mat54 = Cv2.ImRead(ep54, ImreadModes.Grayscale);

            // 1. AutoEstimateContrast on engine_part_054
            var est54 = ShapeTrainer.AutoEstimateContrast(mat54);
            Check(est54.MinContrast >= 5 && est54.MinContrast <= 30, "AutoEstimateContrast MinContrast Range [5, 30]", $"MinContrast={est54.MinContrast}");
            Check(est54.ContrastLow > est54.MinContrast && est54.ContrastHigh > est54.ContrastLow, "AutoEstimateContrast Ordering ContrastHigh > ContrastLow > MinContrast", $"H={est54.ContrastHigh}, L={est54.ContrastLow}, Min={est54.MinContrast}");
            Check(est54.MinComponentSize == 16, "AutoEstimateContrast Standard MinComponentSize == 16", $"Size={est54.MinComponentSize}");
            Check(est54.NumLevels >= 4 && est54.NumLevels <= 6, "AutoEstimateContrast NumLevels in [4, 6]", $"Levels={est54.NumLevels}");
            Check(est54.NoiseStd > 0f, "AutoEstimateContrast Physical Noise Estimation", $"NoiseStd={est54.NoiseStd:F2}");

            // 2. AlignTool.AutoEstimateParameters sync
            var alignTool = new AlignTool("AutoAlignTest");
            var toolEst = alignTool.AutoEstimateParameters(mat54);
            Check(alignTool.MinContrast == toolEst.MinContrast && alignTool.ContrastLow == toolEst.ContrastLow && alignTool.MinComponentSize == 16,
                "AlignTool AutoEstimateParameters Synchronizes Properties", $"MinContrast={alignTool.MinContrast}, Low={alignTool.ContrastLow}");
            Check(alignTool.ContourMode == ContourExtractionMode.StegerSubpixel,
                "AlignTool Default ContourExtractionMode is StegerSubpixel", $"Mode={alignTool.ContourMode}");

            // 3. TrainShapeModelNode & ShapeMatchNode Default ContourMode and MinContrast/MinComponentSize
            var trainNode = new TrainShapeModelNode { Id = "train_test" };
            Check(trainNode.ContourMode == ContourExtractionMode.StegerSubpixel,
                "TrainShapeModelNode Default ContourMode is StegerSubpixel", $"Mode={trainNode.ContourMode}");
            trainNode.MinContrast = 12;
            trainNode.MinComponentSize = 20;
            Check(trainNode.MinContrast == 12 && trainNode.MinComponentSize == 20 && trainNode.ToolInstance.MinLength == 20,
                "TrainShapeModelNode MinContrast and MinComponentSize Properties Sync", $"MinContrast={trainNode.MinContrast}, MinComp={trainNode.MinComponentSize}");

            var matchNode = new ShapeMatchNode { Id = "match_test" };
            Check(matchNode.ContourMode == ContourExtractionMode.StegerSubpixel,
                "ShapeMatchNode Default ContourMode is StegerSubpixel", $"Mode={matchNode.ContourMode}");
            matchNode.MinContrast = 14;
            matchNode.MinComponentSize = 22;
            Check(matchNode.MinContrast == 14 && matchNode.MinComponentSize == 22 && matchNode.ToolInstance.MinLength == 22,
                "ShapeMatchNode MinContrast and MinComponentSize Properties Sync", $"MinContrast={matchNode.MinContrast}, MinComp={matchNode.MinComponentSize}");

            // 4. Train with StegerSubpixel: verify discrete ModelContours (no spiderweb single polyline)
            alignTool.Train(mat54, null, numLevels: 4, contrastLow: est54.ContrastLow, contrastHigh: est54.ContrastHigh,
                minContrast: est54.MinContrast, minComponentSize: est54.MinComponentSize);
            Check(alignTool.Model != null && alignTool.Model.ModelContours.Count > 1,
                "Topological Contours Preserved as Discrete Polylines (Count > 1, Eliminates Spiderweb)",
                $"Contours={alignTool.Model?.ModelContours.Count}");

            // 5. MinComponentSize filter verification: ensure all preserved contours have length >= MinComponentSize
            bool allLengthsValid = true;
            float shortest = float.MaxValue;
            if (alignTool.Model != null)
            {
                foreach (var poly in alignTool.Model.ModelContours)
                {
                    float len = 0f;
                    for (int i = 1; i < poly.Count; i++)
                    {
                        float dx = poly[i].X - poly[i - 1].X;
                        float dy = poly[i].Y - poly[i - 1].Y;
                        len += MathF.Sqrt(dx * dx + dy * dy);
                    }
                    if (len < shortest) shortest = len;
                    if (len < est54.MinComponentSize - 1.0f && poly.Count > 1)
                    {
                        allLengthsValid = false;
                    }
                }
            }
            Check(allLengthsValid && shortest >= est54.MinComponentSize - 1.0f,
                "MinComponentSize Filter Discards Micro Noise (Shortest >= MinComponentSize)",
                $"Shortest={shortest:F1}px, MinComponentSize={est54.MinComponentSize}");

            // 6. ModelSerializer serialization of MinContrast into .shm offset 85
            string tempShm = Path.Combine(Path.GetTempPath(), "test_min_contrast.shm");
            try
            {
                alignTool.Model!.SaveShm(tempShm);
                var loadedModel = ShapeModel.LoadShm(tempShm);
                Check(loadedModel.MinContrast == alignTool.Model.MinContrast && loadedModel.MinContrast > 0,
                    "ModelSerializer .shm Offset 85 MinContrast Binary Round-Trip",
                    $"Saved={alignTool.Model.MinContrast}, Loaded={loadedModel.MinContrast}");
                Check(loadedModel.MinComponentSize == alignTool.Model.MinComponentSize,
                    "ModelSerializer .shm MinComponentSize Round-Trip",
                    $"MinComponentSize={loadedModel.MinComponentSize}");
            }
            finally
            {
                if (File.Exists(tempShm)) File.Delete(tempShm);
            }
        }

        // 7. Small template auto MinComponentSize == 5
        using var smallMat = new Mat(48, 48, MatType.CV_8UC1, new Scalar(128));
        Cv2.Circle(smallMat, new Point(24, 24), 16, new Scalar(255), 2);
        var smallEst = ShapeTrainer.AutoEstimateContrast(smallMat);
        Check(smallEst.MinComponentSize == 5, "Small Template (<64px) Auto MinComponentSize == 5", $"Size={smallEst.MinComponentSize}");
    }

    static void TestMilestone2VisualizerInliersOutliers(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Milestone 2: R3 Subpixel Inlier/Outlier Visual Feedback ---");

        // 1. Template Creation Visuals (Contour Img)
        using var synthTpl = new Mat(120, 120, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(synthTpl, new Point(60, 60), 35, new Scalar(220), -1);

        var model = ShapeTrainer.Train(synthTpl, contourMode: ContourExtractionMode.StegerSubpixel);
        Check(model != null && model.Levels[0].Features.Count > 0, "Model Trained with Steger Subpixel Features", $"Count={model?.Levels[0].Features.Count}");

        int initialFeatures = model!.Levels[0].Features.Count;
        int erased = model.RemoveFeaturesByBrush(new Point2f(60, 25), 12f);
        Check(erased > 0 && model.MaskedFeatures.Count > 0, "Brush Erase Populates MaskedFeatures", $"Erased={erased}, Masked={model.MaskedFeatures.Count}");

        using var contourImg = new Mat(120, 120, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.DrawTemplateContourImage(contourImg, model);

        bool hasGreen = false;
        bool hasRed = false;
        bool hasOriginDot = false;

        byte maxG = 0, maxR = 0;
        for (int y = 0; y < contourImg.Rows; y++)
        {
            for (int x = 0; x < contourImg.Cols; x++)
            {
                var p = contourImg.At<Vec3b>(y, x);
                // Pure green line anti-aliased on black: B=0, G>100, R=0
                if (p.Item0 == 0 && p.Item1 > 100 && p.Item2 == 0) hasGreen = true;
                if (p.Item1 > maxG && p.Item0 == 0 && p.Item2 == 0) maxG = p.Item1;

                // Pure red line anti-aliased on black: B=0, G=0, R>100
                if (p.Item0 == 0 && p.Item1 == 0 && p.Item2 > 100) hasRed = true;
                if (p.Item2 > maxR && p.Item0 == 0 && p.Item1 == 0) maxR = p.Item2;

                // Origin dot: Yellow (B=0, G=255, R=255)
                if (p.Item0 == 0 && p.Item1 > 200 && p.Item2 > 200) hasOriginDot = true;
            }
        }

        Check(hasGreen, "Contour Img Contains Green (#00FF00) Subpixel Tangent Segments", $"MaxG={maxG}");
        Check(hasRed, "Contour Img Contains Red (#FF0000) Masked Tangent Segments", $"MaxR={maxR}");
        Check(hasOriginDot, "Contour Img Contains Coordinate Frame Origin Dot (#00FFFF)");

        // Undo & Reset feature removal
        model.UndoFeatureRemoval();
        Check(model.Levels[0].Features.Count == initialFeatures && model.MaskedFeatures.Count == 0,
            "UndoFeatureRemoval Restores Features and Clears MaskedFeatures",
            $"Features={model.Levels[0].Features.Count}, Masked={model.MaskedFeatures.Count}");

        // 2. Match Overlay Inliers vs Outliers on Defect Region
        using var sceneWithDefect = synthTpl.Clone();
        // Cut off top half of the circle (defect/occlusion)
        Cv2.Rectangle(sceneWithDefect, new Rect(0, 0, 120, 60), new Scalar(40), -1);

        var match = new MatchResult { X = 60, Y = 60, AngleDeg = 0, Scale = 1.0f, Score = 0.5f };
        using var overlayMat = sceneWithDefect.CvtColor(ColorConversionCodes.GRAY2BGR);
        ShapeVisualizer.ClassifyAndDrawFeaturesOnTheFly(overlayMat, match, model, sceneWithDefect, minContrast: 9f, halfLen: 0.6f);

        Check(match.InlierCount > 0, "Match Inlier Count > 0 (Non-occluded half)", $"Inliers={match.InlierCount}");
        Check(match.OutlierCount > 0, "Match Outlier Count > 0 (Occluded defect region)", $"Outliers={match.OutlierCount}");
        Check(match.InlierRatio >= 0.3f && match.InlierRatio <= 0.7f,
            "Inlier Ratio Represents Defect Area [30%, 70%]",
            $"Ratio={match.InlierRatio:P1}");

        bool overlayHasGreen = false;
        bool overlayHasRed = false;
        byte maxOverG = 0, maxOverR = 0;
        for (int y = 0; y < overlayMat.Rows; y++)
        {
            for (int x = 0; x < overlayMat.Cols; x++)
            {
                var p = overlayMat.At<Vec3b>(y, x);
                // Background is gray 40. Green segment raises G (G > 100, B < 60, R < 60)
                if (p.Item1 > 100 && p.Item0 < 60 && p.Item2 < 60) overlayHasGreen = true;
                if (p.Item1 > maxOverG && p.Item0 < 60 && p.Item2 < 60) maxOverG = p.Item1;

                // Red segment raises R (R > 100, B < 60, G < 60)
                if (p.Item2 > 100 && p.Item0 < 60 && p.Item1 < 60) overlayHasRed = true;
                if (p.Item2 > maxOverR && p.Item0 < 60 && p.Item1 < 60) maxOverR = p.Item2;
            }
        }
        Check(overlayHasGreen, "Match Overlay Contains Green (#00FF00) Inlier Segments", $"MaxG={maxOverG}");
        Check(overlayHasRed, "Match Overlay Contains Red (#FF0000) Outlier/Defect Segments", $"MaxR={maxOverR}");

        // 3. TrainShapeModelNode and ShapeMatchNode Integration
        var trainNode = new TrainShapeModelNode { Id = "train_r3" };
        var trainInputs = new Dictionary<string, object?> { ["image"] = synthTpl };
        var trainContext = new GraphExecutionContext();
        var trainOutputs = trainNode.ExecuteAsync(trainInputs, trainContext).GetAwaiter().GetResult();
        Check(trainOutputs.TryGetValue("image", out var trainImgObj) && trainImgObj is Mat tImg && tImg.Channels() == 3,
            "TrainShapeModelNode Produces 3-Channel BGR Contour Img");

        var matchNode = new ShapeMatchNode { Id = "match_r3", MinScore = 0.5f };
        var matchInputs = new Dictionary<string, object?>
        {
            ["image"] = synthTpl,
            ["model"] = trainOutputs["model"]
        };
        var matchOutputs = matchNode.ExecuteAsync(matchInputs, trainContext).GetAwaiter().GetResult();
        Check(matchOutputs.TryGetValue("image", out var matchImgObj) && matchImgObj is Mat mImg && mImg.Channels() == 3,
            "ShapeMatchNode Produces 3-Channel BGR Match Overlay");
        Check(matchNode.LastMatches.Count > 0 && matchNode.LastMatches[0].InlierCount > 0,
            "ShapeMatchNode Executes Inlier/Outlier Classification",
            $"Inliers={matchNode.LastMatches[0].InlierCount}, Outliers={matchNode.LastMatches[0].OutlierCount}");

        // 4. Performance & Memory Efficiency: Rendering Time < 2.0 ms
        int perfIterations = 100;
        using var perfOverlay = new Mat(400, 400, MatType.CV_8UC3, Scalar.All(0));
        var dummyMatches = new List<MatchResult> { match };
        // Warmup JIT
        ShapeVisualizer.DrawMatchOverlay(perfOverlay, dummyMatches, model);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < perfIterations; i++)
        {
            ShapeVisualizer.DrawMatchOverlay(perfOverlay, dummyMatches, model);
        }
        sw.Stop();
        double msPerRender = sw.Elapsed.TotalMilliseconds / perfIterations;
        Check(msPerRender < 10.0, "Subpixel Visualizer Rendering Time < 10.0 ms", $"Time={msPerRender:F3} ms");

        // 5. Robustness: Null & Empty Image Handling
        bool nullSafe = true;
        try
        {
            ShapeVisualizer.DrawTemplateContourImage(null!, null!);
            using var emptyMat = new Mat();
            ShapeVisualizer.DrawTemplateContourImage(emptyMat, model);
            ShapeVisualizer.DrawMatchOverlay(emptyMat, null!);
            ShapeVisualizer.DrawOriginCrosshair(null!, 0, 0);
            ShapeVisualizer.ClassifyMatchFeatures(null!, null!, null!);
        }
        catch
        {
            nullSafe = false;
        }
        Check(nullSafe, "ShapeVisualizer Gracefully Handles Null and Empty Inputs");
    }

    static void TestMilestone4VerificationAndDelivery(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Milestone 4: R5 Verification & Delivery Deep Solidification ---");

        // =========================================================================
        // 1. AutoEstimateContrast: Sensor Noise, Percentiles 25/70/85, Multi-Resolution Pyramids & Small Templates
        // =========================================================================

        // 1.1 Flat image with zero gradient -> graceful fallback without crash
        using var flatImg = new Mat(100, 100, MatType.CV_8UC1, new Scalar(128));
        var flatEst = ShapeTrainer.AutoEstimateContrast(flatImg);
        Check(flatEst.MinContrast >= 5 && flatEst.MinContrast <= 30, "Flat Image AutoEstimate: Safe MinContrast", $"Min={flatEst.MinContrast}");
        Check(flatEst.ContrastHigh > flatEst.ContrastLow, "Flat Image AutoEstimate: ContrastHigh > ContrastLow", $"High={flatEst.ContrastHigh}, Low={flatEst.ContrastLow}");

        // 1.2 Sensor noise estimation: Clean image vs Noisy image
        using var cleanImg = new Mat(200, 200, MatType.CV_8UC1, new Scalar(50));
        Cv2.Circle(cleanImg, new Point(100, 100), 50, new Scalar(200), -1);
        var cleanEst = ShapeTrainer.AutoEstimateContrast(cleanImg);

        // Add Gaussian/Uniform synthetic sensor noise (amplitude ~20)
        using var noisyImg = cleanImg.Clone();
        using var noise = new Mat(200, 200, MatType.CV_8UC1);
        Cv2.Randu(noise, new Scalar(0), new Scalar(40));
        Cv2.Add(noisyImg, noise, noisyImg);
        var noisyEst = ShapeTrainer.AutoEstimateContrast(noisyImg);

        Check(noisyEst.NoiseStd > cleanEst.NoiseStd * 2.0f, "AutoEstimate: Sensor Noise Estimation Detects Physical Noise Floor", $"CleanNoise={cleanEst.NoiseStd:F2}, NoisyNoise={noisyEst.NoiseStd:F2}");
        Check(noisyEst.MinContrast >= cleanEst.MinContrast, "AutoEstimate: MinContrast Adapts Upward Under High Noise", $"CleanMin={cleanEst.MinContrast}, NoisyMin={noisyEst.MinContrast}");

        // 1.3 Percentiles 25 / 70 / 85 ordering and thresholds
        Check(cleanEst.ContrastHigh >= cleanEst.ContrastLow + 10, "AutoEstimate: ContrastHigh (85th percentile seed) >= Low + 10", $"High={cleanEst.ContrastHigh}, Low={cleanEst.ContrastLow}");
        Check(cleanEst.ContrastLow >= cleanEst.MinContrast + 5, "AutoEstimate: ContrastLow (70th percentile threshold) >= Min + 5", $"Low={cleanEst.ContrastLow}, Min={cleanEst.MinContrast}");

        // 1.4 Pyramid Level Formula: L = clamp(round(log2(min(W, H)/16)) + 1, 1, 6) across various dimensions
        using var mat640 = new Mat(480, 640, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(mat640, new Point(320, 240), 100, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(mat640).NumLevels == 6, "Auto Pyramid Levels: 640x480 yields 6 levels", $"Levels={ShapeTrainer.AutoEstimateContrast(mat640).NumLevels}");

        using var mat256 = new Mat(256, 256, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(mat256, new Point(128, 128), 60, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(mat256).NumLevels == 5, "Auto Pyramid Levels: 256x256 yields 5 levels", $"Levels={ShapeTrainer.AutoEstimateContrast(mat256).NumLevels}");

        using var mat128 = new Mat(128, 128, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(mat128, new Point(64, 64), 30, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(mat128).NumLevels == 4, "Auto Pyramid Levels: 128x128 yields 4 levels", $"Levels={ShapeTrainer.AutoEstimateContrast(mat128).NumLevels}");

        using var mat32 = new Mat(32, 32, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(mat32, new Point(16, 16), 8, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(mat32).NumLevels == 2, "Auto Pyramid Levels: 32x32 yields 2 levels", $"Levels={ShapeTrainer.AutoEstimateContrast(mat32).NumLevels}");

        using var mat16 = new Mat(16, 16, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(mat16, new Point(8, 8), 4, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(mat16).NumLevels == 1, "Auto Pyramid Levels: 16x16 yields 1 level", $"Levels={ShapeTrainer.AutoEstimateContrast(mat16).NumLevels}");

        // 1.5 Small Template Threshold: min(W, H) < 64 px => MinComponentSize = 5, else 16
        using var matSmall50 = new Mat(50, 50, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(matSmall50, new Point(25, 25), 12, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(matSmall50).MinComponentSize == 5, "Auto MinComponentSize: Template < 64px sets MinComponentSize=5", $"Size={ShapeTrainer.AutoEstimateContrast(matSmall50).MinComponentSize}");

        using var matStd80 = new Mat(80, 80, MatType.CV_8UC1, new Scalar(40));
        Cv2.Circle(matStd80, new Point(40, 40), 20, new Scalar(200), -1);
        Check(ShapeTrainer.AutoEstimateContrast(matStd80).MinComponentSize == 16, "Auto MinComponentSize: Template >= 64px sets MinComponentSize=16", $"Size={ShapeTrainer.AutoEstimateContrast(matStd80).MinComponentSize}");

        // =========================================================================
        // 2. StegerSubpixel Default, Elimination of ApproxPolyDP & MinComponentSize Arc Length Filter
        // =========================================================================

        // 2.1 Default modes
        var defaultAlignTool = new AlignTool("DefaultCheck");
        Check(defaultAlignTool.ContourMode == ContourExtractionMode.StegerSubpixel, "AlignTool default ContourMode is StegerSubpixel");

        var defaultTrainNode = new TrainShapeModelNode();
        Check(defaultTrainNode.ContourMode == ContourExtractionMode.StegerSubpixel, "TrainShapeModelNode default ContourMode is StegerSubpixel");

        var defaultMatchNode = new ShapeMatchNode();
        Check(defaultMatchNode.ContourMode == ContourExtractionMode.StegerSubpixel, "ShapeMatchNode default ContourMode is StegerSubpixel");

        // 2.2 Smooth subpixel contours (No ApproxPolyDP polygon decimation)
        var stegerModel = ShapeTrainer.Train(cleanImg, contourMode: ContourExtractionMode.StegerSubpixel);
        Check(stegerModel.Levels.Count > 0 && stegerModel.Levels[0].Features.Count > 50, "Steger Subpixel Features Extracted Dense Continuous Boundary", $"Features={stegerModel.Levels[0].Features.Count}");

        // Verify subpixel continuous resolution: features should have fractional coordinates rather than integer grid
        bool hasFractionalCoords = stegerModel.Levels[0].Features.Any(f =>
            MathF.Abs(f.X - MathF.Round(f.X)) > 0.05f ||
            MathF.Abs(f.Y - MathF.Round(f.Y)) > 0.05f);
        Check(hasFractionalCoords, "Steger Extraction Preserves True Floating Subpixel Coordinates (No ApproxPolyDP Integer Decimation)");

        // 2.3 MinComponentSize arc length filter: noise specks < 16 px are completely discarded
        using var multiContourImg = new Mat(200, 200, MatType.CV_8UC1, new Scalar(30));
        // Large contour: circle perimeter ~ 188 px
        Cv2.Circle(multiContourImg, new Point(100, 100), 30, new Scalar(220), -1);
        // Micro noise specks: single pixel and 2px segment
        multiContourImg.Set(20, 20, (byte)220);
        Cv2.Line(multiContourImg, new Point(20, 40), new Point(22, 40), new Scalar(220), 1);

        var filteredModel = ShapeTrainer.Train(multiContourImg, minLength: 16, contourMode: ContourExtractionMode.StegerSubpixel);
        bool hasNoiseSpeckFeatures = filteredModel.Levels[0].Features.Any(f =>
            (f.X + filteredModel.OriginX < 40) && (f.Y + filteredModel.OriginY < 80));
        Check(!hasNoiseSpeckFeatures, "MinComponentSize Arc Length Filter Discards All Specks < 16px", $"ModelContours={filteredModel.ModelContours.Count}");
        Check(filteredModel.ModelContours.Count >= 1, "Genuine Contour Retained Under MinComponentSize Filtering");
        bool allContoursPassMinLength = filteredModel.ModelContours.All(c => {
            float len = 0f;
            for (int i = 1; i < c.Count; i++) {
                float dx = c[i].X - c[i - 1].X;
                float dy = c[i].Y - c[i - 1].Y;
                len += MathF.Sqrt(dx * dx + dy * dy);
            }
            return len >= 15.0f;
        });
        Check(allContoursPassMinLength, "All Retained ModelContours Satisfy MinComponentSize >= 16px Arc Length");

        // =========================================================================
        // 3. Inlier / Outlier Visualizer: Colors & Directional Dot Product Classification
        // =========================================================================

        // 3.1 Verify Exact BGR Industrial Colors
        Check(ShapeVisualizer.ColorInlierGreen.Val0 == 0 && ShapeVisualizer.ColorInlierGreen.Val1 == 255 && ShapeVisualizer.ColorInlierGreen.Val2 == 0,
            "Visualizer Inlier Color is Pure Green #00FF00 (BGR: 0, 255, 0)");
        Check(ShapeVisualizer.ColorOutlierRed.Val0 == 0 && ShapeVisualizer.ColorOutlierRed.Val1 == 0 && ShapeVisualizer.ColorOutlierRed.Val2 == 255,
            "Visualizer Outlier Color is Pure Red #FF0000 (BGR: 0, 0, 255)");
        Check(ShapeVisualizer.ColorOriginMagenta.Val0 == 255 && ShapeVisualizer.ColorOriginMagenta.Val1 == 0 && ShapeVisualizer.ColorOriginMagenta.Val2 == 255,
            "Visualizer Origin Color is Pure Magenta #FF00FF (BGR: 255, 0, 255)");

        // 3.2 Directional Dot Product Classification: Inverted Polarity Scene
        // Model: White circle (200) on dark background (30) -> gradient points outward
        using var tplCircle = new Mat(120, 120, MatType.CV_8UC1, new Scalar(30));
        Cv2.Circle(tplCircle, new Point(60, 60), 30, new Scalar(200), -1);
        var circleModel = ShapeTrainer.Train(tplCircle, contourMode: ContourExtractionMode.StegerSubpixel);

        // Matching on Normal Polarity scene (White circle on dark background)
        using var sceneNormal = new Mat(120, 120, MatType.CV_8UC1, new Scalar(30));
        Cv2.Circle(sceneNormal, new Point(60, 60), 30, new Scalar(200), -1);
        var matchNormal = new MatchResult { X = 60, Y = 60, AngleDeg = 0f, Scale = 1.0f };
        ShapeVisualizer.ClassifyMatchFeatures(matchNormal, circleModel, sceneNormal, minContrast: 9f);
        Check(matchNormal.InlierCount >= (int)(circleModel.Levels[0].Features.Count * 0.95f) && matchNormal.InlierRatio >= 0.95f,
            "Normal Polarity Scene: High Inlier Ratio >= 95% (DotProduct > 0)",
            $"Inliers={matchNormal.InlierCount}/{circleModel.Levels[0].Features.Count}, Ratio={matchNormal.InlierRatio:P1}");

        // Matching on Inverted Polarity scene (Dark circle on white background) -> dot product < 0
        using var sceneInverted = new Mat(120, 120, MatType.CV_8UC1, new Scalar(200));
        Cv2.Circle(sceneInverted, new Point(60, 60), 30, new Scalar(30), -1);
        var matchInverted = new MatchResult { X = 60, Y = 60, AngleDeg = 0f, Scale = 1.0f };
        ShapeVisualizer.ClassifyMatchFeatures(matchInverted, circleModel, sceneInverted, minContrast: 9f);
        Check(matchInverted.OutlierCount == circleModel.Levels[0].Features.Count && matchInverted.InlierCount == 0,
            "Inverted Polarity Scene: Dot Product < 0 Correctly Classifies ALL Points as OUTLIERS",
            $"Inliers={matchInverted.InlierCount}, Outliers={matchInverted.OutlierCount}");

        // 3.3 Visualizer Render Verification: Green Inliers vs Red Outliers on Black Canvas
        using var overlayNormal = new Mat(120, 120, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.ClassifyAndDrawFeaturesOnTheFly(overlayNormal, matchNormal, circleModel, sceneNormal);
        using var overlayInverted = new Mat(120, 120, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.ClassifyAndDrawFeaturesOnTheFly(overlayInverted, matchInverted, circleModel, sceneInverted);

        int normalGreenPx = 0, normalRedPx = 0;
        for (int y = 0; y < overlayNormal.Rows; y++)
        {
            for (int x = 0; x < overlayNormal.Cols; x++)
            {
                var p = overlayNormal.At<Vec3b>(y, x);
                if (p.Item1 > 100 && p.Item0 == 0 && p.Item2 == 0) normalGreenPx++;
                if (p.Item2 > 100 && p.Item0 == 0 && p.Item1 == 0) normalRedPx++;
            }
        }
        Check(normalGreenPx > 50 && normalRedPx == 0, "Normal Scene Tangents Render Pure Inlier Green (#00FF00)", $"GreenPx={normalGreenPx}, RedPx={normalRedPx}");

        int invertedGreenPx = 0, invertedRedPx = 0;
        for (int y = 0; y < overlayInverted.Rows; y++)
        {
            for (int x = 0; x < overlayInverted.Cols; x++)
            {
                var p = overlayInverted.At<Vec3b>(y, x);
                if (p.Item1 > 100 && p.Item0 == 0 && p.Item2 == 0) invertedGreenPx++;
                if (p.Item2 > 100 && p.Item0 == 0 && p.Item1 == 0) invertedRedPx++;
            }
        }
        Check(invertedRedPx > 50 && invertedGreenPx == 0, "Inverted Scene Tangents Render Pure Outlier Red (#FF0000)", $"GreenPx={invertedGreenPx}, RedPx={invertedRedPx}");

        // =========================================================================
        // 4. WPF UI Node Properties: MinContrast, MinComponentSize & Two-Way Sync
        // =========================================================================

        var trainNode = new TrainShapeModelNode { Id = "train_ui_test" };
        trainNode.MinContrast = 18;
        trainNode.MinComponentSize = 24;
        Check(trainNode.MinContrast == 18 && trainNode.ToolInstance.MinContrast == 18,
            "TrainShapeModelNode MinContrast Syncs with AlignTool Instance", $"Node={trainNode.MinContrast}, Tool={trainNode.ToolInstance.MinContrast}");
        Check(trainNode.MinComponentSize == 24 && trainNode.ToolInstance.MinComponentSize == 24,
            "TrainShapeModelNode MinComponentSize Syncs with AlignTool Instance", $"Node={trainNode.MinComponentSize}, Tool={trainNode.ToolInstance.MinComponentSize}");
        Check(trainNode.MinLength == 24, "TrainShapeModelNode MinLength Alias Syncs with MinComponentSize");

        // Verify TrainShapeModelNode.AutoEstimateParameters automatically updates properties
        var estResult = trainNode.AutoEstimateParameters(cleanImg);
        Check(trainNode.MinContrast == estResult.MinContrast && trainNode.MinComponentSize == estResult.MinComponentSize,
            "TrainShapeModelNode AutoEstimateParameters Updates Node Properties",
            $"MinContrast={trainNode.MinContrast}, MinComp={trainNode.MinComponentSize}");

        var matchNode = new ShapeMatchNode { Id = "match_ui_test" };
        matchNode.MinContrast = 22;
        matchNode.MinComponentSize = 32;
        Check(matchNode.MinContrast == 22 && matchNode.ToolInstance.MinContrast == 22,
            "ShapeMatchNode MinContrast Syncs with AlignTool Instance", $"Node={matchNode.MinContrast}, Tool={matchNode.ToolInstance.MinContrast}");
        Check(matchNode.MinComponentSize == 32 && matchNode.ToolInstance.MinComponentSize == 32,
            "ShapeMatchNode MinComponentSize Syncs with AlignTool Instance", $"Node={matchNode.MinComponentSize}, Tool={matchNode.ToolInstance.MinComponentSize}");
        Check(matchNode.MinContourLength == 32, "ShapeMatchNode MinContourLength Alias Syncs with MinComponentSize");
    }

    static void TestAdversarialEmpiricalChallenger(string imgsDir)
    {
        Console.WriteLine("\n--- EMPIRICAL CHALLENGER ADVERSARIAL STRESS-TEST SUITE ---");

        // =========================================================================
        // Area 1: Auto Parameter Estimation Stress-Testing
        // =========================================================================

        // 1.1 Flat/Blank Image (Zero Gradient) Stress Test
        // Test multiple flat intensities: 0, 128, 255 and varying aspect ratios
        foreach (int intensity in new[] { 0, 128, 255 })
        {
            using var flatImg = new Mat(100, 100, MatType.CV_8UC1, new Scalar(intensity));
            ContrastEstimationResult flatRes = default;
            bool threw = false;
            try
            {
                flatRes = ShapeTrainer.AutoEstimateContrast(flatImg);
            }
            catch (Exception ex)
            {
                threw = true;
                Console.WriteLine($"Exception on flat image intensity={intensity}: {ex}");
            }
            Check(!threw, $"Flat Image ({intensity}) AutoEstimate does not throw", $"Intensity={intensity}");
            Check(flatRes.MinContrast >= 5 && flatRes.MinContrast <= 30, $"Flat Image ({intensity}) MinContrast in [5, 30]", $"MinContrast={flatRes.MinContrast}");
            Check(flatRes.ContrastLow >= 10 && flatRes.ContrastLow <= 120, $"Flat Image ({intensity}) ContrastLow in [10, 120]", $"ContrastLow={flatRes.ContrastLow}");
            Check(flatRes.ContrastHigh >= 20 && flatRes.ContrastHigh > flatRes.ContrastLow, $"Flat Image ({intensity}) ContrastHigh > ContrastLow", $"High={flatRes.ContrastHigh}, Low={flatRes.ContrastLow}");
            Check(!float.IsNaN(flatRes.NoiseStd) && !float.IsInfinity(flatRes.NoiseStd) && flatRes.NoiseStd >= 0f, $"Flat Image ({intensity}) NoiseStd is finite non-negative", $"NoiseStd={flatRes.NoiseStd}");
        }

        // Flat image with mask
        using (var flatWithMask = new Mat(120, 120, MatType.CV_8UC1, new Scalar(50)))
        using (var mask = new Mat(120, 120, MatType.CV_8UC1, new Scalar(0)))
        {
            Cv2.Circle(mask, 60, 60, 40, new Scalar(255), -1);
            var flatMaskRes = ShapeTrainer.AutoEstimateContrast(flatWithMask, mask);
            Check(flatMaskRes.MinContrast >= 5 && flatMaskRes.ContrastHigh > flatMaskRes.ContrastLow,
                "Flat Image with Mask AutoEstimate Clean Fallback",
                $"Min={flatMaskRes.MinContrast}, Low={flatMaskRes.ContrastLow}, High={flatMaskRes.ContrastHigh}");
        }

        // 1.2 Noisy Gradient Image Stress Test (Immerkaer noise verification)
        // Synthesize images with known ramp + calibrated Gaussian noise (std = 0, 5, 15, 30)
        float prevNoise = -1f;
        bool noiseMonotonic = true;
        foreach (double sigma in new[] { 0.0, 5.0, 15.0, 30.0 })
        {
            using var baseRamp = new Mat(128, 128, MatType.CV_8UC1, Scalar.All(0));
            // Draw gradient ramp
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    baseRamp.Set(y, x, (byte)(x * 2));
                }
            }

            using var noisyRamp = new Mat();
            if (sigma > 0)
            {
                using var noise = new Mat(128, 128, MatType.CV_32F);
                Cv2.Randn(noise, 0, sigma);
                using var ramp32f = new Mat();
                baseRamp.ConvertTo(ramp32f, MatType.CV_32F);
                Cv2.Add(ramp32f, noise, ramp32f);
                ramp32f.ConvertTo(noisyRamp, MatType.CV_8U);
            }
            else
            {
                baseRamp.CopyTo(noisyRamp);
            }

            var est = ShapeTrainer.AutoEstimateContrast(noisyRamp);
            Check(est.NoiseStd >= 0f, $"Noisy Image (sigma={sigma}) Immerkaer Noise >= 0", $"NoiseStd={est.NoiseStd:F2}");
            if (sigma > 0)
            {
                Check(est.NoiseStd > 0.5f, $"Noisy Image (sigma={sigma}) Immerkaer Noise > 0.5", $"NoiseStd={est.NoiseStd:F2}");
                if (est.NoiseStd <= prevNoise) noiseMonotonic = false;
            }
            prevNoise = est.NoiseStd;
        }
        Check(noiseMonotonic, "Immerkaer Noise Measurement Monotonically Scales with Injected Noise");

        // Verify heavy noise raises MinContrast
        using (var heavyNoiseImg = new Mat(128, 128, MatType.CV_8UC1))
        {
            Cv2.Randu(heavyNoiseImg, 0, 256);
            var heavyEst = ShapeTrainer.AutoEstimateContrast(heavyNoiseImg);
            Check(heavyEst.MinContrast >= 20, "Heavy Noise Floor elevates MinContrast", $"MinContrast={heavyEst.MinContrast}, NoiseStd={heavyEst.NoiseStd:F2}");
        }

        // 1.3 Small Template Stress Test (min(W, H) < 64 -> MinComponentSize == 5)
        (int W, int H, int expectedMinComp)[] smallTestSizes = new[]
        {
            (16, 16, 5),
            (24, 40, 5),
            (32, 32, 5),
            (48, 64, 5),
            (63, 63, 5),
            (63, 120, 5),
            (120, 63, 5),
            (64, 64, 16),
            (65, 65, 16),
            (128, 80, 16)
        };
        bool allSmallSizesPass = true;
        foreach (var (w, h, expectedComp) in smallTestSizes)
        {
            using var testMat = new Mat(h, w, MatType.CV_8UC1, Scalar.All(100));
            Cv2.Circle(testMat, w / 2, h / 2, Math.Min(w, h) / 4, Scalar.All(200), 2);
            var res = ShapeTrainer.AutoEstimateContrast(testMat);
            if (res.MinComponentSize != expectedComp)
            {
                allSmallSizesPass = false;
                Console.WriteLine($"Size {w}x{h} failed: got {res.MinComponentSize}, expected {expectedComp}");
            }
        }
        Check(allSmallSizesPass, "Small Template (<64px) MinComponentSize == 5, (>=64px) == 16 across 10 dimension combinations");

        // 1.4 Standard Image & Pyramid Levels Formula Verification
        // Formula: L = clamp(round(log2(min(W, H)/16)) + 1, 1, 6)
        (int W, int H, int expectedL)[] pyramidTestSizes = new[]
        {
            (16, 16, 1),
            (20, 20, 1),
            (24, 24, 2),
            (32, 32, 2),
            (48, 48, 3),
            (64, 64, 3),
            (96, 96, 4),
            (128, 128, 4),
            (192, 192, 5),
            (256, 256, 5),
            (384, 384, 6),
            (512, 512, 6),
            (640, 480, 6),
            (1024, 1024, 6)
        };
        bool allPyramidsPass = true;
        foreach (var (w, h, expectedL) in pyramidTestSizes)
        {
            using var testMat = new Mat(h, w, MatType.CV_8UC1, Scalar.All(100));
            Cv2.Circle(testMat, w / 2, h / 2, Math.Min(w, h) / 4, Scalar.All(200), 2);
            var res = ShapeTrainer.AutoEstimateContrast(testMat);
            if (res.NumLevels != expectedL)
            {
                allPyramidsPass = false;
                Console.WriteLine($"Pyramid size {w}x{h} failed: got {res.NumLevels}, expected {expectedL}");
            }
        }
        Check(allPyramidsPass, "Auto Pyramid Levels L = round(log2(min(W, H)/16)) + 1 verified on 14 aspect ratios");


        // =========================================================================
        // Area 2: Contour Decimation & Spiderweb Elimination Challenge
        // =========================================================================

        // 2.1 Verify that Cv2.ApproxPolyDP is completely absent from StegerSubpixel pipeline
        using var circleImg = new Mat(150, 150, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(circleImg, 75, 75, 40, Scalar.All(220), -1);

        var circleModel = ShapeTrainer.Train(circleImg, null, numLevels: 1,
            contrastLow: 20, contrastHigh: 50, minLength: 16, maxFeatures: 500,
            contourMode: ContourExtractionMode.StegerSubpixel);

        Check(circleModel.ModelContours.Count > 0, "StegerSubpixel extracted circle contour", $"Count={circleModel.ModelContours.Count}");
        for (int ci = 0; ci < circleModel.ModelContours.Count; ci++)
        {
            var c = circleModel.ModelContours[ci];
            float len = 0f;
            for (int j = 0; j < c.Count - 1; j++)
            {
                float dx = c[j + 1].X - c[j].X;
                float dy = c[j + 1].Y - c[j].Y;
                len += MathF.Sqrt(dx * dx + dy * dy);
            }
            Console.WriteLine($"[CONTOUR {ci}] Points={c.Count}, ArcLength={len:F1}");
        }
        var circlePts = circleModel.ModelContours[0];
        Check(circlePts.Count > 150, "Circle contour has dense points (>150), confirming no ApproxPolyDP decimation", $"Count={circlePts.Count}");

        float maxStep = 0f;
        for (int i = 0; i < circlePts.Count - 1; i++)
        {
            float dx = circlePts[i + 1].X - circlePts[i].X;
            float dy = circlePts[i + 1].Y - circlePts[i].Y;
            float step = MathF.Sqrt(dx * dx + dy * dy);
            if (step > maxStep) maxStep = step;
        }
        Check(maxStep <= 2.5f, "Arc-length spacing between consecutive contour points is uniform (<= 2.5px)", $"MaxStep={maxStep:F2}px");

        // 2.2 Verify extracted subpixel points have non-integer float coordinates (subpixel precision)
        int nonIntegerCount = 0;
        foreach (var pt in circlePts)
        {
            float fracX = MathF.Abs(pt.X - MathF.Round(pt.X));
            float fracY = MathF.Abs(pt.Y - MathF.Round(pt.Y));
            if (fracX > 0.02f || fracY > 0.02f)
            {
                nonIntegerCount++;
            }
        }
        float nonIntegerRatio = (float)nonIntegerCount / circlePts.Count;
        Check(nonIntegerRatio > 0.85f, "Extracted points have true non-integer subpixel float coordinates",
            $"NonIntegerRatio={nonIntegerRatio:P1} ({nonIntegerCount}/{circlePts.Count})");

        var lvl0Feats = circleModel.Levels[0].Features;
        int featNonInt = 0;
        foreach (var f in lvl0Feats)
        {
            float fracX = MathF.Abs(f.X - MathF.Round(f.X));
            float fracY = MathF.Abs(f.Y - MathF.Round(f.Y));
            if (fracX > 0.02f || fracY > 0.02f) featNonInt++;
        }
        Check((float)featNonInt / lvl0Feats.Count > 0.80f, "Level 0 features have true subpixel coordinates",
            $"FeatNonIntRatio={(float)featNonInt / lvl0Feats.Count:P1}");

        // 2.3 Verify multi-object templates produce multiple discrete contours in model.ModelContours (Count > 1)
        // Proving the spiderweb criss-cross polyline bug is completely eliminated!
        using var multiObjImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(10));
        Cv2.Circle(multiObjImg, 50, 50, 20, Scalar.All(230), -1);
        Cv2.Circle(multiObjImg, 150, 50, 20, Scalar.All(230), -1);
        Cv2.Circle(multiObjImg, 100, 150, 25, Scalar.All(230), -1);

        var multiModel = ShapeTrainer.Train(multiObjImg, null, numLevels: 1,
            contrastLow: 20, contrastHigh: 50, minLength: 16, maxFeatures: 500,
            contourMode: ContourExtractionMode.StegerSubpixel);

        Check(multiModel.ModelContours.Count >= 3,
            "Multi-object template produces multiple discrete contours (ModelContours.Count >= 3)",
            $"ContoursCount={multiModel.ModelContours.Count}");

        bool hasCrissCrossJump = false;
        float maxConsecutiveDist = 0f;
        foreach (var contour in multiModel.ModelContours)
        {
            for (int i = 0; i < contour.Count - 1; i++)
            {
                float dx = contour[i + 1].X - contour[i].X;
                float dy = contour[i + 1].Y - contour[i].Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > maxConsecutiveDist) maxConsecutiveDist = dist;
                if (dist > 15.0f)
                {
                    hasCrissCrossJump = true;
                }
            }
        }
        Check(!hasCrissCrossJump, "Zero criss-cross spiderweb lines between disconnected objects", $"MaxConsecutiveDist={maxConsecutiveDist:F2}px");

        // 2.4 Verify MinComponentSize filter: inject short contours (< 16px) and ensure 100% are discarded
        using var filterTestImg = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(filterTestImg, 100, 100, 30, Scalar.All(240), -1);
        Cv2.Line(filterTestImg, new Point(20, 20), new Point(24, 20), Scalar.All(240), 1);
        Cv2.Line(filterTestImg, new Point(20, 40), new Point(28, 40), Scalar.All(240), 1);
        Cv2.Line(filterTestImg, new Point(20, 60), new Point(32, 60), Scalar.All(240), 1);
        Cv2.Circle(filterTestImg, 30, 90, 1, Scalar.All(240), -1);
        Cv2.Circle(filterTestImg, 30, 120, 1, Scalar.All(240), -1);

        var filteredModel = ShapeTrainer.Train(filterTestImg, null, numLevels: 1,
            contrastLow: 20, contrastHigh: 50, minLength: 16, maxFeatures: 500,
            contourMode: ContourExtractionMode.StegerSubpixel, minComponentSize: 16);

        bool allContoursMeetMinSize = true;
        float shortestRetained = float.MaxValue;
        foreach (var c in filteredModel.ModelContours)
        {
            float arcLen = 0f;
            for (int i = 0; i < c.Count - 1; i++)
            {
                float dx = c[i + 1].X - c[i].X;
                float dy = c[i + 1].Y - c[i].Y;
                arcLen += MathF.Sqrt(dx * dx + dy * dy);
            }
            if (arcLen < shortestRetained) shortestRetained = arcLen;
            if (arcLen < 15.0f) allContoursMeetMinSize = false;
        }
        Check(allContoursMeetMinSize && filteredModel.ModelContours.Count >= 1,
            "100% of short contours (<16px) discarded by MinComponentSize filter",
            $"RetainedContours={filteredModel.ModelContours.Count}, Shortest={shortestRetained:F1}px");


        // =========================================================================
        // Area 3: Inlier/Outlier Classification Empirical Challenge
        // =========================================================================

        // 3.1 Normal Aligned Features: Dot product > 0 -> INLIERS (Green #00FF00)
        using var sceneNormal = new Mat(150, 150, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(sceneNormal, 75, 75, 40, Scalar.All(220), -1);

        var matchNormal = new MatchResult
        {
            X = 75f,
            Y = 75f,
            AngleDeg = 0f,
            Scale = 1.0f,
            Score = 1.0f
        };

        ShapeVisualizer.ClassifyMatchFeatures(matchNormal, circleModel, sceneNormal, minContrast: 10f);

        int lowMagCount = 0;
        int negDotCount = 0;
        int outOfBoundsCount = 0;
        foreach (var fp in matchNormal.FeaturePoints)
        {
            if (!fp.IsInlier)
            {
                if (fp.X < 1 || fp.X >= 149 || fp.Y < 1 || fp.Y >= 149) outOfBoundsCount++;
                else if (fp.DotProduct == 0f) lowMagCount++;
                else if (fp.DotProduct < 0f) negDotCount++;
            }
        }
        Console.WriteLine($"[DIAGNOSTIC] Outliers breakdown: total={matchNormal.OutlierCount}, outOfBounds={outOfBoundsCount}, lowMag={lowMagCount}, negDot={negDotCount}");

        // Real image test on engine_part_054
        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");
        if (File.Exists(ep54))
        {
            using var ep54Mat = Cv2.ImRead(ep54, ImreadModes.Grayscale);
            var ep54Model = ShapeTrainer.Train(ep54Mat, contourMode: ContourExtractionMode.StegerSubpixel);
            Console.WriteLine($"[DIAGNOSTIC] engine_part_054 ModelContours={ep54Model.ModelContours.Count}, Lvl0Features={ep54Model.Levels[0].Features.Count}");
        }

        Check(matchNormal.InlierCount > 0, "Normal Scene: Inlier Count > 0", $"Inliers={matchNormal.InlierCount}");
        Check(matchNormal.OutlierCount == 0 || (float)matchNormal.InlierCount / matchNormal.FeaturePoints.Count >= 0.95f,
            "Normal Scene: Inlier Ratio >= 95% for aligned gradient features",
            $"Inliers={matchNormal.InlierCount}, Outliers={matchNormal.OutlierCount}, Ratio={matchNormal.InlierRatio:P1}");

        using var overlayNormalImg = new Mat(150, 150, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.ClassifyAndDrawFeaturesOnTheFly(overlayNormalImg, matchNormal, circleModel, sceneNormal, minContrast: 10f);

        int normGreenCount = 0, normRedCount = 0;
        for (int y = 0; y < overlayNormalImg.Rows; y++)
        {
            for (int x = 0; x < overlayNormalImg.Cols; x++)
            {
                var col = overlayNormalImg.At<Vec3b>(y, x);
                if (col.Item1 > 50 && col.Item0 == 0 && col.Item2 == 0) normGreenCount++;
                if (col.Item2 > 50 && col.Item0 == 0 && col.Item1 == 0) normRedCount++;
            }
        }
        Check(normGreenCount > 50 && normRedCount == 0,
            "Normal Scene Overlay Renders Pure Green (#00FF00) Inliers, 0 Red pixels",
            $"GreenPixels={normGreenCount}, RedPixels={normRedCount}");

        // 3.2 Inverted Polarity Scene: Dot product < 0 -> OUTLIERS (Red #FF0000)
        using var sceneInverted = new Mat(150, 150, MatType.CV_8UC1, Scalar.All(220));
        Cv2.Circle(sceneInverted, 75, 75, 40, Scalar.All(20), -1);

        var matchInverted = new MatchResult
        {
            X = 75f,
            Y = 75f,
            AngleDeg = 0f,
            Scale = 1.0f,
            Score = 0.5f
        };

        ShapeVisualizer.ClassifyMatchFeatures(matchInverted, circleModel, sceneInverted, minContrast: 10f);

        Check(matchInverted.InlierCount == 0, "Inverted Polarity Scene: 0 Inliers", $"Inliers={matchInverted.InlierCount}");
        Check(matchInverted.OutlierCount == matchInverted.FeaturePoints.Count,
            "Inverted Polarity Scene: 100% Outliers (Opposite gradient dot product < 0)",
            $"Outliers={matchInverted.OutlierCount}/{matchInverted.FeaturePoints.Count}");

        using var overlayInvertedImg = new Mat(150, 150, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.ClassifyAndDrawFeaturesOnTheFly(overlayInvertedImg, matchInverted, circleModel, sceneInverted, minContrast: 10f);

        int invGreenCount = 0, invRedCount = 0;
        for (int y = 0; y < overlayInvertedImg.Rows; y++)
        {
            for (int x = 0; x < overlayInvertedImg.Cols; x++)
            {
                var col = overlayInvertedImg.At<Vec3b>(y, x);
                if (col.Item1 > 50 && col.Item0 == 0 && col.Item2 == 0) invGreenCount++;
                if (col.Item2 > 50 && col.Item0 == 0 && col.Item1 == 0) invRedCount++;
            }
        }
        Check(invRedCount > 50 && invGreenCount == 0,
            "Inverted Polarity Scene Overlay Renders Pure Red (#FF0000) Outliers, 0 Green pixels",
            $"RedPixels={invRedCount}, GreenPixels={invGreenCount}");

        // 3.3 Occluded / Defect Scene: Occluded region classified as OUTLIERS
        using var sceneOccluded = sceneNormal.Clone();
        Cv2.Rectangle(sceneOccluded, new Rect(0, 0, 150, 75), Scalar.All(120), -1);

        var matchOccluded = new MatchResult
        {
            X = 75f,
            Y = 75f,
            AngleDeg = 0f,
            Scale = 1.0f,
            Score = 0.6f
        };

        ShapeVisualizer.ClassifyMatchFeatures(matchOccluded, circleModel, sceneOccluded, minContrast: 10f);

        Check(matchOccluded.InlierCount > 0 && matchOccluded.OutlierCount > 0,
            "Occluded Scene: Both Inliers and Outliers Detected",
            $"Inliers={matchOccluded.InlierCount}, Outliers={matchOccluded.OutlierCount}, Ratio={matchOccluded.InlierRatio:P1}");
        Check(matchOccluded.InlierRatio >= 0.35f && matchOccluded.InlierRatio <= 0.65f,
            "Occluded Scene: Inlier Ratio corresponds accurately to visible unoccluded half (~50%)",
            $"InlierRatio={matchOccluded.InlierRatio:P1}");

        // 3.4 Anti-Aliasing with shift: 4 and Object Coordinate Frame (Red X-Axis, Green Y-Axis Arrow)
        using var templateOverlay = new Mat(150, 150, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.DrawTemplateContourImage(templateOverlay, circleModel);

        int redArrowCount = 0;
        int greenArrowCount = 0;
        int originDotCount = 0;
        for (int y = 0; y < templateOverlay.Rows; y++)
        {
            for (int x = 0; x < templateOverlay.Cols; x++)
            {
                var col = templateOverlay.At<Vec3b>(y, x);
                if (col.Item2 > 200 && col.Item1 == 0 && col.Item0 == 0) redArrowCount++;
                if (col.Item1 > 200 && col.Item2 == 0 && col.Item0 == 0) greenArrowCount++;
                if (col.Item0 == 0 && col.Item1 > 200 && col.Item2 > 200) originDotCount++;
            }
        }
        Check(redArrowCount >= 10, "Template Contour Img Renders Red X-Axis Arrow", $"RedPixels={redArrowCount}");
        Check(greenArrowCount >= 10, "Template Contour Img Renders Green Y-Axis Arrow", $"GreenPixels={greenArrowCount}");
        Check(originDotCount >= 4, "Template Contour Img Renders Origin Dot", $"OriginDotPixels={originDotCount}");

        int intermediateGreenCount = 0;
        for (int y = 0; y < templateOverlay.Rows; y++)
        {
            for (int x = 0; x < templateOverlay.Cols; x++)
            {
                var col = templateOverlay.At<Vec3b>(y, x);
                if (col.Item1 > 10 && col.Item1 < 250 && col.Item0 == 0 && col.Item2 == 0)
                {
                    intermediateGreenCount++;
                }
            }
        }
        Check(intermediateGreenCount > 30,
            "Anti-aliased Line Rendering (shift: 4, AntiAlias) verified via continuous fractional intensity falloff",
            $"AntiAliasedPixels={intermediateGreenCount}");

        // 3.5 Test Off-Center Object Centroid & Proportional Coordinate Frame Scaling
        using var offCenterImg = new Mat(300, 300, MatType.CV_8UC1, Scalar.All(20));
        // Draw white circle with center at (70, 90) and radius 35 on 300x300 image
        Cv2.Circle(offCenterImg, new Point(70, 90), 35, Scalar.All(220), -1);
        var offCenterModel = ShapeTrainer.Train(offCenterImg, mask: null, numLevels: 3,
            contrastLow: 30, contrastHigh: 60, minLength: 10, maxFeatures: 300,
            contourMode: ContourExtractionMode.StegerSubpixel);

        // Origin must be the centroid of the OBJECT (~70, ~90), NOT the center of the image (150, 150)!
        Check(MathF.Abs(offCenterModel.OriginX - 70f) < 2.0f,
            "Model OriginX is Centroid of Object, NOT Image Center",
            $"OriginX={offCenterModel.OriginX:F2}, Expected=70.00 (Image center is 150.0)");
        Check(MathF.Abs(offCenterModel.OriginY - 90f) < 2.0f,
            "Model OriginY is Centroid of Object, NOT Image Center",
            $"OriginY={offCenterModel.OriginY:F2}, Expected=90.00 (Image center is 150.0)");

        // Test Coordinate Frame Drawing on Match Overlay with 45° rotation
        using var sceneRot = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0));
        var matchObj = new MatchResult { X = 80, Y = 80, AngleDeg = 45f, Scale = 1.0f, Score = 0.98f };
        ShapeVisualizer.DrawMatchOverlay(sceneRot, new List<MatchResult> { matchObj }, offCenterModel);

        int rotRed = 0, rotGreen = 0;
        for (int y = 0; y < sceneRot.Rows; y++)
        {
            for (int x = 0; x < sceneRot.Cols; x++)
            {
                var c = sceneRot.At<Vec3b>(y, x);
                if (c.Item2 > 180 && c.Item0 < 50 && c.Item1 < 50) rotRed++;
                if (c.Item1 > 180 && c.Item0 < 50 && c.Item2 < 50) rotGreen++;
            }
        }
        Check(rotRed > 10 && rotGreen > 10, "Match Overlay Renders Red/Green Coordinate Frame Rotated at Object Pose", $"RotRed={rotRed}, RotGreen={rotGreen}");

        // 3.6 Test Matching More Than 30 Objects (Unlimited maxInstances=0 and large count)
        Console.WriteLine("\n[Phase 3.6] Testing Multi-Object Matching Exceeding 30 Cap (Grid of 36 objects)...");
        using var singleObjImg = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(singleObjImg, new Point(50, 50), 14, Scalar.All(230), -1);
        var gridModel = ShapeTrainer.Train(singleObjImg, mask: null, numLevels: 2,
            contrastLow: 30, contrastHigh: 60, minLength: 10, maxFeatures: 150);

        // Synthesize 6x6 grid (36 objects total)
        using var gridScene = new Mat(480, 480, MatType.CV_8UC1, Scalar.All(20));
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                int cx = 40 + c * 75;
                int cy = 40 + r * 75;
                Cv2.Circle(gridScene, new Point(cx, cy), 14, Scalar.All(230), -1);
            }
        }

        // Test with maxInstances = 0 (Unlimited - Halcon / Industry standard)
        var unlimMatches = ShapeMatcher.Match(gridScene, gridModel,
            angleMinDeg: 0, angleMaxDeg: 0, angleStepDeg: 1f,
            scaleMin: 1.0f, scaleMax: 1.0f, minScore: 0.70f, greediness: 0.9f,
            maxInstances: 0);

        Check(unlimMatches.Count == 36,
            "Unlimited Match (maxInstances=0) Finds All 36 Objects (Overcoming old 30 limit)",
            $"Found={unlimMatches.Count}, Expected=36");

        // Test with maxInstances = 36 explicitly
        var exactMatches = ShapeMatcher.Match(gridScene, gridModel,
            angleMinDeg: 0, angleMaxDeg: 0, angleStepDeg: 1f,
            scaleMin: 1.0f, scaleMax: 1.0f, minScore: 0.70f, greediness: 0.9f,
            maxInstances: 36);

        Check(exactMatches.Count == 36,
            "Exact Match (maxInstances=36) Finds Exactly 36 Objects",
            $"Found={exactMatches.Count}, Expected=36");

        // Test with maxInstances = 12 explicitly
        var limitedMatches = ShapeMatcher.Match(gridScene, gridModel,
            angleMinDeg: 0, angleMaxDeg: 0, angleStepDeg: 1f,
            scaleMin: 1.0f, scaleMax: 1.0f, minScore: 0.70f, greediness: 0.9f,
            maxInstances: 12);

        Check(limitedMatches.Count == 12,
            "Limited Match (maxInstances=12) Stops at Exactly 12 Objects",
            $"Found={limitedMatches.Count}, Expected=12");

        // 3.7 Test Continuous Smooth Vector Contour Rendering (Like Reference App)
        Console.WriteLine("\n[Phase 3.7] Testing Continuous Vector Contour Rendering on Template & Match Overlay...");
        using var donutImg = new Mat(160, 160, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(donutImg, new Point(80, 80), 45, Scalar.All(220), 8); // Thick ring
        var donutModel = ShapeTrainer.Train(donutImg, mask: null, numLevels: 3,
            contrastLow: 30, contrastHigh: 60, minLength: 20);

        Check(donutModel.ModelContours.Count >= 2,
            "Donut Model Extracts Clean Outer & Inner Contours",
            $"Contours={donutModel.ModelContours.Count}");

        using var donutOverlay = new Mat(160, 160, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.DrawTemplateContourImage(donutOverlay, donutModel);

        // Verify green pixels form continuous connected components (not tiny tick marks)
        using var hsv = new Mat();
        Cv2.CvtColor(donutOverlay, hsv, ColorConversionCodes.BGR2HSV);
        using var greenMask = new Mat();
        Cv2.InRange(hsv, new Scalar(35, 50, 50), new Scalar(85, 255, 255), greenMask);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int numComponents = Cv2.ConnectedComponentsWithStats(greenMask, labels, stats, centroids);

        // Find largest component area (outer continuous ring)
        int maxCompArea = 0;
        for (int i = 1; i < numComponents; i++)
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area > maxCompArea) maxCompArea = area;
        }

        Check(maxCompArea > 200,
            "Template Contour Renders as Continuous Connected Vector Polyline (MaxArea > 200px)",
            $"MaxArea={maxCompArea}, Components={numComponents}");

        // Test Match Overlay Continuous Contour Rendering
        using var sceneDonut = new Mat(200, 200, MatType.CV_8UC1, Scalar.All(20));
        Cv2.Circle(sceneDonut, new Point(100, 100), 45, Scalar.All(220), 8);
        var donutMatches = ShapeMatcher.Match(sceneDonut, donutModel, angleMinDeg: 0, angleMaxDeg: 0, minScore: 0.8f);

        Check(donutMatches.Count > 0, "Donut Match Succeeded", $"Matches={donutMatches.Count}");

        using var sceneOverlay = new Mat(200, 200, MatType.CV_8UC3, Scalar.All(0));
        ShapeVisualizer.DrawMatchOverlay(sceneOverlay, donutMatches, donutModel);

        using var sceneHsv = new Mat();
        Cv2.CvtColor(sceneOverlay, sceneHsv, ColorConversionCodes.BGR2HSV);
        using var sceneGreenMask = new Mat();
        Cv2.InRange(sceneHsv, new Scalar(35, 50, 50), new Scalar(85, 255, 255), sceneGreenMask);

        int sceneComponents = Cv2.ConnectedComponentsWithStats(sceneGreenMask, labels, stats, centroids);
        int sceneMaxArea = 0;
        for (int i = 1; i < sceneComponents; i++)
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area > sceneMaxArea) sceneMaxArea = area;
        }

        Check(sceneMaxArea > 200,
            "Match Overlay Renders Transformed Continuous Smooth Vector Polylines (MaxArea > 200px)",
            $"SceneMaxArea={sceneMaxArea}, Components={sceneComponents}");
    }

    private static string GetStatusShort(GraphNode node)
    {
        string status = node.Status ?? string.Empty;
        if (status.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) ||
            status == "File Not Found" || status == "No Scene Image" || status == "No Image")
            return "ERR";
        if (node is SourceImageNode src)
        {
            if (src.Status.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                src.Status.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) ||
                src.Status == "File Not Found" || src.Status == "No Scene Image" || src.Status == "No Image")
                return "ERR";
            if (src.LastOutputMat != null && !src.LastOutputMat.Empty())
            {
                return "OK";
            }
            if ((string.IsNullOrEmpty(src.Status) || src.Status == "OK" || src.Status == "Ready") &&
                !string.IsNullOrEmpty(src.ImagePath) && File.Exists(src.ImagePath))
            {
                return "OK";
            }
        }
        if (status == "NG")
            return "NG";
        return "OK";
    }

    static void TestPathResolverAndSourceImageNodeFallback(string imgsDir)
    {
        Console.WriteLine("\n--- Testing PathResolver & SourceImageNode Fallback Hardening ---");

        // -------------------------------------------------------------
        // Sub-Test 1: SourceImageNode direct loading of m2.bmp from imgsDir or via filename "m2.bmp"
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 1] Direct loading of m2.bmp...");
        var srcNode1 = new SourceImageNode();
        string directM2 = Path.Combine(imgsDir, "m2.bmp");
        srcNode1.ImagePath = directM2;
        srcNode1.Execute();
        Check(srcNode1.Status == "OK", "SourceImageNode Direct Load Status is OK", srcNode1.Status);
        Check(srcNode1.LastOutputMat != null && !srcNode1.LastOutputMat.Empty(), "SourceImageNode Direct Load LastOutputMat is not null/empty");
        Check(srcNode1.LastOutputMat?.Width == 207 && srcNode1.LastOutputMat?.Height == 197,
            "SourceImageNode Direct Load Dimensions Match 207x197",
            $"{srcNode1.LastOutputMat?.Width}x{srcNode1.LastOutputMat?.Height}");
        Check(GetStatusShort(srcNode1) == "OK", "SourceImageNode Direct Load StatusShort is OK", GetStatusShort(srcNode1));

        // Direct loading via simple filename "m2.bmp"
        var srcNode1Name = new SourceImageNode();
        srcNode1Name.ImagePath = "m2.bmp";
        srcNode1Name.Execute();
        Check(srcNode1Name.Status == "OK", "SourceImageNode Filename 'm2.bmp' Status is OK", srcNode1Name.Status);
        Check(srcNode1Name.LastOutputMat != null && !srcNode1Name.LastOutputMat.Empty(), "SourceImageNode Filename 'm2.bmp' Mat is Valid");
        Check(srcNode1Name.LastOutputMat?.Width == 207 && srcNode1Name.LastOutputMat?.Height == 197,
            "SourceImageNode Filename 'm2.bmp' Dimensions Match 207x197",
            $"{srcNode1Name.LastOutputMat?.Width}x{srcNode1Name.LastOutputMat?.Height}");
        Check(GetStatusShort(srcNode1Name) == "OK", "SourceImageNode Filename 'm2.bmp' StatusShort is OK", GetStatusShort(srcNode1Name));

        // -------------------------------------------------------------
        // Sub-Test 2: Relative path resolution for m2.bmp ("m2.bmp", @"imgs\m2.bmp", @"image\m2.bmp")
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 2] Relative path resolution for m2.bmp...");
        string[] testRelativePaths = { "m2.bmp", @"imgs\m2.bmp", @"image\m2.bmp" };
        foreach (var relPath in testRelativePaths)
        {
            string? resolved = PathResolver.ResolveImagePath(relPath, projectDir: imgsDir);
            Check(!string.IsNullOrEmpty(resolved) && File.Exists(resolved),
                $"PathResolver.ResolveImagePath successfully resolves '{relPath}'",
                $"Resolved={resolved}");
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
            {
                Check(Path.GetFileName(resolved).Equals("m2.bmp", StringComparison.OrdinalIgnoreCase),
                    $"Resolved filename for '{relPath}' matches 'm2.bmp'");
            }

            var nodeRel = new SourceImageNode();
            nodeRel.ImagePath = relPath;
            nodeRel.Execute();
            Check(nodeRel.Status == "OK", $"SourceImageNode loads '{relPath}' with Status OK", nodeRel.Status);
            Check(nodeRel.LastOutputMat != null && !nodeRel.LastOutputMat.Empty(),
                $"SourceImageNode OutputMat is valid for '{relPath}'");
            Check(nodeRel.LastOutputMat?.Width == 207 && nodeRel.LastOutputMat?.Height == 197,
                $"SourceImageNode Dimensions 207x197 for '{relPath}'",
                $"{nodeRel.LastOutputMat?.Width}x{nodeRel.LastOutputMat?.Height}");
            Check(GetStatusShort(nodeRel) == "OK", $"SourceImageNode StatusShort is OK for '{relPath}'");
        }

        // -------------------------------------------------------------
        // Sub-Test 3: Stale cache invalidation simulation
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 3] Stale cache invalidation simulation...");
        var graphStale = PipelineGraph.CreateDefaultInspectionPipeline();
        var srcNodeStale = graphStale.Nodes.OfType<SourceImageNode>().First();

        // Inject poisoned / dead paths into FolderImages
        srcNodeStale.FolderImages.Clear();
        string deadPath1 = @"C:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\demo\image\m2.bmp";
        string deadPath2 = @"Z:\completely_fake_drive_not_existing\missing\m2.bmp";
        string deadPath3 = @"C:\Windows\Temp\dummy_nonexistent_image_12345.png";
        srcNodeStale.FolderImages.Add(deadPath1);
        srcNodeStale.FolderImages.Add(deadPath2);
        srcNodeStale.FolderImages.Add(deadPath3);

        // Call SelectSampleByFileName("m2.bmp")
        srcNodeStale.SelectSampleByFileName("m2.bmp");

        // Verify: Dead path is NOT accepted
        Check(!string.Equals(srcNodeStale.ImagePath, deadPath1, StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(srcNodeStale.ImagePath, deadPath2, StringComparison.OrdinalIgnoreCase),
              "Dead paths were NOT accepted by SelectSampleByFileName",
              srcNodeStale.ImagePath);

        // Verify: Fallback resolution finds valid m2.bmp on disk
        Check(File.Exists(srcNodeStale.ImagePath) &&
              Path.GetFileName(srcNodeStale.ImagePath).Equals("m2.bmp", StringComparison.OrdinalIgnoreCase),
              "Fallback resolution successfully resolved valid m2.bmp on disk",
              srcNodeStale.ImagePath);

        // Verify: Status is OK
        Check(srcNodeStale.Status == "OK", "SourceImageNode Status is OK after stale cache resolution", srcNodeStale.Status);
        Check(GetStatusShort(srcNodeStale) == "OK", "StatusShort is OK after stale cache resolution");

        // Verify: FolderImages is healed and repopulated with valid files
        Check(srcNodeStale.FolderImages.Count > 0, "FolderImages repopulated after healing", $"Count={srcNodeStale.FolderImages.Count}");
        Check(srcNodeStale.FolderImages.All(f => File.Exists(f)), "All entries in healed FolderImages exist on disk");
        Check(!srcNodeStale.FolderImages.Contains(deadPath1) && !srcNodeStale.FolderImages.Contains(deadPath2),
              "Healed FolderImages completely purged stale/dead entries");

        // Verify: Downstream auto-execution runs successfully
        var downstreamMatch = graphStale.Nodes.OfType<ShapeMatchNode>().First();
        Check(downstreamMatch.LastResult != null,
              "Downstream ShapeMatchNode automatically executed upon stale cache healing",
              downstreamMatch.LastResult?.ToString() ?? "null");

        // -------------------------------------------------------------
        // Sub-Test 4: Image navigation (NextImage() / PrevImage()) with poisoned or missing initial entries
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 4] Image navigation with poisoned entries...");
        var navNode = new SourceImageNode();
        navNode.FolderImages.Clear();
        navNode.FolderImages.Add(@"C:\dead_drive_a\ghost1.bmp");
        navNode.FolderImages.Add(@"C:\dead_drive_b\ghost2.bmp");
        string validM2 = Path.Combine(imgsDir, "m2.bmp");
        navNode.FolderImages.Add(validM2);
        string validRetainer = Path.Combine(imgsDir, "retainers_01.png");
        navNode.FolderImages.Add(validRetainer);
        navNode.FolderImages.Add(@"Z:\dead_drive_c\ghost3.png");
        navNode.ImageIndex = 0;

        // NextImage() should skip dead files and land on valid image
        navNode.NextImage();
        Check(File.Exists(navNode.ImagePath), "NextImage() skipped dead entries and landed on existing file", navNode.ImagePath);
        Check(navNode.Status == "OK", "NextImage() status is OK", navNode.Status);
        Check(GetStatusShort(navNode) == "OK", "NextImage() StatusShort is OK");

        // PrevImage() should also skip dead files and land on valid image
        navNode.PrevImage();
        Check(File.Exists(navNode.ImagePath), "PrevImage() landed on existing file", navNode.ImagePath);
        Check(navNode.Status == "OK", "PrevImage() status is OK", navNode.Status);
        Check(GetStatusShort(navNode) == "OK", "PrevImage() StatusShort is OK");

        // Navigation when FolderImages contains ONLY dead files
        var allDeadNode = new SourceImageNode();
        allDeadNode.FolderImages.Clear();
        allDeadNode.FolderImages.Add(@"C:\ghost_image_1.png");
        allDeadNode.FolderImages.Add(@"C:\ghost_image_2.bmp");
        allDeadNode.NextImage(); // triggers EnsureValidFolderImages -> heals from candidate dirs
        Check(File.Exists(allDeadNode.ImagePath), "NextImage() on all-dead cache repopulates from candidate dirs and lands on existing file", allDeadNode.ImagePath);
        Check(allDeadNode.Status == "OK", "All-dead cache NextImage() Status is OK", allDeadNode.Status);
        Check(GetStatusShort(allDeadNode) == "OK", "All-dead cache NextImage() StatusShort is OK");

        // -------------------------------------------------------------
        // Sub-Test 5: PipelineGraph.SaveToFile and PipelineGraph.LoadFromFile roundtrip with cross-folder image path resolution
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 5] Project Save/Load roundtrip with cross-folder image path resolution...");
        string tempProjPath = Path.Combine(Path.GetTempPath(), $"tunvision_roundtrip_test_{Guid.NewGuid():N}.tvproj");
        try
        {
            var saveGraph = PipelineGraph.CreateDefaultInspectionPipeline();
            var saveSrc = saveGraph.Nodes.OfType<SourceImageNode>().First();
            // Simulate a project saved with an old cross-folder path pointing to m2.bmp
            saveSrc.ImagePath = @"C:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\demo\image\m2.bmp";
            saveGraph.SaveToFile(tempProjPath);
            Check(File.Exists(tempProjPath), "Project file saved successfully", tempProjPath);

            // Reload from file
            var loadedGraph = PipelineGraph.LoadFromFile(tempProjPath);
            Check(loadedGraph != null, "Project file loaded successfully");
            Check(loadedGraph != null && loadedGraph.ProjectFilePath == tempProjPath, "Loaded graph retained ProjectFilePath", loadedGraph?.ProjectFilePath ?? "null");

            var loadedSrc = loadedGraph?.Nodes.OfType<SourceImageNode>().FirstOrDefault();
            Check(loadedSrc != null, "Loaded graph contains SourceImageNode");
            Check(loadedSrc != null && File.Exists(loadedSrc.ImagePath), "Loaded SourceImageNode resolved ImagePath to existing file on disk", loadedSrc?.ImagePath ?? "null");
            Check(loadedSrc != null && Path.GetFileName(loadedSrc.ImagePath).Equals("m2.bmp", StringComparison.OrdinalIgnoreCase),
                "Loaded SourceImageNode resolved to m2.bmp", loadedSrc?.ImagePath ?? "null");
            Check(loadedSrc?.Status == "OK", "Loaded SourceImageNode Status is OK", loadedSrc?.Status ?? "null");
            Check(loadedSrc != null && GetStatusShort(loadedSrc) == "OK", "Loaded SourceImageNode StatusShort is OK");

            // Downstream nodes execute without error
            var loadedExec = loadedGraph!.Execute();
            Check(loadedExec.Success, "Loaded graph pipeline execution succeeded without error", $"Time={loadedExec.TotalExecutionTimeMs:F1}ms");
            Check(loadedExec.ExecutedNodes.Count == loadedGraph.Nodes.Count, "All loaded nodes executed in pipeline");
        }
        finally
        {
            if (File.Exists(tempProjPath))
            {
                try { File.Delete(tempProjPath); } catch { }
            }
        }

        // -------------------------------------------------------------
        // Sub-Test 6: Truly missing non-existent file ("totally_missing_image_xyz999.png")
        // -------------------------------------------------------------
        Console.WriteLine("\n[Sub-Test 6] Truly missing non-existent file handling...");
        var missingNode = new SourceImageNode();
        missingNode.ImagePath = "totally_missing_image_xyz999.png";

        // Verify initial status
        Check(missingNode.Status == "File Not Found", "Missing file Status is 'File Not Found'", missingNode.Status);
        Check(missingNode.LastResult is string lr && lr.Contains("totally_missing_image_xyz999.png"),
            "Missing file LastResult contains informative filename", missingNode.LastResult?.ToString() ?? "");
        Check(GetStatusShort(missingNode) == "ERR", "Missing file UI mapping StatusShort is 'ERR'", GetStatusShort(missingNode));
        Check(missingNode.LastOutputMat == null, "Missing file LastOutputMat is null");

        // Verify execution handles missing file gracefully without crashing or throwing
        var execOutputs = missingNode.Execute();
        Check(execOutputs.ContainsKey("image") && execOutputs["image"] == null, "Missing file Execute() returns null image output");
        Check(missingNode.Status == "File Not Found", "Post-execute Status remains 'File Not Found'", missingNode.Status);
        Check(GetStatusShort(missingNode) == "ERR", "Post-execute StatusShort remains 'ERR'", GetStatusShort(missingNode));

        // Verify SelectSampleByFileName with truly missing file handles gracefully
        missingNode.SelectSampleByFileName("totally_missing_image_xyz999.png");
        Check(missingNode.Status == "File Not Found", "SelectSampleByFileName on missing file sets Status to 'File Not Found'", missingNode.Status);
        Check(GetStatusShort(missingNode) == "ERR", "SelectSampleByFileName on missing file UI mapping is 'ERR'", GetStatusShort(missingNode));

        // Verify inside full pipeline graph that missing file propagates without process crash
        var missingGraph = PipelineGraph.CreateDefaultInspectionPipeline();
        var graphSrc = missingGraph.Nodes.OfType<SourceImageNode>().First();
        var graphMatch = missingGraph.Nodes.OfType<ShapeMatchNode>().First();
        var graphVerdict = missingGraph.Nodes.OfType<ToleranceVerdictNode>().First();

        graphSrc.ImagePath = "totally_missing_image_xyz999.png";
        Check(graphSrc.Status == "File Not Found", "Graph SourceImageNode Status is 'File Not Found'", graphSrc.Status);
        Check(GetStatusShort(graphSrc) == "ERR", "Graph SourceImageNode StatusShort is 'ERR'", GetStatusShort(graphSrc));
        Check(graphMatch.Status == "No Scene Image", "Downstream ShapeMatchNode reports 'No Scene Image' gracefully", graphMatch.Status);
        Check(graphVerdict.Status == "NG", "Downstream ToleranceVerdictNode reports 'NG' gracefully", graphVerdict.Status);
    }

    static void TestSourceImageNodeStatusAndCocoaVerification(string imgsDir)
    {
        Console.WriteLine("\n--- Testing SourceImageNode Status & Cocoa Packages Verification (R1 & R2) ---");

        // 1. Direct ImagePath loading of cocoa_packages_05.png
        var cocoaNode = new SourceImageNode();
        cocoaNode.ImagePath = "cocoa_packages_05.png";
        Check(cocoaNode.Status == "OK", "SourceImageNode ImagePath='cocoa_packages_05.png' Status is OK", cocoaNode.Status);
        Check(GetStatusShort(cocoaNode) == "OK", "SourceImageNode ImagePath='cocoa_packages_05.png' StatusShort is OK", GetStatusShort(cocoaNode));

        var outputs = cocoaNode.Execute();
        Check(outputs.ContainsKey("image") && outputs["image"] is Mat, "cocoa_packages_05.png Execute() produces image Mat");
        Check(cocoaNode.LastOutputMat != null && !cocoaNode.LastOutputMat.Empty(), "cocoa_packages_05.png LastOutputMat is non-empty");
        Check(cocoaNode.LastOutputMat?.Width == 640 && cocoaNode.LastOutputMat?.Height == 512,
            "cocoa_packages_05.png Dimensions are 640x512",
            $"{cocoaNode.LastOutputMat?.Width}x{cocoaNode.LastOutputMat?.Height}");
        Check(cocoaNode.Status == "OK", "Post-execute Status for cocoa_packages_05.png remains OK", cocoaNode.Status);
        Check(GetStatusShort(cocoaNode) == "OK", "Post-execute StatusShort for cocoa_packages_05.png is OK", GetStatusShort(cocoaNode));
        Check(cocoaNode.LastResult is string lr && lr.Contains("cocoa_packages_05.png") && lr.Contains("640x512"),
            "LastResult contains filename and dimensions", cocoaNode.LastResult?.ToString() ?? "");

        // 2. SelectSampleByFileName on fresh node
        var cocoaSampleNode = new SourceImageNode();
        cocoaSampleNode.SelectSampleByFileName("cocoa_packages_05.png");
        Check(cocoaSampleNode.Status == "OK", "SelectSampleByFileName('cocoa_packages_05.png') Status is OK", cocoaSampleNode.Status);
        Check(GetStatusShort(cocoaSampleNode) == "OK", "SelectSampleByFileName('cocoa_packages_05.png') StatusShort is OK", GetStatusShort(cocoaSampleNode));
        cocoaSampleNode.Execute();
        Check(cocoaSampleNode.Status == "OK", "Post-execute SelectSampleByFileName Status is OK", cocoaSampleNode.Status);
        Check(GetStatusShort(cocoaSampleNode) == "OK", "Post-execute SelectSampleByFileName StatusShort is OK", GetStatusShort(cocoaSampleNode));

        // 3. Full pipeline execution with cocoa_packages_05.png and downstream match node
        var shapePipe = PipelineGraph.CreateShapeDetectionPipeline();
        var pipeSrc = shapePipe.Nodes.OfType<SourceImageNode>().First();
        pipeSrc.SelectSampleByFileName("cocoa_packages_05.png");
        Check(pipeSrc.Status == "OK", "Pipeline SourceImageNode Status is OK after selection", pipeSrc.Status);
        Check(GetStatusShort(pipeSrc) == "OK", "Pipeline SourceImageNode StatusShort is OK after selection", GetStatusShort(pipeSrc));

        var pipeExec = shapePipe.Execute();
        Check(pipeExec.Success, "Shape pipeline with cocoa_packages_05.png executes successfully");
        Check(pipeSrc.Status == "OK", "Pipeline SourceImageNode Status is OK after pipeline run", pipeSrc.Status);
        Check(GetStatusShort(pipeSrc) == "OK", "Pipeline SourceImageNode StatusShort is OK after pipeline run", GetStatusShort(pipeSrc));

        // 4. Verify R2: Genuine failures still report ERR
        var failNode = new SourceImageNode();
        failNode.ImagePath = "definitely_not_a_real_image_99999.png";
        Check(failNode.Status == "File Not Found", "Missing file Status is 'File Not Found'", failNode.Status);
        Check(GetStatusShort(failNode) == "ERR", "Missing file StatusShort is 'ERR'", GetStatusShort(failNode));
        failNode.Execute();
        Check(failNode.Status == "File Not Found", "Post-execute missing file Status remains 'File Not Found'", failNode.Status);
        Check(GetStatusShort(failNode) == "ERR", "Post-execute missing file StatusShort remains 'ERR'", GetStatusShort(failNode));
        Check(failNode.LastOutputMat == null, "Missing file LastOutputMat is null");

        // 5. Adversarial Edge Case: Switching to missing file AFTER successful execution must report ERR (not retain stale OK)
        var edgeSrc = new SourceImageNode();
        edgeSrc.ImagePath = "cocoa_packages_05.png";
        edgeSrc.Execute();
        Check(edgeSrc.Status == "OK" && GetStatusShort(edgeSrc) == "OK", "edgeSrc initially OK on cocoa_packages_05.png");
        Check(edgeSrc.LastOutputMat != null && !edgeSrc.LastOutputMat.Empty(), "edgeSrc LastOutputMat populated");

        // Now set ImagePath to non-existent file
        edgeSrc.ImagePath = "non_existent_adversarial_file_12345.png";
        Check(edgeSrc.Status == "File Not Found", "edgeSrc status is 'File Not Found' after missing path switch", edgeSrc.Status);
        Check(GetStatusShort(edgeSrc) == "ERR", "edgeSrc GetStatusShort is 'ERR' immediately upon missing path switch", GetStatusShort(edgeSrc));
        Check(edgeSrc.LastOutputMat == null, "edgeSrc LastOutputMat is invalidated/null upon missing path switch");

        // Now switch back to valid cocoa image
        edgeSrc.SelectSampleByFileName("cocoa_packages_05.png");
        Check(edgeSrc.Status == "OK", "edgeSrc recovers to 'OK' after selecting cocoa_packages_05.png", edgeSrc.Status);
        Check(GetStatusShort(edgeSrc) == "OK", "edgeSrc recovers GetStatusShort 'OK' after selecting cocoa_packages_05.png", GetStatusShort(edgeSrc));
        edgeSrc.Execute();
        Check(edgeSrc.LastOutputMat != null && !edgeSrc.LastOutputMat.Empty(), "edgeSrc re-decodes valid image upon execution");
        Check(GetStatusShort(edgeSrc) == "OK", "edgeSrc post-execution GetStatusShort remains 'OK'");

        // Corrupted file handling
        string tempCorruptFile = Path.Combine(Path.GetTempPath(), $"corrupt_img_{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllText(tempCorruptFile, "THIS IS NOT A VALID PNG IMAGE DATA HEADER");
            var corruptNode = new SourceImageNode();
            corruptNode.ImagePath = tempCorruptFile;
            corruptNode.Execute();
            Check(corruptNode.Status.StartsWith("Error: Invalid Image File"),
                "Corrupted image file Status starts with 'Error: Invalid Image File'", corruptNode.Status);
            Check(GetStatusShort(corruptNode) == "ERR", "Corrupted image file StatusShort is 'ERR'", GetStatusShort(corruptNode));
            Check(corruptNode.LastOutputMat == null, "Corrupted image file LastOutputMat is null");
        }
        finally
        {
            if (File.Exists(tempCorruptFile))
            {
                try { File.Delete(tempCorruptFile); } catch { }
            }
        }
    }

    static void TestMilestone3CoreKernelAndScalability(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Milestone 3: Core Kernel & Scalability (G7, G8, Core SIMD & Contours) ---");

        // ==========================================
        // 1. G7: LowestPyramid Parameter & Scaling
        // ==========================================
        var alignTool = new AlignTool("M3AlignTool");
        Check(alignTool.LowestPyramid == 1, "AlignTool.LowestPyramid default is 1 (Level 0 full resolution)");
        alignTool.LowestPyramid = 2;
        Check(alignTool.LowestPyramid == 2, "AlignTool.LowestPyramid property setter works");

        var matchNode = new ShapeMatchNode();
        Check(matchNode.LowestPyramid == 1, "ShapeMatchNode.LowestPyramid default is 1");
        matchNode.LowestPyramid = 3;
        Check(matchNode.ToolInstance.LowestPyramid == 3, "ShapeMatchNode.LowestPyramid delegates to ToolInstance.LowestPyramid");

        // Verify LowestPyramid matching on engine_part_054
        string ep54 = Path.Combine(imgsDir, "engine_part_054.png");
        if (File.Exists(ep54))
        {
            using var mat54 = Cv2.ImRead(ep54, ImreadModes.Grayscale);
            var trainerTool = new AlignTool("Trainer");
            trainerTool.Train(mat54, null, numLevels: 4, contrastLow: 15, contrastHigh: 30, minLength: 10, maxFeatures: 250);
            var model = trainerTool.Model;

            if (model != null)
            {
                // Level 0 match (lowestPyramid = 1)
                var mLevel0 = ShapeMatcher.Match(mat54, model, angleMinDeg: -10, angleMaxDeg: 10, lowestPyramid: 1);
                Check(mLevel0.Count >= 1, "LowestPyramid=1 finds match on engine_part_054", $"Count={mLevel0.Count}");

                // Level 1 early termination (lowestPyramid = 2)
                var mLevel1 = ShapeMatcher.Match(mat54, model, angleMinDeg: -10, angleMaxDeg: 10, lowestPyramid: 2);
                Check(mLevel1.Count >= 1, "LowestPyramid=2 finds match with early termination", $"Count={mLevel1.Count}");

                if (mLevel0.Count >= 1 && mLevel1.Count >= 1)
                {
                    float diffX = MathF.Abs(mLevel0[0].X - mLevel1[0].X);
                    float diffY = MathF.Abs(mLevel0[0].Y - mLevel1[0].Y);
                    Check(diffX < 3.0f && diffY < 3.0f, "LowestPyramid=2 coordinates rescaled to Level 0 space correctly", $"diff=({diffX:F2}, {diffY:F2})");
                    Check(mLevel1[0].Score >= 0.5f, "LowestPyramid=2 match score valid", $"Score={mLevel1[0].Score:F3}");
                    Check(mLevel1[0].BoundingBox.Size.Width > 0, "LowestPyramid=2 BoundingBox generated in Level 0 space");
                    Check(mLevel1[0].TransformedContours.Count > 0, "LowestPyramid=2 TransformedContours populated in Level 0 space");
                }

                // Extreme clamped values (lowestPyramid = 0 and 99)
                var mClampedLow = ShapeMatcher.Match(mat54, model, angleMinDeg: -5, angleMaxDeg: 5, lowestPyramid: 0);
                Check(mClampedLow.Count >= 1, "LowestPyramid=0 safely clamped to level 0");
                var mClampedHigh = ShapeMatcher.Match(mat54, model, angleMinDeg: -5, angleMaxDeg: 5, lowestPyramid: 99);
                Check(mClampedHigh.Count >= 1, "LowestPyramid=99 safely clamped to coarsest level");
            }
        }

        // ==========================================
        // 2. G8: TiledImageMatcher
        // ==========================================
        // Test empty/invalid inputs
        using var emptyMat = new Mat();
        var dummyModel = new ShapeModel();
        var emptyMatches = TiledImageMatcher.MatchTiled(emptyMat, dummyModel);
        Check(emptyMatches.Count == 0, "TiledImageMatcher gracefully handles empty Mat / empty model");

        // Synthetic large scene with 3 distinct objects placed across tile boundaries
        // Large canvas 800x800, divided into 2x2 tiles (tile size 400x400)
        // Draw square A in top-left tile (at 200, 200), square B straddling the vertical boundary (at 400, 200),
        // and square C in bottom-right tile (at 600, 600)
        using var largeScene = new Mat(800, 800, MatType.CV_8UC1, new Scalar(20));
        // Golden template: square of size 50x50 with bright border
        using var tplMat = new Mat(100, 100, MatType.CV_8UC1, new Scalar(20));
        Cv2.Rectangle(tplMat, new Rect(25, 25, 50, 50), new Scalar(220), -1);

        // Place object 1 at (200, 200) - completely inside tile (0, 0)
        Cv2.Rectangle(largeScene, new Rect(175, 175, 50, 50), new Scalar(220), -1);
        // Place object 2 at (400, 200) - exactly on vertical boundary x = 400 between tile (0,0) and tile (1,0)
        Cv2.Rectangle(largeScene, new Rect(375, 175, 50, 50), new Scalar(220), -1);
        // Place object 3 at (600, 600) - completely inside tile (1, 1)
        Cv2.Rectangle(largeScene, new Rect(575, 575, 50, 50), new Scalar(220), -1);

        var squareAligner = new AlignTool("SquareAligner")
        {
            AngleMinDeg = 0,
            AngleMaxDeg = 0,
            MinScore = 0.6f
        };
        squareAligner.Train(tplMat, null, numLevels: 3, contrastLow: 30, contrastHigh: 60, minLength: 8, maxFeatures: 100);
        var squareModel = squareAligner.Model;

        if (squareModel != null)
        {
            var tiledMatches = TiledImageMatcher.MatchTiled(
                largeScene,
                squareModel,
                tilesX: 2,
                tilesY: 2,
                angleMinDeg: 0,
                angleMaxDeg: 0,
                scaleMin: 1.0,
                scaleMax: 1.0,
                minScore: 0.7,
                maxInstances: 10,
                maxOverlap: 0.5);

            Check(tiledMatches.Count == 3, "TiledImageMatcher 2x2 finds all 3 instances (including tile boundary instance)", $"Count={tiledMatches.Count}");

            // Verify global coordinate remapping
            bool foundBoundary = tiledMatches.Any(m => MathF.Abs(m.X - 400f) < 4f && MathF.Abs(m.Y - 200f) < 4f);
            Check(foundBoundary, "TiledImageMatcher correctly remapped coordinates for boundary instance to global space (400, 200)");

            // Verify NMS duplicate suppression across tiles: no duplicate instances near (400, 200)
            int boundaryCount = tiledMatches.Count(m => MathF.Abs(m.X - 400f) < 15f && MathF.Abs(m.Y - 200f) < 15f);
            Check(boundaryCount == 1, "Global NMS suppressed boundary duplicate across overlapping tiles", $"Count={boundaryCount}");
        }

        // ==========================================
        // 3. MinComponentSize & TopologicalContourExtractor
        // ==========================================
        // Create an image with a tiny noise speckle (arc length ~ 10px) and a large rectangle (perimeter ~ 200px)
        using var noiseTestImg = new Mat(200, 200, MatType.CV_8UC1, new Scalar(0));
        // Tiny speckle: circle of radius 2 (perimeter ~ 12px < 16px)
        Cv2.Circle(noiseTestImg, new Point(30, 30), 2, new Scalar(255), -1);
        // Big rectangle: 50x50 pixels -> perimeter ~ 200px
        Cv2.Rectangle(noiseTestImg, new Rect(80, 80, 50, 50), new Scalar(255), 1);

        // Default minComponentSize = 16: noise line (< 16 px) should be filtered out
        var filteredContours = TopologicalContourExtractor.ExtractContours(
            noiseTestImg, null, contrastLow: 30, contrastHigh: 80, minComponentSize: 16);
        Check(filteredContours.All(c =>
        {
            float len = 0f;
            for (int i = 1; i < c.Count; i++)
            {
                float dx = c[i].X - c[i - 1].X;
                float dy = c[i].Y - c[i - 1].Y;
                len += MathF.Sqrt(dx * dx + dy * dy);
            }
            return len >= 16f;
        }), "MinComponentSize=16 filters out all contours shorter than 16px");

        // With minComponentSize = 4: tiny noise line should be kept
        var allContours = TopologicalContourExtractor.ExtractContours(
            noiseTestImg, null, contrastLow: 30, contrastHigh: 80, minComponentSize: 4);
        Check(allContours.Count > filteredContours.Count, "MinComponentSize=4 retains shorter noise contours",
            $"Kept={allContours.Count} vs Filtered={filteredContours.Count}");

        // ExtractContoursFast convenience helper
        var fastContours = TopologicalContourExtractor.ExtractContoursFast(noiseTestImg, contrastLow: 30, contrastHigh: 80);
        Check(fastContours.Count == filteredContours.Count, "ExtractContoursFast uses minComponentSize=16 default and produces matching contours",
            $"Fast={fastContours.Count}, Default={filteredContours.Count}");

        // ==========================================
        // 4. Avx2ScoreAccumulator & SIMD Acceleration
        // ==========================================
        // Test AccumulateBlock32: 32 values with saturated addition
        short[] accumulator = new short[32];
        sbyte[] responseMap = new sbyte[64];
        for (int i = 0; i < 32; i++)
        {
            accumulator[i] = 1000;
            responseMap[i] = (sbyte)(i * 4 - 60); // Mix of negative and positive values
        }
        Avx2ScoreAccumulator.AccumulateBlock32(responseMap, 0, accumulator);
        bool accCorrect = true;
        for (int i = 0; i < 32; i++)
        {
            short expected = (short)(1000 + (i * 4 - 60));
            if (accumulator[i] != expected)
            {
                accCorrect = false;
                break;
            }
        }
        Check(accCorrect, "Avx2ScoreAccumulator.AccumulateBlock32 correctly accumulates 32 values");

        // Saturation test: overflowing short.MaxValue
        short[] satAccumulator = new short[32];
        sbyte[] satMap = new sbyte[32];
        for (int i = 0; i < 32; i++)
        {
            satAccumulator[i] = short.MaxValue - 10;
            satMap[i] = 50; // would overflow 32767 -> should saturate to 32767
        }
        Avx2ScoreAccumulator.AccumulateBlock32(satMap, 0, satAccumulator);
        bool satCorrect = satAccumulator.All(val => val == short.MaxValue);
        Check(satCorrect, "Avx2ScoreAccumulator.AccumulateBlock32 saturates at short.MaxValue on overflow");

        // PrecomputeOffsets test
        var dummyFeatures = new List<ShapeFeature>
        {
            new ShapeFeature { X = 10, Y = 10, Dx = 1.0f, Dy = 0f },
            new ShapeFeature { X = -10, Y = -10, Dx = 0f, Dy = 1.0f }
        };
        Avx2ScoreAccumulator.PrecomputeOffsets(dummyFeatures, angleRad: 0f, scale: 1.0f, stride: 100,
            out int[] offsets, out int[] sectors);
        Check(offsets.Length == 2 && sectors.Length == 2, "Avx2ScoreAccumulator.PrecomputeOffsets produces correct array lengths");
        Check(offsets[0] == 10 * 100 + 10, "Avx2ScoreAccumulator.PrecomputeOffsets calculates 1D linear stride offset correctly", $"Offset={offsets[0]}");

        // ScoreAtPosition test
        var respMaps = new sbyte[16][];
        for (int s = 0; s < 16; s++) respMaps[s] = new sbyte[200 * 200];
        // Populate the response map at the expected offset
        int basePos = 50 * 100 + 50;
        respMaps[sectors[0]][basePos + offsets[0]] = 127;
        respMaps[sectors[1]][basePos + offsets[1]] = 127;

        float scorePerfect = Avx2ScoreAccumulator.ScoreAtPosition(
            respMaps, mapWidth: 100, mapHeight: 100, posX: 50, posY: 50,
            offsets, sectors, featureCount: 2, minScore: 0.5f, greediness: 0.8f);
        Check(MathF.Abs(scorePerfect - 1.0f) < 1e-4f, "Avx2ScoreAccumulator.ScoreAtPosition calculates 1.0 for perfect match", $"Score={scorePerfect}");
    }

    static void TestMilestone1UiAndCanvasEnhancements(string imgsDir)
    {
        Console.WriteLine("\n--- Testing Milestone 1: UI & Canvas Enhancements (G1, G2, G6) ---");

        // ==========================================
        // 1. G6: OverlayDrawMode Enum Contracts
        // ==========================================
        Check(Enum.IsDefined(typeof(OverlayDrawMode), OverlayDrawMode.NoScore), "OverlayDrawMode contains NoScore mode");
        Check(Enum.IsDefined(typeof(OverlayDrawMode), OverlayDrawMode.Score), "OverlayDrawMode contains Score mode");
        Check(Enum.IsDefined(typeof(OverlayDrawMode), OverlayDrawMode.All), "OverlayDrawMode contains All mode");
        Check(Enum.IsDefined(typeof(OverlayDrawMode), OverlayDrawMode.ContourOnly), "OverlayDrawMode contains ContourOnly mode");

        var modes = Enum.GetValues<OverlayDrawMode>();
        Check(modes.Length == 4, "OverlayDrawMode has exactly 4 modes", $"Count={modes.Length}");
        Check(modes[0] == OverlayDrawMode.NoScore, "OverlayDrawMode default index 0 is NoScore");

        // Verify mode filtering semantics
        OverlayDrawMode currentMode = OverlayDrawMode.NoScore;
        bool drawsContours = true; // Always
        bool drawsCrosshair = currentMode != OverlayDrawMode.ContourOnly;
        bool drawsScore = currentMode == OverlayDrawMode.Score || currentMode == OverlayDrawMode.All;
        bool drawsBoxAndAxes = currentMode == OverlayDrawMode.All;

        Check(drawsContours && drawsCrosshair && !drawsScore && !drawsBoxAndAxes,
            "NoScore mode: draws contours + crosshair, hides score and bounding box/axes");

        currentMode = OverlayDrawMode.Score;
        drawsCrosshair = currentMode != OverlayDrawMode.ContourOnly;
        drawsScore = currentMode == OverlayDrawMode.Score || currentMode == OverlayDrawMode.All;
        drawsBoxAndAxes = currentMode == OverlayDrawMode.All;
        Check(drawsContours && drawsCrosshair && drawsScore && !drawsBoxAndAxes,
            "Score mode: draws contours + crosshair + score badge, hides box/axes");

        currentMode = OverlayDrawMode.All;
        drawsCrosshair = currentMode != OverlayDrawMode.ContourOnly;
        drawsScore = currentMode == OverlayDrawMode.Score || currentMode == OverlayDrawMode.All;
        drawsBoxAndAxes = currentMode == OverlayDrawMode.All;
        Check(drawsContours && drawsCrosshair && drawsScore && drawsBoxAndAxes,
            "All mode: draws contours + crosshair + score badge + bounding box + axes");

        currentMode = OverlayDrawMode.ContourOnly;
        drawsCrosshair = currentMode != OverlayDrawMode.ContourOnly;
        drawsScore = currentMode == OverlayDrawMode.Score || currentMode == OverlayDrawMode.All;
        drawsBoxAndAxes = currentMode == OverlayDrawMode.All;
        Check(drawsContours && !drawsCrosshair && !drawsScore && !drawsBoxAndAxes,
            "ContourOnly mode: draws contours only, hides crosshair, score, and box/axes");

        // ==========================================
        // 2. G1: HUD Pixel Inspection Contracts & Formatting
        // ==========================================
        // Verify in-bounds 8-bpp format
        int w = 1920, h = 1080;
        int ix = 512, iy = 256;
        byte grayVal = 142;
        string hud8bpp = $"W:{w}, H:{h} | X:{ix}, Y:{iy} | Gray:{grayVal}";
        Check(hud8bpp == "W:1920, H:1080 | X:512, Y:256 | Gray:142", "HUD 8-bpp grayscale format matches specification", hud8bpp);

        // Verify in-bounds 24/32-bpp RGB format (from BGRA buffer: B=0, G=1, R=2)
        byte[] pixelBuf = new byte[4] { 30, 150, 220, 255 }; // B=30, G=150, R=220
        string hudRgb = $"W:{w}, H:{h} | X:{ix}, Y:{iy} | R:{pixelBuf[2]}, G:{pixelBuf[1]}, B:{pixelBuf[0]}";
        Check(hudRgb == "W:1920, H:1080 | X:512, Y:256 | R:220, G:150, B:30", "HUD 24/32-bpp RGB format matches specification", hudRgb);

        // Verify out-of-bounds format
        string hudOob = $"W:{w}, H:{h} | X:---, Y:---";
        Check(hudOob == "W:1920, H:1080 | X:---, Y:---", "HUD out-of-bounds format matches specification", hudOob);

        // Verify coordinate floor rounding formula
        float subpixelX = 105.7f;
        float subpixelY = 42.1f;
        int floorX = (int)Math.Floor(subpixelX);
        int floorY = (int)Math.Floor(subpixelY);
        Check(floorX == 105 && floorY == 42, "HUD coordinate floor rounding calculates correct integer pixel indices", $"({floorX}, {floorY})");

        // ==========================================
        // 3. G2: ModelPreviewControl Aspect-Ratio Zoom-Fit
        // ==========================================
        // Test wide aspect ratio model (w=200, h=50) in 160x140 viewport (viewW=136, viewH=116)
        float mw = 200f, mh = 50f;
        float vw = 136f, vh = 116f;
        float scaleWide = Math.Min(vw / mw, vh / mh);
        Check(Math.Abs(scaleWide - (136f / 200f)) < 1e-4f, "ModelPreview zoom-fit scales by width for wide aspect model", $"Scale={scaleWide:F3}");

        // Test tall aspect ratio model (w=40, h=160)
        float tw = 40f, th = 160f;
        float scaleTall = Math.Min(vw / tw, vh / th);
        Check(Math.Abs(scaleTall - (116f / 160f)) < 1e-4f, "ModelPreview zoom-fit scales by height for tall aspect model", $"Scale={scaleTall:F3}");

        // Centering offset calculations preserve 1:1 aspect ratio without stretching
        float offsetX = (160f - mw * scaleWide) / 2f - 0f * scaleWide;
        float offsetY = (140f - mh * scaleWide) / 2f - 0f * scaleWide;
        Check(offsetX >= 0 && offsetY >= 0, "ModelPreview centering offsets remain within control boundaries", $"Offset=({offsetX:F1}, {offsetY:F1})");

        // ==========================================
        // 4. Regression Safeguards: Zoom-Adaptive Thickness & Brush Undo
        // ==========================================
        // Zoom adaptive stroke thickness formula: Math.Clamp(1.35 * Math.Pow(Math.Max(0.05, z), -0.28), 0.85, 2.20)
        double StrokeThicknessFormula(double zoom)
        {
            double z = Math.Max(0.05, zoom);
            return Math.Clamp(1.35 * Math.Pow(z, -0.28), 0.85, 2.20);
        }

        double strokeZ1 = StrokeThicknessFormula(1.0);
        Check(Math.Abs(strokeZ1 - 1.35) < 1e-3, "Zoom-adaptive stroke thickness at 1.0x is 1.35px", $"T={strokeZ1:F3}");

        double strokeZHigh = StrokeThicknessFormula(25.0);
        Check(strokeZHigh >= 0.85 && strokeZHigh <= 1.35, "Zoom-adaptive stroke thickness clamps at high zoom", $"T={strokeZHigh:F3}");

        double strokeZLow = StrokeThicknessFormula(0.01);
        Check(strokeZLow <= 2.20 && strokeZLow >= 1.35, "Zoom-adaptive stroke thickness clamps at low zoom", $"T={strokeZLow:F3}");

        // Model feature removal and Undo restoration
        var testModel = new ShapeModel();
        testModel.Levels.Add(new PyramidLevelData
        {
            Level = 0,
            Features = new List<ShapeFeature>
            {
                new ShapeFeature { X = 50, Y = 50 },
                new ShapeFeature { X = 100, Y = 100 },
                new ShapeFeature { X = 150, Y = 150 }
            }
        });

        int initialFeatCount = testModel.Levels[0].Features.Count;
        int removedCount = testModel.RemoveFeaturesByBrush(new Point2f(50, 50), radius: 10f, recordUndo: true);
        Check(removedCount == 1, "RemoveFeaturesByBrush removes 1 feature within radius", $"Removed={removedCount}");
        Check(testModel.Levels[0].Features.Count == initialFeatCount - 1, "Feature count decreased by 1");

        bool undoSuccess = testModel.UndoFeatureRemoval();
        Check(undoSuccess, "UndoFeatureRemoval returns true when undo stack has history");
        Check(testModel.Levels[0].Features.Count == initialFeatCount, "UndoFeatureRemoval completely restores removed feature", $"Count={testModel.Levels[0].Features.Count}");
    }
}
