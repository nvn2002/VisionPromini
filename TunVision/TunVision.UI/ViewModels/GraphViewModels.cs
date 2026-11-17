using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using TunVision.Core.Graph;
using TunVision.UI.Common;

namespace TunVision.UI.ViewModels;

public class PortViewModel : ObservableObject
{
    public NodePort Port { get; }
    public NodeViewModel ParentNode { get; }

    public string Id => Port.Id;
    public string Name => Port.Name;
    public PortKind Kind => Port.Kind;
    public PortDirection Direction => Port.Direction;
    public bool IsInput => Direction == PortDirection.Input;
    public bool IsOutput => Direction == PortDirection.Output;

    public Brush PortBrush { get; }

    private Point _socketPosition;
    public Point SocketPosition
    {
        get => _socketPosition;
        set => SetProperty(ref _socketPosition, value);
    }

    public PortViewModel(NodePort port, NodeViewModel parentNode)
    {
        Port = port;
        ParentNode = parentNode;
        PortBrush = GetBrushForPort(port.Kind);
    }

    public static Brush GetBrushForPort(PortKind kind)
    {
        var brush = kind switch
        {
            PortKind.Image => new SolidColorBrush(Color.FromRgb(0, 255, 136)),
            PortKind.Roi => new SolidColorBrush(Color.FromRgb(255, 165, 0)),
            PortKind.Fixture or PortKind.Pose => new SolidColorBrush(Color.FromRgb(0, 229, 255)),
            PortKind.Point => new SolidColorBrush(Color.FromRgb(255, 110, 255)),
            PortKind.Measurement or PortKind.Score => new SolidColorBrush(Color.FromRgb(255, 215, 0)),
            PortKind.Verdict => new SolidColorBrush(Color.FromRgb(255, 51, 102)),
            PortKind.BlobList => new SolidColorBrush(Color.FromRgb(68, 136, 255)),
            PortKind.Model => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
            _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
        };
        brush.Freeze();
        return brush;
    }
}

public class NodeViewModel : ObservableObject
{
    public GraphNode Node { get; }

    public string Id => Node.Id;
    public string Title => LocalizationManager.Instance.GetNodeTitle(Node);
    public string Category => Node.Category;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    public ObservableCollection<PortViewModel> InputPorts { get; } = new();
    public ObservableCollection<PortViewModel> OutputPorts { get; } = new();
    public ObservableCollection<NodeMeasurementEntry> Measurements { get; } = new();

    public void SyncRuntimeMetrics()
    {
        if (Application.Current?.Dispatcher is { } disp && !disp.CheckAccess())
        {
            disp.BeginInvoke(SyncRuntimeMetrics);
            return;
        }

        ExecutionTimeMs = Node.ExecutionTimeMs;
        Status = Node.Status;
        LastResult = Node.LastResult;
        Measurements.Clear();
        foreach (var m in Node.LastMeasurements)
        {
            Measurements.Add(m);
        }
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(StatusShort));
    }

    private double _x;
    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                Node.PositionX = value;
                UpdatePortPositions();
            }
        }
    }

    private double _y;
    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                Node.PositionY = value;
                UpdatePortPositions();
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private double _executionTimeMs;
    public double ExecutionTimeMs
    {
        get => _executionTimeMs;
        set => SetProperty(ref _executionTimeMs, value);
    }

    private string _status = "OK";
    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusShort));
            }
        }
    }

    public string StatusShort
    {
        get
        {
            if (Node is ToleranceVerdictNode tv)
            {
                if (tv.LastVerdict != null) return tv.LastVerdict.State == VerdictState.Defect ? "NG" : "OK";
                if (tv.LastResult is string s && s.StartsWith("DEFECT", StringComparison.OrdinalIgnoreCase)) return "NG";
                if (tv.Status == "NG") return "NG";
            }
            if (Node is SourceImageNode src)
            {
                string srcStatus = src.Status ?? string.Empty;
                if (srcStatus.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                    srcStatus.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) ||
                    srcStatus == "File Not Found" || srcStatus == "No Scene Image" || srcStatus == "No Image")
                    return "ERR";
                if (src.LastOutputMat != null && !src.LastOutputMat.Empty())
                    return "OK";
                if ((string.IsNullOrEmpty(srcStatus) || srcStatus == "OK" || srcStatus == "Ready") &&
                    !string.IsNullOrEmpty(src.ImagePath) && File.Exists(src.ImagePath))
                    return "OK";
                return "ERR";
            }
            string currentStatus = Node.Status ?? _status;
            if (currentStatus.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                currentStatus.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) ||
                currentStatus == "File Not Found" || currentStatus == "No Scene Image" || currentStatus == "No Image")
                return "ERR";
            if (currentStatus == "NG")
                return "NG";
            return "OK";
        }
    }

    private object? _lastResult;
    public object? LastResult
    {
        get => _lastResult;
        set => SetProperty(ref _lastResult, value);
    }

    public Brush CategoryBrush { get; }

    public string Subtitle => Node switch
    {
        SourceImageNode src => src.FileName,
        ShapeMatchNode sm => sm.LastMatches.Count > 0
            ? $"Match: {sm.LastMatches[0].Score:P0} (n={sm.LastMatches.Count})"
            : (sm.LastResult?.ToString() ?? sm.ModelStatusText),
        CaliperCircleNode cc => cc.LastCaliperResult?.FittedRadius != null
            ? $"R={cc.LastCaliperResult.FittedRadius.Value:F1}px"
            : (cc.LastResult?.ToString() ?? ""),
        ToleranceVerdictNode tv => tv.LastVerdict != null
            ? $"{(tv.LastVerdict.State == VerdictState.Pass ? "PASS" : "DEFECT")}: {tv.LastVerdict.Value:F2}"
            : (tv.LastResult?.ToString() ?? ""),
        SearchCornerNode sc => sc.LastResult?.ToString() ?? "",
        PointToPointNode p2p => p2p.LastResult?.ToString() ?? "",
        BlobDetectNode blob => blob.LastResult?.ToString() ?? "",
        CodeReaderNodeBase code => code.LastResult?.ToString() ?? "",
        _ => Node.LastResult?.ToString() ?? ""
    };

    public double Width => 190;
    public double HeaderHeight => 28;
    public double SubtitleHeight => !string.IsNullOrEmpty(Subtitle) ? 14 : 0;
    public double PortRowHeight => 22;
    public double Height => Math.Max(HeaderHeight + SubtitleHeight + Math.Max(InputPorts.Count, OutputPorts.Count) * PortRowHeight + 14, 76);

    public NodeViewModel(GraphNode node)
    {
        Node = node;
        _x = node.PositionX;
        _y = node.PositionY;
        _executionTimeMs = node.ExecutionTimeMs;
        _status = node.Status;
        _lastResult = node.LastResult;

        CategoryBrush = GetBrushForCategory(node.Category);

        foreach (var inPort in node.Inputs)
            InputPorts.Add(new PortViewModel(inPort, this));
        foreach (var outPort in node.Outputs)
            OutputPorts.Add(new PortViewModel(outPort, this));

        Node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GraphNode.Status) or
                                  nameof(GraphNode.ExecutionTimeMs) or
                                  nameof(GraphNode.LastResult) or
                                  nameof(GraphNode.LastOutputMat))
            {
                SyncRuntimeMetrics();
            }
            else if (e.PropertyName is nameof(SourceImageNode.FileName) or
                                       nameof(SourceImageNode.ImagePath) or
                                       nameof(ShapeMatchNode.ModelStatusText))
            {
                OnPropertyChanged(nameof(Subtitle));
                UpdatePortPositions();
            }
        };

        UpdatePortPositions();
    }

    public void UpdatePortPositions()
    {
        for (int i = 0; i < InputPorts.Count; i++)
        {
            double py = _y + HeaderHeight + SubtitleHeight + 12 + i * PortRowHeight;
            InputPorts[i].SocketPosition = new Point(_x, py);
        }
        for (int i = 0; i < OutputPorts.Count; i++)
        {
            double py = _y + HeaderHeight + SubtitleHeight + 12 + i * PortRowHeight;
            OutputPorts[i].SocketPosition = new Point(_x + Width, py);
        }
    }

    public static Brush GetBrushForCategory(string category)
    {
        var brush = category switch
        {
            "Image" => new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            "ROI" => new SolidColorBrush(Color.FromRgb(230, 81, 0)),
            "Detection" => new SolidColorBrush(Color.FromRgb(21, 101, 192)),
            "Measurement" => new SolidColorBrush(Color.FromRgb(194, 24, 91)),
            "Fixturing" => new SolidColorBrush(Color.FromRgb(0, 131, 143)),
            "Code" => new SolidColorBrush(Color.FromRgb(211, 84, 0)),
            "Blob" => new SolidColorBrush(Color.FromRgb(69, 39, 160)),
            "Verdict" => new SolidColorBrush(Color.FromRgb(216, 67, 21)),
            "Output" => new SolidColorBrush(Color.FromRgb(55, 71, 79)),
            _ => new SolidColorBrush(Color.FromRgb(60, 60, 65))
        };
        brush.Freeze();
        return brush;
    }
}

