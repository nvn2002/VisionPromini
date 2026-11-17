using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace TunVision.Core.ShapeMatching.Core;

public static class ModelSerializer
{
    public const int HeaderSize = 72;

    public static ShapeModel LoadShm(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        long fileSize = fs.Length;
        if (fileSize < HeaderSize)
            throw new InvalidDataException($"File too small to be a valid .shm model ({fileSize} bytes)");

        // Probe header
        int numLevels = br.ReadInt32();
        float angleStart = br.ReadSingle();
        float angleExtent = br.ReadSingle();
        float angleStep = br.ReadSingle();

        // Check if format is MWWZ standard vs TunVision legacy:
        // MWWZ standard has offset 89 as the start of 8 metadata vectors, where each vector has:
        // int32 count (= numLevels), int32 pad (= 0).
        // Let's verify by checking byte 89:
        bool isMwwzStandard = false;
        if (fileSize >= 89 + 8 && numLevels >= 1 && numLevels <= 10)
        {
            fs.Position = 89;
            int testCount = br.ReadInt32();
            int testPad = br.ReadInt32();
            if (testCount == numLevels && testPad == 0)
            {
                isMwwzStandard = true;
            }
        }

        fs.Position = 0; // Rewind
        if (isMwwzStandard)
        {
            return ReadMwwzStandard(br, fileSize);
        }
        else
        {
            return ReadLegacyTunVision(br, fileSize);
        }
    }

    private static ShapeModel ReadMwwzStandard(BinaryReader br, long fileSize)
    {
        var model = new ShapeModel();
        model.NumLevels = br.ReadInt32();
        model.AngleStartDeg = br.ReadSingle();
        model.AngleExtentDeg = br.ReadSingle();
        model.AngleStepDeg = br.ReadSingle();
        model.ScaleMin = br.ReadSingle();
        model.ScaleMax = br.ReadSingle();
        model.ScaleStep = br.ReadSingle();
        float scaleRMin = br.ReadSingle();
        float scaleRMax = br.ReadSingle();
        float scaleRStep = br.ReadSingle();
        float scaleCMin = br.ReadSingle();
        float scaleCMax = br.ReadSingle();
        float scaleCStep = br.ReadSingle();
        model.Metric = br.ReadInt32();
        model.OriginX = br.ReadSingle();
        model.OriginY = br.ReadSingle();
        model.TemplateWidth = br.ReadInt32();
        model.TemplateHeight = br.ReadInt32();

        // Byte 72: Polarity (1 byte)
        byte usePolarity = br.ReadByte();

        // Byte 73-88: Contrast (16 bytes)
        model.ContrastLow = br.ReadInt32();
        model.ContrastHigh = br.ReadInt32();
        model.MinComponentSize = br.ReadInt32();
        model.MinContrast = br.ReadInt32();

        // Byte 89: 8 Pyramid Geometry Metadata Vectors
        // Vector 0: widths (int32[])
        // Vector 1: heights (int32[])
        // Vector 2: min_x (float32[])
        // Vector 3: min_y (float32[])
        // Vector 4: max_x (float32[])
        // Vector 5: max_y (float32[])
        // Vector 6: diameter (float32[])
        // Vector 7: center_offset (float32[])
        int[] widths = new int[model.NumLevels];
        int[] heights = new int[model.NumLevels];
        float[] minX = new float[model.NumLevels];
        float[] minY = new float[model.NumLevels];
        float[] maxX = new float[model.NumLevels];
        float[] maxY = new float[model.NumLevels];
        float[] diameter = new float[model.NumLevels];
        float[] centerOffset = new float[model.NumLevels];

        for (int v = 0; v < 8; v++)
        {
            int count = br.ReadInt32();
            int pad = br.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                if (v == 0) widths[i] = br.ReadInt32();
                else if (v == 1) heights[i] = br.ReadInt32();
                else if (v == 2) minX[i] = br.ReadSingle();
                else if (v == 3) minY[i] = br.ReadSingle();
                else if (v == 4) maxX[i] = br.ReadSingle();
                else if (v == 5) maxY[i] = br.ReadSingle();
                else if (v == 6) diameter[i] = br.ReadSingle();
                else if (v == 7) centerOffset[i] = br.ReadSingle();
            }
        }

