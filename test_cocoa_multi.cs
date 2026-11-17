using System;
using System.IO;
using OpenCvSharp;
using TunVision.Core.ShapeMatching;

class Program
{
    static void Main()
    {
        string baseDir = @"d:\tunvision\mwwz-samples\imgs";
        string mdlPath = Path.Combine(baseDir, "cocoa_package_model.png");
        string scenePath = Path.Combine(baseDir, "cocoa_packages_05.png");

        using var mdl = Cv2.ImRead(mdlPath, ImreadModes.Grayscale);
        using var scene = Cv2.ImRead(scenePath, ImreadModes.Grayscale);

        Console.WriteLine($"mdl: {mdl.Width}x{mdl.Height}, scene: {scene.Width}x{scene.Height}");

        // Train model like TrainShapeModelNode does:
        var model = ShapeTrainer.Train(
            mdl, null,
            numLevels: 4,
            contrastLow: 35, contrastHigh: 75,
            minLength: 60,
            contourMode: ContourExtractionMode.Auto);

        Console.WriteLine($"Model: Origin=({model.OriginX:F1},{model.OriginY:F1}), Levels={model.NumLevels}, L0 feats={model.Levels[0].Features.Count}");

        // Match with Angle [-180, 180], minScore 0.4, maxInstances 10:
        var matches = ShapeMatcher.Match(
            scene, model,
            angleMinDeg: -180, angleMaxDeg: 180, angleStepDeg: 1.0f,
            scaleMin: 0.9f, scaleMax: 1.1f, scaleStep: 0.05f,
            minScore: 0.4f, greediness: 0.8f, maxInstances: 10);

        Console.WriteLine($"Matches found: {matches.Count}");
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            Console.WriteLine($"#{i+1}: Pos=({m.X:F1}, {m.Y:F1}), Angle={m.AngleDeg:F1}°, Scale={m.Scale:F2}, Score={m.Score:F3}, Cov={m.Coverage:P1}");
        }
    }
}
