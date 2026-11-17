using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OpenCvSharp;
using TunVision.Core.Abstractions;
using TunVision.Core.Barcode;
using TunVision.Core.Blob;
using TunVision.Core.Caliper;
using TunVision.Core.Models;
using TunVision.Core.ShapeMatching;

namespace TunVision.Core.Graph;

public record ImageFileItem(string FullPath, string Name)
{
    public override string ToString() => Name;
}

public class SourceImageNode : GraphNode
{
    private string _imagePath = string.Empty;
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (_imagePath == value && !string.IsNullOrEmpty(value)) return;
            string newPath = value ?? string.Empty;
            string? resolved = PathResolver.ResolveImagePath(newPath, Graph?.ProjectDirectory, _folderPath);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
            {
                string dir = Path.GetDirectoryName(resolved) ?? string.Empty;
                RefreshFolderImages(dir);
                _imagePath = resolved;
                int idx = FindImageIndex(resolved);
                _imageIndex = idx >= 0 ? idx : Math.Max(0, FolderImages.Count > 0 ? 0 : -1);
                Status = "OK";
            }
            else
            {
                _imagePath = newPath;
                _imageIndex = FindImageIndex(newPath);
                if (!string.IsNullOrEmpty(newPath))
                {
                    Status = "File Not Found";
                    LastResult = $"File not found: {newPath}";
                    LastOutputMat?.Dispose();
                    LastOutputMat = null;
                }
            }
            NotifyImageChanged();
        }
    }

    private string _folderPath = string.Empty;
    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (_folderPath == value && !string.IsNullOrEmpty(value)) return;
            SetFolder(value ?? string.Empty);
        }
    }

    private int _imageIndex = 0;
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value) return;
            _imageIndex = value;
            if (FolderImages.Count > 0 && _imageIndex >= 0 && _imageIndex < FolderImages.Count)
            {
                _imagePath = FolderImages[_imageIndex];
                if (File.Exists(_imagePath))
                    Status = "OK";
            }
            NotifyImageChanged();
        }
    }

    public List<string> FolderImages { get; } = new();

    public List<ImageFileItem> FolderFileItems =>
        FolderImages.Select(f => new ImageFileItem(f, Path.GetFileName(f))).ToList();

    public string FileName => !string.IsNullOrEmpty(ImagePath) ? Path.GetFileName(ImagePath) : "None";
    public int TotalFolderImages => FolderImages.Count;
    public string IndexDisplay => TotalFolderImages > 0 ? $"{ImageIndex + 1} / {TotalFolderImages}" : "0 / 0";

    public int FindImageIndex(string? path)
    {
        if (string.IsNullOrEmpty(path)) return -1;
        try
        {
            string full = Path.GetFullPath(path);
            return FolderImages.FindIndex(f =>
            {
                try { return string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase); }
                catch { return string.Equals(f, path, StringComparison.OrdinalIgnoreCase); }
            });
        }
        catch
        {
            return FolderImages.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public event EventHandler? ImageChanged;

    private void NotifyImageChanged()
    {
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(FolderPath));
        OnPropertyChanged(nameof(FolderImages));
        OnPropertyChanged(nameof(FolderFileItems));
        OnPropertyChanged(nameof(ImageIndex));
        OnPropertyChanged(nameof(TotalFolderImages));
        OnPropertyChanged(nameof(IndexDisplay));
        ImageChanged?.Invoke(this, EventArgs.Empty);

        if (Graph != null && Graph.AutoExecute)
        {
            Graph.TriggerAutoExecute(this);
        }
    }

    public SourceImageNode() : base("Source Image", "Image")
    {
        AddOutput("image", "Image", PortKind.Image);
        InitializeDefault();
    }

    private void InitializeDefault()
    {
        var candidateDirs = PathResolver.GetCandidateImageDirectories();
        foreach (var dir in candidateDirs)
        {
            if (Directory.Exists(dir))
            {
                string[] testFiles;
                try
                {
                    testFiles = Directory.GetFiles(dir, "*.*")
                        .Where(f => PathResolver.IsValidImageFile(f))
                        .ToArray();
                }
                catch { continue; }

                if (testFiles.Length > 0)
                {
                    SetFolder(dir);
                    string[] preferredNames = { "retainers_01.png", "m1.bmp", "m2.bmp" };
                    string? selected = null;
                    foreach (var pref in preferredNames)
                    {
                        string cand = Path.Combine(dir, pref);
                        if (File.Exists(cand))
                        {
                            selected = cand;
                            break;
                        }
                    }
                    if (selected != null)
                    {
                        SelectImageByPath(selected);
                    }
                    break;
                }
            }
        }
    }

    public void SetImageFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string? resolved = PathResolver.ResolveImagePath(path, Graph?.ProjectDirectory, _folderPath);
        string target = (!string.IsNullOrEmpty(resolved) && File.Exists(resolved)) ? resolved : path;

        if (!File.Exists(target))
        {
            _imagePath = path;
            _imageIndex = -1;
            Status = "File Not Found";
            LastResult = $"File not found: {path}";
            LastOutputMat?.Dispose();
            LastOutputMat = null;
            NotifyImageChanged();
            return;
        }

        string dir = Path.GetDirectoryName(target) ?? "";
        RefreshFolderImages(dir);
        _imagePath = target;
        int idx = FindImageIndex(target);
        _imageIndex = idx >= 0 ? idx : 0;
        Status = "OK";
        NotifyImageChanged();
    }

    public void SetFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        _folderPath = folder;
        RefreshFolderImages(folder);
        if (FolderImages.Count > 0)
        {
            _imageIndex = 0;
            _imagePath = FolderImages[0];
            Status = "OK";
        }
        else
        {
            _imageIndex = -1;
            _imagePath = string.Empty;
        }
        NotifyImageChanged();
    }

    private void SanitizeFolderImages()
    {
        if (FolderImages.Count == 0) return;
        int initialCount = FolderImages.Count;
        FolderImages.RemoveAll(f =>
        {
            try { return !File.Exists(f); } catch { return true; }
        });
        if (FolderImages.Count != initialCount)
        {
            OnPropertyChanged(nameof(FolderImages));
            OnPropertyChanged(nameof(FolderFileItems));
            OnPropertyChanged(nameof(TotalFolderImages));
        }
    }

    private void EnsureValidFolderImages()
    {
        SanitizeFolderImages();
        if (FolderImages.Count == 0)
        {
            var candidates = PathResolver.GetCandidateImageDirectories(Graph?.ProjectDirectory, _folderPath);
            foreach (var dir in candidates)
            {
                RefreshFolderImages(dir);
                if (FolderImages.Count > 0)
                {
                    break;
                }
            }
        }
    }

    public void NextImage()
    {
        EnsureValidFolderImages();
        if (FolderImages.Count == 0) return;

        for (int attempts = 0; attempts < FolderImages.Count; attempts++)
        {
            _imageIndex = (_imageIndex + 1) % FolderImages.Count;
            string candidate = FolderImages[_imageIndex];
            if (File.Exists(candidate))
            {
                _imagePath = candidate;
                Status = "OK";
                NotifyImageChanged();
                return;
            }
        }

        SanitizeFolderImages();
        if (FolderImages.Count > 0)
        {
            _imageIndex = Math.Clamp(_imageIndex, 0, FolderImages.Count - 1);
            _imagePath = FolderImages[_imageIndex];
            Status = "OK";
            NotifyImageChanged();
        }
    }

    public void PrevImage()
    {
        EnsureValidFolderImages();
        if (FolderImages.Count == 0) return;

        for (int attempts = 0; attempts < FolderImages.Count; attempts++)
        {
            _imageIndex = (_imageIndex - 1 + FolderImages.Count) % FolderImages.Count;
            string candidate = FolderImages[_imageIndex];
            if (File.Exists(candidate))
            {
                _imagePath = candidate;
                Status = "OK";
                NotifyImageChanged();
                return;
            }
        }

        SanitizeFolderImages();
        if (FolderImages.Count > 0)
        {
            _imageIndex = Math.Clamp(_imageIndex, 0, FolderImages.Count - 1);
            _imagePath = FolderImages[_imageIndex];
            Status = "OK";
            NotifyImageChanged();
        }
    }

    public void SelectImageByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        string? resolved = PathResolver.ResolveImagePath(path, Graph?.ProjectDirectory, _folderPath);
        string target = (!string.IsNullOrEmpty(resolved) && File.Exists(resolved)) ? resolved : path;

        int idx = FindImageIndex(target);
        if (idx >= 0 && File.Exists(FolderImages[idx]))
        {
            _imageIndex = idx;
            _imagePath = FolderImages[idx];
            Status = "OK";
            NotifyImageChanged();
        }
        else if (File.Exists(target))
        {
            SetImageFile(target);
        }
        else
        {
            _imagePath = path;
            _imageIndex = -1;
            Status = "File Not Found";
            LastResult = $"File not found: {path}";
            LastOutputMat?.Dispose();
            LastOutputMat = null;
            NotifyImageChanged();
        }
    }

    public void SelectSampleByFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        // 1. MUST verify File.Exists(match) before accepting a match from FolderImages!
        var match = FolderImages.FirstOrDefault(f =>
        {
            try
            {
                return string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        });

        if (match != null && File.Exists(match))
        {
            SelectImageByPath(match);
            return;
        }

        // 2. If not found or stale/dead match, probe candidate directories via PathResolver
        string? resolved = PathResolver.ResolveImagePath(fileName, Graph?.ProjectDirectory, _folderPath);
        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
        {
            // Update _imagePath and re-populate FolderImages from the directory containing that valid file
            SetImageFile(resolved);
            return;
        }

        // 3. Fallback: file could not be found anywhere
        _imagePath = fileName;
        _imageIndex = -1;
        Status = "File Not Found";
        LastResult = $"File not found: {fileName}";
        LastOutputMat?.Dispose();
        LastOutputMat = null;
        NotifyImageChanged();
    }

    private void RefreshFolderImages(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        _folderPath = dir;
        FolderImages.Clear();
        var exts = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tif", "*.tiff" };
        foreach (var ext in exts)
        {
            try
            {
                FolderImages.AddRange(Directory.GetFiles(dir, ext));
            }
            catch { }
        }
        FolderImages.Sort(StringComparer.OrdinalIgnoreCase);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        Mat? mat = null;
        string? path = ImagePath;

        // Attempt multi-tier resolution without firing recursive NotifyImageChanged events
        string? resolved = PathResolver.ResolveImagePath(path, Graph?.ProjectDirectory, _folderPath);

        if (string.IsNullOrEmpty(resolved) && string.IsNullOrEmpty(path))
        {
            if (FolderImages.Count > 0 && ImageIndex >= 0 && ImageIndex < FolderImages.Count)
            {
                resolved = PathResolver.ResolveImagePath(FolderImages[ImageIndex], Graph?.ProjectDirectory, _folderPath);
            }
            else if (!string.IsNullOrEmpty(context.RootImagePath))
            {
                resolved = PathResolver.ResolveImagePath(context.RootImagePath, Graph?.ProjectDirectory, _folderPath);
            }
        }

        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
        {
            if (_imagePath != resolved)
            {
                _imagePath = resolved;
                string dir = Path.GetDirectoryName(resolved) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !string.Equals(_folderPath, dir, StringComparison.OrdinalIgnoreCase))
                {
                    RefreshFolderImages(dir);
                }
                int idx = FindImageIndex(resolved);
                _imageIndex = idx >= 0 ? idx : Math.Max(0, FolderImages.Count > 0 ? 0 : -1);
                OnPropertyChanged(nameof(ImagePath));
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(FolderPath));
                OnPropertyChanged(nameof(FolderImages));
                OnPropertyChanged(nameof(FolderFileItems));
                OnPropertyChanged(nameof(ImageIndex));
                OnPropertyChanged(nameof(TotalFolderImages));
                OnPropertyChanged(nameof(IndexDisplay));
            }

            try
            {
                mat = Cv2.ImRead(resolved, ImreadModes.Color);
                if (mat != null && mat.Empty())
                {
                    mat.Dispose();
                    mat = null;
                    Status = "Error: Invalid Image File";
                    LastResult = $"Corrupted or unsupported image: {FileName}";
                }
                else if (mat != null)
                {
                    Status = "OK";
                    LastResult = $"{FileName} ({mat.Width}x{mat.Height}) [{IndexDisplay}]";
                }
                else
                {
                    Status = "Error: Invalid Image File";
                    LastResult = $"Failed to decode image: {FileName}";
                }
            }
            catch (Exception ex)
            {
                mat = null;
                Status = $"Error: {ex.Message}";
                LastResult = $"Read error: {ex.Message}";
            }
        }
        else
        {
            mat = null;
            if (string.IsNullOrEmpty(path))
            {
                Status = "No Image";
                LastResult = "No Image";
            }
            else
            {
                Status = "File Not Found";
                LastResult = $"File not found: {path}";
            }
        }

        LastOutputMat = mat;

        return Task.FromResult(new Dictionary<string, object?> { ["image"] = mat });
    }
}

public class CropNode : GraphNode
{
    public CropNode() : base("Crop", "Image")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("roi", "ROI", PortKind.Roi);
        AddOutput("image", "Image", PortKind.Image);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi roi)
            {
                var rect = roi.GetBoundingBox();
                rect.X = Math.Max(0, Math.Min(srcMat.Width - 1, rect.X));
                rect.Y = Math.Max(0, Math.Min(srcMat.Height - 1, rect.Y));
                rect.Width = Math.Max(1, Math.Min(srcMat.Width - rect.X, rect.Width));
                rect.Height = Math.Max(1, Math.Min(srcMat.Height - rect.Y, rect.Height));

                var cropped = new Mat(srcMat, rect).Clone();
                LastOutputMat = cropped;
                LastResult = $"Cropped: {cropped.Width}x{cropped.Height}";
                return Task.FromResult(new Dictionary<string, object?> { ["image"] = cropped });
            }
            LastOutputMat = srcMat.Clone();
            return Task.FromResult(new Dictionary<string, object?> { ["image"] = LastOutputMat });
        }
        return Task.FromResult(new Dictionary<string, object?> { ["image"] = null });
    }
}

public enum ImageColorMode
{
    Original,
    Grayscale,
    RedChannel,
    GreenChannel,
    BlueChannel,
    HsvHue,
    HsvSaturation,
    HsvValue,
    PseudoJet,
    PseudoHot
}

