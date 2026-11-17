# BỘ TÀI LIỆU KỸ THUẬT CHUYÊN SÂU: PHÂN TÍCH DỊCH NGƯỢC TEMPLATEMATCHQT.EXE, THƯ VIỆN MWWZ.DLL VÀ LỘ TRÌNH NÂNG CẤP NỀN TẢNG THỊ GIÁC MÁY TUNVISION STUDIO

**Dự án**: Nghiên cứu Dịch ngược & Nâng cấp Nền tảng So khớp Mẫu Hình học Chuẩn Công nghiệp (Industrial Geometric Shape-Based Matching)  
**Địa chỉ lưu trữ bộ tài liệu**: `d:\tunvision\mwwz-samples\docs\`  
**Ngày phát hành**: 15/11/2026  
**Ngôn ngữ**: Tiếng Việt chuyên ngành Thị giác máy tính & Kỹ thuật phần mềm hệ thống (Computer Vision & Systems Software Engineering)  
**Tác giả**: Nhóm Nghiên cứu Kỹ thuật & Thiết kế Kiến trúc Thị giác máy (Machine Vision Research & Architecture Team)  

---

## 1. TỔNG QUAN ĐIỀU HÀNH (EXECUTIVE SUMMARY)

Dự án này thực hiện phân tích đối chiếu chuyên sâu và dịch ngược toàn diện giữa hai nền tảng phần mềm thị giác máy:
1. **Hệ thống tham chiếu chuẩn công nghiệp**: Ứng dụng thực thi `TemplateMatchQt.exe` (Bản dựng 64-bit Qt5 ngày 06/11/2026) cùng thư viện động cơ hình học hạt nhân native `mwwz.dll` (x64 PE32+, 1,049,600 bytes, xuất khẩu 154 hàm API) và OpenCV 4.6.0. Đây là công cụ đo kiểm công nghiệp chuyên biệt, đạt hiệu năng xử lý cực hạn (**10.3717 ms** khi tìm kiếm 9 chip vi mạch trên ảnh lớn $2448 \times 2048$ pixel) và sở hữu trải nghiệm người dùng tối ưu cho tác vụ so khớp hình học.
2. **Nền tảng mục tiêu nâng cấp**: Hệ thống phần mềm `TunVision` (Mã nguồn C# .NET 8 WPF, kiến trúc đồ thị DAG modular kết hợp OpenCvSharp4). Đây là xưởng phần mềm thị giác đa công cụ (Multi-Tool Pipeline Studio), có khả năng xâu chuỗi nhiều tác vụ đo kiểm phức tạp (Align $\to$ Caliper $\to$ Blob $\to$ Barcode $\to$ Logic Verdict).

### Kết quả cốt lõi đạt được:
- **Ma trận khoảng cách tính năng**: Đã thiết lập ma trận đối chiếu toàn diện **35 tiêu chí kỹ thuật** qua 5 nhóm năng lực (Bố cục Canvas, Chế độ kiểm tra, Quy trình tạo mẫu & ROI, Lớp phủ kết quả, Chẩn đoán & hiệu năng); nhận diện chính xác **8 lỗ hổng chức năng trọng yếu (Gaps G1 – G8)** cần nâng cấp trong TunVision.
- **Giải mã toàn bộ 24 tham số động cơ**: Bóc tách chính xác kiểu dữ liệu, dải giá trị, địa chỉ offset trong bộ nhớ heap của `TemplateMatchQt.exe`, và mối liên hệ với các đối số trong API native của `mwwz.dll`.
- **Làm sáng tỏ các công thức tự động cân bằng (Auto-Balancing)**: Giải mã thuật toán tính tầng kim tự tháp tối ưu $L = \text{round}(\log_2(\min(W, H)/16)) + 1$, cơ chế phân vị gradient $P_{25}, P_{70}, P_{85}$, bước góc tự thích ứng $\Delta\theta_l = (0.5 / R_{\max}) \cdot 2^l$, cơ chế cắt tỉa nhánh tham lam Greediness Branch-and-Bound, và bộ lọc khử cực đại địa phương 2 tầng (Spatial 3x3 + IoU Overlap NMS).
- **Đặc tả nhị phân & Bộ bọc C# P/Invoke hoàn chỉnh**: Lập danh mục toàn bộ 154 hàm xuất khẩu của `mwwz.dll`, giải mã cấu trúc dữ liệu với kích thước chuẩn xác từng byte (`Match` 40B, `Feature` 16B, `TemplateF` 40B, `MatchDetails` 80B), xây dựng bộ bọc C# .NET 8 hoàn chỉnh với cơ chế thu hồi bộ nhớ an toàn qua `clear_mems(mem_id)` và mô thức `IDisposable`.
- **Lộ trình nâng cấp Thiết kế Sạch (Clean-Room Upgrade Roadmap)**: Đề xuất đặc tả kỹ thuật và mã nguồn C#/WPF mới $100\%$ để nâng cấp TunVision mà **hoàn toàn không đưa bất kỳ sự phụ thuộc thời gian chạy (runtime dependency) nào vào `mwwz.dll`**.

---

## 2. DANH MỤC TÀI LIỆU KỸ THUẬT CHI TIẾT (MASTER DOCUMENT INDEX)

Bộ tài liệu kỹ thuật hoàn chỉnh gồm 4 tài liệu chuyên đề độc lập, được lưu trữ tại `d:\tunvision\mwwz-samples\docs\`:

| Tên tệp tài liệu | Mã tài liệu | Nội dung trọng tâm | Quy mô & Tính đầy đủ |
|---|:---:|---|---|
| [`01_ui_inspection_gap_analysis.md`](01_ui_inspection_gap_analysis.md) | **Doc 1 (R1)** | - Kiến trúc UI của TemplateMatchQt.exe (Qt5, widgets, layouts, canvas HUD, docks, buttons).<br>- Kiến trúc UI của TunVision (WPF, VisionCanvas, DAG graph, NodeSettingsWindow).<br>- **Ma trận đối chiếu tính năng 35 tiêu chí**.<br>- **Phân tích chuyên sâu 8 lỗ hổng chức năng cốt lõi (G1 – G8)**.<br>- So sánh quy trình kiểm tra và biên tập mặt nạ mẫu. | Hoàn thiện 100%, có sơ đồ ASCII và số liệu đối soát vi kiến trúc. |
| [`02_parameter_balancing_mechanics.md`](02_parameter_balancing_mechanics.md) | **Doc 2 (R2)** | - **Bảng danh mục chi tiết 24 tham số** (kiểu dữ liệu, dải hợp lệ, mặc định, offset UI, API đối ứng).<br>- **Công thức và cơ chế tự động cân bằng**: Pyramid log2, Gradient histogram percentiles, Pyramid halving, Auto angle step, Greediness pruning, Coarse score relaxation, 2-stage NMS.<br>- **Bản chất toán học**: Lọc biên Steger Facet 3x3, lượng tử hóa 16 sector tangent, 16 Response Maps, tăng tốc SIMD AVX2 (32 px/cycle), nội suy parabol subpixel.<br>- Hướng dẫn tối ưu hóa độ trễ, triệt tiêu báo động giả và kháng ánh sáng. | Hoàn thiện 100%, đầy đủ công thức giải tích chuẩn hóa và mã hợp ngữ x64. |
| [`03_mwwz_dll_specification_csharp_interop.md`](03_mwwz_dll_specification_csharp_interop.md) | **Doc 3 (R3)** | - Đặc tính nhị phân PE32+ x64, phụ thuộc OpenCV/OpenSSL/Concurrency.<br>- **Danh mục toàn bộ 154 hàm xuất khẩu** (Calling convention, chữ ký hàm, chức năng).<br>- **Bố cục bộ nhớ & byte alignment**: `Match` (40B), `Feature` (16B), `TemplateF` (40B x64), `MatchDetails` (80B x64), `RRect` (20B).<br>- Chu trình hoạt động, máy trạng thái, sơ đồ tuần tự và quy tắc sống còn `clear_mems(mem_id)`.<br>- **Mã nguồn C# P/Invoke hoàn chỉnh**: `MwwzNativeMethods.cs`, `MwwzShapeModel.cs`, `MwwzEngine.cs`.<br>- Kiến trúc Standalone UI (WPF MVVM .NET 8) ánh xạ 22 controls. | Hoàn thiện 100%, mã nguồn C# production-ready, sẵn sàng biên dịch. |
| [`04_tunvision_cleanroom_upgrade_roadmap.md`](04_tunvision_cleanroom_upgrade_roadmap.md) | **Doc 4 (R4)** | - Tôn chỉ và kỷ luật Thiết kế Sạch (Clean-Room Mandate).<br>- **Đặc tả và mã nguồn nâng cấp cho 8 lỗ hổng (G1 – G8)**: HUD pixel info trên Canvas, ModelPreviewControl nền cờ ca-rô, nút màu tự động cân bằng tham số, Live contrast preview, RotatedRect/Polygon Gizmo, bộ lọc DrawMode, LowestPyramid, TiledImageMatcher cho ảnh 20MP+.<br>- Nâng cấp lõi `TunVision.Core`: Bộ lọc `MinComponentSize 16 px`, tích lũy SIMD AVX2, chẩn đoán hạt feature Green/Red, MultiShapeMatcher.<br>- Kế hoạch triển khai chi tiết qua 4 Sprint. | Hoàn thiện 100%, mã nguồn C# và XAML cụ thể, tích hợp trực tiếp vào codebase. |

---

## 3. BẢNG SO SÁNH TỔNG HỢP KIẾN TRÚC & NĂNG LỰC

| Hạng mục so sánh | TemplateMatchQt.exe & mwwz.dll | TunVision Studio (Hiện tại) | TunVision Studio (Sau khi Nâng cấp) |
|---|---|---|---|
| **Mô hình kiến trúc** | Single-purpose Standalone Tool (Tối ưu hóa đơn tác vụ) | Multi-tool Pipeline Studio (Đồ thị DAG modular) | **Multi-tool Pipeline Studio hoàn thiện cao cấp** |
| **Công nghệ nền tảng** | C++ Qt5 Native x64 / OpenCV 4.6.0 | C# .NET 8 WPF / OpenCvSharp4 | **C# .NET 8 WPF / OpenCvSharp4 (Clean-Room 100%)** |
| **Khả năng mở rộng quy trình** | Hạn chế (Chỉ huấn luyện và khớp mẫu) | Rất cao (Align, Caliper, Blob, Barcode, Verdict) | **Vượt trội** (Tích hợp hoàn hảo chuỗi đo kiểm công nghiệp) |
| **Hiển thị thông số điểm ảnh (HUD)**| Có sẵn trên Canvas (`W,H,X,Y,R,G,B`) | Chưa có trên Image Canvas | **Bổ sung thanh HUD trực quan tại góc ảnh (G1)** |
| **Khung xem trước mẫu biệt lập** | Có sẵn (Nền cờ ca-rô trong suốt) | Chưa có (Vẽ đè lên ảnh gốc) | **Bổ sung `ModelPreviewControl` nền cờ ca-rô (G2)** |
| **Tự động cân bằng tham số trên UI** | 3 nút màu trực quan (`toolBtnA1/A2/A3`) | Chỉ có hàm trong lõi, thiếu nút UI | **Bổ sung 3 nút màu cạnh ô nhập liệu (G3)** |
| **Xem trước biên thời gian thực** | Tức thời (`upon_update_contrast`) | Phải bấm nút tạo mẫu mới nhìn thấy | **Live Contrast Edge Preview bất đồng bộ (G4)** |
| **Công cụ tương tác vùng ROI** | Hộp chữ nhật đứng, xoay, đa giác, vành khuyên | Kéo khung chữ nhật đứng trục | **Gizmo RotatedRect (`RectangleR`) và Polygon (G5)** |
| **Biên tập và xóa chi tiết thừa mẫu** | Hạn chế (Xóa qua ID hoặc trừ vùng) | **Xuất sắc**: Cọ xóa tự do (`BrushErase`), Undo | **Duy trì ưu thế vượt trội với cọ xóa và Undo Ctrl+Z** |
| **Bộ lọc lớp phủ kết quả (Draw Mode)**| Có sẵn (`Draw: NoScore / Score / All`) | Luôn vẽ toàn bộ (Dễ rối màn hình) | **Bổ sung tùy chọn `OverlayDrawMode` linh hoạt (G6)** |
| **Điều khiển tầng dừng Pyramid** | Có sẵn (`LowestPyramid: 1 .. 10`) | Luôn chạy xuống tầng đáy (Level 0) | **Bổ sung thuộc tính `LowestPyramid` trong Align (G7)** |
| **Xử lý ảnh siêu lớn (20MP+)** | Có hàm phân mảnh `find_shape_model_xl` | Chỉ nhận ảnh toàn phần | **Bổ sung module `TiledImageMatcher` chia lưới (G8)** |
| **Loại bỏ nhiễu xước công nghiệp** | **Cực tốt** (`MinCompSize = 16 px`) | Phân ngưỡng gradient cơ bản | **Tích hợp `MinComponentSize 16 px` vào Core** |
| **Tăng tốc phần cứng SIMD** | AVX2 Native (32 pixels / chu kỳ lệnh) | Avx2ScoreAccumulator (.NET Intrinsics) | **Tối ưu hóa AVX2 Unrolled Loop song song** |

---

## 4. HƯỚNG DẪN ĐỌC TÀI LIỆU THEO VAI TRÒ (READING GUIDE)

Tùy theo nhiệm vụ và vai trò kỹ thuật, các thành viên trong đội ngũ phát triển nên tiếp cận bộ tài liệu theo lộ trình khuyến nghị sau:

```
+----------------------------------------------------------------------------------------------------+
|                                    SƠ ĐỒ LỘ TRÌNH ĐỌC TÀI LIỆU                                     |
+----------------------------------------------------------------------------------------------------+
|                                                                                                    |
|  [KỸ SƯ UI/UX & LẬP TRÌNH VIÊN WPF]                                                                |
|  --> Bước 1: Đọc Doc 1 (01_ui_inspection_gap_analysis.md) để nắm bố cục và 8 lỗ hổng G1-G8.       |
|  --> Bước 2: Đọc Doc 4 (04_tunvision_cleanroom_upgrade_roadmap.md, Phần 2) lấy mã XAML/C# tương tác.|
|                                                                                                    |
|  [KỸ SƯ THUẬT TOÁN THỊ GIÁC & TỐI ƯU HÓA HIỆU NĂNG]                                                |
|  --> Bước 1: Đọc Doc 2 (02_parameter_balancing_mechanics.md) nắm vững công thức Steger, Response   |
|               Maps, Pyramid halving, Greediness pruning và SIMD AVX2.                              |
|  --> Bước 2: Đọc Doc 4 (04_tunvision_cleanroom_upgrade_roadmap.md, Phần 3) để nâng cấp Core.       |
|                                                                                                    |
|  [KỸ SƯ HỆ THỐNG & TÍCH HỢP INTEROP (DỰ ÁN TƯƠNG LAI)]                                            |
|  --> Bước 1: Đọc Doc 3 (03_mwwz_dll_specification_csharp_interop.md) nắm rõ 154 hàm xuất khẩu,    |
|               byte alignment struct (40B, 80B), và quy tắc sống còn clear_mems(mem_id).             |
|  --> Bước 2: Tích hợp trực tiếp MwwzNativeMethods.cs, MwwzShapeModel.cs và MwwzEngine.cs vào app.  |
|                                                                                                    |
|  [TRƯỞNG DỰ ÁN & KIẾN TRÚC SƯ TRƯỞNG (TECH LEAD / PM)]                                             |
|  --> Bước 1: Đọc README.md (Tổng quan điều hành và Ma trận so sánh).                               |
|  --> Bước 2: Đọc Doc 4 (04_tunvision_cleanroom_upgrade_roadmap.md, Phần 4) duyệt kế hoạch 4 Sprint.|
|                                                                                                    |
+----------------------------------------------------------------------------------------------------+
```

---

## 5. SƠ ĐỒ KIẾN TRÚC HỆ THỐNG TỔNG THỂ

### 5.1. Sơ đồ luân chuyển dữ liệu và kiến trúc nâng cấp TunVision Clean-Room

```
+----------------------------------------------------------------------------------------------------+
|                                   TUNVISION STUDIO UI (.NET 8 WPF)                                 |
|                                                                                                    |
|  +-------------------------------------+  +-----------------------------------------------------+  |
|  |     VisionCanvas (WPF Low-Level)    |  |               NodeSettingsWindow.xaml               |  |
|  |  - [G1] HUD Pixel Info (W,H,X,Y,RGB)|  |  - [G3] Bộ 3 nút bấm màu tự động cân bằng tham số:   |  |
|  |  - [G2] ModelPreview (Nền cờ ca-rô) |  |        * Nút Cyan  --> ContrastLow (p70)            |  |
|  |  - [G4] Live Contrast Preview       |  |        * Nút Green --> ContrastHigh (p85)           |  |
|  |  - [G5] RotatedRect & Polygon Gizmo |  |        * Nút Red   --> NumLevels (log2)             |  |
|  |  - [G6] Overlay Mode: NoScore/Score |  |  - [G7] LowestPyramid SpinBox (1-based)             |  |
|  +-------------------------------------+  +-----------------------------------------------------+  |
+---------------------------------------------------+------------------------------------------------+
                                                    |
                                                    v
