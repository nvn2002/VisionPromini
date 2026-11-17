using System;
using TunVision.Core.Graph;

namespace TunVision.UI.Common;

public class LocalizationManager : ObservableObject
{
    public static LocalizationManager Instance { get; } = new();

    private bool _isVietnamese = true; // Default to Vietnamese for convenience, switchable anytime
    public bool IsVietnamese
    {
        get => _isVietnamese;
        set
        {
            if (SetProperty(ref _isVietnamese, value))
            {
                OnPropertyChanged(string.Empty); // Notify all bound properties in XAML
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string CurrentLangCode => _isVietnamese ? "VI" : "EN";
    public event EventHandler? LanguageChanged;

    public void ToggleLanguage() => IsVietnamese = !IsVietnamese;
    public void SetLanguage(string code) => IsVietnamese = string.Equals(code, "VI", StringComparison.OrdinalIgnoreCase);

    // Window & Toolbar
    public string WindowTitle => _isVietnamese
        ? "TunVision Studio - Hệ thống Thị giác Máy tính Kéo thả DAG (MWWZ & Forge Workflow)"
        : "TunVision Studio - Visual Inspection DAG Pipeline (ForgeStation Workflow)";

    public string NewPipelineBtn => _isVietnamese ? "✨ Tạo Pipeline" : "✨ New Pipeline";
    public string MenuBlankTitle => _isVietnamese ? "📄 Bảng trắng (Tự thiết kế từ đầu)" : "📄 Blank Canvas (Empty Pipeline)";
    public string MenuDetectTitle => _isVietnamese ? "🎯 Tool Nhận diện mẫu (Train + Shape Match)" : "🎯 Shape Detection Tool (Train + Match)";
    public string MenuMeasureTitle => _isVietnamese ? "📏 Tool Đo đạc & Kiểm tra (Fixture + Caliper + OK/NG)" : "📏 Metrology & Gauging Tool (Fixture + Caliper)";
    public string MenuMetrologyProTitle => _isVietnamese ? "📐 Tool Đo khoảng cách Điểm-Điểm (Circle + Corner + P2P)" : "📐 Point-to-Point Metrology (Circle + Corner + P2P)";
    public string MenuCodeTitle => _isVietnamese ? "🏷 Tool Đọc mã 1D/2D (ZXing QR & Barcode)" : "🏷 1D/2D Code Reading (ZXing QR & Barcode)";
    public string MenuBlobTitle => _isVietnamese ? "🔵 Tool Đếm & Phân tích vùng (Blob Analysis)" : "🔵 Blob Counting & Analysis Tool";

    public string SaveProjectBtn => _isVietnamese ? "💾 Lưu" : "💾 Save";
    public string OpenProjectBtn => _isVietnamese ? "📂 Mở" : "📂 Open";
    public string ContinuousRunBtn => _isVietnamese ? "⟳ Chạy liên tục" : "⟳ Loop Run";
    public string StopContinuousBtn => _isVietnamese ? "⏹ Dừng" : "⏹ Stop";
    public string DelayMsLabel => _isVietnamese ? "nghỉ" : "delay";

    public string OpenImageBtn => _isVietnamese ? "📂 Mở ảnh" : "📂 Open Image";
    public string QuickSamplesLabel => _isVietnamese ? "Mẫu nhanh:" : "Quick Samples:";
    public string FitGraphBtn => _isVietnamese ? "🔍 Căn giữa" : "🔍 Fit Graph";
    public string DeleteSelectedBtn => _isVietnamese ? "🗑 Xóa thẻ chọn" : "🗑 Delete Selected";
    public string LangSwitchTooltip => _isVietnamese ? "Chuyển ngôn ngữ (Anh / Việt)" : "Switch Language (EN / VI)";

    // Context Action Bar & Interactive Canvas Gizmos
    public string SelectRoiToolBtn => _isVietnamese ? "📐 Chọn ROI tạo mẫu" : "📐 Draw Template ROI";
    public string EraserBrushBtn => _isVietnamese ? "🖌 Xóa cọ" : "🖌 Brush Erase";
    public string BrushSizeLabel => _isVietnamese ? "Cỡ cọ:" : "Size:";
    public string EraserBoxBtn => _isVietnamese ? "⬜ Xóa khung" : "⬜ Box Erase";
    public string UndoEraseBtn => _isVietnamese ? "↩ Hoàn tác (Ctrl+Z)" : "↩ Undo (Ctrl+Z)";
    public string ClearEraseBtn => _isVietnamese ? "🗑 Khôi phục mẫu" : "🗑 Reset Model";
    public string SetOriginFromMatchBtn => _isVietnamese ? "🎯 Đặt gốc = kết quả khớp gần nhất" : "🎯 Set Origin = Last Match";
    public string PanModeBtn => _isVietnamese ? "✋ Kéo ảnh / Sửa ROI" : "✋ Pan / Edit Gizmo";
    public string AutoRerunOnMouseUpLabel => _isVietnamese ? "Tự chạy lại khi thả chuột" : "Auto-run on release";
    public string OnlySelectedOverlayLabel => _isVietnamese ? "Chỉ vẽ overlay node đang chọn" : "Selected node overlay only";

    // Split Bottom Result Tables
    public string ColNodeHeader => "Node";
    public string ColStatusHeader => _isVietnamese ? "KQ" : "Status";
    public string ColTimeHeader => "ms";
    public string ColNoteHeader => _isVietnamese ? "Ghi chú" : "Summary";
    public string ColMetricHeader => _isVietnamese ? "Số đo (node đang chọn)" : "Measurement (selected node)";
    public string ColValueHeader => _isVietnamese ? "Giá trị" : "Value";

    // Multi-Row Tolerance
    public string CaptureNominalsBtn => _isVietnamese ? "🎯 Lấy danh nghĩa từ lần đo gần nhất" : "🎯 Capture Nominals from Last Run";
    public string ClearRulesBtn => _isVietnamese ? "🧹 Xóa bảng (Dùng 1 cổng)" : "🧹 Clear Table (Single Port)";
    public string ColRuleEnabled => _isVietnamese ? "Bật" : "On";
    public string ColRuleName => _isVietnamese ? "Phép đo" : "Metric";
    public string ColRuleNominal => _isVietnamese ? "Danh nghĩa" : "Nominal";
    public string ColRuleMinus => _isVietnamese ? "Dưới (-)" : "Min (-)";
    public string ColRulePlus => _isVietnamese ? "Trên (+)" : "Max (+)";
    public string ColRuleActual => _isVietnamese ? "Thực tế" : "Actual";

    // Left Panel: Module Palette
    public string ModulePaletteHeader => _isVietnamese ? "THƯ VIỆN THẺ CÔNG CỤ" : "MODULE PALETTE";

    // Center Panel: Canvas & Live Preview
    public string DagCanvasTitle => _isVietnamese ? "⚡ Sơ đồ khối DAG" : "⚡ DAG Canvas";
    public string DagCanvasHint => _isVietnamese
        ? "Kéo nền: Di chuyển | Ctrl + Kéo chuột: Bôi đen chọn nhiều thẻ | Lăn chuột: Zoom | Đúp chuột: Căn vừa"
        : "Pan: Drag Empty Space | Ctrl + Drag: Box Select Nodes | Scroll: Zoom | Double-Click: Fit";
    public string LivePreviewHeader => _isVietnamese ? "XEM TRƯỚC & KẾT QUẢ TRỰC TIẾP" : "LIVE PREVIEW & OVERLAY";

    // Right Panel: Result & Run
    public string ResultAndRunHeader => _isVietnamese ? "KẾT QUẢ & ĐIỀU KHIỂN" : "RESULT & RUN";
    public string LiveBtnText => _isVietnamese ? "● Tự động" : "● Live";
    public string RunBtnText => _isVietnamese ? "▶ Chạy (F5)" : "▶ Run (F5)";
    public string TabParametersHeader => _isVietnamese ? "⚙ Tham số thẻ" : "⚙ Parameters";
    public string TabReadoutsHeader => _isVietnamese ? "📊 Bảng kết quả" : "📊 Readouts";

    // Shape Match Inspector
    public string ShapeMatchHeader => _isVietnamese ? "THAM SỐ NHẬN DIỆN MẪU (MWWZ)" : "MWWZ SHAPE MATCH PARAMETERS";
    public string ModelStatusLabel => _isVietnamese ? "Trạng thái Model:" : "Model Status:";
    public string LoadShmBtn => _isVietnamese ? "📂 Tải .shm" : "📂 Load .shm";
    public string SaveShmBtn => _isVietnamese ? "💾 Lưu .shm" : "💾 Save .shm";
    public string TrainCurrentBtn => _isVietnamese ? "🎯 Huấn luyện trên ảnh hiện tại" : "🎯 Train on Current Image";
    public string ModelFilePathLabel => _isVietnamese ? "Đường dẫn file Model (.shm):" : "Model File Path (.shm):";
    public string TemplateImgPathLabel => _isVietnamese ? "Đường dẫn ảnh mẫu (Template):" : "Template Image Path:";
    public string ContourModeLabel => _isVietnamese ? "Chế độ trích xuất biên dạng:" : "Contour Extraction Mode:";
    public string MinContourLenLabel => _isVietnamese ? "Chiều dài biên tối thiểu (px):" : "Min Contour Length (px):";
    public string MinContrastLabel => _isVietnamese ? "Độ tương phản tối thiểu (Min Contrast):" : "Min Contrast:";
    public string MinComponentSizeLabel => _isVietnamese ? "Kích thước thành phần tối thiểu (px):" : "Min Component Size (px):";
    public string MinScoreLabel => _isVietnamese ? "Điểm khớp tối thiểu (0.1 - 1.0):" : "Min Score (0.1 - 1.0):";
    public string GreedinessLabel => _isVietnamese ? "Độ tham lam tốc độ (0.0 - 1.0):" : "Greediness (0.0 - 1.0):";
    public string AngleMinLabel => _isVietnamese ? "Góc nhỏ nhất (°):" : "Angle Min (°):";
    public string AngleMaxLabel => _isVietnamese ? "Góc lớn nhất (°):" : "Angle Max (°):";
    public string ScaleMinLabel => _isVietnamese ? "Tỷ lệ nhỏ nhất:" : "Scale Min:";
    public string ScaleMaxLabel => _isVietnamese ? "Tỷ lệ lớn nhất:" : "Scale Max:";
    public string MaxInstancesLabel => _isVietnamese ? "Số lượng tìm tối đa (0 = tất cả):" : "Max Instances (0 = all):";
    public string MaxOverlapLabel => _isVietnamese ? "Độ chồng lấp (0-1):" : "Max Overlap (0-1):";
    public string MatchesOutputLabel => _isVietnamese ? "Kết quả nhận diện:" : "Matches Output:";

    // Train Shape Model Inspector
    public string TrainShapeHeader => _isVietnamese ? "THAM SỐ HUẤN LUYỆN MODEL (TRAIN)" : "TRAIN SHAPE MODEL PARAMETERS";
    public string ModelTrainStatusLabel => _isVietnamese ? "Trạng thái huấn luyện:" : "Model Training Status:";
    public string TemplateImgFileLabel => _isVietnamese ? "File ảnh mẫu (Template):" : "Template Image File:";
    public string BrowseFileBtn => _isVietnamese ? "📁 Chọn..." : "📁 File...";
    public string ContrastLowLabel => _isVietnamese ? "Tương phản thấp:" : "Contrast Low:";
    public string ContrastHighLabel => _isVietnamese ? "Tương phản cao:" : "Contrast High:";
    public string ExportShmPathLabel => _isVietnamese ? "Đường dẫn xuất Model (.shm):" : "Export Model (.shm) Path:";
    public string BrowseSaveBtn => _isVietnamese ? "💾 Lưu..." : "💾 Browse...";

    // Tolerance Verdict Inspector
    public string ToleranceHeader => _isVietnamese ? "GIỚI HẠN DUNG SAI (PASS / DEFECT)" : "TOLERANCE LIMITS";
    public string NominalValueLabel => _isVietnamese ? "Giá trị chuẩn (Nominal):" : "Nominal Value:";
    public string MinToleranceLabel => _isVietnamese ? "Giới hạn dưới (Min):" : "Min Tolerance Limit:";
    public string MaxToleranceLabel => _isVietnamese ? "Giới hạn trên (Max):" : "Max Tolerance Limit:";

    // Caliper Circle Inspector
    public string CaliperHeader => _isVietnamese ? "THAM SỐ ĐO ĐƯỜNG TRÒN (CALIPER)" : "CALIPER CIRCLE PARAMETERS";
    public string ExpectedRadiusLabel => _isVietnamese ? "Bán kính dự kiến (px):" : "Expected Radius (px):";

    // ROI Rectangle Inspector
    public string RoiRectHeader => _isVietnamese ? "THAM SỐ VÙNG CHỌN (ROI RECTANGLE)" : "ROI RECTANGLE PARAMETERS";
    public string WidthLabel => _isVietnamese ? "Chiều rộng (Width):" : "Width:";
    public string HeightLabel => _isVietnamese ? "Chiều cao (Height):" : "Height:";

    // Source Image Inspector
    public string SourceImgHeader => _isVietnamese ? "THAM SỐ ẢNH ĐẦU VÀO (SOURCE)" : "SOURCE IMAGE PARAMETERS";
    public string SelectedImageLabel => _isVietnamese ? "Ảnh đang chọn:" : "Selected Image:";
    public string SelectImageBtn => _isVietnamese ? "📂 Chọn ảnh..." : "📂 Select Image...";
    public string SelectFolderBtn => _isVietnamese ? "📁 Chọn thư mục..." : "📁 Select Folder...";
    public string PrevBtn => _isVietnamese ? "◀ Trước" : "◀ Prev";
    public string NextBtn => _isVietnamese ? "Tiếp ▶" : "Next ▶";
    public string ImagesInFolderLabel => _isVietnamese ? "Danh sách ảnh trong thư mục (Bấm để chọn):" : "Images in Folder (Click to Select):";

    // Localized Node Title Helper
    public string GetNodeTitle(GraphNode node)
    {
        if (!_isVietnamese) return node.Title;
        return node switch
        {
            SourceImageNode => "Ảnh đầu vào (Source)",
            ImageFilterNode => "Lọc & Chỉnh sáng ảnh (Filter)",
            CropNode => "Cắt vùng ảnh (Crop)",
            RoiRectangleNode => "Vùng chữ nhật (ROI)",
            RoiCircleNode => "Vùng hình tròn (ROI)",
            ShapeMatchNode => "Nhận diện mẫu (MWWZ)",
            TrainShapeModelNode => "Huấn luyện mẫu (Train)",
            FixtureNode => "Gốc tọa độ (Fixture)",
            CaliperCircleNode => "Dò đường tròn (Search Circle)",
            SearchLineNode => "Dò đường thẳng (Search Line)",
            SearchCornerNode => "Dò góc / Giao điểm (Search Corner)",
            PointToPointNode => "Khoảng cách điểm–điểm",
            ScanQrNode => "Đọc mã 2D (Scan QR)",
            ScanBarcodeNode => "Đọc mã vạch 1D (Scan Barcode)",
            BlobDetectNode => "Phân tích vùng (Blob)",
            ToleranceVerdictNode => "Dung sai Pass/Fail",
            CalibrationNode => "Hiệu chuẩn px→mm",
            CsvExportNode => "Ghi kết quả CSV",
            FileRouteNode => "Chuyển file OK/NG",
            DisplayOutputNode => "Hiển thị đầu ra",
            _ => node.Title
        };
    }

    public (string Title, string Description) GetPaletteItemText(Type nodeType, string defaultTitle, string defaultDesc)
    {
        if (!_isVietnamese) return (defaultTitle, defaultDesc);

        if (nodeType == typeof(SourceImageNode))
            return ("Ảnh đầu vào (Source)", "Tải ảnh đơn hoặc danh sách ảnh từ thư mục");
        if (nodeType == typeof(ImageFilterNode))
            return ("Lọc & Chỉnh sáng ảnh (Filter)", "Chuyển hệ màu, tăng phơi sáng, tương phản, gamma, khử nhiễu");
        if (nodeType == typeof(CropNode))
            return ("Cắt vùng ảnh (Crop)", "Cắt ảnh theo vùng ROI được nối vào");
        if (nodeType == typeof(RoiRectangleNode))
            return ("Vùng chữ nhật (ROI)", "Tạo vùng giới hạn hình chữ nhật");
        if (nodeType == typeof(RoiCircleNode))
            return ("Vùng hình tròn (ROI)", "Tạo vùng giới hạn hình tròn");
        if (nodeType == typeof(ShapeMatchNode))
            return ("Nhận diện mẫu (MWWZ)", "Khớp biên dạng, tẩy điểm nhiễu & xuất hệ trục Frame");
        if (nodeType == typeof(TrainShapeModelNode))
            return ("Huấn luyện mẫu (Train)", "Tạo model hình dạng từ ảnh mẫu chuẩn");
        if (nodeType == typeof(FixtureNode))
            return ("Gốc tọa độ (Fixture)", "Bám tọa độ động theo vị trí & góc xoay vật thể");
        if (nodeType == typeof(CaliperCircleNode))
            return ("Dò đường tròn (Search Circle)", "Vành thước đo hướng tâm Inward/Outward + RANSAC");
        if (nodeType == typeof(SearchLineNode))
            return ("Dò đường thẳng (Search Line)", "Dải thước đo Caliper 2D + Khớp đường thẳng RANSAC");
        if (nodeType == typeof(SearchCornerNode))
            return ("Dò góc (Search Corner)", "Tìm giao điểm và góc kẹp giữa 2 cạnh Caliper");
        if (nodeType == typeof(PointToPointNode))
            return ("Khoảng cách điểm–điểm", "Đo khoảng cách dist, ΔX, ΔY, góc giữa 2 điểm");
        if (nodeType == typeof(ScanQrNode))
            return ("Đọc mã 2D (Scan QR)", "Đọc mã QR, Data Matrix, Aztec, PDF417 bằng ZXing");
        if (nodeType == typeof(ScanBarcodeNode))
            return ("Đọc mã vạch 1D (Scan Barcode)", "Đọc mã vạch Code 128, Code 39, EAN, UPC bằng ZXing");
        if (nodeType == typeof(BlobDetectNode))
            return ("Phân tích vùng (Blob)", "Phân ngưỡng, hình thái học và đếm vùng");
        if (nodeType == typeof(ToleranceVerdictNode))
            return ("Dung sai Pass/Fail", "Bảng kiểm tra đa chỉ tiêu + Tự lấy danh nghĩa");
        if (nodeType == typeof(CalibrationNode))
            return ("Hiệu chuẩn px→mm", "Quy đổi đơn vị pixel sang milimet (mm)");
        if (nodeType == typeof(CsvExportNode))
            return ("Ghi kết quả CSV", "Xuất lịch sử kiểm tra và số đo ra file CSV");
        if (nodeType == typeof(FileRouteNode))
            return ("Chuyển file OK/NG", "Tự động phân loại ảnh vào thư mục OK / NG");
        if (nodeType == typeof(DisplayOutputNode))
            return ("Hiển thị đầu ra", "Xuất ảnh kết quả và trạng thái ra màn hình");

        return (defaultTitle, defaultDesc);
    }
}
