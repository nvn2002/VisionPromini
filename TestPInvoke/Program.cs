using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using MwwzWpfTool.Core.Interop;
using MwwzWpfTool.Core.ShapeMatching;

namespace TestPInvoke;

class Program
{
    static void Main()
    {
        string ep99 = @"d:\tunvision\mwwz-samples\imgs\engine_part_099.png";
        string ep54 = @"d:\tunvision\mwwz-samples\imgs\engine_part_054.png";

        using var mat99 = Cv2.ImRead(ep99, ImreadModes.Grayscale);
        using var mat54 = Cv2.ImRead(ep54, ImreadModes.Grayscale);

        Console.WriteLine($"mat99: {mat99.Width}x{mat99.Height}, mat54: {mat54.Width}x{mat54.Height}");

        // 1. Train C# ShapeModel automatically on full mat99
        var sm = ShapeTrainer.Train(mat99, null, null, 3, -30, 60, 1.0, 1.0, 15, 30, 200);
        Console.WriteLine($"Full template polylines: {sm.ContourPolylines.Count}");

        // Filter out short polylines < 20 px
        sm.ContourPolylines.RemoveAll(poly =>
        {
            double len = 0;
            for (int i = 0; i < poly.Count - 1; i++)
            {
                double dx = poly[i+1].X - poly[i].X;
                double dy = poly[i+1].Y - poly[i].Y;
                len += Math.Sqrt(dx * dx + dy * dy);
            }
            return len < 20.0;
        });
        Console.WriteLine($"After filtering noise < 20px: {sm.ContourPolylines.Count} polylines");

        // 2. Build editMask for native MWWZ
        using var editMask = new Mat(mat99.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var poly in sm.ContourPolylines)
        {
            var pts = poly.Select(p => new OpenCvSharp.Point((int)Math.Round(p.X + mat99.Width / 2f),
                                                            (int)Math.Round(p.Y + mat99.Height / 2f))).ToArray();
            if (pts.Length >= 2)
            {
                Cv2.Polylines(editMask, new[] { pts }, false, Scalar.All(255), thickness: 8);
            }
        }

        using var nativeModel = MwwzUpgradedModel.Train(mat99, editMask, 3, -30, 60, 1.0f, 15, 30);
        if (nativeModel != null)
        {
            Console.WriteLine($"Native model with editMask trained, ID={nativeModel.ModelId}");
            var nativeContour = nativeModel.GetModelContour();
            Console.WriteLine($"Native contour features with mask: {nativeContour.Count}");

            var matches = nativeModel.Find(mat54, -30, 60, 0.40f, 5, 0.5f, 2, 0.85f);
            Console.WriteLine($"FindStandard returned {matches.Count} matches");
            foreach (var m in matches)
            {
                Console.WriteLine($"   Match at ({m.X:F1}, {m.Y:F1}), score={m.Score:F3}, angle={m.AngleDeg:F1}");
            }

            var defoMatches = nativeModel.FindPlanarDeformable(mat54, -30, 60, 0.40f, 5, 0.5f);
            Console.WriteLine($"FindPlanarDeformable returned {defoMatches.Count} matches");
            foreach (var m in defoMatches)
            {
                Console.WriteLine($"   DefoMatch at ({m.X:F1}, {m.Y:F1}), score={m.Score:F3}, angle={m.AngleDeg:F1}");
            }
        }
    }
}