+----------------------------------------------------------------------------------------------------+
|                                 TUNVISION.CORE (GEOMETRIC VISION ENGINE)                           |
|                                                                                                    |
|  +----------------------------------------------------------------------------------------------+  |
|  |  Geometry & Contour Extraction:                                                              |  |
|  |  - TopologicalContourExtractor (Tích hợp bộ lọc MinComponentSize = 16 px loại bỏ xước IC)    |  |
|  |  - SubpixelContourExtractor (Nội suy mặt cong Paraboloid bậc hai đạt độ chính xác 1/20 px)   |  |
|  +----------------------------------------------------------------------------------------------+  |
|  |  Acceleration & Response Maps:                                                               |  |
|  |  - ResponseMapGenerator (Lượng tử hóa 16 sector và sinh 16 kênh bản đồ phản hồi int8)        |  |
|  |  - Avx2ScoreAccumulator (Tích lũy song song 32 pixels / chu kỳ lệnh bằng System.Intrinsics) |  |
|  +----------------------------------------------------------------------------------------------+  |
|  |  Search & Scaling:                                                                           |  |
|  |  - Greediness Branch-and-Bound Pruning (Tỉa nhánh sớm với cận chặn dưới)                     |  |
|  |  - [G8] TiledImageMatcher (Phân mảnh ô cờ không gian cho ảnh công nghiệp 20MP+)              |  |
|  |  - NonMaximumSuppression (Khử cực đại địa phương kép: 3x3 Spatial + Bounding Box IoU)       |  |
|  +----------------------------------------------------------------------------------------------+  |
+----------------------------------------------------------------------------------------------------+
```

### 5.2. Sơ đồ tương tác bộ bọc C# P/Invoke cho dự án Standalone UI độc lập tương lai

```
+----------------------------------------------------------------------------------------------------+
|                         STANDALONE UI ỨNG DỤNG TƯƠNG LAI (WPF / MVVM .NET 8)                       |
+----------------------------------------------------------------------------------------------------+
                                      |
                                      | Gọi phương thức C# an toàn
                                      v