public class ConnectionViewModel : ObservableObject
{
    public NodeConnection Connection { get; }
    public PortViewModel FromPort { get; }
    public PortViewModel ToPort { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Brush WireBrush => FromPort.PortBrush;

    public ConnectionViewModel(NodeConnection connection, PortViewModel fromPort, PortViewModel toPort)
    {
        Connection = connection;
        FromPort = fromPort;
        ToPort = toPort;
    }
}

public class PaletteItemViewModel : ObservableObject
{
    private readonly string _defaultTitle;
    private readonly string _defaultDescription;

    public string ShortTitle { get; }
    public string Title => LocalizationManager.Instance.GetPaletteItemText(NodeType, _defaultTitle, _defaultDescription).Title;
    public string Category { get; }
    public string GroupKey { get; }
    public string Description => LocalizationManager.Instance.GetPaletteItemText(NodeType, _defaultTitle, _defaultDescription).Description;
    public Type NodeType { get; }
    public Brush CategoryBrush { get; }
    public Geometry IconGeometry { get; }

    public PaletteItemViewModel(string title, string shortTitle, string category, string groupKey, string description, Type nodeType, string iconPathData)
    {
        _defaultTitle = title;
        ShortTitle = shortTitle;
        Category = category;
        GroupKey = groupKey;
        _defaultDescription = description;
        NodeType = nodeType;
        CategoryBrush = NodeViewModel.GetBrushForCategory(category);
        IconGeometry = Geometry.Parse(iconPathData);
        IconGeometry.Freeze();
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}

public class PaletteGroupViewModel : ObservableObject
{
    public string GroupKey { get; }
    private readonly string _titleVi;
    private readonly string _titleEn;

    public string GroupTitle => LocalizationManager.Instance.IsVietnamese ? _titleVi : _titleEn;
    public ObservableCollection<PaletteItemViewModel> Items { get; } = new();
    public int Count => Items.Count;

    public PaletteGroupViewModel(string groupKey, string titleVi, string titleEn)
    {
        GroupKey = groupKey;
        _titleVi = titleVi;
        _titleEn = titleEn;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(GroupTitle));
        foreach (var item in Items)
            item.RefreshLocalization();
    }
}

public record NodePortGuideItem(
    string PortName,
    string TypeBadge,
    Brush PortBrush,
    string Description);

public class NodeGuideInfo
{
    public string Summary { get; init; } = "";
    public List<NodePortGuideItem> Inputs { get; init; } = new();
    public List<NodePortGuideItem> Outputs { get; init; } = new();
    public string WiringExample { get; init; } = "";
    public string UsageTips { get; init; } = "";

    public bool HasNoInputs => Inputs.Count == 0;
}

public static class NodeGuideProvider
{
    private static Brush PortColor(PortKind kind) => PortViewModel.GetBrushForPort(kind);

