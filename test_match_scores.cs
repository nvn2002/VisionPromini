using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using TunVision.Core.ShapeMatching;

class Program
{
    static void Main()
    {
        string baseDir = @"d:\tunvision\mwwz-samples";
        string modelPath = Path.Combine(baseDir, @"TunVision\Models\retainers_model.shm");

        ShapeModel model;
        if (File.Exists(modelPath))
        {
            model = ShapeModel.LoadShm(modelPath);
            Console.WriteLine($"Loaded model from {modelPath}, numLevels={model.NumLevels}, L0 features={model.Levels[0].Features.Count}");
        }
        else
        {
            using var tpl = Cv2.ImRead(Path.Combine(baseDir, @"imgs\retainers_01.png"), ImreadModes.Grayscale);
            model = ShapeTrainer.Train(tpl, null, numLevels: 4, contrastLow: 35, contrastHigh: 75, minLength: 60);
            Console.WriteLine($"Trained model on retainers_01, L0 features={model.Levels[0].Features.Count}");
        }

        string[] testImages = new[]
        {
            Path.Combine(baseDir, @"imgs\retainers_01.png"),
            Path.Combine(baseDir, @"imgs\retainers_02.png"),
            Path.Combine(baseDir, @"imgs\retainers_03.png"),
        };

        foreach (var imgPath in testImages)
        {
            if (!File.Exists(imgPath)) continue;
            using var scene = Cv2.ImRead(imgPath, ImreadModes.Grayscale);
            var matches = ShapeMatcher.Match(
                scene, model,
                angleMinDeg: -180f, angleMaxDeg: 180f, angleStepDeg: 1.0f,
                scaleMin: 0.9f, scaleMax: 1.1f, scaleStep: 0.05f,
                minScore: 0.35f, greediness: 0.8f, maxInstances: 5);

            Console.WriteLine($"--- Image: {Path.GetFileName(imgPath)} --- Matches: {matches.Count}");
            foreach (var m in matches)
            {
                Console.WriteLine($"   Match: X={m.X:F1}, Y={m.Y:F1}, Angle={m.AngleDeg:F1}°, Scale={m.Scale:F2}, Score={m.Score:F3}, Coverage={m.Coverage:P1}");
            }
        }
    }
}