        // Features Container
        int levelCount = br.ReadInt32();
        int containerPad = br.ReadInt32();

        for (int l = 0; l < levelCount; l++)
        {
            int featCount = br.ReadInt32();
            int featPad = br.ReadInt32();

            var lvl = new PyramidLevelData
            {
                Level = l,
                Width = l < widths.Length && widths[l] > 0 ? widths[l] : model.TemplateWidth / (1 << l),
                Height = l < heights.Length && heights[l] > 0 ? heights[l] : model.TemplateHeight / (1 << l)
            };

            for (int f = 0; f < featCount; f++)
            {
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                float nx = br.ReadSingle();
                float ny = br.ReadSingle();

                float angle = MathF.Atan2(ny, nx);
                lvl.Features.Add(new ShapeFeature(x, y, nx, ny, angle, 1.0f));
            }
            model.Levels.Add(lvl);
        }

        // Build synthetic / inferred ModelContours for UI rendering if not explicitly present
        if (model.Levels.Count > 0 && model.Levels[0].Features.Count > 0)
        {
            var lvl0 = model.Levels[0].Features;
            var points = new List<Point2f>(lvl0.Count);
            foreach (var f in lvl0)
            {
                points.Add(new Point2f(f.X, f.Y));
            }
            model.ModelContours.Add(points);
        }