public enum ImageFilterOp
{
    None,
    GaussianBlur,
    MedianBlur,
    Bilateral,
    Sharpen,
    MorphOpen,
    MorphClose,
    MorphGradient,
    CannyEdge
}

public class ImageFilterNode : GraphNode
{
    private ImageColorMode _colorMode = ImageColorMode.Original;
    public ImageColorMode ColorMode
    {
        get => _colorMode;
        set { if (_colorMode != value) { _colorMode = value; OnPropertyChanged(); } }
    }

    private double _exposure = 0.0;
    /// <summary>Exposure / Brightness offset (-100 to +100).</summary>
    public double Exposure
    {
        get => _exposure;
        set
        {
            var clamped = Math.Clamp(value, -100.0, 100.0);
            if (Math.Abs(_exposure - clamped) > 1e-4) { _exposure = clamped; OnPropertyChanged(); }
        }
    }

    private double _contrast = 1.0;
    /// <summary>Contrast multiplier (0.1 to 4.0, 1.0 = neutral).</summary>
    public double Contrast
    {
        get => _contrast;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 4.0);
            if (Math.Abs(_contrast - clamped) > 1e-4) { _contrast = clamped; OnPropertyChanged(); }
        }
    }

    private double _gamma = 1.0;
    /// <summary>Gamma correction (0.1 to 5.0, 1.0 = neutral, &gt;1 brightens shadows, &lt;1 darkens).</summary>
    public double Gamma
    {
        get => _gamma;
        set
        {
            var clamped = Math.Clamp(value, 0.1, 5.0);
            if (Math.Abs(_gamma - clamped) > 1e-4) { _gamma = clamped; OnPropertyChanged(); }
        }
    }

    private double _saturation = 1.0;
    /// <summary>Color saturation multiplier (0.0 to 3.0, 1.0 = neutral).</summary>
    public double Saturation
    {
        get => _saturation;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 3.0);
            if (Math.Abs(_saturation - clamped) > 1e-4) { _saturation = clamped; OnPropertyChanged(); }
        }
    }

    private ImageFilterOp _filterOperation = ImageFilterOp.None;
    public ImageFilterOp FilterOperation
    {
        get => _filterOperation;
        set { if (_filterOperation != value) { _filterOperation = value; OnPropertyChanged(); } }
    }

    private int _kernelSize = 3;
    public int KernelSize
    {
        get => _kernelSize;
        set
        {
            int k = Math.Clamp(value, 1, 31);
            if (k % 2 == 0) k++;
            if (_kernelSize != k) { _kernelSize = k; OnPropertyChanged(); }
        }
    }

    private bool _autoClahe = false;
    public bool AutoClahe
    {
        get => _autoClahe;
        set { if (_autoClahe != value) { _autoClahe = value; OnPropertyChanged(); } }
    }

    private bool _invert = false;
    public bool Invert
    {
        get => _invert;
        set { if (_invert != value) { _invert = value; OnPropertyChanged(); } }
    }

    private bool _enableThreshold = false;
    public bool EnableThreshold
    {
        get => _enableThreshold;
        set { if (_enableThreshold != value) { _enableThreshold = value; OnPropertyChanged(); } }
    }

    private int _thresholdValue = 128;
    public int ThresholdValue
    {
        get => _thresholdValue;
        set
        {
            int v = Math.Clamp(value, 0, 255);
            if (_thresholdValue != v) { _thresholdValue = v; OnPropertyChanged(); }
        }
    }

    public ImageFilterNode() : base("Image Filter", "Image")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("roi", "ROI", PortKind.Roi);
        AddOutput("image", "Image", PortKind.Image);
    }

    public void ResetDefaults()
    {
        ColorMode = ImageColorMode.Original;
        Exposure = 0.0;
        Contrast = 1.0;
        Gamma = 1.0;
        Saturation = 1.0;
        FilterOperation = ImageFilterOp.None;
        KernelSize = 3;
        AutoClahe = false;
        Invert = false;
        EnableThreshold = false;
        ThresholdValue = 128;
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();

        if (!inputs.TryGetValue("image", out var imgObj) || imgObj is not Mat srcMat || srcMat.Empty())
        {
            LastOutputMat = null;
            LastResult = "Waiting for Image";
            return Task.FromResult(new Dictionary<string, object?> { ["image"] = null });
        }

        Rect? roiRect = null;
        if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi roi)
        {
            var r = roi.GetBoundingBox();
            r.X = Math.Max(0, Math.Min(srcMat.Width - 1, r.X));
            r.Y = Math.Max(0, Math.Min(srcMat.Height - 1, r.Y));
            r.Width = Math.Max(1, Math.Min(srcMat.Width - r.X, r.Width));
            r.Height = Math.Max(1, Math.Min(srcMat.Height - r.Y, r.Height));
            roiRect = r;
        }

        Mat fullOut = srcMat.Clone();
        Mat targetView = roiRect.HasValue ? new Mat(fullOut, roiRect.Value) : fullOut;
        Mat processed = ProcessRegion(targetView);

        if (roiRect.HasValue)
        {
            if (processed.Channels() != fullOut.Channels())
            {
                if (processed.Channels() == 1 && fullOut.Channels() == 3)
                    Cv2.CvtColor(processed, processed, ColorConversionCodes.GRAY2BGR);
                else if (processed.Channels() == 3 && fullOut.Channels() == 1)
                {
                    Cv2.CvtColor(fullOut, fullOut, ColorConversionCodes.GRAY2BGR);
                    targetView = new Mat(fullOut, roiRect.Value);
                }
            }
            processed.CopyTo(targetView);
            processed.Dispose();
        }
        else
        {
            fullOut.Dispose();
            fullOut = processed;
        }

        // Compute brightness & contrast statistics on output
        using var grayStat = fullOut.Channels() == 3
            ? fullOut.CvtColor(ColorConversionCodes.BGR2GRAY)
            : fullOut.Clone();
        Cv2.MeanStdDev(grayStat, out var meanScalar, out var stdScalar);

        RecordTextMeasurement("Chế độ màu", ColorMode.ToString());
        RecordMeasurement("Độ sáng TB", meanScalar.Val0, "F1");
        RecordMeasurement("Độ tương phản", stdScalar.Val0, "F1");
        RecordMeasurement("Số kênh màu", fullOut.Channels(), "F0");

        LastOutputMat = fullOut;
        LastResult = $"{ColorMode}, Exp:{Exposure:+0.#;-0.#;0}, C:{Contrast:F2}, G:{Gamma:F2}" +
                     (FilterOperation != ImageFilterOp.None ? $", {FilterOperation}(k={KernelSize})" : "") +
                     $" | Bright:{meanScalar.Val0:F1}";

        return Task.FromResult(new Dictionary<string, object?> { ["image"] = fullOut });
    }

    private Mat ProcessRegion(Mat src)
    {
        Mat work = src.Clone();

        // 1. Optional saturation adjustment on 3-channel BGR before color space conversion
        if (work.Channels() == 3 && Math.Abs(Saturation - 1.0) > 1e-3 && ColorMode == ImageColorMode.Original)
        {
            using var hsv = work.CvtColor(ColorConversionCodes.BGR2HSV);
            Cv2.Split(hsv, out var hsvPlanes);
            hsvPlanes[1].ConvertTo(hsvPlanes[1], MatType.CV_8UC1, Saturation, 0);
            Cv2.Merge(hsvPlanes, hsv);
            foreach (var p in hsvPlanes) p.Dispose();
            Cv2.CvtColor(hsv, work, ColorConversionCodes.HSV2BGR);
        }

        // 2. Color mode / channel extraction
        if (ColorMode != ImageColorMode.Original)
        {
            Mat converted = new Mat();
            switch (ColorMode)
            {
                case ImageColorMode.Grayscale:
                    if (work.Channels() == 3)
                        Cv2.CvtColor(work, converted, ColorConversionCodes.BGR2GRAY);
                    else
                        converted = work.Clone();
                    break;

                case ImageColorMode.RedChannel:
                case ImageColorMode.GreenChannel:
                case ImageColorMode.BlueChannel:
                    if (work.Channels() == 3)
                    {
                        Cv2.Split(work, out var bgr);
                        int idx = ColorMode == ImageColorMode.BlueChannel ? 0
                                : ColorMode == ImageColorMode.GreenChannel ? 1 : 2;
                        converted = bgr[idx].Clone();
                        foreach (var ch in bgr) ch.Dispose();
                    }
                    else
                    {
                        converted = work.Clone();
                    }
                    break;

                case ImageColorMode.HsvHue:
                case ImageColorMode.HsvSaturation:
                case ImageColorMode.HsvValue:
                    if (work.Channels() == 3)
                    {
                        using var hsv = work.CvtColor(ColorConversionCodes.BGR2HSV);
                        Cv2.Split(hsv, out var hPlanes);
                        int hIdx = ColorMode == ImageColorMode.HsvHue ? 0
                                 : ColorMode == ImageColorMode.HsvSaturation ? 1 : 2;
                        converted = hPlanes[hIdx].Clone();
                        foreach (var ch in hPlanes) ch.Dispose();
                    }
                    else
                    {
                        converted = work.Clone();
                    }
                    break;

                case ImageColorMode.PseudoJet:
                case ImageColorMode.PseudoHot:
                    using (var gray = work.Channels() == 3 ? work.CvtColor(ColorConversionCodes.BGR2GRAY) : work.Clone())
                    {
                        Cv2.ApplyColorMap(
                            gray,
                            converted,
                            ColorMode == ImageColorMode.PseudoJet ? ColormapTypes.Jet : ColormapTypes.Hot);
                    }
                    break;

                default:
                    converted = work.Clone();
                    break;
            }
            work.Dispose();
            work = converted;
        }

        // 3. Exposure / Brightness & Contrast adjustment: I_out = Contrast * I_in + Exposure * 2.2
        if (Math.Abs(Contrast - 1.0) > 1e-3 || Math.Abs(Exposure) > 1e-3)
        {
            double beta = Exposure * 2.2;
            // Pivot contrast around mid-gray (128) + exposure shift
            double offset = 128.0 * (1.0 - Contrast) + beta;
            work.ConvertTo(work, work.Type(), Contrast, offset);
        }

        // 4. Gamma correction via 256-entry LUT
        if (Math.Abs(Gamma - 1.0) > 1e-3)
        {
            double invGamma = 1.0 / Math.Max(0.1, Gamma);
            byte[] lutData = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double v = Math.Pow(i / 255.0, invGamma) * 255.0;
                lutData[i] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
            }
            using var lut = Mat.FromPixelData(1, 256, MatType.CV_8UC1, lutData);
            var gammaOut = new Mat();
            Cv2.LUT(work, lut, gammaOut);
            work.Dispose();
            work = gammaOut;
        }

        // 5. Local histogram equalization (CLAHE)
        if (AutoClahe)
        {
            using var clahe = Cv2.CreateCLAHE(2.5, new Size(8, 8));
            if (work.Channels() == 1)
            {
                var eq = new Mat();
                clahe.Apply(work, eq);
                work.Dispose();
                work = eq;
            }
            else if (work.Channels() == 3)
            {
                using var lab = work.CvtColor(ColorConversionCodes.BGR2Lab);
                Cv2.Split(lab, out var labPlanes);
                clahe.Apply(labPlanes[0], labPlanes[0]);
                Cv2.Merge(labPlanes, lab);
                foreach (var p in labPlanes) p.Dispose();
                Cv2.CvtColor(lab, work, ColorConversionCodes.Lab2BGR);
            }
        }

        // 6. Spatial Filtering (Blur / Denoise / Sharpen / Morphology / Canny)
        int k = Math.Max(1, KernelSize | 1);
        if (FilterOperation != ImageFilterOp.None)
        {
            Mat filtered = new Mat();
            switch (FilterOperation)
            {
                case ImageFilterOp.GaussianBlur:
                    int gk = Math.Max(3, k);
                    Cv2.GaussianBlur(work, filtered, new Size(gk, gk), 0);
                    break;

                case ImageFilterOp.MedianBlur:
                    int mk = Math.Max(3, k);
                    Cv2.MedianBlur(work, filtered, mk);
                    break;

                case ImageFilterOp.Bilateral:
                    int d = Math.Max(5, k * 2 + 1);
                    Cv2.BilateralFilter(work, filtered, d, 50, 50);
                    break;

                case ImageFilterOp.Sharpen:
                    using (var blurred = new Mat())
                    {
                        int sk = Math.Max(3, k);
                        Cv2.GaussianBlur(work, blurred, new Size(sk, sk), 0);
                        Cv2.AddWeighted(work, 1.8, blurred, -0.8, 0, filtered);
                    }
                    break;

                case ImageFilterOp.MorphOpen:
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(3, k), Math.Max(3, k))))
                        Cv2.MorphologyEx(work, filtered, MorphTypes.Open, kernel);
                    break;

                case ImageFilterOp.MorphClose:
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(3, k), Math.Max(3, k))))
                        Cv2.MorphologyEx(work, filtered, MorphTypes.Close, kernel);
                    break;

                case ImageFilterOp.MorphGradient:
                    using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(3, k), Math.Max(3, k))))
                        Cv2.MorphologyEx(work, filtered, MorphTypes.Gradient, kernel);
                    break;

                case ImageFilterOp.CannyEdge:
                    using (var gray = work.Channels() == 3 ? work.CvtColor(ColorConversionCodes.BGR2GRAY) : work.Clone())
                    {
                        Cv2.Canny(gray, filtered, 60, 160);
                    }
                    break;

                default:
                    filtered = work.Clone();
                    break;
            }
            work.Dispose();
            work = filtered;
        }

        // 7. Optional Binary Thresholding
        if (EnableThreshold)
        {
            using var gray = work.Channels() == 3 ? work.CvtColor(ColorConversionCodes.BGR2GRAY) : work.Clone();
            var bin = new Mat();
            Cv2.Threshold(gray, bin, ThresholdValue, 255, ThresholdTypes.Binary);
            work.Dispose();
            work = bin;
        }

        // 8. Optional Invert (Negative image)
        if (Invert)
        {
            Cv2.BitwiseNot(work, work);
        }

        return work;
    }
}

public class RoiRectangleNode : GraphNode
{
    public float X { get; set; } = 100;
    public float Y { get; set; } = 100;
    public float Width { get; set; } = 200;
    public float Height { get; set; } = 200;
    public float AngleDeg { get; set; } = 0;

