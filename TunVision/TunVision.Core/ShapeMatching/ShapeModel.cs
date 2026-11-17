using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;

namespace TunVision.Core.ShapeMatching;

public class PyramidLevelData
{
    public int Level { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<ShapeFeature> Features { get; set; } = new();

    public PyramidLevelData Clone() => new()
    {
        Level = Level,
        Width = Width,
        Height = Height,
        Features = new List<ShapeFeature>(Features)
    };
}

internal sealed class ShapeModelSnapshot
{
    public float OriginX { get; init; }
    public float OriginY { get; init; }
    public List<PyramidLevelData> Levels { get; init; } = new();
    public List<List<Point2f>> ModelContours { get; init; } = new();
    public List<ShapeFeature> MaskedFeatures { get; init; } = new();
}

public class ShapeModel
{
    public int NumLevels { get; set; } = 4;
    public float AngleStartDeg { get; set; } = -180f;
    public float AngleExtentDeg { get; set; } = 360f;
    public float AngleStepDeg { get; set; } = 1.0f;
    public float ScaleMin { get; set; } = 0.9f;
    public float ScaleMax { get; set; } = 1.1f;
    public float ScaleStep { get; set; } = 0.05f;
    public int ContrastLow { get; set; } = 15;
    public int ContrastHigh { get; set; } = 30;
    public int MinContrast { get; set; } = 9;
    public int MinComponentSize { get; set; } = 16;
    public int MinLength
    {
        get => MinComponentSize;
        set => MinComponentSize = value;
    }
    public int Metric { get; set; } = 0; // 0: use_polarity, 1: ignore_global_polarity, 2: ignore_local_polarity

    public float OriginX { get; set; }
    public float OriginY { get; set; }
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }

    public List<PyramidLevelData> Levels { get; set; } = new();
    public List<List<Point2f>> ModelContours { get; set; } = new();
    public List<ShapeFeature> MaskedFeatures { get; set; } = new();

    [JsonIgnore]
    private readonly Stack<ShapeModelSnapshot> _undoStack = new();

    [JsonIgnore]
    private ShapeModelSnapshot? _initialSnapshot;

    [JsonIgnore]
    public int UndoCount => _undoStack.Count;

    public void CaptureInitialSnapshot()
    {
        _initialSnapshot = CreateSnapshot();
        _undoStack.Clear();
    }

    private ShapeModelSnapshot CreateSnapshot() => new()
    {
        OriginX = OriginX,
        OriginY = OriginY,
        Levels = Levels.Select(l => l.Clone()).ToList(),
        ModelContours = ModelContours.Select(poly => new List<Point2f>(poly)).ToList(),
        MaskedFeatures = new List<ShapeFeature>(MaskedFeatures)
    };

    private void RestoreSnapshot(ShapeModelSnapshot snap)
    {
        OriginX = snap.OriginX;
        OriginY = snap.OriginY;
        Levels = snap.Levels.Select(l => l.Clone()).ToList();
        ModelContours = snap.ModelContours.Select(poly => new List<Point2f>(poly)).ToList();
        MaskedFeatures = new List<ShapeFeature>(snap.MaskedFeatures);
    }

