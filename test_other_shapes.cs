using System;
using System.IO;
using OpenCvSharp;
using TunVision.Core.ShapeMatching;

class Program
{
    static void Main()
    {
        string baseDir = @"d:\tunvision\mwwz-samples\imgs";

        // 1. Gasket test
        string gMdlPath = Path.Combine(baseDir, "gasket_model.png");
        string gScenePath = Path.Combine(baseDir, "gasket_04.png");
        if (File.Exists(gMdlPath) && File.Exists(gScenePath))
        {
            using var gMdl = Cv2.ImRead(gMdlPath, ImreadModes.Grayscale);
            using var gScene = Cv2.ImRead(gScenePath, ImreadModes.Grayscale);

            var model = ShapeTrainer.Train(gMdl, null, numLevels: 4, contrastLow: 25, contrastHigh: 50, minLength: 30);
            var matches = ShapeMatcher.Match(gScene, model, angleMinDeg: -180, angleMaxDeg: 180, angleStepDeg: 1.0f, minScore: 0.5f);
            Console.WriteLine($"Gasket Matches on gasket_04: {matches.Count}");
            foreach (var m in matches)
                Console.WriteLine($"   Gasket Match: Pos=({m.X:F1},{m.Y:F1}), Angle={m.AngleDeg:F1}°, Score={m.Score:F3}, Cov={m.Coverage:P1}");
        }

        // 2. Cocoa Package test
        string cMdlPath = Path.Combine(baseDir, "cocoa_package_model.png");
        string cScenePath = Path.Combine(baseDir, "cocoa_packages_03.png");
        if (File.Exists(cMdlPath) && File.Exists(cScenePath))
        {
            using var cMdl = Cv2.ImRead(cMdlPath, ImreadModes.Grayscale);
            using var cScene = Cv2.ImRead(cScenePath, ImreadModes.Grayscale);

            var model = ShapeTrainer.Train(cMdl, null, numLevels: 4, contrastLow: 30, contrastHigh: 60, minLength: 30);
            var matches = ShapeMatcher.Match(cScene, model, angleMinDeg: -45, angleMaxDeg: 45, angleStepDeg: 1.0f, minScore: 0.5f);
            Console.WriteLine($"Cocoa Matches on cocoa_03: {matches.Count}");
            foreach (var m in matches)
                Console.WriteLine($"   Cocoa Match: Pos=({m.X:F1},{m.Y:F1}), Angle={m.AngleDeg:F1}°, Score={m.Score:F3}, Cov={m.Coverage:P1}");
        }
    }
}