    public RoiRectangleNode() : base("ROI Rectangle", "ROI")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Fixture", PortKind.Fixture);
        AddOutput("roi", "ROI", PortKind.Roi);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        float targetX = X;
        float targetY = Y;
        float targetAngle = AngleDeg;

        if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
        {
            var p = fix.Transform(new Point2f(X, Y));
            targetX = p.X;
            targetY = p.Y;
            targetAngle += fix.RotationDeg;
        }

        BaseRoi roi;
        if (Math.Abs(targetAngle) > 0.01f)
        {
            roi = new RotatedRectRoi(new Point2f(targetX + Width * 0.5f, targetY + Height * 0.5f), new Size2f(Width, Height), targetAngle);
        }
        else
        {
            roi = new RectRoi((int)Math.Round(targetX), (int)Math.Round(targetY), (int)Math.Round(Width), (int)Math.Round(Height));
        }

        LastResult = $"Rect: ({targetX:F1}, {targetY:F1}, {Width:F1}x{Height:F1}, {targetAngle:F1}°)";
        return Task.FromResult(new Dictionary<string, object?> { ["roi"] = roi });
    }
}

public class RoiCircleNode : GraphNode
{
    public float CenterX { get; set; } = 200;
    public float CenterY { get; set; } = 200;
    public float Radius { get; set; } = 50;

    public RoiCircleNode() : base("ROI Circle", "ROI")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Fixture", PortKind.Fixture);
        AddOutput("roi", "ROI", PortKind.Roi);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        float cx = CenterX;
        float cy = CenterY;
        if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
        {
            var p = fix.Transform(new Point2f(cx, cy));
            cx = p.X;
            cy = p.Y;
        }

        var roi = new CircleRoi(new Point2f(cx, cy), Radius);
        LastResult = $"Circle: ({cx:F1}, {cy:F1}, R={Radius:F1})";
        return Task.FromResult(new Dictionary<string, object?> { ["roi"] = roi });
    }
}

public class RoiPolygonNode : GraphNode
{
    public List<Point2f> Points { get; set; } = new()
    {
        new Point2f(120, 120),
        new Point2f(280, 120),
        new Point2f(280, 260),
        new Point2f(120, 260)
    };

    [JsonIgnore]
    public int VertexCount => Points.Count;

    public RoiPolygonNode() : base("ROI Polygon", "ROI")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Fixture", PortKind.Fixture);
        AddOutput("roi", "ROI", PortKind.Roi);
    }

    public void AddVertexAtLongestEdge()
    {
        if (Points.Count < 2)
        {
            ResetDefaultQuad();
            return;
        }

        int bestIdx = 0;
        double bestDistSq = -1.0;
        for (int i = 0; i < Points.Count; i++)
        {
            var a = Points[i];
            var b = Points[(i + 1) % Points.Count];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double dSq = dx * dx + dy * dy;
            if (dSq > bestDistSq)
            {
                bestDistSq = dSq;
                bestIdx = i;
            }
        }

        var p1 = Points[bestIdx];
        var p2 = Points[(bestIdx + 1) % Points.Count];
        Points.Insert(bestIdx + 1, new Point2f((p1.X + p2.X) * 0.5f, (p1.Y + p2.Y) * 0.5f));
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(VertexCount));
    }

    public void RemoveLastVertex()
    {
        if (Points.Count > 3)
        {
            Points.RemoveAt(Points.Count - 1);
            OnPropertyChanged(nameof(Points));
            OnPropertyChanged(nameof(VertexCount));
        }
    }

    public void ResetDefaultQuad(float x = 120, float y = 120, float w = 160, float h = 140)
    {
        Points = new List<Point2f>
        {
            new Point2f(x, y),
            new Point2f(x + w, y),
            new Point2f(x + w, y + h),
            new Point2f(x, y + h)
        };
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(VertexCount));
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        List<Point2f> transformedPts;
        if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
        {
            transformedPts = Points.Select(p => fix.Transform(p)).ToList();
        }
        else
        {
            transformedPts = Points.ToList();
        }

        var roi = new PolygonRoi(transformedPts);
        LastResult = $"Polygon: {transformedPts.Count} vertices";
        return Task.FromResult(new Dictionary<string, object?> { ["roi"] = roi });
    }
}

/// <summary>
/// TrainShapeModelNode: Trains a reusable ShapeModel from a golden template image and model ROI.
/// Outputs the trained model to wire directly into ShapeMatchNode or export to .shm!
/// </summary>
public class TrainShapeModelNode : GraphNode
{
    public AlignTool ToolInstance { get; } = new("ShapeTrainer");
    public string ExportPath { get; set; } = string.Empty;

    private int _lastAutoBalancedImageKey = 0;
    private bool _hasManualParamEdit = false;
    private bool _isApplyingAutoBalance = false;
    private bool _needsRetrain = false;

    public void RequestRetrain() => _needsRetrain = true;

    public void ResetAutoBalanceState(bool clearManualEdit = true)
    {
        _lastAutoBalancedImageKey = 0;
        if (clearManualEdit)
            _hasManualParamEdit = false;
    }

    public void NotifyBalancedParametersChanged()
    {
        OnPropertyChanged(nameof(ContrastLow));
        OnPropertyChanged(nameof(ContrastHigh));
        OnPropertyChanged(nameof(MinContrast));
        OnPropertyChanged(nameof(MinComponentSize));
        OnPropertyChanged(nameof(MinLength));
        OnPropertyChanged(nameof(PyramidLevels));
        OnPropertyChanged(nameof(LowestPyramid));
        OnPropertyChanged(nameof(ContourMode));
    }

    internal static int ComputeImageKey(Mat mat, BaseRoi? roi = null)
    {
        var mean = Cv2.Mean(mat);
        var bbox = roi?.GetBoundingBox() ?? default;
        return HashCode.Combine(
            mat.Width,
            mat.Height,
            mat.Type(),
            Math.Round(mean.Val0, 2),
            Math.Round(mean.Val1, 2),
            Math.Round(mean.Val2, 2),
            bbox);
    }