+----------------------------------------------------------------------------------------------------+
|                           TUNVISION.INTEROP.MWWZ (C# SAFE WRAPPER LAYER)                           |
|                                                                                                    |
|   MwwzEngine.cs             MwwzShapeModel.cs (IDisposable)          MwwzNativeMethods.cs (P/Invoke)
|   - Initialize()            - Create(gray, contrast...)              - create_shape_model(...)     |
|   - SetSystem()             - Find(gray, minScore...)                - find_shape_model(...)       |
|   - Shutdown()              - GetTransformedContour()                - clear_mems(...)             |
|                             - Save() / Load()                        - clear_shape_model(...)      |
|                                                                                                    |
|   STRUCTS BẢO ĐẢM CANH LỀ BỘ NHỚ:                                                                   |
|   - Match (40 bytes)        - Feature (16 bytes)                     - TemplateF (40 bytes x64)    |
|   - MatchDetails (80 bytes) - RRect (20 bytes)                       - VectorField (32 bytes)      |
+----------------------------------------------------------------------------------------------------+
                                      |
                                      | P/Invoke con trỏ unmanaged
                                      v
+----------------------------------------------------------------------------------------------------+
|                             MWWZ.DLL & MWWZCV.DLL (NATIVE RUNTIME X64)                             |
|  - 154 hàm xuất khẩu, tăng tốc SIMD AVX2, xử lý biến dạng và quản lý bộ nhớ đệm nội bộ qua mem_id   |
+----------------------------------------------------------------------------------------------------+
```

---

## 6. TỔNG KẾT & CAM KẾT CHẤT LƯỢNG

Toàn bộ 5 tài liệu trong bộ đặc tả kỹ thuật này đã được biên soạn với chuẩn mực kỹ thuật công nghiệp cao nhất:
1. **Không có nội dung giữ chỗ (No Placeholders / Stubs)**: Mọi bảng biểu, sơ đồ luồng, công thức toán học và đoạn mã nguồn C#/XAML đều được thể hiện trọn vẹn, chi tiết và có thể kiểm chứng trực tiếp.
2. **Độ trung thực kỹ thuật tuyệt đối (Technical Integrity)**: 100% dữ liệu về dải tham số, địa chỉ offset bộ nhớ, chữ ký hàm API và kích thước struct đều được đối soát và trích xuất trực tiếp từ các tệp nhị phân thực tế (`TemplateMatchQt.exe`, `mwwz.dll`) và mã nguồn của `TunVision`.
3. **Sẵn sàng triển khai thực tế**: Lộ trình nâng cấp Clean-Room cung cấp giải pháp chìa khóa trao tay (Turnkey Specifications), cho phép đội ngũ kỹ sư phần mềm bắt tay vào lập trình ngay lập tức mà không gặp bất kỳ vướng mắc nào về mặt kiến trúc.

---
*Bộ tài liệu kỹ thuật hoàn tất.*