        model.CaptureInitialSnapshot();
        return model;
    }

    private static ShapeModel ReadLegacyTunVision(BinaryReader br, long fileSize)
    {
        var model = new ShapeModel();
        model.NumLevels = br.ReadInt32();
        model.AngleStartDeg = br.ReadSingle() * 180f / MathF.PI;
        model.AngleExtentDeg = br.ReadSingle() * 180f / MathF.PI;
        model.AngleStepDeg = br.ReadSingle() * 180f / MathF.PI;
        model.ScaleMin = br.ReadSingle();
        model.ScaleMax = br.ReadSingle();
        model.ScaleStep = br.ReadSingle();
        br.ReadSingle(); // scale_c_min
        br.ReadSingle(); // scale_c_max
        br.ReadSingle(); // scale_c_step
        model.Metric = br.ReadInt32();
        model.ContrastLow = (int)br.ReadSingle();
        model.ContrastHigh = (int)br.ReadSingle();
        model.MinLength = (int)br.ReadSingle();
        model.OriginX = br.ReadSingle();
        model.OriginY = br.ReadSingle();
        model.TemplateWidth = br.ReadInt32();
        model.TemplateHeight = br.ReadInt32();

        int levelCount = br.ReadInt32();
        for (int l = 0; l < levelCount; l++)
        {
            var lvl = new PyramidLevelData();
            lvl.Level = br.ReadInt32();
            lvl.Width = br.ReadInt32();
            lvl.Height = br.ReadInt32();
            int featCount = br.ReadInt32();
            for (int f = 0; f < featCount; f++)
            {
                lvl.Features.Add(new ShapeFeature(
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle(),
                    br.ReadSingle()));
            }
            model.Levels.Add(lvl);
        }

        if (br.BaseStream.Position < fileSize)
        {
            int polyCount = br.ReadInt32();
            for (int p = 0; p < polyCount; p++)
            {
                int ptCount = br.ReadInt32();
                var poly = new List<Point2f>(ptCount);
                for (int i = 0; i < ptCount; i++)
                {
                    poly.Add(new Point2f(br.ReadSingle(), br.ReadSingle()));
                }
                model.ModelContours.Add(poly);
            }
        }

        model.CaptureInitialSnapshot();
        return model;
    }

    public static void SaveShm(ShapeModel model, string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        int numLevels = model.NumLevels > 0 ? model.NumLevels : (model.Levels.Count > 0 ? model.Levels.Count : 1);

        // Header (72 bytes)
        bw.Write(numLevels);
        bw.Write(model.AngleStartDeg);
        bw.Write(model.AngleExtentDeg);
        bw.Write(model.AngleStepDeg);
        bw.Write(model.ScaleMin);
        bw.Write(model.ScaleMax);
        bw.Write(model.ScaleStep);
        bw.Write(model.ScaleMin);  // scale_r_min
        bw.Write(model.ScaleMax);  // scale_r_max
        bw.Write(model.ScaleStep); // scale_r_step
        bw.Write(model.ScaleMin);  // scale_c_min
        bw.Write(model.ScaleMax);  // scale_c_max
        bw.Write(model.ScaleStep); // scale_c_step
        bw.Write(model.Metric);
        bw.Write(model.OriginX);
        bw.Write(model.OriginY);
        bw.Write(model.TemplateWidth);
        bw.Write(model.TemplateHeight);

        // Polarity (1 byte at offset 0x48)
        byte polByte = (byte)(model.Metric == 0 ? 0 : 1);
        bw.Write(polByte);

        // Contrast (16 bytes at offset 0x49)
        bw.Write(model.ContrastLow);
        bw.Write(model.ContrastHigh);
        bw.Write(model.MinComponentSize);
        bw.Write(model.MinContrast);

        // Compute 8 Pyramid Geometry Metadata Vectors at offset 0x59
        int[] widths = new int[numLevels];
        int[] heights = new int[numLevels];
        float[] minX = new float[numLevels];
        float[] minY = new float[numLevels];
        float[] maxX = new float[numLevels];
        float[] maxY = new float[numLevels];
        float[] diameter = new float[numLevels];
        float[] centerOffset = new float[numLevels];

        for (int l = 0; l < numLevels; l++)
        {
            var lvl = l < model.Levels.Count ? model.Levels[l] : null;
            widths[l] = lvl?.Width ?? Math.Max(1, model.TemplateWidth >> l);
            heights[l] = lvl?.Height ?? Math.Max(1, model.TemplateHeight >> l);

            if (lvl != null && lvl.Features.Count > 0)
            {
                float x0 = float.MaxValue, y0 = float.MaxValue;
                float x1 = float.MinValue, y1 = float.MinValue;
                float maxR2 = 0f;

                foreach (var f in lvl.Features)
                {
                    if (f.X < x0) x0 = f.X;
                    if (f.X > x1) x1 = f.X;
                    if (f.Y < y0) y0 = f.Y;
                    if (f.Y > y1) y1 = f.Y;
                    float r2 = f.X * f.X + f.Y * f.Y;
                    if (r2 > maxR2) maxR2 = r2;
                }

                minX[l] = x0;
                minY[l] = y0;
                maxX[l] = x1;
                maxY[l] = y1;
                diameter[l] = MathF.Sqrt(maxR2);
                centerOffset[l] = 0f;
            }
            else
            {
                minX[l] = -widths[l] * 0.5f;
                minY[l] = -heights[l] * 0.5f;
                maxX[l] = widths[l] * 0.5f;
                maxY[l] = heights[l] * 0.5f;
                diameter[l] = MathF.Sqrt(minX[l] * minX[l] + minY[l] * minY[l]);
                centerOffset[l] = 0f;
            }
        }

        // Write the 8 metadata vectors
        for (int v = 0; v < 8; v++)
        {
            bw.Write(numLevels); // count
            bw.Write(0);          // padding
            for (int i = 0; i < numLevels; i++)
            {
                if (v == 0) bw.Write(widths[i]);
                else if (v == 1) bw.Write(heights[i]);
                else if (v == 2) bw.Write(minX[i]);
                else if (v == 3) bw.Write(minY[i]);
                else if (v == 4) bw.Write(maxX[i]);
                else if (v == 5) bw.Write(maxY[i]);
                else if (v == 6) bw.Write(diameter[i]);
                else if (v == 7) bw.Write(centerOffset[i]);
            }
        }

        // Features Container
        bw.Write(numLevels); // level_count
        bw.Write(0);         // padding

        for (int l = 0; l < numLevels; l++)
        {
            var lvl = l < model.Levels.Count ? model.Levels[l] : null;
            int featCount = lvl?.Features.Count ?? 0;

            bw.Write(featCount); // feat_count
            bw.Write(0);         // padding

            if (lvl != null)
            {
                foreach (var f in lvl.Features)
                {
                    bw.Write(f.X);
                    bw.Write(f.Y);
                    bw.Write(f.Dx); // unit normal Nx
                    bw.Write(f.Dy); // unit normal Ny
                }
            }
        }
    }
}
