# LỘ TRÌNH VÀ THIẾT KẾ KIẾN TRÚC NÂNG CẤP HỆ THỐNG TUNVISION THEO NGUYÊN TẮC THIẾT KẾ SẠCH (CLEAN-ROOM UPGRADE ROADMAP)

**Dự án**: Nâng cấp Hệ sinh thái Thị giác máy tính TunVision Studio (.NET 8 WPF / C#)  
**Tài liệu**: Lộ trình Nâng cấp và Đặc tả Kỹ thuật Thiết kế Sạch (Clean-Room Architecture Roadmap)  
**Mã tài liệu**: `04_tunvision_cleanroom_upgrade_roadmap.md`  
**Đường dẫn lưu trữ**: `d:\tunvision\mwwz-samples\docs\04_tunvision_cleanroom_upgrade_roadmap.md`  
**Ngày phát hành**: 15/11/2026  
**Ngôn ngữ**: Tiếng Việt chuyên ngành Công nghệ phần mềm & Thị giác máy tính công nghiệp  

---

## MỤC LỤC TỔNG QUAN

1. [NGUYÊN TẮC THIẾT KẾ SẠCH VÀ RÀNG BUỘC PHÁP LÝ (CLEAN-ROOM PRINCIPLES)](#1-nguyên-tắc-thiết-kế-sạch-và-ràng-buộc-pháp-lý-clean-room-principles)
   - 1.1. Tôn chỉ Thiết kế Sạch (Clean-Room Mandate)
   - 1.2. Phân lập tuyệt đối giữa Khảo sát Dịch ngược và Cài đặt Mã nguồn
   - 1.3. Bảo toàn tính độc lập mã nguồn mở của TunVision trên nền .NET 8 / C#
2. [ĐẶC TẢ THIẾT KẾ VÀ MÃ NGUỒN NÂNG CẤP CHO 8 LỖ HỔNG CHỨC NĂNG (G1 - G8)](#2-đặc-tả-thiết-kế-và-mã-nguồn-nâng-cấp-cho-8-lỗ-hổng-chức-năng-g1---g8)
   - 2.1. Nâng cấp G1: Bổ sung thanh HUD hiển thị thông số điểm ảnh trên `VisionCanvas.cs`
   - 2.2. Nâng cấp G2: Khung xem trước mô hình mẫu biệt lập trên nền cờ ca-rô (`ModelPreviewControl.xaml`)
   - 2.3. Nâng cấp G3: Bộ nút bấm màu tự động cân bằng tham số trên `NodeSettingsWindow.xaml`
   - 2.4. Nâng cấp G4: Cơ chế xem trước đường biên tương tác thời gian thực (`LiveContrastEdgePreview`)
   - 2.5. Nâng cấp G5: Bộ công cụ tương tác vẽ ROI hình chữ nhật xoay (`RectangleR`) và Đa giác (`Polygon`)
   - 2.6. Nâng cấp G6: Bộ lọc chế độ hiển thị lớp phủ kết quả (`DrawOverlayMode: NoScore / Score / All`)
   - 2.7. Nâng cấp G7: Tham số điều khiển tầng kim tự tháp kết thúc (`LowestPyramid`) trong `AlignTool.cs`
   - 2.8. Nâng cấp G8: Giải thuật phân mảnh ô cờ cho ảnh siêu độ phân giải (`TiledImageMatcher`)
3. [NÂNG CẤP NĂNG LỰC THUẬT TOÁN HẠT NHÂN TRONG TUNVISION.CORE](#3-nâng-cấp-năng-lực-thuật-toán-hạt-nhân-trong-tunvisioncore)
   - 3.1. Tích hợp bộ lọc kích thước thành phần liên thông (`MinComponentSize = 16 px`) vào `TopologicalContourExtractor`
   - 3.2. Nâng cấp 16 kênh Response Maps và bộ tích lũy SIMD `Avx2ScoreAccumulator.cs`
   - 3.3. Cơ chế chẩn đoán chất lượng từng hạt đặc trưng (Tô màu Green/Red dựa trên ngưỡng)
   - 3.4. Kiến trúc chia sẻ kim tự tháp cho so khớp đa mô hình đồng thời (`MultiShapeMatcher`)
4. [KẾ HOẠCH TRIỂN KHAI THEO CÁC GIAI ĐOẠN (SPRINT-BY-SPRINT PLAN)](#4-kế-hoạch-triển-khai-theo-các-giai-đoạn-sprint-by-sprint-plan)
   - 4.1. Sprint 1: Nâng cấp trải nghiệm tương tác Canvas (G1, G2, G6)
   - 4.2. Sprint 2: Nâng cấp quy trình tạo mẫu và cấu hình tham số (G3, G4, G5)
   - 4.3. Sprint 3: Tối ưu hóa hiệu năng động cơ thuật toán lõi (G7, G8, MinComponentSize)
   - 4.4. Sprint 4: Kiểm thử tích hợp, Benchmark dây chuyền & Đóng gói phát hành
5. [KẾT LUẬN & ĐÁNH GIÁ TÁC ĐỘNG HỆ THỐNG](#5-kết-luận--đánh-giá-tác-động-hệ-thống)

---

## 1. NGUYÊN TẮC THIẾT KẾ SẠCH VÀ RÀNG BUỘC PHÁP LÝ (CLEAN-ROOM PRINCIPLES)

### 1.1. Tôn chỉ Thiết kế Sạch (Clean-Room Mandate)

Ràng buộc tối thượng của dự án nâng cấp TunVision là: **TUYỆT ĐỐI KHÔNG ĐƯỢC PHÉP ĐƯA BẤT KỲ SỰ PHỤ THUỘC THỜI GIAN CHẠY (RUNTIME DEPENDENCY) HOẶC LIÊN KẾT NHỊ PHÂN NÀO VỚI `mwwz.dll`, `mwwzcv.dll` HOẶC CÁC TỆP LIÊN QUAN VÀO CODEBASE CỦA TUNVISION**.

Toàn bộ các cải tiến, nâng cấp tính năng và bổ sung thuật toán trong `TunVision.Core` và `TunVision.UI` phải được viết mới $100\%$ bằng ngôn ngữ C# thuần trên nền tảng **.NET 8**, thư viện giao diện **WPF (Windows Presentation Foundation)** và thư viện xử lý ảnh nguồn mở chuẩn **OpenCvSharp4**.

### 1.2. Phân lập tuyệt đối giữa Khảo sát Dịch ngược và Cài đặt Mã nguồn

Dự án áp dụng phương pháp luận Phòng thí nghiệm Thiết kế Sạch (Clean-Room Engineering Discipline):
1. **Bên khảo sát (Dirty Room / Reverse Engineers)**: Khảo sát hành vi giao diện của `TemplateMatchQt.exe`, dịch ngược giải thuật của `mwwz.dll`, trích xuất các công thức toán học (Pyramid log2, Hysteresis, Arc-length filtering, NMS) và ghi nhận thành các tài liệu đặc tả chức năng độc lập (`01_ui_inspection_gap_analysis.md`, `02_parameter_balancing_mechanics.md`).
2. **Bên triển khai nâng cấp (Clean Room / Implementers)**: Tiếp nhận các tài liệu đặc tả yêu cầu chức năng thuần túy, thiết kế kiến trúc và viết mã nguồn mới hoàn toàn dựa trên cấu trúc hiện có của `TunVision` mà không tham chiếu hay sao chép bất kỳ đoạn mã máy nào của đối tượng khảo sát.

### 1.3. Bảo toàn tính độc lập mã nguồn mở của TunVision trên nền .NET 8 / C#

Sau khi hoàn thành lộ trình nâng cấp, bộ mã nguồn của TunVision tiếp tục duy trì tính độc lập hoàn toàn, dễ dàng biên dịch chéo, đóng gói tự động qua `dotnet build` / `dotnet publish` mà không yêu cầu cài đặt thêm các tệp DLL native độc quyền từ bên thứ ba.

---

## 2. ĐẶC TẢ THIẾT KẾ VÀ MÃ NGUỒN NÂNG CẤP CHO 8 LỖ HỔNG CHỨC NĂNG (G1 - G8)

Dưới đây là đặc tả kỹ thuật chi tiết cùng mã nguồn C# và XAML cụ thể để tích hợp trực tiếp vào codebase của TunVision:

### 2.1. Nâng cấp G1: Bổ sung thanh HUD hiển thị thông số điểm ảnh trên `VisionCanvas.cs`

- **Tệp chỉnh sửa**: `d:\tunvision\mwwz-samples\TunVision\TunVision.UI\Controls\VisionCanvas.cs`
- **Mục tiêu**: Hiển thị kích thước ảnh, tọa độ con trỏ và giá trị màu sắc/mức xám tại vị trí chuột ở góc dưới bên trái của ảnh.
- **Giải pháp kỹ thuật**:
  1. Thêm một `FormattedText` hoặc phần tử HUD bán trong suốt được vẽ trong phương thức `OnRender`.
  2. Bắt sự kiện `OnMouseMove`, tính toán tọa độ ảnh tương ứng:
     ```csharp
     Point mousePos = e.GetPosition(this);
     Point imgPos = ScreenToImage(mousePos);
     ```
  3. Lấy giá trị pixel từ `BitmapSource` nguồn hoặc `Mat` hiện thời:
     ```csharp
     // Trong VisionCanvas.cs
     private string _hudPixelInfo = string.Empty;

     protected override void OnMouseMove(MouseEventArgs e)
     {
         base.OnMouseMove(e);
         if (CurrentPreviewSource != null)
         {
             Point mousePos = e.GetPosition(this);
             Point imgPos = ScreenToImage(mousePos);
             int ix = (int)Math.Floor(imgPos.X);
             int iy = (int)Math.Floor(imgPos.Y);

             if (ix >= 0 && ix < CurrentPreviewSource.PixelWidth && iy >= 0 && iy < CurrentPreviewSource.PixelHeight)
             {
                 // Đọc màu pixel từ BitmapSource
                 byte[] pixel = new byte[4];
                 var stride = (CurrentPreviewSource.PixelWidth * CurrentPreviewSource.Format.BitsPerPixel + 7) / 8;
                 try
                 {
                     CurrentPreviewSource.CopyPixels(new Int32Rect(ix, iy, 1, 1), pixel, stride, 0);
                     if (CurrentPreviewSource.Format.BitsPerPixel == 8)
                     {
                         _hudPixelInfo = $"W:{CurrentPreviewSource.PixelWidth}, H:{CurrentPreviewSource.PixelHeight} | X:{ix}, Y:{iy} | Gray:{pixel[0]}";
                     }
                     else
                     {
                         _hudPixelInfo = $"W:{CurrentPreviewSource.PixelWidth}, H:{CurrentPreviewSource.PixelHeight} | X:{ix}, Y:{iy} | R:{pixel[2]}, G:{pixel[1]}, B:{pixel[0]}";
                     }
                 }
                 catch { _hudPixelInfo = $"W:{CurrentPreviewSource.PixelWidth}, H:{CurrentPreviewSource.PixelHeight} | X:{ix}, Y:{iy}"; }
             }
             else
             {
                 _hudPixelInfo = $"W:{CurrentPreviewSource.PixelWidth}, H:{CurrentPreviewSource.PixelHeight} | X:---, Y:---";
             }
             InvalidateVisual(); // Kích hoạt vẽ lại HUD
         }
     }
     ```
  4. Trong `OnRender(DrawingContext dc)`:
     ```csharp
     if (!string.IsNullOrEmpty(_hudPixelInfo))
     {
         var ft = new FormattedText(
             _hudPixelInfo,
             CultureInfo.InvariantCulture,
             FlowDirection.LeftToRight,
             new Typeface("Consolas"),
             12,
             Brushes.LimeGreen,
             VisualTreeHelper.GetDpi(this).PixelsPerDip);

         Rect bgRect = new Rect(10, ActualHeight - 30, ft.Width + 16, 22);
         dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(180, 20, 20, 20)), null, bgRect, 4, 4);
         dc.DrawText(ft, new Point(18, ActualHeight - 27));
     }
     ```

---

### 2.2. Nâng cấp G2: Khung xem trước mô hình mẫu biệt lập trên nền cờ ca-rô (`ModelPreviewControl.xaml`)

- **Tệp mới**: `d:\tunvision\mwwz-samples\TunVision\TunVision.UI\Controls\ModelPreviewControl.xaml` và `.cs`
- **Mục tiêu**: Hiển thị đường biên mô hình mẫu hình học độc lập trên nền cờ ca-rô xám-trắng (Alpha Checkerboard).
- **Mã nguồn XAML**:
  ```xml
  <UserControl x:Class="TunVision.UI.Controls.ModelPreviewControl"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               Width="160" Height="140" Background="#1E1E1E">
      <Border BorderBrush="#3E3E42" BorderThickness="1" CornerRadius="4">
          <Grid ClipToBounds="True">
              <!-- Lớp nền cờ ca-rô xám trắng -->
              <DrawingBrush x:Key="CheckerboardBrush" Viewport="0,0,16,16" ViewportUnits="Absolute" TileMode="Tile">
                  <DrawingBrush.Drawing>
                      <DrawingGroup>
                          <GeometryDrawing Brush="#2A2A2A">
                              <GeometryDrawing.Geometry>
                                  <RectangleGeometry Rect="0,0,16,16"/>
                              </GeometryDrawing.Geometry>
                          </GeometryDrawing>
                          <GeometryDrawing Brush="#333333">
                              <GeometryDrawing.Geometry>
                                  <GeometryGroup>
                                      <RectangleGeometry Rect="0,0,8,8"/>
                                      <RectangleGeometry Rect="8,8,8,8"/>
                                  </GeometryGroup>
                              </GeometryDrawing.Geometry>
                          </GeometryDrawing>
                      </DrawingGroup>
                  </DrawingBrush.Drawing>
              </DrawingBrush>
              <Rectangle Fill="{StaticResource CheckerboardBrush}"/>
              
              <!-- Vùng vẽ đường biên vector -->
              <Canvas x:Name="ContourHostCanvas"/>
              
              <TextBlock Text="Model Preview" Foreground="#888888" FontSize="10" Margin="4" VerticalAlignment="Top" HorizontalAlignment="Left"/>
          </Grid>
      </Border>
  </UserControl>
  ```
- **Mã nguồn C# (`ModelPreviewControl.xaml.cs`)**:
  ```csharp
  public void UpdateModel(ShapeModel model)
  {
      ContourHostCanvas.Children.Clear();
      if (model == null || model.Levels.Count == 0) return;

      var level0 = model.Levels[0];
      if (level0.Features.Count == 0) return;

      // Tính toán hộp bao và tỉ lệ scale vừa khung 160x140
      float minX = level0.Features.Min(f => f.X);
      float maxX = level0.Features.Max(f => f.X);
      float minY = level0.Features.Min(f => f.Y);
      float maxY = level0.Features.Max(f => f.Y);
      float w = maxX - minX;
      float h = maxY - minY;
      if (w <= 0 || h <= 0) return;

      float scale = Math.Min(140f / w, 120f / h);
      float offsetX = (160f - w * scale) / 2f - minX * scale;
      float offsetY = (140f - h * scale) / 2f - minY * scale;

      var pathGeom = new PathGeometry();
      var fig = new PathFigure { StartPoint = new Point(level0.Features[0].X * scale + offsetX, level0.Features[0].Y * scale + offsetY), IsClosed = false };
      for (int i = 1; i < level0.Features.Count; i++)
      {
          fig.Segments.Add(new LineSegment(new Point(level0.Features[i].X * scale + offsetX, level0.Features[i].Y * scale + offsetY), true));
      }
      pathGeom.Figures.Add(fig);

      var path = new System.Windows.Shapes.Path
      {
          Data = pathGeom,
          Stroke = Brushes.LimeGreen,
          StrokeThickness = 1.2
      };
      ContourHostCanvas.Children.Add(path);
  }
  ```

---

### 2.3. Nâng cấp G3: Bộ nút bấm màu tự động cân bằng tham số trên `NodeSettingsWindow.xaml`

- **Tệp chỉnh sửa**: `d:\tunvision\mwwz-samples\TunVision\TunVision.UI\NodeSettingsWindow.xaml` và `.cs`
- **Mục tiêu**: Bổ sung 3 nút bấm vuông màu sắc ngay cạnh các ô nhập liệu của `ContrastLow`, `ContrastHigh` và `NumLevels`.
- **Giao diện XAML**:
  ```xml
  <!-- Contrast Low với nút Cyan -->
  <DockPanel Margin="0,4">
      <TextBlock Text="Contrast Low" Width="120" VerticalAlignment="Center"/>
      <Button x:Name="BtnAutoContrastLow" Content="A" Width="24" Height="24" DockPanel.Dock="Right" Margin="4,0,0,0"
              Background="#00BCD4" Foreground="Black" FontWeight="Bold" ToolTip="Tự động tính Contrast Low tối ưu"
              Click="BtnAutoContrastLow_Click"/>
      <TextBox Text="{Binding ContrastLow, UpdateSourceTrigger=PropertyChanged}"/>
  </DockPanel>

  <!-- Contrast High với nút Green -->
  <DockPanel Margin="0,4">
      <TextBlock Text="Contrast High" Width="120" VerticalAlignment="Center"/>
      <Button x:Name="BtnAutoContrastHigh" Content="A" Width="24" Height="24" DockPanel.Dock="Right" Margin="4,0,0,0"
              Background="#4CAF50" Foreground="White" FontWeight="Bold" ToolTip="Tự động tính Contrast High tối ưu"
              Click="BtnAutoContrastHigh_Click"/>
      <TextBox Text="{Binding ContrastHigh, UpdateSourceTrigger=PropertyChanged}"/>
  </DockPanel>

  <!-- NumLevels với nút Red -->
  <DockPanel Margin="0,4">
      <TextBlock Text="Num Levels" Width="120" VerticalAlignment="Center"/>
      <Button x:Name="BtnAutoNumLevels" Content="A" Width="24" Height="24" DockPanel.Dock="Right" Margin="4,0,0,0"
              Background="#F44336" Foreground="White" FontWeight="Bold" ToolTip="Tự động tính số tầng Pyramid"
              Click="BtnAutoNumLevels_Click"/>
      <TextBox Text="{Binding NumLevels, UpdateSourceTrigger=PropertyChanged}"/>
  </DockPanel>
  ```
- **Sự kiện C# (`NodeSettingsWindow.xaml.cs`)**:
  ```csharp
  private void BtnAutoContrastLow_Click(object sender, RoutedEventArgs e)
  {
      if (_viewModel.SelectedNode is ShapeMatchNode node && _mainViewModel.CurrentImage != null)
      {
          var estimates = ShapeTrainer.AutoEstimateContrast(_mainViewModel.CurrentImage, node.Roi);
          node.ContrastLow = estimates.ContrastLow;
      }
  }

  private void BtnAutoContrastHigh_Click(object sender, RoutedEventArgs e)
  {
      if (_viewModel.SelectedNode is ShapeMatchNode node && _mainViewModel.CurrentImage != null)
      {
          var estimates = ShapeTrainer.AutoEstimateContrast(_mainViewModel.CurrentImage, node.Roi);
          node.ContrastHigh = estimates.ContrastHigh;
      }
  }

  private void BtnAutoNumLevels_Click(object sender, RoutedEventArgs e)
  {
      if (_viewModel.SelectedNode is ShapeMatchNode node)
      {
          int w = node.Roi.Width > 0 ? (int)node.Roi.Width : _mainViewModel.CurrentImage?.Width ?? 100;
          int h = node.Roi.Height > 0 ? (int)node.Roi.Height : _mainViewModel.CurrentImage?.Height ?? 100;
          node.NumLevels = (int)Math.Max(1, Math.Round(Math.Log2(Math.Min(w, h) / 16.0)) + 1);
      }
  }
  ```

---

### 2.4. Nâng cấp G4: Cơ chế xem trước đường biên tương tác thời gian thực (`LiveContrastEdgePreview`)

- **Tệp chỉnh sửa**: `TunVision.UI\ViewModels\MainViewModel.cs` và `VisionCanvas.cs`
- **Mục tiêu**: Khi kéo Slider `ContrastLow` hoặc `ContrastHigh`, đường biên mẫu tạm thời được trích xuất bằng một tác vụ bất đồng bộ nhẹ (`Task.Run`) và vẽ đè màu vàng cam lên ảnh.
- **Mã nguồn điều phối**:
  ```csharp
  private CancellationTokenSource? _contrastPreviewCts;

  public void RequestLiveContrastPreview(int contrastLow, int contrastHigh, Roi roi)
  {
      _contrastPreviewCts?.Cancel();
      _contrastPreviewCts = new CancellationTokenSource();
      var token = _contrastPreviewCts.Token;

      Task.Run(() =>
      {
          if (CurrentImage == null) return;
          using var roiMat = new Mat(CurrentImage, roi.ToRect());
          var edges = TopologicalContourExtractor.ExtractContoursFast(roiMat, contrastLow, contrastHigh, minCompSize: 16);
          if (!token.IsCancellationRequested)
          {
              Application.Current.Dispatcher.Invoke(() =>
              {
                  PreviewTemporaryContours = edges; // Kích hoạt vẽ overlay màu cam trên VisionCanvas
              });
          }
      }, token);
  }
  ```

---

### 2.5. Nâng cấp G5: Bộ công cụ tương tác vẽ ROI hình chữ nhật xoay (`RectangleR`) và Đa giác (`Polygon`)

- **Tệp chỉnh sửa**: `TunVision.UI\Controls\VisionCanvas.cs` và `TunVision.Core\Abstractions\Roi.cs`
- **Mở rộng lớp `Roi.cs`**:
  ```csharp
  public enum RoiShapeType { AxisAlignedRect, RotatedRect, Polygon, AnnularRing }

  public class RotatedRectRoi : Roi
  {
      public double CenterX { get; set; }
      public double CenterY { get; set; }
      public double Length1 { get; set; } // Nửa chiều dài
      public double Length2 { get; set; } // Nửa chiều rộng
      public double AngleDeg { get; set; }
  }

  public class PolygonRoi : Roi
  {
      public List<Point> Vertices { get; set; } = new();
  }
  ```
- **Tương tác chuột trên `VisionCanvas.cs`**:
  * Khi chọn chế độ `RoiMode.RotatedRectangle`: Click chuột đầu tiên đặt tâm $(x_c, y_c)$; kéo chuột đặt trục chính và góc $\phi$; nhả chuột kéo tiếp để định nghĩa bán trục phụ `Length2`.
  * Vẽ 4 điểm điều khiển (Control Handles) ở các góc và 1 handle định hướng ở đỉnh để người dùng dùng chuột xoay linh hoạt.

---

### 2.6. Nâng cấp G6: Bộ lọc chế độ hiển thị lớp phủ kết quả (`DrawOverlayMode: NoScore / Score / All`)

- **Tệp chỉnh sửa**: `TunVision.UI\Controls\VisionCanvas.cs` và `MainWindow.xaml`
- **Định nghĩa Enum**:
  ```csharp
  public enum OverlayDrawMode
  {
      All,      // Vẽ Contour, Crosshair, Score, Box, Coordinate Axes
      Score,    // Vẽ Contour, Crosshair, Score
      NoScore,  // Chỉ vẽ Contour và Crosshair (Tối giản công nghiệp)
      ContourOnly // Chỉ vẽ Contour
  }
  ```
- **Cấu hình trên `VisionCanvas.cs`**:
  ```csharp
  public OverlayDrawMode DrawMode { get; set; } = OverlayDrawMode.NoScore; // Mặc định sạch sẽ

  private void RenderMatchResult(DrawingContext dc, AlignResult match)
  {
      // 1. Luôn vẽ Contour xanh lá cây
      DrawContour(dc, match.TransformedContours, Brushes.LimeGreen);

      // 2. Luôn vẽ dấu chữ thập tâm đỏ
      DrawCrosshair(dc, match.Center, 10, Brushes.Red);

      // 3. Nếu DrawMode >= Score: Vẽ thêm nhãn điểm số
      if (DrawMode == OverlayDrawMode.Score || DrawMode == OverlayDrawMode.All)
      {
          DrawScoreBadge(dc, match.Center, match.Score);
      }

      // 4. Nếu DrawMode == All: Vẽ thêm hệ trục tọa độ 2D và hộp bao
      if (DrawMode == OverlayDrawMode.All)
      {
          DrawCoordinateFrame(dc, match.Center, match.AngleDeg);
          DrawRotatedBox(dc, match.BoundingBox);
      }
  }
  ```

---

### 2.7. Nâng cấp G7: Tham số điều khiển tầng kim tự tháp kết thúc (`LowestPyramid`) trong `AlignTool.cs`

- **Tệp chỉnh sửa**: `TunVision.Core\ShapeMatching\AlignTool.cs` và `ShapeMatcher.cs`
- **Mã nguồn tích hợp**:
  ```csharp
  // Trong AlignTool.cs
  public int LowestPyramid { get; set; } = 1; // 1-based: 1 = Tầng 0 ảnh gốc, 2 = Dừng tại tầng 1/2

  // Trong ShapeMatcher.cs
  public List<MatchResult> Match(Mat scene, ShapeModel model, MatchParameters parameters)
  {
      int stopLevel = Math.Max(0, parameters.LowestPyramid - 1); // Chuyển sang 0-based
      
      // Duyệt từ tầng cao nhất L-1 xuống stopLevel
      for (int level = model.NumLevels - 1; level >= stopLevel; level--)
      {
          // Tinh chỉnh tọa độ và điểm số tại tầng level
          ...
      }
      
      // Nếu stopLevel > 0: Phóng to tọa độ về không gian tầng 0
      if (stopLevel > 0)
      {
          float scaleFactor = (float)Math.Pow(2, stopLevel);
          foreach (var m in matches)
          {
              m.X *= scaleFactor;
              m.Y *= scaleFactor;
          }
      }
      return matches;
  }
  ```

---

### 2.8. Nâng cấp G8: Giải thuật phân mảnh ô cờ cho ảnh siêu độ phân giải (`TiledImageMatcher`)

- **Tệp mới**: `TunVision.Core\ShapeMatching\Acceleration\TiledImageMatcher.cs`
- **Mục tiêu**: Xử lý mượt mà ảnh công nghiệp $20\text{ MP} \dots 65\text{ MP}$ bằng cách chia lưới không gian với khoảng đè an toàn.
- **Mã nguồn triển khai**:
  ```csharp
  public class TiledImageMatcher
  {
      public static List<MatchResult> MatchTiled(
          Mat largeScene,
          ShapeModel model,
          MatchParameters parameters,
          int tilesX = 2,
          int tilesY = 2)
      {
          int tileW = largeScene.Width / tilesX;
          int tileH = largeScene.Height / tilesY;
          int overlap = (int)Math.Ceiling(model.Diameter); // Khoảng đè an toàn

          var allMatches = new ConcurrentBag<MatchResult>();

          Parallel.For(0, tilesX * tilesY, idx =>
          {
              int tx = idx % tilesX;
              int ty = idx / tilesX;

              int x0 = Math.Max(0, tx * tileW - overlap);
              int y0 = Math.Max(0, ty * tileH - overlap);
              int x1 = Math.Min(largeScene.Width, (tx + 1) * tileW + overlap);
              int y1 = Math.Min(largeScene.Height, (ty + 1) * tileH + overlap);

              var roiRect = new Rect(x0, y0, x1 - x0, y1 - y0);
              using var subMat = new Mat(largeScene, roiRect);

              var localMatches = ShapeMatcher.Find(subMat, model, parameters);
              foreach (var m in localMatches)
              {
                  m.X += x0;
                  m.Y += y0;
                  allMatches.Add(m);
              }
          });

          // Áp dụng NMS triệt tiêu các đối tượng bị phát hiện đúp tại vùng overlap
          return NonMaximumSuppression.FilterOverlap(allMatches.ToList(), parameters.MaxOverlap);
      }
  }
  ```

---

## 3. NÂNG CẤP NĂNG LỰC THUẬT TOÁN HẠT NHÂN TRONG TUNVISION.CORE

### 3.1. Tích hợp bộ lọc kích thước thành phần liên thông (`MinComponentSize = 16 px`) vào `TopologicalContourExtractor`

- **Vị trí**: `TunVision.Core\ShapeMatching\Geometry\TopologicalContourExtractor.cs`
- **Mô tả giải thuật**:
  Sau khi hoàn tất bước bám biên vi phân Hysteresis, danh sách các chuỗi contour được kiểm tra độ dài cung hình học (Arc-Length):
  $$\text{Length}_{\text{contour}} = \sum_{i=1}^{K-1} \sqrt{(x_{i+1} - x_i)^2 + (y_{i+1} - y_i)^2}$$
  Nếu $\text{Length}_{\text{contour}} < \text{MinComponentSize}$ (mặc định đặt là **`16`** pixel thay vì 5 pixel như trước đây), toàn bộ chuỗi điểm biên đó bị loại bỏ ngay lập tức.  
- **Tác động**: Triệt tiêu $100\%$ các hạt nhiễu xước tế vi trên bề mặt linh kiện cơ khí, ngăn ngừa báo động giả.

### 3.2. Nâng cấp 16 kênh Response Maps và bộ tích lũy SIMD `Avx2ScoreAccumulator.cs`

- **Vị trí**: `TunVision.Core\ShapeMatching\Acceleration\Avx2ScoreAccumulator.cs`
- **Tối ưu hóa AVX2**:
  Sử dụng con trỏ không an toàn `unsafe` kết hợp các nội tại phần cứng của .NET 8 (`System.Runtime.Intrinsics.X86.Avx2`):
  * Lệnh `Avx2.ConvertToVector256Int16`: Mở rộng dấu 16 phần tử `sbyte` từ kênh Response Map thành `short`.
  * Lệnh `Avx2.AddSaturate`: Cộng dồn bão hòa 16-bit vào accumulator.
  * Tốc độ tích lũy đạt $32 \text{ pixels}$ trong mỗi vòng lặp không cuộn (Unrolled Loop).

### 3.3. Cơ chế chẩn đoán chất lượng từng hạt đặc trưng

- Trong lớp `MatchResult.cs`, bổ sung danh sách điểm biên chi tiết:
  ```csharp
  public struct FeatureDiagnostic
  {
      public float X;
      public float Y;
      public float Score; // Điểm tương quan cục bộ [-1.0 .. +1.0]
      public bool IsInlier => Score >= DiagnosticScoreThreshold;
  }
  ```
- Trên `VisionCanvas.cs`: Điểm có `IsInlier == true` vẽ màu **Xanh lá cây**; điểm có `IsInlier == false` (bị mờ, xước, che khuất) vẽ màu **Đỏ**. Kỹ sư quan sát thấy ngay các điểm khuyết tật trên dây chuyền.

### 3.4. Kiến trúc chia sẻ kim tự tháp cho so khớp đa mô hình đồng thời (`MultiShapeMatcher`)

- Cho phép đưa vào một danh sách các mô hình mẫu: `List<ShapeModel> models`.
- Ảnh hiện trường được tính toán tháp kim tự tháp và 16 Response Maps một lần duy nhất, sau đó lần lượt so khớp tất cả các mô hình trên cùng một không gian bộ nhớ chia sẻ, giúp thời gian xử lý khi tìm $N$ mô hình giảm từ $N \times T$ xuống chỉ còn $T_{\text{prep}} + N \times T_{\text{search}}$.

---

## 4. KẾ HOẠCH TRIỂN KHAI THEO CÁC GIAI ĐOẠN (SPRINT-BY-SPRINT PLAN)

Lộ trình nâng cấp được chia thành 4 Sprint phát triển tuần tự, bảo đảm chất lượng và tính kiểm thử liên tục:

```
+----------------------------------------------------------------------------------------------------+
| LỘ TRÌNH 4 SPRINT NÂNG CẤP HỆ THỐNG TUNVISION (CLEAN-ROOM UPGRADE ROADMAP)                        |
+------------------------------------+---------------------------------------------------------------+
| SPRINT                             | HẠNG MỤC CÔNG VIỆC TRỌNG TÂM                                  |
+------------------------------------+---------------------------------------------------------------+
| Sprint 1: Trải nghiệm Canvas       | - [G1] Tích hợp thanh HUD điểm ảnh trực tiếp trên VisionCanvas|
| (Interactive Canvas & Overlays)    | - [G2] Tạo UserControl xem trước mẫu biệt lập ModelPreview    |
|                                    | - [G6] Tùy biến bộ lọc hiển thị lớp phủ (NoScore / Score / All|
+------------------------------------+---------------------------------------------------------------+
| Sprint 2: Quy trình Huấn luyện     | - [G3] Bổ sung 3 nút bấm tự động cân bằng tham số màu trên UI |
| (Template Workflow & Gizmos)       | - [G4] Cài đặt Live Contrast Edge Preview khi kéo thanh trượt |
|                                    | - [G5] Phát triển tương tác vẽ RotatedRect và Polygon Gizmo   |
+------------------------------------+---------------------------------------------------------------+
| Sprint 3: Động cơ Thuật toán Lõi   | - [G7] Thêm tham số LowestPyramid tăng tốc định vị thô        |
| (Core Engine & Performance)        | - [G8] Xây dựng giải thuật Tiled Matching cho ảnh 20MP+       |
|                                    | - Tích hợp bộ lọc MinComponentSize 16px vào Core Extractor    |
|                                    | - Hoàn thiện chẩn đoán hạt feature tô màu Green / Red         |
+------------------------------------+---------------------------------------------------------------+
| Sprint 4: Kiểm thử & Đóng gói      | - Chạy toàn bộ bộ test suite tự động trong TunVision.Tests    |
| (Verification & Production Ready)  | - Đánh giá độ trễ (Latency Benchmark) trên ảnh IC 2448x2048   |
|                                    | - Đóng gói bản phát hành TunVision Studio v2.0 Clean-Room     |
+------------------------------------+---------------------------------------------------------------+
```

---

## 5. KẾT LUẬN & ĐÁNH GIÁ TÁC ĐỘNG HỆ THỐNG

1. **Bảo tồn tính độc lập và sạch sẽ**: Toàn bộ kiến trúc đề xuất trong lộ trình nâng cấp đều tuân thủ nghiêm ngặt nguyên tắc Clean-Room. TunVision không có bất kỳ dòng mã nào phụ thuộc vào `mwwz.dll`, bảo toàn tính pháp lý và tính mở của nền tảng .NET 8.
2. **Nâng tầm trải nghiệm người dùng**: Việc giải quyết triệt để 8 lỗ hổng chức năng (G1 đến G8) sẽ đưa trải nghiệm vận hành của TunVision vượt qua `TemplateMatchQt.exe`: người dùng vừa có được sự tiện ích, tức thời của các nút tự động cân bằng tham số và xem trước đường biên sống động, vừa sở hữu sức mạnh kết nối đồ thị DAG đa công cụ của một xưởng phần mềm đo kiểm chuyên nghiệp.
3. **Hiệu năng công nghiệp vượt trội**: Với việc nâng cấp bộ lọc `MinComponentSize 16 px`, bộ tích lũy SIMD AVX2, cơ chế dừng kim tự tháp `LowestPyramid` và phân vùng ô cờ `TiledImageMatcher`, TunVision hoàn toàn đủ năng lực đáp ứng các tiêu chuẩn khắt khe nhất của các dây chuyền sản xuất bán dẫn và lắp ráp điện tử tốc độ cao.

---
*Tài liệu kết thúc.*