    /// <summary>
    /// Equivalent to mwwz::remove_model_feature (RVA 0x7AFCD):
    /// Erases model features and contour vertices within a circular brush at image coordinates.
    /// </summary>
    public int RemoveFeaturesByBrush(Point2f imageCenter, float radius, bool recordUndo = true)
    {
        if (radius <= 0.5f) return 0;
        if (_initialSnapshot == null) CaptureInitialSnapshot();

        float relCx = imageCenter.X - OriginX;
        float relCy = imageCenter.Y - OriginY;
        float r2Base = radius * radius;

        // Check if anything is within brush radius before pushing undo
        bool anyHit = false;
        foreach (var lvl in Levels)
        {
            float scale = 1.0f / (1 << lvl.Level);
            float cx = relCx * scale;
            float cy = relCy * scale;
            float r = Math.Max(1.0f, radius * scale);
            float r2 = r * r;
            if (lvl.Features.Any(f => (f.X - cx) * (f.X - cx) + (f.Y - cy) * (f.Y - cy) <= r2))
            {
                anyHit = true;
                break;
            }
        }

        if (!anyHit)
        {
            anyHit = ModelContours.Any(poly => poly.Any(p => (p.X - relCx) * (p.X - relCx) + (p.Y - relCy) * (p.Y - relCy) <= r2Base));
        }

        if (!anyHit) return 0;

        if (recordUndo)
        {
            _undoStack.Push(CreateSnapshot());
        }

        int removedLevel0 = 0;
        foreach (var lvl in Levels)
        {
            float scale = 1.0f / (1 << lvl.Level);
            float cx = relCx * scale;
            float cy = relCy * scale;
            float r = Math.Max(1.0f, radius * scale);
            float r2 = r * r;
            if (lvl.Level == 0)
            {
                var removedF = lvl.Features.Where(f => (f.X - cx) * (f.X - cx) + (f.Y - cy) * (f.Y - cy) <= r2).ToList();
                MaskedFeatures.AddRange(removedF);
            }
            int removed = lvl.Features.RemoveAll(f => (f.X - cx) * (f.X - cx) + (f.Y - cy) * (f.Y - cy) <= r2);
            if (lvl.Level == 0) removedLevel0 = removed;
        }

        // Split / filter ModelContours so visual green contour also shows erased gap
        FilterContoursPredicate(p => (p.X - relCx) * (p.X - relCx) + (p.Y - relCy) * (p.Y - relCy) <= r2Base);
        return removedLevel0;
    }

    /// <summary>
    /// Box-erase model features and contour points inside an image-space rectangle.
    /// </summary>
    public int RemoveFeaturesByRect(Rect2f imageRect, bool recordUndo = true)
    {
        if (imageRect.Width <= 1f || imageRect.Height <= 1f) return 0;
        if (_initialSnapshot == null) CaptureInitialSnapshot();

        float minX = imageRect.X - OriginX;
        float minY = imageRect.Y - OriginY;
        float maxX = minX + imageRect.Width;
        float maxY = minY + imageRect.Height;

        bool anyHit = false;
        foreach (var lvl in Levels)
        {
            float scale = 1.0f / (1 << lvl.Level);
            float lx0 = minX * scale, ly0 = minY * scale, lx1 = maxX * scale, ly1 = maxY * scale;
            if (lvl.Features.Any(f => f.X >= lx0 && f.X <= lx1 && f.Y >= ly0 && f.Y <= ly1))
            {
                anyHit = true;
                break;
            }
        }

        if (!anyHit)
        {
            anyHit = ModelContours.Any(poly => poly.Any(p => p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY));
        }

        if (!anyHit) return 0;

        if (recordUndo)
        {
            _undoStack.Push(CreateSnapshot());
        }

        int removedLevel0 = 0;
        foreach (var lvl in Levels)
        {
            float scale = 1.0f / (1 << lvl.Level);
            float lx0 = minX * scale, ly0 = minY * scale, lx1 = maxX * scale, ly1 = maxY * scale;
            if (lvl.Level == 0)
            {
                var removedF = lvl.Features.Where(f => f.X >= lx0 && f.X <= lx1 && f.Y >= ly0 && f.Y <= ly1).ToList();
                MaskedFeatures.AddRange(removedF);
            }
            int removed = lvl.Features.RemoveAll(f => f.X >= lx0 && f.X <= lx1 && f.Y >= ly0 && f.Y <= ly1);
            if (lvl.Level == 0) removedLevel0 = removed;
        }

        FilterContoursPredicate(p => p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY);
        return removedLevel0;
    }

    private void FilterContoursPredicate(Func<Point2f, bool> shouldRemove)
    {
        var updatedPolys = new List<List<Point2f>>();
        foreach (var poly in ModelContours)
        {
            var currentSegment = new List<Point2f>();
            foreach (var pt in poly)
            {
                if (shouldRemove(pt))
                {
                    if (currentSegment.Count >= 2)
                        updatedPolys.Add(currentSegment);
                    currentSegment = new List<Point2f>();
                }
                else
                {
                    currentSegment.Add(pt);
                }
            }
            if (currentSegment.Count >= 2)
                updatedPolys.Add(currentSegment);
        }
        ModelContours = updatedPolys;
    }