    public int ContrastLow
    {
        get => ToolInstance.ContrastLow;
        set
        {
            int clamped = Math.Clamp(value, 1, 250);
            if (ToolInstance.ContrastLow != clamped)
            {
                ToolInstance.ContrastLow = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int ContrastHigh
    {
        get => ToolInstance.ContrastHigh;
        set
        {
            int clamped = Math.Clamp(value, 2, 255);
            if (ToolInstance.ContrastHigh != clamped)
            {
                ToolInstance.ContrastHigh = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int MinContrast
    {
        get => ToolInstance.MinContrast;
        set
        {
            int clamped = Math.Clamp(value, 1, 250);
            if (ToolInstance.MinContrast != clamped)
            {
                ToolInstance.MinContrast = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int MinComponentSize
    {
        get => ToolInstance.MinComponentSize;
        set
        {
            int clamped = Math.Clamp(value, 1, 5000);
            if (ToolInstance.MinComponentSize != clamped)
            {
                ToolInstance.MinComponentSize = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int MinLength
    {
        get => ToolInstance.MinLength;
        set
        {
            int clamped = Math.Clamp(value, 1, 5000);
            if (ToolInstance.MinLength != clamped)
            {
                ToolInstance.MinLength = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int PyramidLevels
    {
        get => ToolInstance.PyramidLevels;
        set
        {
            int clamped = Math.Clamp(value, 0, 6);
            if (ToolInstance.PyramidLevels != clamped)
            {
                ToolInstance.PyramidLevels = clamped;
                if (!_isApplyingAutoBalance) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public ContourExtractionMode ContourMode
    {
        get => ToolInstance.ContourMode;
        set
        {
            if (ToolInstance.ContourMode != value)
            {
                ToolInstance.ContourMode = value;
                OnPropertyChanged();
            }
        }
    }

    public float ScaleMin
    {
        get => ToolInstance.ScaleMin;
        set
        {
            if (Math.Abs(ToolInstance.ScaleMin - value) > 1e-6f)
            {
                ToolInstance.ScaleMin = value;
                OnPropertyChanged();
            }
        }
    }

    public float ScaleMax
    {
        get => ToolInstance.ScaleMax;
        set
        {
            if (Math.Abs(ToolInstance.ScaleMax - value) > 1e-6f)
            {
                ToolInstance.ScaleMax = value;
                OnPropertyChanged();
            }
        }
    }

    public int LowestPyramid
    {
        get => ToolInstance.LowestPyramid;
        set
        {
            int clamped = Math.Clamp(value, 1, 10);
            if (ToolInstance.LowestPyramid != clamped)
            {
                ToolInstance.LowestPyramid = clamped;
                OnPropertyChanged();
            }
        }
    }

    public string TemplateImagePath { get; set; } = string.Empty;

    // Optional interactive ROI drawn directly on the canvas (if no external ROI port is wired)
    public float RoiX { get; set; } = 0;
    public float RoiY { get; set; } = 0;
    public float RoiWidth { get; set; } = 0;
    public float RoiHeight { get; set; } = 0;
    public bool HasCustomRoi => RoiWidth > 8 && RoiHeight > 8;

    [JsonIgnore]
    public Mat? RawTemplateMat { get; private set; }

    public TrainShapeModelNode() : base("Train Shape Model", "Detection")
    {
        AddInput("image", "Template Img", PortKind.Image);
        AddInput("roi", "Model ROI", PortKind.Roi);
        AddOutput("model", "Model Out", PortKind.Model);
        AddOutput("image", "Contour Img", PortKind.Image);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        Mat? tplMat = null;
        bool ownsMat = false;

        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat wiredMat && !wiredMat.Empty())
        {
            tplMat = wiredMat;
        }
        else if (!string.IsNullOrEmpty(TemplateImagePath) && File.Exists(TemplateImagePath))
        {
            tplMat = Cv2.ImRead(TemplateImagePath, ImreadModes.Color);
            ownsMat = true;
        }

        if (tplMat != null && !tplMat.Empty())
        {
            BaseRoi? roi = null;
            if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi r)
                roi = r;
            else if (HasCustomRoi)
                roi = new RectRoi((int)Math.Round(RoiX), (int)Math.Round(RoiY), (int)Math.Round(RoiWidth), (int)Math.Round(RoiHeight));

            int imgKey = ComputeImageKey(tplMat, roi);
            bool imageChanged = _lastAutoBalancedImageKey != imgKey;
            if (imageChanged || ToolInstance.Model == null || _needsRetrain)
            {
                _lastAutoBalancedImageKey = imgKey;
                _needsRetrain = false;
                if (!_hasManualParamEdit)
                {
                    AutoEstimateParametersCore(tplMat, roi, notifyUi: false);
                }
                ToolInstance.Train(tplMat, roi);
            }
            var model = ToolInstance.Model;

            if (model != null && !string.IsNullOrEmpty(ExportPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(ExportPath) ?? "";
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    model.SaveShm(ExportPath);
                }
                catch { }
            }

            Mat rawBgr = tplMat.Channels() == 1
                ? tplMat.CvtColor(ColorConversionCodes.GRAY2BGR)
                : tplMat.Clone();
            RawTemplateMat = rawBgr;

            Mat overlay = rawBgr.Clone();
            if (model != null)
            {
                ShapeVisualizer.DrawTemplateContourImage(overlay, model, roi);
            }

            LastOutputMat = overlay;
            int featCount = model?.Levels.FirstOrDefault()?.Features.Count ?? 0;
            RecordMeasurement("features", featCount, "F0");
            RecordMeasurement("levels", model?.NumLevels ?? 0, "F0");
            RecordMeasurement("contours", model?.ModelContours.Count ?? 0, "F0");
            RecordMeasurement("min_contrast", ToolInstance.MinContrast, "F0");
            RecordMeasurement("min_comp_size", ToolInstance.MinComponentSize, "F0", "px");
            RecordMeasurement("origin_x", model?.OriginX ?? 0, "F1", "px");
            RecordMeasurement("origin_y", model?.OriginY ?? 0, "F1", "px");
            LastResult = $"Trained: {featCount} features, {model?.NumLevels ?? 0} levels, {model?.ModelContours.Count ?? 0} contours";

            if (ownsMat) tplMat.Dispose();

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["model"] = model,
                ["image"] = overlay
            });
        }

        RawTemplateMat = null;
        LastResult = "No Template Image";
        return Task.FromResult(new Dictionary<string, object?>
        {
            ["model"] = null,
            ["image"] = null
        });
    }

    public void RetrainNow(Mat tplMat, BaseRoi? roi = null)
    {
        if (roi == null && HasCustomRoi)
            roi = new RectRoi((int)Math.Round(RoiX), (int)Math.Round(RoiY), (int)Math.Round(RoiWidth), (int)Math.Round(RoiHeight));
        _lastAutoBalancedImageKey = ComputeImageKey(tplMat, roi);
        _needsRetrain = false;
        ToolInstance.Train(tplMat, roi);
        NotifyBalancedParametersChanged();
    }

    public ShapeMatching.ContrastEstimationResult AutoEstimateParameters(Mat? tplMat = null, BaseRoi? roi = null)
        => AutoEstimateParametersCore(tplMat, roi, notifyUi: true);

    private ShapeMatching.ContrastEstimationResult AutoEstimateParametersCore(Mat? tplMat, BaseRoi? roi, bool notifyUi)
    {
        if (roi == null && HasCustomRoi)
            roi = new RectRoi((int)Math.Round(RoiX), (int)Math.Round(RoiY), (int)Math.Round(RoiWidth), (int)Math.Round(RoiHeight));

        _isApplyingAutoBalance = true;
        try
        {
            if (tplMat != null && !tplMat.Empty())
            {
                var res = ToolInstance.AutoEstimateParameters(tplMat, roi);
                if (notifyUi) NotifyBalancedParametersChanged();
                return res;
            }

            if (!string.IsNullOrEmpty(TemplateImagePath) && File.Exists(TemplateImagePath))
            {
                using var diskMat = Cv2.ImRead(TemplateImagePath, ImreadModes.Color);
                var res = ToolInstance.AutoEstimateParameters(diskMat, roi);
                if (notifyUi) NotifyBalancedParametersChanged();
                return res;
            }

            return new ShapeMatching.ContrastEstimationResult(9, 39, 52, 16, 4, 1.5f);
        }
        finally
        {
            _isApplyingAutoBalance = false;
        }
    }
}

/// <summary>
/// ShapeMatchNode: The core MWWZ gradient-direction pattern matching engine!
/// Requires either:
/// 1. A pre-trained model file (.shm / .json) via ModelPath or wired input 'model'
/// 2. A golden template image via TemplateImagePath or wired input 'template'
/// </summary>
public class ShapeMatchNode : GraphNode
{
    public AlignTool ToolInstance { get; } = new("ShapeMatch");

    public string ModelPath { get; set; } = string.Empty;
    public string TemplateImagePath { get; set; } = string.Empty;

    private bool _hasManualParamEdit = false;
    private bool _isSyncingOrBalancing = false;
    private bool _needsRetrain = false;
    private ShapeModel? _lastSyncedModel;
    private int _lastSceneImageKey = 0;

    public void RequestRetrain() => _needsRetrain = true;

    public void ResetAutoBalanceState(bool clearManualEdit = true)
    {
        _lastSceneImageKey = 0;
        _lastSyncedModel = null;
        if (clearManualEdit)
            _hasManualParamEdit = false;
    }

    public void NotifyBalancedParametersChanged()
    {
        OnPropertyChanged(nameof(ContrastLow));
        OnPropertyChanged(nameof(ContrastHigh));
        OnPropertyChanged(nameof(MinContrast));
        OnPropertyChanged(nameof(MinComponentSize));
        OnPropertyChanged(nameof(MinContourLength));
        OnPropertyChanged(nameof(PyramidLevels));
        OnPropertyChanged(nameof(LowestPyramid));
        OnPropertyChanged(nameof(UseTiledSearch));
        OnPropertyChanged(nameof(ContourMode));
        OnPropertyChanged(nameof(ModelStatusText));
    }

    public void SyncFromModel(ShapeModel model, bool notifyUi = true)
    {
        _isSyncingOrBalancing = true;
        try
        {
            if (model.ContrastLow > 0) ToolInstance.ContrastLow = model.ContrastLow;
            if (model.ContrastHigh > 0) ToolInstance.ContrastHigh = model.ContrastHigh;
            if (model.MinContrast > 0) ToolInstance.MinContrast = model.MinContrast;
            if (model.MinComponentSize > 0) ToolInstance.MinComponentSize = model.MinComponentSize;
            if (model.NumLevels > 0) ToolInstance.PyramidLevels = model.NumLevels;
            _lastSyncedModel = model;
            if (notifyUi) NotifyBalancedParametersChanged();
        }
        finally
        {
            _isSyncingOrBalancing = false;
        }
    }

    public void MarkModelSynced()
    {
        _lastSyncedModel = ToolInstance.Model;
    }

    public ShapeMatching.ContrastEstimationResult AutoEstimateParameters(Mat? imgMat = null, BaseRoi? roi = null)
        => AutoEstimateParametersCore(imgMat, roi, notifyUi: true);

    private ShapeMatching.ContrastEstimationResult AutoEstimateParametersCore(Mat? imgMat, BaseRoi? roi, bool notifyUi)
    {
        if (roi == null && HasTrainRoi)
            roi = new RectRoi((int)Math.Round(TrainRoiX), (int)Math.Round(TrainRoiY), (int)Math.Round(TrainRoiWidth), (int)Math.Round(TrainRoiHeight));

        _isSyncingOrBalancing = true;
        try
        {
            if (imgMat != null && !imgMat.Empty())
            {
                var res = ToolInstance.AutoEstimateParameters(imgMat, roi);
                if (notifyUi) NotifyBalancedParametersChanged();
                return res;
            }
            if (!string.IsNullOrEmpty(TemplateImagePath) && File.Exists(TemplateImagePath))
            {
                using var diskMat = Cv2.ImRead(TemplateImagePath, ImreadModes.Color);
                var res = ToolInstance.AutoEstimateParameters(diskMat, roi);
                if (notifyUi) NotifyBalancedParametersChanged();
                return res;
            }
            return new ShapeMatching.ContrastEstimationResult(9, 39, 52, 16, 4, 1.5f);
        }
        finally
        {
            _isSyncingOrBalancing = false;
        }
    }

    public ShapeModel? Model
    {
        get => ToolInstance.Model;
        set
        {
            if (!ReferenceEquals(ToolInstance.Model, value))
            {
                ToolInstance.Model = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModelStatusText));
            }
        }
    }

    public string ModelStatusText => ToolInstance.Model != null
        ? $"Ready: {ToolInstance.Model.Levels.FirstOrDefault()?.Features.Count ?? 0} features, {ToolInstance.Model.NumLevels} levels"
        : (!string.IsNullOrEmpty(ModelPath) ? $"Configured: {Path.GetFileName(ModelPath)}" : "NO MODEL (Load .shm or wire template)");

    public float MinScore
    {
        get => ToolInstance.MinScore;
        set
        {
            if (Math.Abs(ToolInstance.MinScore - value) > 1e-6f)
            {
                ToolInstance.MinScore = value;
                OnPropertyChanged();
            }
        }
    }

    public float Greediness
    {
        get => ToolInstance.Greediness;
        set
        {
            if (Math.Abs(ToolInstance.Greediness - value) > 1e-6f)
            {
                ToolInstance.Greediness = value;
                OnPropertyChanged();
            }
        }
    }

    public float AngleMinDeg
    {
        get => ToolInstance.AngleMinDeg;
        set
        {
            if (Math.Abs(ToolInstance.AngleMinDeg - value) > 1e-6f)
            {
                ToolInstance.AngleMinDeg = value;
                OnPropertyChanged();
            }
        }
    }

    public float AngleMaxDeg
    {
        get => ToolInstance.AngleMaxDeg;
        set
        {
            if (Math.Abs(ToolInstance.AngleMaxDeg - value) > 1e-6f)
            {
                ToolInstance.AngleMaxDeg = value;
                OnPropertyChanged();
            }
        }
    }

    public ContourExtractionMode ContourMode
    {
        get => ToolInstance.ContourMode;
        set
        {
            if (ToolInstance.ContourMode != value)
            {
                ToolInstance.ContourMode = value;
                OnPropertyChanged();
            }
        }
    }

    public int ContrastLow
    {
        get => ToolInstance.ContrastLow;
        set
        {
            int clamped = Math.Clamp(value, 1, 250);
            if (ToolInstance.ContrastLow != clamped)
            {
                ToolInstance.ContrastLow = clamped;
                if (!_isSyncingOrBalancing) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int ContrastHigh
    {
        get => ToolInstance.ContrastHigh;
        set
        {
            int clamped = Math.Clamp(value, 2, 255);
            if (ToolInstance.ContrastHigh != clamped)
            {
                ToolInstance.ContrastHigh = clamped;
                if (!_isSyncingOrBalancing) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int PyramidLevels
    {
        get => ToolInstance.PyramidLevels;
        set
        {
            int clamped = Math.Clamp(value, 0, 6);
            if (ToolInstance.PyramidLevels != clamped)
            {
                ToolInstance.PyramidLevels = clamped;
                if (!_isSyncingOrBalancing) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    private bool _useTiledSearch = false;
    public bool UseTiledSearch
    {
        get => _useTiledSearch;
        set
        {
            if (_useTiledSearch != value)
            {
                _useTiledSearch = value;
                OnPropertyChanged();
            }
        }
    }

    public int MinContrast
    {
        get => ToolInstance.MinContrast;
        set
        {
            int clamped = Math.Clamp(value, 1, 250);
            if (ToolInstance.MinContrast != clamped)
            {
                ToolInstance.MinContrast = clamped;
                if (!_isSyncingOrBalancing) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int MinComponentSize
    {
        get => ToolInstance.MinComponentSize;
        set
        {
            int clamped = Math.Clamp(value, 1, 5000);
            if (ToolInstance.MinComponentSize != clamped)
            {
                ToolInstance.MinComponentSize = clamped;
                if (!_isSyncingOrBalancing) _hasManualParamEdit = true;
                OnPropertyChanged();
            }
        }
    }

    public int MinContourLength
    {
        get => ToolInstance.MinLength;
        set
        {
            int clamped = Math.Clamp(value, 1, 5000);
            if (ToolInstance.MinLength != clamped)
            {
                ToolInstance.MinLength = clamped;
                OnPropertyChanged();
            }
        }
    }

    public int MaxInstances
    {
        get => ToolInstance.MaxInstances;
        set
        {
            if (ToolInstance.MaxInstances != value)
            {
                ToolInstance.MaxInstances = value;
                OnPropertyChanged();
            }
        }
    }

    public float MaxOverlap
    {
        get => ToolInstance.MaxOverlap;
        set
        {
            if (Math.Abs(ToolInstance.MaxOverlap - value) > 1e-6f)
            {
                ToolInstance.MaxOverlap = value;
                OnPropertyChanged();
            }
        }
    }

    public float ScaleMin
    {
        get => ToolInstance.ScaleMin;
        set
        {
            if (Math.Abs(ToolInstance.ScaleMin - value) > 1e-6f)
            {
                ToolInstance.ScaleMin = value;
                OnPropertyChanged();
            }
        }
    }

    public float ScaleMax
    {
        get => ToolInstance.ScaleMax;
        set
        {
            if (Math.Abs(ToolInstance.ScaleMax - value) > 1e-6f)
            {
                ToolInstance.ScaleMax = value;
                OnPropertyChanged();
            }
        }
    }

    public int LowestPyramid
    {
        get => ToolInstance.LowestPyramid;
        set
        {
            int clamped = Math.Clamp(value, 1, 10);
            if (ToolInstance.LowestPyramid != clamped)
            {
                ToolInstance.LowestPyramid = clamped;
                OnPropertyChanged();
            }
        }
    }

    public List<MatchResult> LastMatches { get; private set; } = new();

    [JsonIgnore]
    public Mat? RawSceneMat { get; private set; }

    // Integrated Fixture Reference Origin (for "Đặt gốc = kết quả khớp gần nhất")
    public float ReferenceOriginX { get; set; } = 0;
    public float ReferenceOriginY { get; set; } = 0;

    // Optional interactive template ROI drawn directly on the image
    public float TrainRoiX { get; set; } = 0;
    public float TrainRoiY { get; set; } = 0;
    public float TrainRoiWidth { get; set; } = 0;
    public float TrainRoiHeight { get; set; } = 0;
    public bool HasTrainRoi => TrainRoiWidth > 8 && TrainRoiHeight > 8;

    public ShapeMatchNode() : base("Shape Match (MWWZ)", "Detection")
    {
        AddInput("image", "Scene Image", PortKind.Image);
        AddInput("model", "Model In", PortKind.Model);
        AddInput("template", "Template In", PortKind.Image);
        AddInput("roi", "Search ROI", PortKind.Roi);
        AddOutput("pose", "Pose", PortKind.Pose);
        AddOutput("frame", "Frame", PortKind.Fixture);
        AddOutput("score", "Score", PortKind.Score);
        AddOutput("count", "Count", PortKind.Score);
        AddOutput("image", "Overlay", PortKind.Image);
    }

    public void SetOriginFromLastMatch()
    {
        if (LastMatches.Count > 0)
        {
            ReferenceOriginX = LastMatches[0].X;
            ReferenceOriginY = LastMatches[0].Y;
        }
        else if (ToolInstance.Model != null)
        {
            ReferenceOriginX = ToolInstance.Model.OriginX;
            ReferenceOriginY = ToolInstance.Model.OriginY;
        }
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();

        // 1. Resolve Model from input port 'model'
        if (inputs.TryGetValue("model", out var modelObj))
        {
            if (modelObj is ShapeModel wiredModel)
            {
                ToolInstance.Model = wiredModel;
            }
            else if (modelObj is Mat wiredMat && !wiredMat.Empty())
            {
                ToolInstance.Train(wiredMat);
            }
        }
        else if (inputs.TryGetValue("template", out var tplModelObj))
        {
            if (tplModelObj is ShapeModel wiredTplModel)
            {
                ToolInstance.Model = wiredTplModel;
            }
            else if (tplModelObj is Mat wiredTplMat && !wiredTplMat.Empty())
            {
                if (!_hasManualParamEdit)
                {
                    AutoEstimateParametersCore(wiredTplMat, null, notifyUi: false);
                }
                ToolInstance.Train(wiredTplMat);
            }
        }

        // 1b. Graph-level fallback: check if any connected upstream node is TrainShapeModelNode with trained model
        if (ToolInstance.Model == null && context.GlobalVariables.TryGetValue("__graph", out var gObj) && gObj is PipelineGraph graph)
        {
            var upstreamWires = graph.Connections.Where(c => c.ToNodeId == Id && (c.ToPortId == "model" || c.ToPortId == "template")).ToList();
            foreach (var w in upstreamWires)
            {
                var parent = graph.Nodes.FirstOrDefault(n => n.Id == w.FromNodeId);
                if (parent is TrainShapeModelNode trainNode && trainNode.ToolInstance.Model != null)
                {
                    ToolInstance.Model = trainNode.ToolInstance.Model;
                    break;
                }
            }
        }

        // 2. If model not in memory yet, try loading from ModelPath
        if (ToolInstance.Model == null && !string.IsNullOrEmpty(ModelPath))
        {
            if (File.Exists(ModelPath))
            {
                ToolInstance.Model = ModelPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? ShapeModel.LoadJson(ModelPath)
                    : ShapeModel.LoadShm(ModelPath);
            }
            else
            {
                var candidate = Path.Combine(@"d:\tunvision\mwwz-samples\TunVision\Models", Path.GetFileName(ModelPath));
                if (File.Exists(candidate))
                {
                    ToolInstance.Model = ShapeModel.LoadShm(candidate);
                    ModelPath = candidate;
                }
            }
        }

        // 3. If still no model (or explicitly flagged to retrain from ROI), check if a template image was wired or configured
        if (_needsRetrain)
        {
            _needsRetrain = false;
            if (HasTrainRoi)
            {
                ToolInstance.Model = null;
            }
        }

        if (ToolInstance.Model == null)
        {
            Mat? tplMat = null;
            bool ownsTpl = false;
            if (inputs.TryGetValue("template", out var tplObj) && tplObj is Mat wiredTpl && !wiredTpl.Empty())
            {
                tplMat = wiredTpl;
            }
            else if (HasTrainRoi && inputs.TryGetValue("image", out var sceneTrainObj) && sceneTrainObj is Mat sceneTrain && !sceneTrain.Empty())
            {
                tplMat = sceneTrain;
            }
            else if (!string.IsNullOrEmpty(TemplateImagePath) && File.Exists(TemplateImagePath))
            {
                tplMat = Cv2.ImRead(TemplateImagePath, ImreadModes.Color);
                ownsTpl = true;
            }

            if (tplMat != null && !tplMat.Empty())
            {
                BaseRoi? tRoi = HasTrainRoi
                    ? new RectRoi((int)Math.Round(TrainRoiX), (int)Math.Round(TrainRoiY), (int)Math.Round(TrainRoiWidth), (int)Math.Round(TrainRoiHeight))
                    : null;
                if (!_hasManualParamEdit)
                {
                    AutoEstimateParametersCore(tplMat, tRoi, notifyUi: false);
                }
                ToolInstance.Train(tplMat, tRoi);
                if (ownsTpl) tplMat.Dispose();
            }
        }

        if (ToolInstance.Model != null && !ReferenceEquals(_lastSyncedModel, ToolInstance.Model) && !_hasManualParamEdit)
        {
            SyncFromModel(ToolInstance.Model, notifyUi: false);
        }

        // 4. If STILL NO MODEL: Do not guess or train on the search image! Fail clearly!
        if (ToolInstance.Model == null)
        {
            Mat? rawIn = inputs.TryGetValue("image", out var imgFallback) && imgFallback is Mat mFallback && !mFallback.Empty()
                ? mFallback
                : null;
            RawSceneMat = rawIn == null
                ? null
                : (rawIn.Channels() == 1 ? rawIn.CvtColor(ColorConversionCodes.GRAY2BGR) : rawIn.Clone());
            if (rawIn != null && !rawIn.Empty())
            {
                int sceneKey = TrainShapeModelNode.ComputeImageKey(rawIn);
                if (_lastSceneImageKey != sceneKey)
                {
                    _lastSceneImageKey = sceneKey;
                    if (!_hasManualParamEdit)
                    {
                        AutoEstimateParametersCore(rawIn, null, notifyUi: false);
                    }
                }
            }
            LastMatches.Clear();
            LastResult = "ERROR: No Model loaded. Please load .shm or wire template image.";
            Status = "No Model";
            return Task.FromResult(new Dictionary<string, object?>
            {
                ["pose"] = null,
                ["frame"] = FixtureTransform.Identity,
                ["matches"] = LastMatches,
                ["score"] = 0.0,
                ["count"] = 0.0,
                ["image"] = inputs.TryGetValue("image", out var i) && i is Mat m ? m : null
            });
        }

        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi roi)
            {
                ToolInstance.SearchRoi = roi;
            }
            else
            {
                ToolInstance.SearchRoi = null;
            }

            List<MatchResult> matches;
            if (UseTiledSearch && ToolInstance.Model != null)
            {
                int tilesX = Math.Max(1, (int)Math.Ceiling(srcMat.Width / 2048.0));
                int tilesY = Math.Max(1, (int)Math.Ceiling(srcMat.Height / 2048.0));
                if (tilesX == 1 && tilesY == 1 && (srcMat.Width > 1024 || srcMat.Height > 1024))
                {
                    tilesX = 2;
                    tilesY = 2;
                }
                matches = ShapeMatching.Acceleration.TiledImageMatcher.MatchTiled(
                    srcMat,
                    ToolInstance.Model,
                    tilesX,
                    tilesY,
                    AngleMinDeg,
                    AngleMaxDeg,
                    ToolInstance.AngleStepDeg,
                    ScaleMin,
                    ScaleMax,
                    ToolInstance.ScaleStep,
                    MinScore,
                    Greediness,
                    MaxInstances,
                    MaxOverlap,
                    ToolInstance.Subpixel,
                    LowestPyramid);
            }
            else
            {
                var result = ToolInstance.Run(srcMat) as AlignResult;
                matches = result?.Matches ?? new List<MatchResult>();
            }
            LastMatches = matches;
            var bestMatch = matches.FirstOrDefault();

            float refX = ReferenceOriginX != 0 || ReferenceOriginY != 0
                ? ReferenceOriginX
                : (ToolInstance.Model?.OriginX ?? 0);
            float refY = ReferenceOriginX != 0 || ReferenceOriginY != 0
                ? ReferenceOriginY
                : (ToolInstance.Model?.OriginY ?? 0);

            var frame = bestMatch != null
                ? new FixtureTransform(bestMatch.X, bestMatch.Y, bestMatch.AngleDeg, bestMatch.Scale, refX, refY)
                : FixtureTransform.Identity;

            // R3: Classify inliers vs outliers against scene gradient
            if (ToolInstance.Model != null && matches.Count > 0)
            {
                Mat sceneGray = srcMat.Channels() == 1 ? srcMat : srcMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                bool disposeGray = srcMat.Channels() != 1;
                try
                {
                    float minContrast = MinContrast > 0
                        ? MinContrast
                        : (ToolInstance.Model.MinContrast > 0 ? ToolInstance.Model.MinContrast : 9f);

                    foreach (var match in matches)
                    {
                        if (match.FeaturePoints.Count == 0)
                        {
                            ShapeVisualizer.ClassifyMatchFeatures(match, ToolInstance.Model, sceneGray, minContrast);
                        }
                    }
                }
                finally
                {
                    if (disposeGray) sceneGray.Dispose();
                }
            }

            Mat rawBgr = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            RawSceneMat = rawBgr;
            Mat overlay = rawBgr.Clone();
            DrawOverlay(overlay);

            LastOutputMat = overlay;
            RecordMeasurement("count", matches.Count, "F0");
            if (bestMatch != null)
            {
                RecordMeasurement("score", bestMatch.Score, "F4");
                RecordMeasurement("x", bestMatch.X, "F2", "px");
                RecordMeasurement("y", bestMatch.Y, "F2", "px");
                RecordMeasurement("angle", bestMatch.AngleDeg, "F2", "°");
                RecordMeasurement("scale", bestMatch.Scale, "F3");
                RecordMeasurement("inliers", bestMatch.InlierCount, "F0");
                RecordMeasurement("outliers", bestMatch.OutlierCount, "F0");
                RecordMeasurement("inlier_ratio", bestMatch.InlierRatio * 100.0, "F1", "%");
            }
            else
            {
                RecordMeasurement("score", 0.0, "F4");
            }

            LastResult = matches.Count > 0
                ? $"score={bestMatch!.Score:F3}, ({bestMatch.X:F1}, {bestMatch.Y:F1}), θ={bestMatch.AngleDeg:F1}°, n={matches.Count}"
                : "No Match";

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["pose"] = bestMatch,
                ["frame"] = frame,
                ["matches"] = matches,
                ["score"] = (double)(bestMatch?.Score ?? 0),
                ["count"] = (double)matches.Count,
                ["image"] = overlay
            });
        }

        RawSceneMat = null;
        LastMatches.Clear();
        LastResult = "ERROR: Scene Image chưa được nối (nối cổng Image của Load Image vào Scene Image)";
        Status = "No Scene Image";
        return Task.FromResult(new Dictionary<string, object?>
        {
            ["pose"] = null,
            ["frame"] = FixtureTransform.Identity,
            ["matches"] = LastMatches,
            ["score"] = 0.0,
            ["count"] = 0.0,
            ["image"] = null
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (overlay == null || overlay.Empty() || LastMatches.Count == 0) return;

        ShapeVisualizer.DrawMatchOverlay(overlay, LastMatches, ToolInstance.Model);
    }
}

public class FixtureNode : GraphNode
{
    public float ReferenceX { get; set; } = 0;
    public float ReferenceY { get; set; } = 0;

    public FixtureNode() : base("Fixture", "Fixturing")
    {
        AddInput("pose", "Pose", PortKind.Pose);
        AddOutput("fixture", "Fixture", PortKind.Fixture);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        FixtureTransform fix = FixtureTransform.Identity;
        if (inputs.TryGetValue("pose", out var poseObj))
        {
            if (poseObj is MatchResult match)
            {
                fix = new FixtureTransform(match.X, match.Y, match.AngleDeg, match.Scale, ReferenceX, ReferenceY);
                LastResult = $"dx={fix.OffsetX:F1}, dy={fix.OffsetY:F1}, θ={fix.RotationDeg:F1}°";
            }
            else if (poseObj is FixtureTransform t)
            {
                fix = t;
                LastResult = $"dx={fix.OffsetX:F1}, dy={fix.OffsetY:F1}, θ={fix.RotationDeg:F1}°";
            }
        }
        else
        {
            LastResult = "Identity (0, 0, 0°)";
        }

        RecordMeasurement("dx", fix.OffsetX, "F2", "px");
        RecordMeasurement("dy", fix.OffsetY, "F2", "px");
        RecordMeasurement("angle", fix.RotationDeg, "F2", "°");

        return Task.FromResult(new Dictionary<string, object?> { ["fixture"] = fix });
    }
}

/// <summary>
/// CaliperCircleNode (Search Circle): Spoke caliper array around an annulus with Inward/Outward scan
/// and RANSAC/Huber circle fitting (equivalent to mwwz::gen_measure_circle + do_measure + do_fit2).
/// </summary>
public class CaliperCircleNode : GraphNode
{
    public CaliperTool ToolInstance { get; } = new("CircleCaliper") { Mode = CaliperMode.Circle };

    // Base coordinates in reference frame (not mutated by fixture transform!)
    public float CenterX { get; set; } = 240f;
    public float CenterY { get; set; } = 240f;

    public float ExpectedRadius
    {
        get => ToolInstance.CircleRadius;
        set => ToolInstance.CircleRadius = value;
    }

    public float SearchRange
    {
        get => ToolInstance.SearchRange;
        set => ToolInstance.SearchRange = value;
    }

    public int NumSpokes
    {
        get => ToolInstance.NumSpokes;
        set => ToolInstance.NumSpokes = value;
    }

    public CircleScanDirection ScanDirection
    {
        get => ToolInstance.ScanDirection;
        set => ToolInstance.ScanDirection = value;
    }

    public EdgePolarity Polarity
    {
        get => ToolInstance.Polarity;
        set => ToolInstance.Polarity = value;
    }

    public EdgeSelection Selection
    {
        get => ToolInstance.Selection;
        set => ToolInstance.Selection = value;
    }

    public float Threshold
    {
        get => ToolInstance.Threshold;
        set => ToolInstance.Threshold = value;
    }

    public float GaussianSigma
    {
        get => ToolInstance.GaussianSigma;
        set => ToolInstance.GaussianSigma = value;
    }

    public FixtureTransform LastActiveFixture { get; private set; } = FixtureTransform.Identity;
    public Point2f LastEffectiveCenter { get; private set; } = new(240, 240);
    public CaliperResult? LastCaliperResult { get; private set; }

    public CaliperCircleNode() : base("Search Circle (Caliper)", "Measurement")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Frame", PortKind.Fixture);
        AddOutput("measurement", "Radius", PortKind.Measurement);
        AddOutput("diameter", "Diameter", PortKind.Measurement);
        AddOutput("center", "Center", PortKind.Point);
        AddOutput("score", "RMS Error", PortKind.Score);
        AddOutput("image", "Overlay", PortKind.Image);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            var baseCenter = new Point2f(CenterX, CenterY);
            if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
            {
                LastActiveFixture = fix;
                // Legacy compatibility: if ReferenceX==0 && ReferenceY==0 && CenterX==240 && CenterY==240 and OffsetX > 0,
                // snap directly to OffsetX, OffsetY if within reasonable range
                if (fix.ReferenceX == 0 && fix.ReferenceY == 0 && Math.Abs(CenterX - 240f) < 0.1f && Math.Abs(CenterY - 240f) < 0.1f && (fix.OffsetX != 0 || fix.OffsetY != 0))
                {
                    LastEffectiveCenter = new Point2f(fix.OffsetX, fix.OffsetY);
                }
                else
                {
                    LastEffectiveCenter = fix.Transform(baseCenter);
                }
            }
            else
            {
                LastActiveFixture = FixtureTransform.Identity;
                LastEffectiveCenter = baseCenter;
            }

            ToolInstance.CircleCenter = LastEffectiveCenter;
            var res = ToolInstance.Run(srcMat) as CaliperResult;
            LastCaliperResult = res;

            Mat overlay = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            DrawOverlay(overlay);

            LastOutputMat = overlay;
            bool circleOk = res != null && res.FittedRadius.HasValue && res.FittedCircleCenter.HasValue;
            double scale = context.PxToMmScale.MmPerPixel;
            string unit = context.PxToMmScale.UnitName;

            double rVal = (res?.FittedRadius ?? 0) * scale;
            double dVal = rVal * 2.0;
            double cx = res?.FittedCircleCenter?.X ?? 0;
            double cy = res?.FittedCircleCenter?.Y ?? 0;
            var centerPt = res?.FittedCircleCenter ?? LastEffectiveCenter;

            RecordMeasurement("R", rVal, "F2", unit);
            RecordMeasurement("D", dVal, "F2", unit);
            RecordMeasurement("cx", cx, "F2", "px");
            RecordMeasurement("cy", cy, "F2", "px");
            RecordMeasurement("rms", res?.RmsError ?? 0, "F3", "px");
            RecordMeasurement("inliers", res?.InlierCount ?? 0, "F0");

            var meas = new MeasurementResult("Radius", rVal, unit, cx, cy, res?.RmsError ?? 0);
            var diamMeas = new MeasurementResult("Diameter", dVal, unit, cx, cy, res?.RmsError ?? 0);
            LastResult = circleOk
                ? $"R={rVal:F1}{unit} (D={dVal:F1}{unit}), C=({cx:F1}, {cy:F1})"
                : "Fit Failed";

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["measurement"] = meas,
                ["diameter"] = diamMeas,
                ["center"] = centerPt,
                ["score"] = res?.RmsError ?? 999.0,
                ["image"] = overlay
            });
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["measurement"] = new MeasurementResult("Radius", 0),
            ["diameter"] = new MeasurementResult("Diameter", 0),
            ["center"] = new Point2f(0, 0),
            ["score"] = 999.0,
            ["image"] = null
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (LastCaliperResult == null) return;

        // Draw Inlier (Green) and Outlier (Red) edge points (matching Smart Vision video at 00:25)
        foreach (var e in LastCaliperResult.Edges)
        {
            var pt = new Point((int)Math.Round(e.X), (int)Math.Round(e.Y));
            var color = e.IsInlier ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Cv2.Circle(overlay, pt, 3, color, -1, LineTypes.AntiAlias);
        }

        if (LastCaliperResult.FittedRadius.HasValue && LastCaliperResult.FittedCircleCenter.HasValue)
        {
            var center = LastCaliperResult.FittedCircleCenter.Value;
            int r = (int)Math.Round(LastCaliperResult.FittedRadius.Value);
            if (r > 0)
            {
                Cv2.Circle(overlay, new Point((int)Math.Round(center.X), (int)Math.Round(center.Y)), r, new Scalar(0, 255, 80), 2, LineTypes.AntiAlias);
                Cv2.DrawMarker(overlay, new Point((int)Math.Round(center.X), (int)Math.Round(center.Y)), new Scalar(0, 220, 255), MarkerTypes.Cross, 14, 2);
            }
        }
    }
}

/// <summary>
/// SearchLineNode (Search Line): 2D Rake Caliper along a rotatable line strip with RANSAC line fit
/// (equivalent to mwwz::gen_measure_line + do_measure + do_fit2 + ppw_to_rr).
/// </summary>
public class SearchLineNode : GraphNode
{
    public CaliperTool ToolInstance { get; } = new("SearchLine") { Mode = CaliperMode.SearchLine };

    public float StartX { get; set; } = 120f;
    public float StartY { get; set; } = 150f;
    public float EndX { get; set; } = 320f;
    public float EndY { get; set; } = 150f;

    public float SearchHalfWidth
    {
        get => ToolInstance.ProjectionLength;
        set => ToolInstance.ProjectionLength = value;
    }

    public int NumCalipers
    {
        get => ToolInstance.NumSpokes;
        set => ToolInstance.NumSpokes = value;
    }

    public EdgePolarity Polarity
    {
        get => ToolInstance.Polarity;
        set => ToolInstance.Polarity = value;
    }

    public EdgeSelection Selection
    {
        get => ToolInstance.Selection;
        set => ToolInstance.Selection = value;
    }

    public float Threshold
    {
        get => ToolInstance.Threshold;
        set => ToolInstance.Threshold = value;
    }

    public FixtureTransform LastActiveFixture { get; private set; } = FixtureTransform.Identity;
    public Point2f LastEffectiveStart { get; private set; } = new(120, 150);
    public Point2f LastEffectiveEnd { get; private set; } = new(320, 150);
    public CaliperResult? LastCaliperResult { get; private set; }

    public SearchLineNode() : base("Search Line", "Measurement")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Frame", PortKind.Fixture);
        AddOutput("image", "Overlay", PortKind.Image);
        AddOutput("midpoint", "MidPoint", PortKind.Point);
        AddOutput("angle", "Angle", PortKind.Measurement);
        AddOutput("length", "Length", PortKind.Measurement);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            var p1 = new Point2f(StartX, StartY);
            var p2 = new Point2f(EndX, EndY);
            if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
            {
                LastActiveFixture = fix;
                LastEffectiveStart = fix.Transform(p1);
                LastEffectiveEnd = fix.Transform(p2);
            }
            else
            {
                LastActiveFixture = FixtureTransform.Identity;
                LastEffectiveStart = p1;
                LastEffectiveEnd = p2;
            }

            ToolInstance.LineStart = LastEffectiveStart;
            ToolInstance.LineEnd = LastEffectiveEnd;
            var res = ToolInstance.Run(srcMat) as CaliperResult;
            LastCaliperResult = res;

            Mat overlay = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            DrawOverlay(overlay);
            LastOutputMat = overlay;

            var mid = res?.FittedMidPoint ?? new Point2f((LastEffectiveStart.X + LastEffectiveEnd.X) * 0.5f, (LastEffectiveStart.Y + LastEffectiveEnd.Y) * 0.5f);
            double angle = res?.FittedAngleDeg ?? 0;
            double len = (res?.Distance ?? 0) * context.PxToMmScale.MmPerPixel;
            string unit = context.PxToMmScale.UnitName;

            RecordMeasurement("angle", angle, "F2", "°");
            RecordMeasurement("length", len, "F2", unit);
            RecordMeasurement("mid_x", mid.X, "F2", "px");
            RecordMeasurement("mid_y", mid.Y, "F2", "px");
            RecordMeasurement("rms", res?.RmsError ?? 0, "F3", "px");
            RecordMeasurement("inliers", res?.InlierCount ?? 0, "F0");

            LastResult = res != null && res.Success
                ? $"θ={angle:F1}°, mid=({mid.X:F1}, {mid.Y:F1}), RMS={res.RmsError:F2}"
                : "Line Fit Failed";

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["image"] = overlay,
                ["midpoint"] = mid,
                ["angle"] = new MeasurementResult("Angle", angle, "°", mid.X, mid.Y),
                ["length"] = new MeasurementResult("Length", len, unit, mid.X, mid.Y)
            });
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["image"] = null,
            ["midpoint"] = new Point2f(0, 0),
            ["angle"] = new MeasurementResult("Angle", 0, "°"),
            ["length"] = new MeasurementResult("Length", 0, "px")
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (LastCaliperResult == null) return;

        foreach (var e in LastCaliperResult.Edges)
        {
            var pt = new Point((int)Math.Round(e.X), (int)Math.Round(e.Y));
            Cv2.Circle(overlay, pt, 3, e.IsInlier ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
        }

        if (LastCaliperResult.FittedPoint1.HasValue && LastCaliperResult.FittedPoint2.HasValue)
        {
            var p1 = LastCaliperResult.FittedPoint1.Value;
            var p2 = LastCaliperResult.FittedPoint2.Value;
            Cv2.Line(overlay,
                new Point((int)Math.Round(p1.X), (int)Math.Round(p1.Y)),
                new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)),
                new Scalar(0, 255, 100), 2, LineTypes.AntiAlias);

            if (LastCaliperResult.FittedMidPoint.HasValue)
            {
                var mid = LastCaliperResult.FittedMidPoint.Value;
                Cv2.DrawMarker(overlay, new Point((int)Math.Round(mid.X), (int)Math.Round(mid.Y)), new Scalar(0, 230, 255), MarkerTypes.Cross, 12, 2);
            }
        }
    }
}

/// <summary>
/// SearchCornerNode (Search Corner): Finds the intersection point and included angle of two perpendicular/angled edges.
/// </summary>
public class SearchCornerNode : GraphNode
{
    public CaliperTool ToolInstance { get; } = new("SearchCorner") { Mode = CaliperMode.SearchCorner };

    public float Line1StartX { get; set; } = 160f;
    public float Line1StartY { get; set; } = 120f;
    public float Line1EndX { get; set; } = 280f;
    public float Line1EndY { get; set; } = 120f;

    public float Line2StartX { get; set; } = 140f;
    public float Line2StartY { get; set; } = 140f;
    public float Line2EndX { get; set; } = 140f;
    public float Line2EndY { get; set; } = 260f;

    public float SearchHalfWidth
    {
        get => ToolInstance.ProjectionLength;
        set
        {
            ToolInstance.ProjectionLength = value;
            ToolInstance.Line2ProjectionLength = value;
        }
    }

    public int NumCalipers
    {
        get => ToolInstance.NumSpokes;
        set => ToolInstance.NumSpokes = value;
    }

    public EdgePolarity Polarity
    {
        get => ToolInstance.Polarity;
        set => ToolInstance.Polarity = value;
    }

    public float Threshold
    {
        get => ToolInstance.Threshold;
        set => ToolInstance.Threshold = value;
    }

    public FixtureTransform LastActiveFixture { get; private set; } = FixtureTransform.Identity;
    public Point2f LastEffL1Start { get; private set; }
    public Point2f LastEffL1End { get; private set; }
    public Point2f LastEffL2Start { get; private set; }
    public Point2f LastEffL2End { get; private set; }
    public CaliperResult? LastCaliperResult { get; private set; }

    public SearchCornerNode() : base("Search Corner", "Measurement")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("fixture", "Frame", PortKind.Fixture);
        AddOutput("image", "Overlay", PortKind.Image);
        AddOutput("corner", "Corner", PortKind.Point);
        AddOutput("angle", "Angle", PortKind.Measurement);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            var a1 = new Point2f(Line1StartX, Line1StartY);
            var a2 = new Point2f(Line1EndX, Line1EndY);
            var b1 = new Point2f(Line2StartX, Line2StartY);
            var b2 = new Point2f(Line2EndX, Line2EndY);

            if (inputs.TryGetValue("fixture", out var fixObj) && fixObj is FixtureTransform fix)
            {
                LastActiveFixture = fix;
                LastEffL1Start = fix.Transform(a1);
                LastEffL1End = fix.Transform(a2);
                LastEffL2Start = fix.Transform(b1);
                LastEffL2End = fix.Transform(b2);
            }
            else
            {
                LastActiveFixture = FixtureTransform.Identity;
                LastEffL1Start = a1;
                LastEffL1End = a2;
                LastEffL2Start = b1;
                LastEffL2End = b2;
            }

            ToolInstance.LineStart = LastEffL1Start;
            ToolInstance.LineEnd = LastEffL1End;
            ToolInstance.Line2Start = LastEffL2Start;
            ToolInstance.Line2End = LastEffL2End;

            var res = ToolInstance.Run(srcMat) as CaliperResult;
            LastCaliperResult = res;

            Mat overlay = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            DrawOverlay(overlay);
            LastOutputMat = overlay;

            var corner = res?.CornerPoint ?? new Point2f(0, 0);
            double angle = res?.IncludedAngleDeg ?? 0;

            RecordMeasurement("x", corner.X, "F2", "px");
            RecordMeasurement("y", corner.Y, "F2", "px");
            RecordMeasurement("angle", angle, "F2", "°");
            RecordMeasurement("inliers", res?.InlierCount ?? 0, "F0");

            LastResult = res != null && res.Success
                ? $"({corner.X:F1}, {corner.Y:F1}), ∠={angle:F1}°"
                : "Corner Failed";

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["image"] = overlay,
                ["corner"] = corner,
                ["angle"] = new MeasurementResult("CornerAngle", angle, "°", corner.X, corner.Y)
            });
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["image"] = null,
            ["corner"] = new Point2f(0, 0),
            ["angle"] = new MeasurementResult("CornerAngle", 0, "°")
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (LastCaliperResult == null) return;

        foreach (var e in LastCaliperResult.Edges)
        {
            var pt = new Point((int)Math.Round(e.X), (int)Math.Round(e.Y));
            Cv2.Circle(overlay, pt, 2, e.IsInlier ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
        }

        if (LastCaliperResult.CornerPoint.HasValue)
        {
            var c = LastCaliperResult.CornerPoint.Value;
            var cPt = new Point((int)Math.Round(c.X), (int)Math.Round(c.Y));

            if (LastCaliperResult.FittedPoint1.HasValue && LastCaliperResult.FittedPoint2.HasValue)
            {
                var p2 = LastCaliperResult.FittedPoint2.Value;
                Cv2.Line(overlay, cPt, new Point((int)Math.Round(p2.X), (int)Math.Round(p2.Y)), new Scalar(0, 255, 80), 2, LineTypes.AntiAlias);
            }
            if (LastCaliperResult.Line2Point1.HasValue && LastCaliperResult.Line2Point2.HasValue)
            {
                var q2 = LastCaliperResult.Line2Point2.Value;
                Cv2.Line(overlay, cPt, new Point((int)Math.Round(q2.X), (int)Math.Round(q2.Y)), new Scalar(0, 255, 80), 2, LineTypes.AntiAlias);
            }

            Cv2.DrawMarker(overlay, cPt, new Scalar(0, 215, 255), MarkerTypes.Cross, 16, 2);
            Cv2.Circle(overlay, cPt, 4, new Scalar(0, 0, 255), -1, LineTypes.AntiAlias);
        }
    }
}

/// <summary>
/// PointToPointNode (Khoảng cách điểm–điểm): Computes distance, |dX|, |dY|, and angle between Point A and Point B,
/// rendering a high-contrast dimension line and pill badge on the composite image.
/// </summary>
public class PointToPointNode : GraphNode
{
    public Point2f LastPointA { get; private set; }
    public Point2f LastPointB { get; private set; }
    public double LastDist { get; private set; }
    public double LastDx { get; private set; }
    public double LastDy { get; private set; }
    public double LastAngleDeg { get; private set; }
    public string LastUnit { get; private set; } = "px";
    public bool HasValidPoints { get; private set; }

    public PointToPointNode() : base("Point-to-Point Distance", "Measurement")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("point_a", "Point A", PortKind.Point);
        AddInput("point_b", "Point B", PortKind.Point);
        AddOutput("image", "Overlay", PortKind.Image);
        AddOutput("dist", "dist", PortKind.Measurement);
        AddOutput("dx", "ΔX", PortKind.Measurement);
        AddOutput("dy", "ΔY", PortKind.Measurement);
        AddOutput("angle", "angle", PortKind.Measurement);
    }

    private static bool TryExtractPoint(object? obj, out Point2f pt)
    {
        if (obj is Point2f p2f) { pt = p2f; return true; }
        if (obj is MatchResult m) { pt = new Point2f(m.X, m.Y); return true; }
        if (obj is List<MatchResult> list && list.Count > 0) { pt = new Point2f(list[0].X, list[0].Y); return true; }
        if (obj is MeasurementResult mr && (mr.CenterX != 0 || mr.CenterY != 0))
        {
            pt = new Point2f((float)mr.CenterX, (float)mr.CenterY);
            return true;
        }
        if (obj is FixtureTransform f) { pt = new Point2f(f.OffsetX, f.OffsetY); return true; }
        pt = default;
        return false;
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        inputs.TryGetValue("point_a", out var objA);
        inputs.TryGetValue("point_b", out var objB);

        bool okA = TryExtractPoint(objA, out var ptA);
        bool okB = TryExtractPoint(objB, out var ptB);
        HasValidPoints = okA && okB;

        double scale = context.PxToMmScale.MmPerPixel;
        LastUnit = context.PxToMmScale.UnitName;

        if (HasValidPoints)
        {
            LastPointA = ptA;
            LastPointB = ptB;
            CaliperTool.ComputePointToPoint(ptA, ptB, out double rawDist, out double rawDx, out double rawDy, out double angle);
            LastDist = rawDist * scale;
            LastDx = rawDx * scale;
            LastDy = rawDy * scale;
            LastAngleDeg = angle;

            RecordMeasurement("dist", LastDist, "F2", LastUnit);
            RecordMeasurement("dx", LastDx, "F2", LastUnit);
            RecordMeasurement("dy", LastDy, "F2", LastUnit);
            RecordMeasurement("angle", LastAngleDeg, "F2", "°");

            LastResult = $"{LastDist:F1} {LastUnit} (ΔX={LastDx:F1}, ΔY={LastDy:F1})";
        }
        else
        {
            LastDist = LastDx = LastDy = LastAngleDeg = 0;
            LastResult = okA ? "Chưa có Point B (cổng Point B chưa nối hoặc node phía trước chưa tìm thấy điểm)"
                : okB ? "Chưa có Point A (cổng Point A chưa nối hoặc node phía trước chưa tìm thấy điểm)"
                : "Chưa có Point A và Point B (kiểm tra node phía trước có ra kết quả không)";
        }

        Mat? overlay = null;
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            overlay = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            DrawOverlay(overlay);
        }
        LastOutputMat = overlay;

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["image"] = overlay,
            ["dist"] = new MeasurementResult("dist", LastDist, LastUnit, LastPointA.X, LastPointA.Y),
            ["dx"] = new MeasurementResult("dx", LastDx, LastUnit, LastPointA.X, LastPointA.Y),
            ["dy"] = new MeasurementResult("dy", LastDy, LastUnit, LastPointA.X, LastPointA.Y),
            ["angle"] = new MeasurementResult("angle", LastAngleDeg, "°", LastPointA.X, LastPointA.Y)
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (!HasValidPoints) return;

        var pA = new Point((int)Math.Round(LastPointA.X), (int)Math.Round(LastPointA.Y));
        var pB = new Point((int)Math.Round(LastPointB.X), (int)Math.Round(LastPointB.Y));

        Cv2.Line(overlay, pA, pB, new Scalar(0, 255, 100), 2, LineTypes.AntiAlias);
        Cv2.Circle(overlay, pA, 4, new Scalar(0, 230, 255), -1, LineTypes.AntiAlias);
        Cv2.Circle(overlay, pB, 4, new Scalar(0, 230, 255), -1, LineTypes.AntiAlias);

        string label = $"{LastDist:F2} {LastUnit}";
        int baseline = 0;
        var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.48, 1, out baseline);
        var mid = new Point((pA.X + pB.X) / 2 - textSize.Width / 2, (pA.Y + pB.Y) / 2 - 8);

        Cv2.Rectangle(overlay,
            new Point(mid.X - 5, mid.Y - textSize.Height - 4),
            new Point(mid.X + textSize.Width + 5, mid.Y + baseline + 4),
            new Scalar(15, 15, 15), -1);
        Cv2.Rectangle(overlay,
            new Point(mid.X - 5, mid.Y - textSize.Height - 4),
            new Point(mid.X + textSize.Width + 5, mid.Y + baseline + 4),
            new Scalar(0, 215, 255), 1);
        Cv2.PutText(overlay, label, mid, HersheyFonts.HersheySimplex, 0.48, new Scalar(0, 235, 255), 1, LineTypes.AntiAlias);
    }
}

public class BlobDetectNode : GraphNode
{
    public BlobTool ToolInstance { get; } = new("BlobDetector");
    public BlobResult? LastBlobResult { get; private set; }

    public BlobDetectNode() : base("Blob Detect", "Blob")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("roi", "ROI", PortKind.Roi);
        AddOutput("blob_list", "Blobs", PortKind.BlobList);
        AddOutput("count", "Count", PortKind.Score);
        AddOutput("image", "Overlay", PortKind.Image);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        if (inputs.TryGetValue("image", out var imgObj) && imgObj is Mat srcMat && !srcMat.Empty())
        {
            if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi roi)
                ToolInstance.SearchRoi = roi;

            var res = ToolInstance.Run(srcMat) as BlobResult;
            LastBlobResult = res;

            Mat overlay = srcMat.Channels() == 1 ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR) : srcMat.Clone();
            DrawOverlay(overlay);

            LastOutputMat = overlay;
            int count = res?.Blobs.Count ?? 0;
            double totalArea = res?.Blobs.Sum(b => b.Area) ?? 0;

            RecordMeasurement("count", count, "F0");
            RecordMeasurement("total_area", totalArea, "F0", "px²");
            if (res != null && res.Blobs.Count > 0)
            {
                RecordMeasurement("max_area", res.Blobs.Max(b => b.Area), "F0", "px²");
            }

            LastResult = $"Blobs Found: {count}";

            return Task.FromResult(new Dictionary<string, object?>
            {
                ["blob_list"] = res,
                ["count"] = (double)count,
                ["image"] = overlay
            });
        }

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["blob_list"] = null,
            ["count"] = 0.0,
            ["image"] = null
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (LastBlobResult == null) return;
        foreach (var b in LastBlobResult.Blobs)
        {
            Cv2.Rectangle(overlay, b.BoundingBox, new Scalar(255, 200, 0), 2);
            Cv2.DrawMarker(overlay, new Point((int)b.Centroid.X, (int)b.Centroid.Y), new Scalar(0, 255, 255), MarkerTypes.Cross, 10, 2);
        }
    }
}

public class ToleranceRuleItem
{
    public bool Enabled { get; set; } = true;
    public string NodeId { get; set; } = string.Empty;
    public string MetricKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double Nominal { get; set; } = 100.0;
    public double MinusTolerance { get; set; } = 5.0;
    public double PlusTolerance { get; set; } = 5.0;
    public double ActualValue { get; set; } = 0.0;
    public bool Passed { get; set; } = true;

    public double MinLimit => Nominal - Math.Abs(MinusTolerance);
    public double MaxLimit => Nominal + Math.Abs(PlusTolerance);
}

public class ToleranceVerdictNode : GraphNode
{
    public double Nominal { get; set; } = 50.0;
    public double MinTolerance { get; set; } = 48.0;
    public double MaxTolerance { get; set; } = 52.0;

    // Multi-row tolerance inspection table (matching Smart Vision "Dung sai Pass/Fail" dialog!)
    public List<ToleranceRuleItem> Rules { get; set; } = new();

    [JsonIgnore]
    public VerdictResult? LastVerdict { get; private set; }

    public ToleranceVerdictNode() : base("Tolerance Verdict", "Verdict")
    {
        AddInput("score", "Value / Meas", PortKind.Score);
        AddInput("val2", "Meas 2", PortKind.Score);
        AddInput("val3", "Meas 3", PortKind.Score);
        AddOutput("verdict", "Verdict", PortKind.Verdict);
    }

    /// <summary>
    /// Automatically populates tolerance rules from the latest measured values of all upstream nodes in the pipeline
    /// ("Lấy danh nghĩa từ lần đo gần nhất" button in video at 00:54).
    /// </summary>
    public void CaptureNominalsFromGraph(PipelineGraph graph, double defaultTolerance = 5.0)
    {
        var newRules = new List<ToleranceRuleItem>();
        foreach (var node in graph.GetExecutionOrder())
        {
            if (node == this || node is SourceImageNode || node is DisplayOutputNode || node is TrainShapeModelNode || node is FixtureNode)
                continue;

            foreach (var kv in node.NumericMeasurements)
            {
                // Skip secondary administrative fields unless primary
                if (kv.Key is "levels" or "contours" or "inliers" or "scale") continue;

                string ruleName = $"{node.Title}.{kv.Key}";
                double tol = kv.Key == "score" ? 0.15 : (kv.Key == "count" ? 0.5 : defaultTolerance);
                var existing = Rules.FirstOrDefault(r => r.NodeId == node.Id && r.MetricKey == kv.Key);

                newRules.Add(new ToleranceRuleItem
                {
                    Enabled = existing?.Enabled ?? true,
                    NodeId = node.Id,
                    MetricKey = kv.Key,
                    DisplayName = ruleName,
                    Nominal = Math.Round(kv.Value, 2),
                    MinusTolerance = existing?.MinusTolerance ?? tol,
                    PlusTolerance = existing?.PlusTolerance ?? tol,
                    ActualValue = Math.Round(kv.Value, 2),
                    Passed = true
                });
            }
        }

        if (newRules.Count > 0)
        {
            Rules = newRules;
        }
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();

        // Mode 1: Multi-row Tolerance Table if Rules are configured
        var activeRules = Rules.Where(r => r.Enabled).ToList();
        if (activeRules.Count > 0 && context.GlobalVariables.TryGetValue("__graph", out var gObj) && gObj is PipelineGraph graph)
        {
            int passCount = 0;
            foreach (var rule in activeRules)
            {
                var targetNode = graph.Nodes.FirstOrDefault(n => n.Id == rule.NodeId);
                if (targetNode != null && targetNode.NumericMeasurements.TryGetValue(rule.MetricKey, out double actual))
                {
                    rule.ActualValue = Math.Round(actual, 2);
                    rule.Passed = actual >= rule.MinLimit && actual <= rule.MaxLimit;
                }
                else
                {
                    rule.Passed = false;
                }

                if (rule.Passed) passCount++;
                RecordMeasurement(rule.DisplayName, rule.ActualValue, "F2", rule.Passed ? "OK" : "NG");
            }

            bool allPass = passCount == activeRules.Count;
            string msg = $"{passCount}/{activeRules.Count} đạt";
            var multiVerdict = new VerdictResult(
                allPass ? VerdictState.Pass : VerdictState.Defect,
                msg,
                passCount, activeRules.Count, activeRules.Count);

            LastVerdict = multiVerdict;
            Status = allPass ? "OK" : "NG";
            LastResult = msg;
            return Task.FromResult(new Dictionary<string, object?> { ["verdict"] = multiVerdict });
        }

        // Mode 2: Single wired value check
        double val = 0;
        if (inputs.TryGetValue("score", out var obj))
        {
            if (obj is double d) val = d;
            else if (obj is float f) val = f;
            else if (obj is MeasurementResult m) val = m.Value;
        }

        bool pass = val >= MinTolerance && val <= MaxTolerance;
        var verdict = new VerdictResult(
            pass ? VerdictState.Pass : VerdictState.Defect,
            pass ? $"Value {val:F2} is within [{MinTolerance:F2}, {MaxTolerance:F2}]"
                 : $"Value {val:F2} EXCEEDS limits [{MinTolerance:F2}, {MaxTolerance:F2}]",
            val, MinTolerance, MaxTolerance);

        RecordMeasurement("value", val, "F2");
        RecordMeasurement("min", MinTolerance, "F2");
        RecordMeasurement("max", MaxTolerance, "F2");
        LastVerdict = verdict;
        Status = pass ? "OK" : "NG";
        LastResult = verdict.ToString();
        return Task.FromResult(new Dictionary<string, object?> { ["verdict"] = verdict });
    }
}

/// <summary>
/// CalibrationNode (Hiệu chuẩn px→mm): Sets global pixel-to-millimeter conversion factor for downstream measurements.
/// Supports interactive 2-point ruler line on the image: measures pixel length and divides KnownRealLength (mm) / PixelDistance (px).
/// </summary>
public class CalibrationNode : GraphNode
{
    private float _point1X = 150f;
    private float _point1Y = 200f;
    private float _point2X = 250f;
    private float _point2Y = 200f;
    private double _knownRealLength = 5.0;
    private double _mmPerPixel = 0.05;
    private string _unitName = "mm";
    private bool _enabled = true;
    private bool _isUpdatingScale = false;

    public float Point1X
    {
        get => _point1X;
        set
        {
            if (Math.Abs(_point1X - value) > 1e-4f)
            {
                _point1X = value;
                RecalculateScaleFromLine();
                OnPropertyChanged();
            }
        }
    }

    public float Point1Y
    {
        get => _point1Y;
        set
        {
            if (Math.Abs(_point1Y - value) > 1e-4f)
            {
                _point1Y = value;
                RecalculateScaleFromLine();
                OnPropertyChanged();
            }
        }
    }

    public float Point2X
    {
        get => _point2X;
        set
        {
            if (Math.Abs(_point2X - value) > 1e-4f)
            {
                _point2X = value;
                RecalculateScaleFromLine();
                OnPropertyChanged();
            }
        }
    }

    public float Point2Y
    {
        get => _point2Y;
        set
        {
            if (Math.Abs(_point2Y - value) > 1e-4f)
            {
                _point2Y = value;
                RecalculateScaleFromLine();
                OnPropertyChanged();
            }
        }
    }

    public double PixelDistance
    {
        get
        {
            double dx = _point2X - _point1X;
            double dy = _point2Y - _point1Y;
            return Math.Round(Math.Sqrt(dx * dx + dy * dy), 2);
        }
    }

    public double KnownRealLength
    {
        get => _knownRealLength;
        set
        {
            double clamped = Math.Max(0.0001, value);
            if (Math.Abs(_knownRealLength - clamped) > 1e-6)
            {
                _knownRealLength = clamped;
                RecalculateScaleFromLine();
                OnPropertyChanged();
            }
        }
    }

    public double MmPerPixel
    {
        get => _mmPerPixel;
        set
        {
            double clamped = Math.Max(1e-7, value);
            if (Math.Abs(_mmPerPixel - clamped) > 1e-8)
            {
                _mmPerPixel = clamped;
                if (!_isUpdatingScale && PixelDistance > 0.1)
                {
                    _isUpdatingScale = true;
                    _knownRealLength = Math.Round(_mmPerPixel * PixelDistance, 4);
                    OnPropertyChanged(nameof(KnownRealLength));
                    _isUpdatingScale = false;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationFormulaText));
            }
        }
    }

    public string UnitName
    {
        get => _unitName;
        set
        {
            if (_unitName != value)
            {
                _unitName = string.IsNullOrWhiteSpace(value) ? "mm" : value.Trim();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalibrationFormulaText));
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string CalibrationFormulaText =>
        $"{PixelDistance:F2} px  =  {KnownRealLength:F3} {UnitName}   ⇒   1 px = {MmPerPixel:F6} {UnitName}";

    public void SetLinePoints(float x1, float y1, float x2, float y2)
    {
        _point1X = x1;
        _point1Y = y1;
        _point2X = x2;
        _point2Y = y2;
        RecalculateScaleFromLine();
        OnPropertyChanged(nameof(Point1X));
        OnPropertyChanged(nameof(Point1Y));
        OnPropertyChanged(nameof(Point2X));
        OnPropertyChanged(nameof(Point2Y));
    }

    private void RecalculateScaleFromLine()
    {
        if (_isUpdatingScale) return;
        _isUpdatingScale = true;
        double px = PixelDistance;
        if (px > 0.1 && _knownRealLength > 0)
        {
            _mmPerPixel = Math.Round(_knownRealLength / px, 6);
        }
        OnPropertyChanged(nameof(PixelDistance));
        OnPropertyChanged(nameof(MmPerPixel));
        OnPropertyChanged(nameof(CalibrationFormulaText));
        _isUpdatingScale = false;
    }

    public CalibrationNode() : base("Calibration px→mm", "Measurement")
    {
        AddInput("image", "Image", PortKind.Image);
        AddOutput("image", "Image", PortKind.Image);
        AddOutput("scale", "Scale", PortKind.Score);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        if (Enabled && MmPerPixel > 0)
        {
            context.PxToMmScale = new doubleMmScale(MmPerPixel, UnitName);
        }
        else
        {
            context.PxToMmScale = new doubleMmScale(1.0, "px");
        }

        RecordMeasurement("px_dist", PixelDistance, "F2", "px");
        RecordMeasurement("real_len", KnownRealLength, "F3", UnitName);
        RecordMeasurement("mm_per_px", MmPerPixel, "F5", UnitName);
        LastResult = Enabled
            ? $"{PixelDistance:F1}px = {KnownRealLength:F2}{UnitName} (1px={MmPerPixel:F4}{UnitName})"
            : "Disabled (px)";
        inputs.TryGetValue("image", out var img);
        if (img is Mat m) LastOutputMat = m;

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["image"] = img,
            ["scale"] = MmPerPixel
        });
    }
}

/// <summary>
/// CsvExportNode (Ghi kết quả CSV): Appends inspection verdicts and measurements to a CSV log file.
/// </summary>
public class CsvExportNode : GraphNode
{
    public string CsvFilePath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;

    public CsvExportNode() : base("CSV Export", "Output")
    {
        AddInput("verdict", "Verdict", PortKind.Verdict);
        AddOutput("verdict", "Verdict", PortKind.Verdict);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        inputs.TryGetValue("verdict", out var vObj);
        var verdict = vObj as VerdictResult;

        if (Enabled && !string.IsNullOrEmpty(CsvFilePath))
        {
            try
            {
                string dir = Path.GetDirectoryName(CsvFilePath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                bool writeHeader = !File.Exists(CsvFilePath);
                using var sw = new StreamWriter(CsvFilePath, append: true);
                if (writeHeader)
                {
                    sw.WriteLine("Timestamp,State,Message,Value");
                }
                sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{verdict?.State ?? VerdictState.None},\"{verdict?.Message ?? ""}\",{verdict?.Value ?? 0:F3}");
                LastResult = $"Logged to {Path.GetFileName(CsvFilePath)}";
            }
            catch (Exception ex)
            {
                LastResult = $"CSV Error: {ex.Message}";
            }
        }
        else
        {
            LastResult = Enabled ? "No CSV Path" : "Standby (Disabled)";
        }

        return Task.FromResult(new Dictionary<string, object?> { ["verdict"] = verdict });
    }
}

/// <summary>
/// FileRouteNode (Chuyển file OK/NG): Routes or copies inspected images into OK / NG output folders.
/// </summary>
public class FileRouteNode : GraphNode
{
    public string OutputBaseFolder { get; set; } = string.Empty;
    public bool Enabled { get; set; } = false;

    public FileRouteNode() : base("Route OK/NG File", "Output")
    {
        AddInput("image", "Image", PortKind.Image);
        AddInput("verdict", "Verdict", PortKind.Verdict);
        AddOutput("verdict", "Verdict", PortKind.Verdict);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        inputs.TryGetValue("verdict", out var vObj);
        var verdict = vObj as VerdictResult;

        if (Enabled && !string.IsNullOrEmpty(OutputBaseFolder) && inputs.TryGetValue("image", out var imgObj) && imgObj is Mat mat && !mat.Empty())
        {
            try
            {
                string sub = verdict?.State == VerdictState.Pass ? "OK" : "NG";
                string targetDir = Path.Combine(OutputBaseFolder, sub);
                Directory.CreateDirectory(targetDir);
                string fileName = $"inspect_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                Cv2.ImWrite(Path.Combine(targetDir, fileName), mat);
                LastResult = $"Saved to {sub}\\{fileName}";
            }
            catch (Exception ex)
            {
                LastResult = $"Route Error: {ex.Message}";
            }
        }
        else
        {
            LastResult = Enabled ? "No Output Folder" : "Standby (Disabled)";
        }

        return Task.FromResult(new Dictionary<string, object?> { ["verdict"] = verdict });
    }
}

public class DisplayOutputNode : GraphNode
{
    public DisplayOutputNode() : base("Display Output", "Output")
    {
        AddInput("value", "Value In", PortKind.Any);
        AddOutput("value", "Value Out", PortKind.Any);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();
        inputs.TryGetValue("value", out var val);
        if (val is Mat m)
            LastOutputMat = m;

        LastResult = val != null ? $"Output: {val}" : "No Data";
        RecordTextMeasurement("output", LastResult.ToString() ?? "");
        return Task.FromResult(new Dictionary<string, object?> { ["value"] = val });
    }
}

public enum TextMatchRule
{
    None,
    Equals,
    Contains,
    StartsWith,
    RegEx
}

public abstract class CodeReaderNodeBase : GraphNode
{
    private CodeKind _expectedKind = CodeKind.AutoDetect;
    public CodeKind ExpectedKind
    {
        get => _expectedKind;
        set { if (_expectedKind != value) { _expectedKind = value; OnPropertyChanged(); } }
    }

    private bool _tryHarder = true;
    public bool TryHarder
    {
        get => _tryHarder;
        set { if (_tryHarder != value) { _tryHarder = value; OnPropertyChanged(); } }
    }

    private bool _tryInverted = true;
    public bool TryInverted
    {
        get => _tryInverted;
        set { if (_tryInverted != value) { _tryInverted = value; OnPropertyChanged(); } }
    }

    private string _expectedText = string.Empty;
    public string ExpectedText
    {
        get => _expectedText;
        set { if (_expectedText != value) { _expectedText = value; OnPropertyChanged(); } }
    }

    private TextMatchRule _matchRule = TextMatchRule.None;
    public TextMatchRule MatchRule
    {
        get => _matchRule;
        set { if (_matchRule != value) { _matchRule = value; OnPropertyChanged(); } }
    }

    [JsonIgnore]
    public CodeReadResult? LastCodeResult { get; private set; }

    [JsonIgnore]
    public bool IsMatchPassed { get; private set; } = true;

    protected CodeReaderNodeBase(string title, CodeKind defaultKind) : base(title, "Code")
    {
        _expectedKind = defaultKind;
        AddInput("image", "Image", PortKind.Image);
        AddInput("roi", "ROI", PortKind.Roi);

        AddOutput("image", "Overlay", PortKind.Image);
        AddOutput("text", "Text", PortKind.Any);
        AddOutput("format", "Format", PortKind.Any);
        AddOutput("center", "Center", PortKind.Point);
        AddOutput("pose", "Pose", PortKind.Pose);
        AddOutput("frame", "Frame", PortKind.Fixture);
        AddOutput("score", "Score", PortKind.Score);
    }

    public override Task<Dictionary<string, object?>> ExecuteAsync(
        Dictionary<string, object?> inputs,
        GraphExecutionContext context)
    {
        ClearMeasurements();

        if (!inputs.TryGetValue("image", out var imgObj) || imgObj is not Mat srcMat || srcMat.Empty())
        {
            LastOutputMat = null;
            LastResult = "Waiting for Image";
            LastCodeResult = null;
            return Task.FromResult(new Dictionary<string, object?>
            {
                ["image"] = null,
                ["text"] = string.Empty,
                ["format"] = string.Empty,
                ["center"] = new Point2f(0, 0),
                ["pose"] = null,
                ["frame"] = FixtureTransform.Identity,
                ["score"] = 0.0
            });
        }

        Rect? roiRect = null;
        if (inputs.TryGetValue("roi", out var roiObj) && roiObj is BaseRoi roi)
        {
            roiRect = roi.GetBoundingBox();
        }

        var res = BarcodeReadingEngine.ReadCode(srcMat, roiRect, ExpectedKind, TryHarder, TryInverted);
        LastCodeResult = res;

        // Verify with ExpectedText if rule is specified
        IsMatchPassed = true;
        if (res.Success && MatchRule != TextMatchRule.None && !string.IsNullOrEmpty(ExpectedText))
        {
            IsMatchPassed = MatchRule switch
            {
                TextMatchRule.Equals => string.Equals(res.Text, ExpectedText, StringComparison.OrdinalIgnoreCase),
                TextMatchRule.Contains => res.Text.Contains(ExpectedText, StringComparison.OrdinalIgnoreCase),
                TextMatchRule.StartsWith => res.Text.StartsWith(ExpectedText, StringComparison.OrdinalIgnoreCase),
                TextMatchRule.RegEx => System.Text.RegularExpressions.Regex.IsMatch(res.Text, ExpectedText),
                _ => true
            };
        }

        // Record metrics
        RecordTextMeasurement("Nội dung", res.Text);
        RecordTextMeasurement("Định dạng", res.FormatName);
        RecordMeasurement("Tọa độ X", res.Center.X, "F1", "px");
        RecordMeasurement("Tọa độ Y", res.Center.Y, "F1", "px");
        RecordMeasurement("Góc xoay", res.AngleDeg, "F1", "°");
        RecordMeasurement("Thời gian", res.ExecutionTimeMs, "F1", "ms");
        double scoreVal = res.Success ? (IsMatchPassed ? 1.0 : 0.0) : 0.0;
        RecordMeasurement("score", scoreVal, "F0");

        if (res.Success)
        {
            LastResult = IsMatchPassed
                ? $"[{res.FormatName}] {res.Text}"
                : $"NG: [{res.FormatName}] {res.Text} != {ExpectedText}";
            Status = IsMatchPassed ? "OK" : "NG";
        }
        else
        {
            LastResult = "Code Not Found";
            Status = "NG";
        }

        Mat overlay = srcMat.Channels() == 1
            ? srcMat.CvtColor(ColorConversionCodes.GRAY2BGR)
            : srcMat.Clone();
        DrawOverlay(overlay);
        LastOutputMat = overlay;

        var pose = res.Success
            ? new MatchResult { X = res.Center.X, Y = res.Center.Y, AngleDeg = res.AngleDeg, Scale = 1.0f, Score = (float)scoreVal }
            : null;

        var frame = res.Success
            ? new FixtureTransform(res.Center.X, res.Center.Y, res.AngleDeg, 1.0f)
            : FixtureTransform.Identity;

        return Task.FromResult(new Dictionary<string, object?>
        {
            ["image"] = overlay,
            ["text"] = res.Text,
            ["format"] = res.FormatName,
            ["center"] = res.Center,
            ["pose"] = pose,
            ["frame"] = frame,
            ["score"] = scoreVal
        });
    }

    public override void DrawOverlay(Mat overlay)
    {
        if (LastCodeResult == null || !LastCodeResult.Success) return;

        var allItems = new List<CodeReadResult> { LastCodeResult };
        if (LastCodeResult.AdditionalResults.Count > 0)
        {
            allItems.AddRange(LastCodeResult.AdditionalResults);
        }

        foreach (var r in allItems)
        {
            var boxColor = IsMatchPassed ? new Scalar(0, 255, 100) : new Scalar(0, 0, 255);
            var textColor = IsMatchPassed ? new Scalar(255, 255, 255) : new Scalar(100, 100, 255);

            // Draw polygon around code corners
            if (r.Points.Count >= 2)
            {
                var ptArray = r.Points.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
                for (int i = 0; i < ptArray.Length; i++)
                {
                    var p1 = ptArray[i];
                    var p2 = ptArray[(i + 1) % ptArray.Length];
                    Cv2.Line(overlay, p1, p2, boxColor, 2, LineTypes.AntiAlias);
                    Cv2.Circle(overlay, p1, 4, new Scalar(0, 200, 255), -1, LineTypes.AntiAlias);
                }
            }
            else if (r.BoundingBox.Width > 0 && r.BoundingBox.Height > 0)
            {
                var rect = new Rect((int)r.BoundingBox.X, (int)r.BoundingBox.Y, (int)r.BoundingBox.Width, (int)r.BoundingBox.Height);
                Cv2.Rectangle(overlay, rect, boxColor, 2, LineTypes.AntiAlias);
            }

            // Draw center marker
            Cv2.DrawMarker(overlay, new Point((int)Math.Round(r.Center.X), (int)Math.Round(r.Center.Y)), new Scalar(0, 255, 0), MarkerTypes.Cross, 14, 2);

            // Draw text badge
            string badgeText = $"[{r.FormatName}] {r.Text}";
            if (!IsMatchPassed && !string.IsNullOrEmpty(ExpectedText))
            {
                badgeText += $" (NG: != {ExpectedText})";
            }

            var textSize = Cv2.GetTextSize(badgeText, HersheyFonts.HersheySimplex, 0.55, 1, out int baseline);
            int badgeX = Math.Max(2, (int)Math.Round(r.Center.X - textSize.Width * 0.5));
            int badgeY = Math.Max(textSize.Height + 6, (int)Math.Round(r.BoundingBox.Y - 8));

            // Background rect for text
            var bgRect = new Rect(badgeX - 4, badgeY - textSize.Height - 4, textSize.Width + 8, textSize.Height + baseline + 6);
            Cv2.Rectangle(overlay, bgRect, new Scalar(20, 20, 24), -1);
            Cv2.Rectangle(overlay, bgRect, boxColor, 1);

            Cv2.PutText(overlay, badgeText, new Point(badgeX, badgeY), HersheyFonts.HersheySimplex, 0.55, textColor, 1, LineTypes.AntiAlias);
        }
    }
}

public class ScanQrNode : CodeReaderNodeBase
{
    public ScanQrNode() : base("Scan QR", CodeKind.QrCode)
    {
    }
}

public class ScanBarcodeNode : CodeReaderNodeBase
{
    public ScanBarcodeNode() : base("Scan Barcode", CodeKind.Code128)
    {
    }
}
