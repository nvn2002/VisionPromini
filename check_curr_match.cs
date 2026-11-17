using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using TunVision.Core.Graph;
using TunVision.Core.ShapeMatching;

class Program
{
    static void Main()
    {
        var model = ShapeModel.LoadShm(@"d:\tunvision\mwwz-samples\TunVision\Models\retainers_model.shm");
        Console.WriteLine($"Model: Template={model.TemplateWidth}x{model.TemplateHeight}, Origin=({model.OriginX},{model.OriginY}), L0 features={model.Levels[0].Features.Count}");

        string[] imgs = new[] { "retainers_01.png", "retainers_02.png", "retainers_03.png" };
        foreach (var imgName in imgs)
        {
            using var mat = Cv2.ImRead(@"d:\tunvision\mwwz-samples\imgs\" + imgName, ImreadModes.Color);
            var node = new ShapeMatchNode
            {
                Model = model,
                MinScore = 0.3f
            };
            var inputs = new System.Collections.Generic.Dictionary<string, object?> { ["image"] = mat };
            var outputs = node.ExecuteAsync(inputs, new GraphExecutionContext()).Result;
            Console.WriteLine($"{imgName}: LastResult = {node.LastResult}");
        }
    }
}