    public bool UndoFeatureRemoval()
    {
        if (_undoStack.Count == 0) return false;
        var prev = _undoStack.Pop();
        RestoreSnapshot(prev);
        return true;
    }

    public bool ResetAllRemovedFeatures()
    {
        if (_initialSnapshot == null) return false;
        _undoStack.Clear();
        RestoreSnapshot(_initialSnapshot);
        MaskedFeatures.Clear();
        return true;
    }

    /// <summary>
    /// Equivalent to mwwz::set_shape_model_origin (RVA 0x7B9CC):
    /// Shifts the model reference origin in image coordinates and updates relative feature offsets.
    /// </summary>
    public void SetOrigin(float newOriginX, float newOriginY)
    {
        float dx = newOriginX - OriginX;
        float dy = newOriginY - OriginY;
        if (MathF.Abs(dx) < 1e-4f && MathF.Abs(dy) < 1e-4f) return;

        foreach (var lvl in Levels)
        {
            float scale = 1.0f / (1 << lvl.Level);
            float sdx = dx * scale;
            float sdy = dy * scale;
            for (int i = 0; i < lvl.Features.Count; i++)
            {
                var f = lvl.Features[i];
                lvl.Features[i] = new ShapeFeature(f.X - sdx, f.Y - sdy, f.Dx, f.Dy, f.Angle, f.Magnitude);
            }
        }

        for (int i = 0; i < MaskedFeatures.Count; i++)
        {
            var f = MaskedFeatures[i];
            MaskedFeatures[i] = new ShapeFeature(f.X - dx, f.Y - dy, f.Dx, f.Dy, f.Angle, f.Magnitude);
        }

        foreach (var poly in ModelContours)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                poly[i] = new Point2f(poly[i].X - dx, poly[i].Y - dy);
            }
        }

        OriginX = newOriginX;
        OriginY = newOriginY;
    }

    /// <summary>
    /// Equivalent to mwwz::set_match_result_offset (RVA 0x7BA0F):
    /// Transforms a relative offset (dx, dy) from model origin into scene coordinates using match pose.
    /// </summary>
    public static Point2f TransformByMatchOffset(float matchX, float matchY, float matchAngleDeg, float matchScale, float dx, float dy)
    {
        float rad = matchAngleDeg * (MathF.PI / 180f);
        float cosA = MathF.Cos(rad) * matchScale;
        float sinA = MathF.Sin(rad) * matchScale;
        return new Point2f(
            matchX + dx * cosA - dy * sinA,
            matchY + dx * sinA + dy * cosA);
    }


    public void SaveJson(string filePath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(filePath, json);
    }

    public static ShapeModel LoadJson(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var model = JsonSerializer.Deserialize<ShapeModel>(json)
            ?? throw new InvalidDataException("Failed to deserialize ShapeModel JSON");
        model.CaptureInitialSnapshot();
        return model;
    }

    /// <summary>
    /// Convert legacy ShapeFeature list to StandardFeature array for use in new engine
    /// </summary>
    public Core.StandardFeature[] GetStandardFeatures(int level)
    {
        if (level < 0 || level >= Levels.Count) return Array.Empty<Core.StandardFeature>();
        var lvl = Levels[level];
        var result = new Core.StandardFeature[lvl.Features.Count];
        for (int i = 0; i < lvl.Features.Count; i++)
        {
            var f = lvl.Features[i];
            result[i] = new Core.StandardFeature(f.X, f.Y, f.Dx, f.Dy);
        }
        return result;
    }

    /// <summary>
    /// Save model in binary .shm format compatible with mwwz / halcon format
    /// </summary>
    public void SaveShm(string filePath)
    {
        Core.ModelSerializer.SaveShm(this, filePath);
    }

    /// <summary>
    /// Load model from binary .shm file (auto-detects MWWZ standard or TunVision legacy format)
    /// </summary>
    public static ShapeModel LoadShm(string filePath)
    {
        return Core.ModelSerializer.LoadShm(filePath);
    }
}
