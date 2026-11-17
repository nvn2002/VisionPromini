using System;
using System.IO;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.ShapeMatching;

class Program
{
    static void Main()
    {
        string baseDir = @"d:\tunvision\mwwz-samples";
        using var ret1 = Cv2.ImRead(Path.Combine(baseDir, @"imgs\retainers_01.png"), ImreadModes.Grayscale);

        // Train on ONE retainer using an ROI around the center large retainer
        // Contour 8 was bbox=(217, 187, 108, 108)
        var roi = new RectRoi(210, 180, 125, 125);
        using var mask = roi.CreateMask(ret1.Width, ret1.Height);

        var model = ShapeTrainer.Train(
            ret1, mask,
            numLevels: 4,
            contrastLow: 35, contrastHigh: 75,
            minLength: 40,
            contourMode: ContourExtractionMode.Silhouette);

        Console.WriteLine($"Model Origin: ({model.OriginX:F1}, {model.OriginY:F1})");
        Console.WriteLine($"Num levels: {model.NumLevels}, L0 features: {model.Levels[0].Features.Count}");
        Console.WriteLine($"Model contours: {model.ModelContours.Count}");

        string[] testImgs = new[] { "retainers_01.png", "retainers_02.png", "retainers_03.png" };
        foreach (var imgName in testImgs)
        {
            using var scene = Cv2.ImRead(Path.Combine(baseDir, "imgs", imgName), ImreadModes.Grayscale);
            var matches = ShapeMatcher.Match(
                scene, model,
                angleMinDeg: -180f, angleMaxDeg: 180f, angleStepDeg: 1.0f,
                scaleMin: 0.95f, scaleMax: 1.05f, scaleStep: 0.05f,
                minScore: 0.5f, greediness: 0.75f, maxInstances: 15);

            Console.WriteLine($"\n=== {imgName} === Found {matches.Count} matches:");
            foreach (var m in matches)
            {
                Console.WriteLine($"   Match: Pos=({m.X:F1}, {m.Y:F1}), Angle={m.AngleDeg:F1}°, Score={m.Score:F3}, Coverage={m.Coverage:P1}");
            }
        }
    }
}