    public static NodeGuideInfo GetGuide(GraphNode? node, bool isVi = true)
    {
        if (node == null)
        {
            return new NodeGuideInfo
            {
                Summary = isVi ? "Hãy chọn một thẻ trên sơ đồ để xem hướng dẫn chi tiết." : "Select a node on the graph to view its guide."
            };
        }

        return node switch
        {
            SourceImageNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Thẻ khởi nguồn cung cấp ảnh đầu vào (từ 1 file ảnh lẻ hoặc duyệt tuần tự cả thư mục ảnh) cho toàn bộ lưu đồ kiểm tra."
                    : "Root image source node that loads a single image file or steps through an image folder for the pipeline.",
                Inputs = new(),
                Outputs = new()
                {
                    new("Image", isVi ? "Ảnh (Mat)" : "Image (Mat)", PortColor(PortKind.Image),
                        isVi ? "Ảnh màu/xám gốc đọc từ đĩa. Nối cổng này sang cổng [Image] hoặc [Scene Image] của các thẻ định vị, đo đạc, cắt ảnh hoặc tìm vùng Blob."
                             : "Loaded image matrix. Connect this to [Image] or [Scene Image] of downstream vision tools.")
                },
                WiringExample = "Load Image [Image] ➔ Contour Matching [Scene Image]\nLoad Image [Image] ➔ Search Circle [Image]",
                UsageTips = isVi
                    ? "• Nhấn [Chọn ảnh...] để mở 1 ảnh hoặc [Chọn thư mục...] để nạp cả danh sách ảnh.\n• Dùng nút [◀ Ảnh trước] / [Ảnh sau ▶] hoặc [⟳ Chạy liên tục] trên thanh công cụ để duyệt qua từng ảnh trong thư mục."
                    : "• Click [Select Image] or [Select Folder] to load images.\n• Use Prev/Next or Continuous Run on the top toolbar to step through folder images."
            },

            ImageFilterNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tiền xử lý & tăng cường chất lượng ảnh trước khi đưa vào các thẻ định vị và đo đạc: chuyển đổi hệ màu/tách kênh màu (Grayscale, R/G/B, HSV, bản đồ nhiệt), tăng/giảm độ phơi sáng (Exposure), độ tương phản (Contrast), hiệu chỉnh Gamma, cân bằng sáng cục bộ (CLAHE), khử nhiễu/làm nét biên, phân ngưỡng nhị phân và đảo âm bản."
                    : "Preprocesses and enhances images before vision inspection: color space/channel conversion, exposure/brightness, contrast, gamma, CLAHE, spatial filtering (blur/sharpen/morphology/Canny), thresholding, and invert.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh gốc [Bắt buộc]" : "Input Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh gốc cần lọc và chỉnh sáng (nối từ cổng [Image] của thẻ Load Image)."
                             : "Source image to filter (wire from Load Image [Image])."),
                    new("ROI", isVi ? "Vùng lọc [Tùy chọn]" : "ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Nếu nối vùng ROI (từ Search Rectangle / ROI Circle), bộ lọc chỉ tác động bên trong vùng ROI đó. Không nối sẽ xử lý toàn bộ ảnh."
                             : "Optional ROI to restrict filtering to a sub-region.")
                },
                Outputs = new()
                {
                    new("Image", isVi ? "Ảnh đã xử lý" : "Filtered Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh đầu ra sau khi chuyển màu, tăng sáng và lọc nhiễu. Nối cổng này vào [Scene Image] của Contour Matching hoặc [Image] của Search Circle / Search Line / Blob Detect."
                             : "Enhanced output image ready for downstream matching, caliper, or blob tools.")
                },
                WiringExample = "Load Image [Image] ➔ Image Filter [Image] ➔ Contour Matching [Scene Image]\nImage Filter [Image] ➔ Search Circle [Image] & Search Line [Image]",
                UsageTips = isVi
                    ? "• Khi ảnh bị tối hoặc thiếu sáng: kéo thanh Phơi sáng (Exposure) sang dương (+15 đến +40) hoặc tăng Gamma (> 1.2) để làm nổi chi tiết vùng tối.\n• Khi chi tiết kim loại bị lóa sáng không đều: bật [Cân bằng sáng cục bộ (CLAHE)] hoặc tách kênh màu R/G/B/HSV có độ tương phản biên cao nhất.\n• Khi ảnh bị nhiễu hạt làm lệch thước Caliper: chọn bộ lọc GaussianBlur hoặc Bilateral (giữ sắc biên) với cỡ nhân 3 hoặc 5."
                    : "• Increase Exposure or Gamma (> 1.0) to brighten dark images, or enable CLAHE for uneven lighting.\n• Use Bilateral or GaussianBlur to suppress sensor noise before running Caliper or Contour Matching."
            },

            CropNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Cắt nhỏ vùng ảnh theo khung ROI được nối vào, giúp các bước xử lý phía sau chỉ tập trung vào vùng quan tâm."
                    : "Crops the input image to the bounding box of the wired ROI.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh gốc cần cắt (nối từ cổng [Image] của thẻ Load Image)."
                             : "Source image to crop (wire from Load Image [Image])."),
                    new("ROI", isVi ? "Vùng ROI [Bắt buộc]" : "ROI [Required]", PortColor(PortKind.Roi),
                        isVi ? "Vùng giới hạn cắt (nối từ cổng [ROI] của thẻ Search Rectangle hoặc ROI Circle)."
                             : "Region of interest to crop (wire from ROI Rectangle or ROI Circle [ROI]).")
                },
                Outputs = new()
                {
                    new("Image", isVi ? "Ảnh đã cắt" : "Cropped Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh con sau khi cắt theo khung ROI. Có thể nối tiếp sang Blob Detect hoặc các thẻ đo đạc."
                             : "Cropped sub-image ready for downstream inspection nodes.")
                },
                WiringExample = "Load Image [Image] ➔ Crop ROI [Image]\nSearch Rectangle [ROI] ➔ Crop ROI [ROI]\nCrop ROI [Image] ➔ Blob Detect [Image]",
                UsageTips = isVi
                    ? "• Nếu chưa nối cổng [ROI], thẻ sẽ giữ nguyên kích thước ảnh gốc.\n• Kết hợp với Search Rectangle có nối [Fixture] để cắt vùng ảnh bám theo vật thể chuyển động."
                    : "• Wire both [Image] and [ROI] ports to crop the target region dynamically."
            },

            ShapeMatchNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Định vị biên dạng đối tượng tốc độ cao (MWWZ Contour Matching). Tìm vị trí (X, Y), góc xoay (θ), tỉ lệ (Scale) của vật mẫu trên ảnh và xuất hệ trục tọa độ động (Frame) để bù vị trí cho các thước đo Caliper."
                    : "High-speed gradient-direction shape matching (MWWZ). Finds object pose (X, Y, Angle, Scale) and outputs a dynamic coordinate Frame for downstream calipers.",
                Inputs = new()
                {
                    new("Scene Image", isVi ? "Ảnh cần tìm [Bắt buộc]" : "Scene Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh thực tế cần dò tìm vật thể (nối từ cổng [Image] của thẻ Load Image)."
                             : "Input scene image to search (wire from Load Image [Image])."),
                    new("Model In", isVi ? "Model mẫu [Tùy chọn]" : "Model In [Optional]", PortColor(PortKind.Model),
                        isVi ? "Nhận trực tiếp mẫu biên dạng từ cổng [Model Out] của thẻ Train Template (nếu không tạo mẫu trực tiếp trên thẻ này)."
                             : "Receives a trained ShapeModel from Train Template [Model Out]."),
                    new("Template In", isVi ? "Ảnh mẫu [Tùy chọn]" : "Template In [Optional]", PortColor(PortKind.Image),
                        isVi ? "Ảnh mẫu chuẩn để tự động huấn luyện nếu chưa nạp file .shm."
                             : "Optional golden template image to auto-train."),
                    new("Search ROI", isVi ? "Vùng tìm kiếm [Tùy chọn]" : "Search ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Giới hạn chỉ tìm vật thể trong vùng ROI (nối từ cổng [ROI] của Search Rectangle / ROI Circle). Không nối sẽ tìm trên toàn ảnh."
                             : "Restricts the search area to the wired ROI.")
                },
                Outputs = new()
                {
                    new("Pose", isVi ? "Tư thế khớp (Pose)" : "Match Pose", PortColor(PortKind.Pose),
                        isVi ? "Kết quả khớp tốt nhất (X, Y, Góc xoay, Scale). Nối vào cổng [Pose] của thẻ Fixture hoặc cổng [Point A/B] của thẻ Khoảng cách."
                             : "Best match pose (X, Y, Angle, Scale). Wire to Fixture [Pose] or Point-to-Point."),
                    new("Frame", isVi ? "Hệ trục động (Fixture)" : "Coordinate Frame", PortColor(PortKind.Fixture),
                        isVi ? "Hệ tọa độ bám theo vật thể (bù cả độ lệch X, Y và góc xoay θ). Nối trực tiếp vào cổng [Frame] của Search Circle, Search Line, Search Corner!"
                             : "Dynamic fixture transform. Wire directly to [Frame] of Search Circle, Search Line, or Search Corner!"),
                    new("Score", isVi ? "Điểm khớp (0–1)" : "Match Score (0–1)", PortColor(PortKind.Score),
                        isVi ? "Độ giống nhau của mẫu tìm được (0.0 đến 1.0). Có thể nối vào thẻ Tolerance OK/NG."
                             : "Similarity score (0.0 to 1.0). Can be wired to Tolerance OK/NG."),
                    new("Count", isVi ? "Số lượng tìm thấy" : "Match Count", PortColor(PortKind.Score),
                        isVi ? "Số lượng vật thể khớp trên ảnh (tối đa theo thông số Số lượng tìm tối đa)."
                             : "Number of matched instances found."),
                    new("Overlay", isVi ? "Ảnh kèm khung vẽ" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh đã vẽ đường biên xanh lá và khung bao kết quả khớp."
                             : "Image with rendered match contours and bounding boxes.")
                },
                WiringExample = "Load Image [Image] ➔ Contour Matching [Scene Image]\nContour Matching [Frame] ➔ Search Circle [Frame]\nContour Matching [Frame] ➔ Search Line [Frame]",
                UsageTips = isVi
                    ? "• Bước 1: Trên thanh Contour Matching phía trên ảnh, chọn (•) Chọn ROI rồi kéo khung bao quanh vật mẫu trên ảnh.\n• Bước 2: Dùng (•) Xóa cọ hoặc (•) Xóa khung để tẩy bỏ các điểm nhiễu không mong muốn (nhấn Ctrl+Z nếu muốn hoàn tác).\n• Bước 3: Nhấn [Đặt gốc = kết quả khớp gần nhất] để khóa gốc chuẩn cho các thước đo hình học."
                    : "• Select 'Chọn ROI' and drag a box around the object to train a template.\n• Use 'Xóa cọ' or 'Xóa khung' to erase unwanted background contour points."
            },

            TrainShapeModelNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Huấn luyện trước mô hình biên dạng (Shape Model) từ ảnh mẫu chuẩn và xuất mô hình sang thẻ Contour Matching hoặc lưu thành file .shm."
                    : "Trains a reusable ShapeModel from a template image and optional ROI, outputting the model to Contour Matching.",
                Inputs = new()
                {
                    new("Template Img", isVi ? "Ảnh mẫu [Tùy chọn]" : "Template Img [Optional]", PortColor(PortKind.Image),
                        isVi ? "Ảnh chứa vật mẫu chuẩn để trích xuất biên dạng (hoặc chọn trực tiếp đường dẫn file trong tab Thông số)."
                             : "Golden template image to extract contour features from."),
                    new("Model ROI", isVi ? "Vùng lấy mẫu [Tùy chọn]" : "Model ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Vùng ROI bao quanh vật mẫu (nối từ Search Rectangle hoặc vẽ trực tiếp bằng nút Chọn ROI trên ảnh)."
                             : "ROI enclosing the target template pattern.")
                },
                Outputs = new()
                {
                    new("Model Out", isVi ? "Mô hình biên dạng" : "Trained Model", PortColor(PortKind.Model),
                        isVi ? "Đối tượng ShapeModel đã huấn luyện. Nối vào cổng [Model In] của thẻ Contour Matching."
                             : "Trained ShapeModel object. Wire into Contour Matching [Model In]."),
                    new("Contour Img", isVi ? "Ảnh biên dạng" : "Contour Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh hiển thị các đường biên dạng đặc trưng đã trích xuất được."
                             : "Preview image showing extracted model contours.")
                },
                WiringExample = "Train Template [Model Out] ➔ Contour Matching [Model In]",
                UsageTips = isVi
                    ? "• Điều chỉnh Ngưỡng tương phản thấp / cao (Contrast Low / High) và Chiều dài biên tối thiểu để lọc bỏ nhiễu hạt trên bề mặt mẫu."
                    : "• Adjust Contrast Low/High and Min Contour Length to filter out surface noise."
            },

            FixtureNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Chuyển đổi tư thế khớp (Pose: X, Y, Góc xoay) từ thẻ Contour Matching thành hệ trục tọa độ động (Fixture) để dẫn hướng cho các công cụ đo và ROI."
                    : "Converts a matched Pose (X, Y, Angle) into a dynamic Fixture coordinate system for downstream tools.",
                Inputs = new()
                {
                    new("Pose", isVi ? "Tư thế (Pose) [Bắt buộc]" : "Pose [Required]", PortColor(PortKind.Pose),
                        isVi ? "Nhận kết quả vị trí & góc xoay từ cổng [Pose] hoặc [Frame] của thẻ Contour Matching."
                             : "Receives match pose from Contour Matching [Pose].")
                },
                Outputs = new()
                {
                    new("Fixture", isVi ? "Hệ trục động (Fixture)" : "Fixture Transform", PortColor(PortKind.Fixture),
                        isVi ? "Nối vào cổng [Frame] / [Fixture] của Search Circle, Search Line, Search Corner, Search Rectangle hoặc ROI Circle."
                             : "Wire to [Frame] / [Fixture] of geometric measurement or ROI nodes.")
                },
                WiringExample = "Contour Matching [Pose] ➔ Fixture Coordinate [Pose]\nFixture Coordinate [Fixture] ➔ Search Circle [Frame]",
                UsageTips = isVi
                    ? "• Bạn cũng có thể nối trực tiếp cổng [Frame] của thẻ Contour Matching sang các thẻ đo hình học mà không cần qua thẻ Fixture trung gian."
                    : "• You can either use this Fixture node or wire Contour Matching [Frame] directly to caliper nodes."
            },

            CaliperCircleNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Dò tìm và khớp đường tròn chính xác cao bằng vòng thước cặp nan hoa (Spoke Caliper) kết hợp thuật toán lọc nhiễu RANSAC. Đo bán kính (R), đường kính (D) và tọa độ tâm (Center)."
                    : "Fits a high-precision circle using radial spoke calipers and RANSAC outlier rejection. Outputs Radius, Diameter, and Center point.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh đo [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh cần đo đường tròn (nối từ cổng [Image] của Load Image)."
                             : "Input image to measure (wire from Load Image [Image])."),
                    new("Frame", isVi ? "Gốc động [Khuyên dùng]" : "Frame [Recommended]", PortColor(PortKind.Fixture),
                        isVi ? "Hệ trục động từ cổng [Frame] của Contour Matching hoặc [Fixture] của thẻ Fixture. Giúp vòng đo tự động dịch chuyển bám theo chi tiết!"
                             : "Dynamic fixture from Contour Matching [Frame] so the search ring tracks moving parts.")
                },
                Outputs = new()
                {
                    new("Radius", isVi ? "Bán kính R (Số đo)" : "Radius (Measurement)", PortColor(PortKind.Measurement),
                        isVi ? "Giá trị bán kính đường tròn khớp được (px hoặc mm). Nối vào thẻ Tolerance OK/NG nếu muốn kiểm tra đơn lẻ."
                             : "Fitted circle radius (px or mm)."),
                    new("Diameter", isVi ? "Đường kính D (Số đo)" : "Diameter (Measurement)", PortColor(PortKind.Measurement),
                        isVi ? "Giá trị đường kính D = 2×R (px hoặc mm)."
                             : "Fitted circle diameter D = 2*R."),
                    new("Center", isVi ? "Tọa độ Tâm (Point)" : "Center Point (X, Y)", PortColor(PortKind.Point),
                        isVi ? "Tọa độ tâm (X, Y) chính xác dưới mức pixel. Nối vào cổng [Point A] hoặc [Point B] của thẻ Khoảng cách điểm-điểm để đo khoảng cách giữa 2 tâm lỗ!"
                             : "Sub-pixel circle center (X, Y). Wire to Point-to-Point [Point A] or [Point B]!"),
                    new("RMS Error", isVi ? "Sai số khớp (RMS)" : "RMS Fit Error", PortColor(PortKind.Score),
                        isVi ? "Độ lệch chuẩn của các điểm biên so với đường tròn lý tưởng (càng nhỏ càng tròn đều)."
                             : "Root-mean-square fitting error in pixels."),
                    new("Overlay", isVi ? "Ảnh kèm vòng đo" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh vẽ đường tròn khớp (xanh lá), điểm biên hợp lệ (xanh) và điểm nhiễu bị loại (đỏ)."
                             : "Image with fitted circle and inlier/outlier edge points.")
                },
                WiringExample = "Load Image [Image] ➔ Search Circle [Image]\nContour Matching [Frame] ➔ Search Circle [Frame]\nSearch Circle [Center] ➔ Khoảng cách điểm-điểm [Point A]",
                UsageTips = isVi
                    ? "• Trên màn hình ảnh, khi chọn thẻ Search Circle bạn có thể kéo trực tiếp 3 tay nắm:\n  - Ô vuông VÀNG (ở tâm): Di chuyển vị trí tâm vòng dò.\n  - Ô vuông TRẮNG (bên phải): Thay đổi bán kính dự kiến.\n  - Ô vuông CAM (phía trên): Mở rộng/thu hẹp bề rộng vành khăn quét biên.\n• Chọn Hướng quét: Outward (từ tâm phóng ra ngoài — đo lỗ tròn) hoặc Inward (từ ngoài thu vào tâm — đo trụ tròn)."
                    : "• Drag the Gold handle on the image to move the center, White handle to resize radius, and Orange handle to adjust search range."
            },

            SearchLineNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Dò tìm và khớp đoạn thẳng bằng lược thước cặp 2D (Rake Caliper) kết hợp RANSAC. Xuất tọa độ trung điểm (MidPoint), góc nghiêng (Angle) và chiều dài cạnh."
                    : "2D rake caliper with RANSAC line fitting. Outputs MidPoint, Angle, and Length of the detected edge.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh đo [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh cần dò cạnh thẳng (nối từ cổng [Image] của Load Image)."
                             : "Input image to measure (wire from Load Image [Image])."),
                    new("Frame", isVi ? "Gốc động [Khuyên dùng]" : "Frame [Recommended]", PortColor(PortKind.Fixture),
                        isVi ? "Nhận hệ trục động từ cổng [Frame] của Contour Matching để vùng dò tự động xoay và dịch chuyển theo vật thể."
                             : "Dynamic fixture from Contour Matching [Frame] to track part motion and rotation.")
                },
                Outputs = new()
                {
                    new("Overlay", isVi ? "Ảnh kèm đường khớp" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh hiển thị đoạn thẳng khớp được và các điểm biên Inlier/Outlier."
                             : "Image with fitted line segment and edge points."),
                    new("MidPoint", isVi ? "Trung điểm cạnh (Point)" : "MidPoint (Point)", PortColor(PortKind.Point),
                        isVi ? "Tọa độ trung điểm (X, Y) trên đường thẳng tìm được. Nối vào cổng [Point A] hoặc [Point B] của thẻ Khoảng cách điểm-điểm!"
                             : "Midpoint (X, Y) of the fitted line. Wire to Point-to-Point [Point A/B]!"),
                    new("Angle", isVi ? "Góc nghiêng (°)" : "Line Angle (°)", PortColor(PortKind.Measurement),
                        isVi ? "Góc nghiêng của cạnh thẳng tìm được (đơn vị: độ)."
                             : "Fitted line angle in degrees."),
                    new("Length", isVi ? "Chiều dài (px/mm)" : "Line Length", PortColor(PortKind.Measurement),
                        isVi ? "Khoảng cách chiều dài đoạn thẳng khảo sát."
                             : "Measured length of the line segment.")
                },
                WiringExample = "Load Image [Image] ➔ Search Line [Image]\nContour Matching [Frame] ➔ Search Line [Frame]\nSearch Line [MidPoint] ➔ Khoảng cách điểm-điểm [Point B]",
                UsageTips = isVi
                    ? "• Kéo 2 ô vuông VÀNG (điểm đầu) và TRẮNG (điểm cuối) trên ảnh để đặt thước dọc theo mép cạnh cần bắt.\n• Kéo ô vuông CAM ở mũi tên vuông góc giữa đoạn để chỉnh bề rộng vùng quét biên."
                    : "• Drag the Gold (Start) and White (End) handles along the target edge, and the Orange handle to adjust search width."
            },

            SearchCornerNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tìm tọa độ đỉnh góc giao nhau (Corner) và góc kẹp (Angle) giữa 2 cạnh thẳng bằng 2 lược thước cặp RANSAC độc lập."
                    : "Finds the exact intersection vertex (Corner) and included angle between two edges using dual RANSAC line calipers.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh đo [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh cần tìm góc giao điểm (nối từ cổng [Image] của Load Image)."
                             : "Input image (wire from Load Image [Image])."),
                    new("Frame", isVi ? "Gốc động [Khuyên dùng]" : "Frame [Recommended]", PortColor(PortKind.Fixture),
                        isVi ? "Hệ trục động từ cổng [Frame] của Contour Matching giúp 2 nhánh dò cạnh tự động bám theo chi tiết."
                             : "Dynamic fixture from Contour Matching [Frame].")
                },
                Outputs = new()
                {
                    new("Overlay", isVi ? "Ảnh kèm góc giao" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh hiển thị 2 cạnh khớp và dấu cộng tại đỉnh giao điểm."
                             : "Image showing the two fitted edges and their intersection vertex."),
                    new("Corner", isVi ? "Đỉnh góc (Point)" : "Corner Vertex (Point)", PortColor(PortKind.Point),
                        isVi ? "Tọa độ (X, Y) giao điểm của 2 cạnh. Nối vào cổng [Point A] hoặc [Point B] của thẻ Khoảng cách điểm-điểm!"
                             : "Intersection point (X, Y). Wire to Point-to-Point [Point A/B]!"),
                    new("Angle", isVi ? "Góc kẹp (°)" : "Included Angle (°)", PortColor(PortKind.Measurement),
                        isVi ? "Góc mở giữa 2 cạnh thẳng (ví dụ ~90° cho góc vuông)."
                             : "Included angle in degrees between the two fitted edges.")
                },
                WiringExample = "Load Image [Image] ➔ Search Corner [Image]\nContour Matching [Frame] ➔ Search Corner [Frame]\nSearch Corner [Corner] ➔ Khoảng cách điểm-điểm [Point B]",
                UsageTips = isVi
                    ? "• Kéo 4 tay nắm (Vàng/Trắng) của Cạnh 1 và Cạnh 2 trên ảnh đặt dọc theo 2 mép tạo thành góc của chi tiết."
                    : "• Drag the 4 endpoint handles on the image to align Rake 1 and Rake 2 along the two intersecting edges."
            },

            PointToPointNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tính khoảng cách tổng (dist), độ lệch ngang (ΔX), độ lệch dọc (ΔY) và góc nghiêng giữa 2 điểm bất kỳ (Tâm đường tròn, Trung điểm cạnh, Đỉnh góc hoặc Tâm mẫu khớp), đồng thời vẽ đường kích thước trực tiếp lên ảnh."
                    : "Calculates Euclidean distance (dist), ΔX, ΔY, and angle between any two points (Circle Center, Line MidPoint, Corner, or Match Pose).",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh nền [Khuyên dùng]" : "Image [Recommended]", PortColor(PortKind.Image),
                        isVi ? "Ảnh để vẽ đường gióng kích thước và nhãn khoảng cách (nối từ cổng [Image] của Load Image)."
                             : "Image on which the dimension line and distance badge are drawn."),
                    new("Point A", isVi ? "Điểm thứ 1 [Bắt buộc]" : "Point A [Required]", PortColor(PortKind.Point),
                        isVi ? "Nối từ cổng [Center] của Search Circle, [MidPoint] của Search Line, [Corner] của Search Corner hoặc [Pose] của Contour Matching."
                             : "First point: wire from Search Circle [Center], Search Line [MidPoint], or Search Corner [Corner]."),
                    new("Point B", isVi ? "Điểm thứ 2 [Bắt buộc]" : "Point B [Required]", PortColor(PortKind.Point),
                        isVi ? "Nối từ cổng [Center], [MidPoint], [Corner] hoặc [Pose] của một thẻ định vị / đo đạc thứ hai."
                             : "Second point: wire from another geometric tool's point output.")
                },
                Outputs = new()
                {
                    new("Overlay", isVi ? "Ảnh kèm đường đo" : "Dimension Overlay", PortColor(PortKind.Image),
                        isVi ? "Ảnh có vẽ đoạn nối giữa Điểm A – Điểm B và nhãn giá trị khoảng cách."
                             : "Image with rendered point-to-point dimension line."),
                    new("dist", isVi ? "Khoảng cách (px/mm)" : "Distance (px/mm)", PortColor(PortKind.Measurement),
                        isVi ? "Khoảng cách đường chim bay √(ΔX² + ΔY²) giữa Điểm A và Điểm B."
                             : "Euclidean distance between Point A and Point B."),
                    new("ΔX", isVi ? "Độ lệch ngang |ΔX|" : "Horizontal |ΔX|", PortColor(PortKind.Measurement),
                        isVi ? "Khoảng cách chiếu theo phương ngang |Xa - Xb|."
                             : "Horizontal distance |Xa - Xb|."),
                    new("ΔY", isVi ? "Độ lệch dọc |ΔY|" : "Vertical |ΔY|", PortColor(PortKind.Measurement),
                        isVi ? "Khoảng cách chiếu theo phương dọc |Ya - Yb|."
                             : "Vertical distance |Ya - Yb|."),
                    new("angle", isVi ? "Góc nối AB (°)" : "Angle AB (°)", PortColor(PortKind.Measurement),
                        isVi ? "Góc của vectơ nối từ Điểm A sang Điểm B."
                             : "Angle of vector from Point A to Point B.")
                },
                WiringExample = "Search Circle [Center] ➔ Khoảng cách điểm-điểm [Point A]\nSearch Corner [Corner] ➔ Khoảng cách điểm-điểm [Point B]",
                UsageTips = isVi
                    ? "• Các chỉ số dist, dx, dy sẽ tự động được quy đổi sang đơn vị mm nếu trong sơ đồ có bật thẻ [Calibration px→mm].\n• Bấm [Lấy danh nghĩa từ lần đo gần nhất] trong thẻ Tolerance OK/NG để tự động đưa khoảng cách này vào bảng kiểm tra Pass/Fail."
                    : "• Use Calibration px→mm to convert distances from pixels to millimeters automatically."
            },

            ScanQrNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Đọc mã 2D tốc độ cao bằng thư viện ZXing: QR Code, Data Matrix, Aztec, PDF417. Tự động xác định khung bao, 4 góc, tâm tọa độ, giải mã nội dung và so sánh với chuỗi ký tự kỳ vọng."
                    : "High-speed 2D code reader powered by ZXing: QR Code, Data Matrix, Aztec, PDF417. Decodes text, corners, center pose, and validates against expected string.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh chứa mã [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh chứa mã 2D cần đọc (nối từ cổng [Image] của Load Image hoặc Image Filter)."
                             : "Input image containing 2D code (wire from Load Image or Image Filter)."),
                    new("ROI", isVi ? "Vùng giới hạn [Tùy chọn]" : "ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Giới hạn vùng quét mã (từ Search Rectangle). Giúp tăng tốc độ đọc và tránh đọc nhầm mã khác trên ảnh."
                             : "Optional ROI to restrict code search area.")
                },
                Outputs = new()
                {
                    new("Overlay", isVi ? "Ảnh kèm khung đọc mã" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh vẽ viền xanh quanh mã, dấu tâm và nhãn hiển thị nội dung chuỗi đã giải mã."
                             : "Image with drawn code boundary, center marker, and decoded text badge."),
                    new("Text", isVi ? "Chuỗi nội dung (String)" : "Decoded Text", PortColor(PortKind.Any),
                        isVi ? "Nội dung ký tự đọc được từ mã 2D (ví dụ số serial, link URL, mã linh kiện)."
                             : "Decoded text string (serial number, URL, lot code)."),
                    new("Format", isVi ? "Định dạng mã" : "Code Format", PortColor(PortKind.Any),
                        isVi ? "Tên chuẩn mã đọc được: QR_CODE, DATA_MATRIX, AZTEC, PDF_417..."
                             : "Barcode format name: QR_CODE, DATA_MATRIX, AZTEC, PDF_417..."),
                    new("Center", isVi ? "Tọa độ tâm (Point)" : "Center (Point)", PortColor(PortKind.Point),
                        isVi ? "Tọa độ tâm (X, Y) của mã. Có thể nối vào thẻ Khoảng cách điểm-điểm!"
                             : "Center coordinates (X, Y) of the code. Wire to Point-to-Point!"),
                    new("Frame", isVi ? "Hệ trục động (Fixture)" : "Coordinate Frame", PortColor(PortKind.Fixture),
                        isVi ? "Gốc tọa độ bám theo vị trí và góc xoay của mã QR để dẫn hướng cho các thước đo Caliper!"
                             : "Dynamic fixture aligned to the 2D code position and rotation."),
                    new("Score", isVi ? "Trạng thái Đạt (1 / 0)" : "Score (1 / 0)", PortColor(PortKind.Score),
                        isVi ? "1.0 nếu đọc thành công (và khớp chuỗi kỳ vọng), 0.0 nếu không đọc được hoặc sai chuỗi."
                             : "1.0 on successful decode (and match), 0.0 otherwise.")
                },
                WiringExample = "Load Image [Image] ➔ Scan QR [Image] ➔ Tolerance OK/NG [Value / Meas]\nScan QR [Center] ➔ Khoảng cách điểm-điểm [Point A]",
                UsageTips = isVi
                    ? "• Nếu mã khắc laser trên kim loại hoặc chữ trắng nền đen: bật tùy chọn [Đọc mã âm bản (Try Inverted)].\n• Bật [Dò tìm chuyên sâu (Try Harder)] để nhận diện các mã bị mờ hoặc góc nghiêng lớn."
                    : "• Enable 'Try Inverted' for white-on-black or laser etched codes.\n• Enable 'Try Harder' for blurry or tilted codes."
            },

            ScanBarcodeNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Đọc mã vạch 1D tuyến tính bằng ZXing: Code 128, Code 39, Code 93, EAN-13, EAN-8, UPC-A, UPC-E, Codabar, ITF. Xuất nội dung chuỗi ký tự, tọa độ quét và kiểm tra đối chiếu chuỗi."
                    : "Linear 1D barcode scanner using ZXing: Code 128, Code 39, EAN-13, UPC, Codabar, ITF. Decodes text, scan coordinates, and validates expected text.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh chứa mã [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh chứa mã vạch cần đọc (nối từ cổng [Image] của Load Image hoặc Image Filter)."
                             : "Input image containing 1D barcode."),
                    new("ROI", isVi ? "Vùng quét [Tùy chọn]" : "ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Giới hạn quét mã vạch trong khung chữ nhật ROI."
                             : "Optional ROI to restrict barcode scan.")
                },
                Outputs = new()
                {
                    new("Overlay", isVi ? "Ảnh kèm vạch nhận diện" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh hiển thị khung bao vạch, tâm và nhãn chuỗi mã vạch."
                             : "Image with barcode bounding line and decoded text badge."),
                    new("Text", isVi ? "Chuỗi mã vạch (String)" : "Decoded Text", PortColor(PortKind.Any),
                        isVi ? "Chuỗi số hoặc ký tự giải mã được từ các sọc mã vạch."
                             : "Decoded barcode character string."),
                    new("Format", isVi ? "Chuẩn mã vạch" : "Barcode Standard", PortColor(PortKind.Any),
                        isVi ? "Chuẩn mã vạch nhận diện được (CODE_128, EAN_13, CODE_39...)."
                             : "Detected 1D barcode standard."),
                    new("Center", isVi ? "Tọa độ tâm (Point)" : "Center (Point)", PortColor(PortKind.Point),
                        isVi ? "Tọa độ trung tâm của dải mã vạch."
                             : "Center coordinates (X, Y) of the barcode."),
                    new("Score", isVi ? "Trạng thái Đạt (1 / 0)" : "Score (1 / 0)", PortColor(PortKind.Score),
                        isVi ? "1.0 nếu đọc thành công (và khớp chuỗi kỳ vọng), 0.0 nếu không đọc được."
                             : "1.0 on successful decode, 0.0 on failure.")
                },
                WiringExample = "Load Image [Image] ➔ Scan Barcode [Image] ➔ Tolerance OK/NG [Value / Meas]",
                UsageTips = isVi
                    ? "• Nếu mã vạch in dọc hoặc chéo: thuật toán tự động xoay góc quét (AutoRotate) để tìm đường quét tối ưu.\n• Dùng thẻ Image Filter tăng độ tương phản (Contrast) trước khi đọc nếu mã vạch bị mờ trên bao bì."
                    : "• AutoRotate handles vertical and tilted barcodes automatically.\n• Increase Contrast with Image Filter for low-contrast prints."
            },

            RoiRectangleNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tạo vùng quan tâm hình chữ nhật (có hỗ trợ xoay góc) để giới hạn vùng tìm kiếm cho Contour Matching, Blob Detect hoặc cắt ảnh Crop ROI."
                    : "Creates a rectangular or rotatable region of interest (ROI) for downstream nodes.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh tham chiếu [Tùy chọn]" : "Image [Optional]", PortColor(PortKind.Image),
                        isVi ? "Ảnh nền để hiển thị vị trí khung ROI."
                             : "Reference image."),
                    new("Fixture", isVi ? "Gốc động [Tùy chọn]" : "Fixture [Optional]", PortColor(PortKind.Fixture),
                        isVi ? "Nối từ [Frame] / [Fixture] để khung chữ nhật tự động di chuyển và xoay theo chi tiết."
                             : "Optional fixture transform so the ROI tracks the part.")
                },
                Outputs = new()
                {
                    new("ROI", isVi ? "Vùng ROI chữ nhật" : "Rectangle ROI", PortColor(PortKind.Roi),
                        isVi ? "Nối vào cổng [ROI] của Blob Detect, Crop ROI hoặc [Search ROI] của Contour Matching."
                             : "Wire to [ROI] of Blob Detect, Crop ROI, or Contour Matching [Search ROI].")
                },
                WiringExample = "Search Rectangle [ROI] ➔ Blob Detect [ROI]\nSearch Rectangle [ROI] ➔ Crop ROI [ROI]",
                UsageTips = isVi
                    ? "• Kéo ô vuông VÀNG (góc trên-trái) và TRẮNG (góc dưới-phải) trực tiếp trên ảnh để thay đổi vị trí và kích thước khung ROI."
                    : "• Drag the Gold (top-left) and White (bottom-right) handles on the image to resize the ROI."
            },

            RoiCircleNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tạo vùng quan tâm hình tròn (Circle ROI) để giới hạn vùng kiểm tra cho Blob Detect, Contour Matching hoặc Crop ROI."
                    : "Creates a circular region of interest (ROI) for downstream inspection nodes.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh tham chiếu [Tùy chọn]" : "Image [Optional]", PortColor(PortKind.Image),
                        isVi ? "Ảnh nền tham chiếu."
                             : "Reference image."),
                    new("Fixture", isVi ? "Gốc động [Tùy chọn]" : "Fixture [Optional]", PortColor(PortKind.Fixture),
                        isVi ? "Nối từ [Frame] / [Fixture] để tâm vùng tròn tự động dịch chuyển theo chi tiết."
                             : "Optional fixture transform so the circular ROI tracks the part.")
                },
                Outputs = new()
                {
                    new("ROI", isVi ? "Vùng ROI hình tròn" : "Circle ROI", PortColor(PortKind.Roi),
                        isVi ? "Nối vào cổng [ROI] của Blob Detect, Crop ROI hoặc [Search ROI] của Contour Matching."
                             : "Wire to [ROI] of Blob Detect, Crop ROI, or Contour Matching.")
                },
                WiringExample = "ROI Circle [ROI] ➔ Blob Detect [ROI]",
                UsageTips = isVi
                    ? "• Kéo ô vuông VÀNG ở tâm để di chuyển và ô vuông TRẮNG ở mép phải để chỉnh bán kính trực tiếp trên ảnh."
                    : "• Drag the Gold center handle and White radius handle directly on the canvas."
            },

            BlobDetectNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Phân ngưỡng sáng/tối, xử lý hình thái học (Morphology), lấp lỗ thủng và đếm/đo diện tích các đốm vùng (Blobs) như vết bẩn, bavia, lỗ khuyết hoặc chi tiết."
                    : "Performs thresholding, morphology, hole filling, and connected-component blob analysis (count, area, centroids).",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh kiểm tra [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh cần phân tích vùng Blob (nối từ Load Image [Image] hoặc Crop ROI [Image])."
                             : "Input image to segment (wire from Load Image or Crop ROI)."),
                    new("ROI", isVi ? "Vùng giới hạn [Tùy chọn]" : "ROI [Optional]", PortColor(PortKind.Roi),
                        isVi ? "Chỉ tìm Blob bên trong vùng ROI được nối vào (từ Search Rectangle hoặc ROI Circle)."
                             : "Optional ROI to restrict blob segmentation.")
                },
                Outputs = new()
                {
                    new("Blobs", isVi ? "Danh sách Blob" : "Blob List", PortColor(PortKind.BlobList),
                        isVi ? "Tập hợp các vùng Blob tìm được (diện tích, tâm, khung bao)."
                             : "Detected connected components."),
                    new("Count", isVi ? "Số lượng Blob" : "Blob Count", PortColor(PortKind.Score),
                        isVi ? "Tổng số vùng Blob thỏa mãn điều kiện diện tích [MinArea, MaxArea]. Có thể nối vào Tolerance OK/NG."
                             : "Number of filtered blobs found."),
                    new("Overlay", isVi ? "Ảnh đánh dấu Blob" : "Overlay Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh vẽ khung bao và tâm của từng vùng Blob tìm được."
                             : "Image with bounding boxes and centroids drawn on detected blobs.")
                },
                WiringExample = "Load Image [Image] ➔ Blob Detect [Image]\nSearch Rectangle [ROI] ➔ Blob Detect [ROI]\nBlob Detect [Count] ➔ Tolerance OK/NG [Value / Meas]",
                UsageTips = isVi
                    ? "• Bật [Đảo ngược nền (Invert)] nếu muốn bắt các vết đen trên nền sáng.\n• Dùng Diện tích tối thiểu / tối đa để lọc bỏ các hạt bụi nhỏ."
                    : "• Enable 'Invert' when detecting dark defects on a bright background."
            },

            ToleranceVerdictNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Đánh giá tổng hợp đạt / lỗi (OK / NG) cho toàn bộ dây chuyền dựa trên bảng dung sai nhiều thông số (Danh nghĩa ± Dung sai) hoặc giá trị đo nối vào."
                    : "Evaluates overall Pass/Fail (OK/NG) verdict using a multi-row nominal ± tolerance table or wired measurement values.",
                Inputs = new()
                {
                    new("Value / Meas", isVi ? "Số đo 1 [Tùy chọn]" : "Value / Meas [Optional]", PortColor(PortKind.Score),
                        isVi ? "Nhận trực tiếp số đo (Radius, Diameter, dist, Score, Count...) nếu kiểm tra theo chế độ nối dây đơn."
                             : "Receives a primary measurement or score."),
                    new("Meas 2", isVi ? "Số đo 2 [Tùy chọn]" : "Meas 2 [Optional]", PortColor(PortKind.Score),
                        isVi ? "Cổng phụ nhận thêm chỉ số đo thứ 2."
                             : "Secondary measurement input."),
                    new("Meas 3", isVi ? "Số đo 3 [Tùy chọn]" : "Meas 3 [Optional]", PortColor(PortKind.Score),
                        isVi ? "Cổng phụ nhận thêm chỉ số đo thứ 3."
                             : "Tertiary measurement input.")
                },
                Outputs = new()
                {
                    new("Verdict", isVi ? "Phán định OK / NG" : "OK / NG Verdict", PortColor(PortKind.Verdict),
                        isVi ? "Kết quả phán định cuối cùng (Pass = OK xanh lá, Defect = NG đỏ). Nối sang cổng [Verdict] của thẻ CSV Export hoặc Route OK/NG."
                             : "Final Pass/Fail verdict. Wire to CSV Export [Verdict] or Route OK/NG [Verdict].")
                },
                WiringExample = "Search Circle [Radius] ➔ Tolerance OK/NG [Value / Meas]\nTolerance OK/NG [Verdict] ➔ CSV Export [Verdict]\nTolerance OK/NG [Verdict] ➔ Route OK/NG [Verdict]",
                UsageTips = isVi
                    ? "• Cách nhanh nhất: Đặt 1 sản phẩm mẫu chuẩn lên ảnh, bấm Chạy (F5), sau đó mở cài đặt thẻ này và nhấn nút [📥 Lấy danh nghĩa từ lần đo gần nhất]. Phần mềm sẽ tự động thu thập mọi kích thước (R, D, dist, góc, điểm khớp...) vào bảng để kiểm tra tự động!"
                    : "• Click 'Capture Nominals from Last Run' on a golden sample to automatically populate all pipeline measurements into the Pass/Fail table."
            },

            CalibrationNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Hiệu chuẩn và quy đổi đơn vị đo từ Pixel (px) sang Milimét (mm) bằng thước đo đoạn thẳng 2 điểm trực tiếp trên ảnh."
                    : "Calibrates and converts pixel measurements (px) into physical units (mm or um) using an interactive 2-point ruler on the image.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh đi qua [Tùy chọn]" : "Image [Optional]", PortColor(PortKind.Image),
                        isVi ? "Nhận ảnh từ Load Image để đảm bảo thẻ chạy trước các bước đo đạc."
                             : "Pass-through image input.")
                },
                Outputs = new()
                {
                    new("Image", isVi ? "Ảnh chuyển tiếp" : "Pass-through Image", PortColor(PortKind.Image),
                        isVi ? "Ảnh nguyên bản chuyển tiếp sang các thẻ phía sau."
                             : "Unmodified image passed to downstream nodes."),
                    new("Scale", isVi ? "Hệ số mm/px" : "Scale Factor", PortColor(PortKind.Score),
                        isVi ? "Giá trị hệ số quy đổi tự động (mm trên mỗi pixel)."
                             : "Conversion factor (mm per pixel).")
                },
                WiringExample = "Load Image [Image] ➔ Calibration px→mm [Image] ➔ Search Circle [Image]",
                UsageTips = isVi
                    ? "• Bước 1: Trên màn hình ảnh, kéo trực tiếp 2 điểm đầu mút VÀNG và TRẮNG (hoặc bấm nút [📏 Kéo vẽ đoạn thẳng 2 điểm trên ảnh]) đặt vào 2 mép của một cạnh chuẩn mà bạn đã biết kích thước ngoài đời thực.\n• Bước 2: Phần mềm sẽ đo và hiện ngay độ dài đoạn thẳng đó bằng Pixel (ví dụ: 100.00 px).\n• Bước 3: Nhập kích thước thực tế (mm) của đoạn đó vào ô mục 2 (ví dụ: 10 mm) — hệ số quy đổi (mm/px) sẽ tự động được tính và áp dụng cho mọi thẻ đo hình học!"
                    : "• Drag the Gold and White endpoints of the calibration ruler onto a known feature on the image, then enter its real length in mm."
            },

            CsvExportNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tự động ghi nhật ký (Log) thời gian kiểm tra, trạng thái OK/NG và kết quả đo vào cuối file bảng tính .CSV sau mỗi lần chạy."
                    : "Appends inspection timestamp, OK/NG verdict, and summary values to a .CSV log file on every run.",
                Inputs = new()
                {
                    new("Verdict", isVi ? "Phán định [Bắt buộc]" : "Verdict [Required]", PortColor(PortKind.Verdict),
                        isVi ? "Nhận kết quả phán định từ cổng [Verdict] của thẻ Tolerance OK/NG."
                             : "Receives verdict from Tolerance OK/NG [Verdict].")
                },
                Outputs = new()
                {
                    new("Verdict", isVi ? "Phán định chuyển tiếp" : "Pass-through Verdict", PortColor(PortKind.Verdict),
                        isVi ? "Chuyển tiếp kết quả phán định sang thẻ Route OK/NG."
                             : "Forwards the verdict to downstream output nodes.")
                },
                WiringExample = "Tolerance OK/NG [Verdict] ➔ CSV Export [Verdict] ➔ Route OK/NG [Verdict]",
                UsageTips = isVi
                    ? "• Tích chọn [Bật tự động ghi CSV] và nhập đường dẫn file (ví dụ: D:\\log\\ket_qua.csv) để bắt đầu lưu lịch sử sản xuất."
                    : "• Check 'Enable CSV logging' and specify a target .csv file path."
            },

            FileRouteNode => new NodeGuideInfo
            {
                Summary = isVi
                    ? "Tự động phân loại và lưu ảnh kết quả kiểm tra vào 2 thư mục con \\OK và \\NG riêng biệt dựa trên phán định của dây chuyền."
                    : "Automatically saves inspected images into separate \\OK and \\NG subfolders based on the pipeline verdict.",
                Inputs = new()
                {
                    new("Image", isVi ? "Ảnh cần lưu [Bắt buộc]" : "Image [Required]", PortColor(PortKind.Image),
                        isVi ? "Ảnh gốc hoặc ảnh Overlay kết quả cần lưu lại bằng chứng (nối từ Load Image hoặc cổng [Overlay] của các thẻ đo)."
                             : "Image or overlay to save (wire from Load Image or an [Overlay] port)."),
                    new("Verdict", isVi ? "Phán định [Bắt buộc]" : "Verdict [Required]", PortColor(PortKind.Verdict),
                        isVi ? "Nhận kết quả OK/NG từ cổng [Verdict] của thẻ Tolerance OK/NG hoặc CSV Export."
                             : "Receives OK/NG verdict from Tolerance OK/NG.")
                },
                Outputs = new()
                {
                    new("Verdict", isVi ? "Phán định" : "Verdict", PortColor(PortKind.Verdict),
                        isVi ? "Kết quả phán định sau khi đã lưu ảnh."
                             : "Pass-through verdict.")
                },
                WiringExample = "Load Image [Image] ➔ Route OK/NG [Image]\nTolerance OK/NG [Verdict] ➔ Route OK/NG [Verdict]",
                UsageTips = isVi
                    ? "• Khi bật tính năng này và đặt Thư mục gốc (ví dụ: D:\\Output), phần mềm sẽ tự tạo D:\\Output\\OK và D:\\Output\\NG để lưu ảnh theo thời gian thực."
                    : "• Automatically creates OK and NG subdirectories inside the configured base folder."
            },

            _ => new NodeGuideInfo
            {
                Summary = node.Title
            }
        };
    }
}
