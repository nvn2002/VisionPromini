# BÁO CÁO PHÂN TÍCH CHUYÊN SÂU: ĐỐI CHIẾU KIẾN TRÚC GIAO DIỆN (UI/UX), QUY TRÌNH KIỂM TRA (INSPECTION WORKFLOW) VÀ MA TRẬN KHOẢNG CÁCH TÍNH NĂNG GIỮA TEMPLATEMATCHQT.EXE VÀ TUNVISION

**Dự án**: Nghiên cứu & Nâng cấp Hệ thống So khớp Mẫu Hình học Công nghiệp (Geometric Shape Matching Platform)  
**Tài liệu**: Phân tích Khoảng cách Giao diện và Quy trình Đo kiểm (UI & Inspection Workflow Gap Analysis)  
**Mã tài liệu**: `01_ui_inspection_gap_analysis.md`  
**Đường dẫn lưu trữ**: `d:\tunvision\mwwz-samples\docs\01_ui_inspection_gap_analysis.md`  
**Ngày phát hành**: 15/11/2026  
**Ngôn ngữ**: Tiếng Việt chuyên ngành Thị giác máy tính & Tự động hóa công nghiệp (Machine Vision & Industrial Automation)  

---

## MỤC LỤC

1. [TỔNG QUAN HỆ THỐNG VÀ PHƯƠNG PHÁP LUẬN KHẢO SÁT](#1-tổng-quan-hệ-thống-và-phương-pháp-luận-khảo-sát)
   - 1.1. Bối cảnh nghiên cứu và đối tượng so sánh
   - 1.2. Bằng chứng kỹ thuật thực nghiệm và dữ liệu nguồn
   - 1.3. Phương pháp luận đối soát chéo và dịch ngược
2. [KIẾN TRÚC GIAO DIỆN NGƯỜI DÙNG CỦA TEMPLATEMATCHQT.EXE](#2-kiến-trúc-giao-diện-người-dùng-của-templatematchqtexe)
   - 2.1. Triết lý thiết kế công cụ chuyên dụng (Single-Purpose Standalone Tool)
   - 2.2. Bố cục không gian làm việc và phân bố widget trên Qt5
   - 2.3. Vùng đồ họa chính (Main Graphics Viewport) và lớp phủ Heads-Up Display (HUD)
   - 2.4. Bảng điều khiển tham số dọc bên phải (Right Control Sidebar)
   - 2.5. Khung xem trước mô hình biệt lập trên nền cờ ca-rô (Checkerboard Preview Widget)
   - 2.6. Thanh trạng thái công nghiệp và điều khiển chu kỳ máy
3. [KIẾN TRÚC GIAO DIỆN NGƯỜI DÙNG CỦA HỆ THỐNG TUNVISION](#3-kiến-trúc-giao-diện-người-dùng-của-hệ-thống-tunvision)
   - 3.1. Triết lý thiết kế xưởng thị giác tích hợp (Multi-Tool Pipeline Studio)
   - 3.2. Cấu trúc đồ thị DAG (Directed Acyclic Graph) và cơ chế luân chuyển dữ liệu
   - 3.3. Vùng tương tác thị giác (VisionCanvas) và cơ chế kết xuất WPF DrawingContext
   - 3.4. Thanh ngữ cảnh (Context Toolbar) và cửa sổ cấu hình thông số (NodeSettingsWindow)
   - 3.5. Hệ thống bảng dữ liệu đo kiểm song song
4. [MA TRẬN ĐỐI CHIẾU TÍNH NĂNG TOÀN DIỆN 35 TIÊU CHÍ (COMPREHENSIVE FEATURE MATRIX)](#4-ma-trận-đối-chiếu-tính-năng-toàn-diện-35-tiêu-chí-comprehensive-feature-matrix)
5. [PHÂN TÍCH CHUYÊN SÂU 8 LỖ HỔNG CHỨC NĂNG CỐT LÕI (G1 - G8)](#5-phân-tích-chuyên-sâu-8-lỗ-hổng-chức-năng-cốt-lõi-g1---g8)
   - 5.1. Lỗ hổng G1: Thiếu Heads-Up Display (HUD) hiển thị thông số điểm ảnh trên Canvas
   - 5.2. Lỗ hổng G2: Thiếu khung xem trước mô hình mẫu biệt lập (Dedicated Model Preview Widget)
   - 5.3. Lỗ hổng G3: Thiếu hệ thống nút bấm tự động cân bằng tham số màu (toolBtnA1/A2/A3) trên UI
   - 5.4. Lỗ hổng G4: Thiếu cơ chế xem trước biên thời gian thực khi tinh chỉnh độ tương phản (Live Contrast Edge Preview)
   - 5.5. Lỗ hổng G5: Hạn chế công cụ vẽ ROI tương tác (Thiếu Rotated Rectangle và Polygon trên Canvas)
   - 5.6. Lỗ hổng G6: Thiếu bộ lọc tùy biến lớp phủ kết quả (Draw Modes: NoScore / Score / All)
   - 5.7. Lỗ hổng G7: Thiếu tham số điều khiển tầng kim tự tháp kết thúc (LowestPyramid / EndPyramidLevel)
   - 5.8. Lỗ hổng G8: Thiếu cơ chế phân vùng xử lý cho ảnh siêu độ phân giải (Tiled Matching / Division)
6. [ĐỐI CHIẾU QUY TRÌNH KIỂM TRA (INSPECTION MODES & WORKFLOWS)](#6-đối-chiếu-quy-trình-kiểm-tra-inspection-modes--workflows)
   - 6.1. Quy trình kiểm tra đơn bước và vòng lặp kiểm tra liên tục
   - 6.2. Quy trình so khớp đa mô hình đồng thời (Multi-Template Matching)
   - 6.3. Quy trình xử lý biến dạng và co giãn bất đẳng hướng
   - 6.4. Đánh giá trải nghiệm người dùng biên tập và che mặt nạ mẫu (Feature Masking UX)
7. [KẾT LUẬN VÀ ĐỊNH HƯỚNG NÂNG CẤP KIẾN TRÚC](#7-kết-luận-và-định-hướng-nâng-cấp-kiến-trúc)

---

## 1. TỔNG QUAN HỆ THỐNG VÀ PHƯƠNG PHÁP LUẬN KHẢO SÁT

### 1.1. Bối cảnh nghiên cứu và đối tượng so sánh

Trong kỷ nguyên công nghiệp 4.0, các dây chuyền lắp ráp vi mạch bán dẫn tử, đóng gói bao bì dược phẩm, và chế tạo cơ khí chính xác đòi hỏi hệ thống thị giác máy (Machine Vision Inspection System) không chỉ dừng lại ở độ chính xác cấp độ subpixel của giải thuật so khớp, mà còn phụ thuộc sâu sắc vào **năng lực tương tác giao diện người máy (HMI/UI)**, **sự tiện lợi của quy trình huấn luyện mẫu (Template Training Workflow)**, và **tốc độ phản hồi chẩn đoán trực quan thời gian thực (Real-time Diagnostics)**.

Tài liệu này thực hiện phân tích đối chiếu chuyên sâu giữa hai nền tảng thị giác máy:
1. **Hệ thống tham chiếu chuẩn công nghiệp**: Ứng dụng thực thi `TemplateMatchQt.exe` (Bản dựng 64-bit ngày 06/11/2026, xây dựng trên khung lập trình C++ Qt5, kết nối nhân xử lý hình học hiệu năng cao `mwwz.dll` và OpenCV 4.6.0). Đây là công cụ đo kiểm công nghiệp đại diện cho tư duy tối ưu hóa đơn tác vụ (Single-Task Specialization), tập trung toàn lực vào tốc độ và sự tiện dụng tối đa cho tác vụ so khớp hình học.
2. **Hệ thống phần mềm mục tiêu nâng cấp**: Nền tảng `TunVision` (Bao gồm các module `TunVision.Core`, `TunVision.UI`, `TunVision.Tests`, xây dựng trên C# .NET 8 WPF). Đây là môi trường phòng thí nghiệm thị giác máy đa công cụ theo kiến trúc đồ thị có hướng không chu trình (Directed Acyclic Graph - DAG), cho phép tích hợp chuỗi công cụ xử lý từ Định vị mẫu (Align), Đo lường thước kẹp (Caliper), Phân tích vùng nhị phân (Blob) đến Đọc mã vạch (Barcode/QR) và Đưa ra quyết định logic (Verdict).

### 1.2. Bằng chứng kỹ thuật thực nghiệm và dữ liệu nguồn

Toàn bộ các phân tích, nhận định và ma trận đối chiếu trong tài liệu này đều được xây dựng dựa trên bằng chứng kỹ thuật trực tiếp, trích xuất từ các dữ liệu sau:
- **Ảnh chụp màn hình vận hành chuẩn**: `media_1794742572058_0c8a4605.png` ghi lại phiên làm việc thực tế của `TemplateMatchQt.exe` (tiêu đề `shape-based matching 2026-11-06 QQ:66557370 mwwztech@163.com`). Phiên làm việc thực hiện kiểm tra khay chip vi xử lý chứa logo chữ 'V' và mã khắc laser `AXP223 M4307BC K011` trên ảnh độ phân giải $2448 \times 2048$ pixel; tìm kiếm thành công 9 mẫu linh kiện trong thời gian thực thi cực ngắn là **10.3717 ms**.
- **Tập tin nhị phân PE64**: `C:\Users\ngocnv\Downloads\demo_qt_x64_2026.11.06\demo64\TemplateMatchQt.exe` (Kích thước 825,344 bytes) và `mwwz.dll` (Kích thước 1,049,600 bytes, xuất khẩu 154 hàm API).
- **Mã nguồn hoàn chỉnh của TunVision**: Thư mục `d:\tunvision\mwwz-samples\TunVision\`, tập trung vào các tệp giao diện cốt lõi:
  * `TunVision.UI\MainWindow.xaml` và `MainWindow.xaml.cs` (Quản lý cửa sổ chính, thanh công cụ, bảng danh sách node, canvas sơ đồ và canvas ảnh).
  * `TunVision.UI\Controls\VisionCanvas.cs` (Bộ điều khiển kết xuất đồ họa tùy biến kế thừa `FrameworkElement`, quản lý vẽ ảnh gốc và các vector overlay).
  * `TunVision.UI\NodeSettingsWindow.xaml` và `NodeSettingsWindow.xaml.cs` (Cửa sổ cấu hình tham số chuyên sâu cho từng node trong pipeline).
  * `TunVision.UI\ViewModels\MainViewModel.cs` (Mô hình ViewModel kết nối dữ liệu MVVM).
  * `TunVision.Core\ShapeMatching\` (Các lớp thuật toán huấn luyện và so khớp mẫu: `AlignTool.cs`, `ShapeModel.cs`, `ShapeTrainer.cs`, `ShapeMatcher.cs`).

### 1.3. Phương pháp luận đối soát chéo và dịch ngược

Tài liệu sử dụng phương pháp luận dịch ngược tĩnh (Static Reverse Engineering) kết hợp phân tích siêu dữ liệu Qt Meta-Object (`QMetaObject`, `QMetaMethod`, danh sách tín hiệu/khe cắm Signals/Slots, bố cục Widget ID và cấu trúc offset con trỏ bộ nhớ). Kết quả phân tích nhị phân được đối soát chéo từng dòng lệnh (Cross-Audit) với mã nguồn XAML và C# của TunVision, đảm bảo mọi kết luận đều có chứng cứ xác thực, định vị chính xác tên biến, hàm số và lớp đối tượng liên quan.

---

## 2. KIẾN TRÚC GIAO DIỆN NGƯỜI DÙNG CỦA TEMPLATEMATCHQT.EXE

### 2.1. Triết lý thiết kế công cụ chuyên dụng (Single-Purpose Standalone Tool)

Khác với các hệ thống phần mềm thị giác máy đa năng dạng xưởng làm việc (Studio/Workbench), `TemplateMatchQt.exe` được phát triển theo triết lý **Công cụ chuyên dụng tối giản (Single-Purpose Industrial Tool)**. Tất cả mọi không gian hiển thị, tương tác chuột, bố trí nút bấm và bảng nhập liệu đều hướng đến một mục tiêu duy nhất: **Giúp kỹ sư vận hành dây chuyền nạp mẫu, tinh chỉnh độ tương phản, huấn luyện mô hình và kiểm tra so khớp trên sản phẩm thật chỉ trong vòng dưới 60 giây ngay trên một màn hình duy nhất mà không cần mở bất kỳ hộp thoại phụ nào**.

### 2.2. Bố cục không gian làm việc và phân bố widget trên Qt5

Dựa trên việc bóc tách cấu trúc hàm dựng `Ui::TemplateMatchQt::setupUi` (RVA `0x140045e00` - `0x14004a500`), cấu trúc tổng thể của cửa sổ chính được phân chia theo dạng lưới hai cột bất đối xứng:

```
+---------------------------------------------------------------------------------------+--------------------+
| shape-based matching 2026-11-06 QQ:66557370 mwwztech@163.com                          | _   []   X         |
+---------------------------------------------------------------------------------------+--------------------+
|                                                                                       | [Create Model]     |
|                                                                                       |  ContrastLow [ 39]■| (toolBtnA1 Cyan)
|                                                                                       |  ContrastHigh[ 52]■| (toolBtnA2 Green)
|                     VÙNG ĐỒ HỌA TRUNG TÂM (MAIN VIEWPORT)                             |  NumLevels   [  0]■| (toolBtnA3 Red)
|                                                                                       |  MinContrast [  9] |
|                     QGraphicsView / QGraphicsScene                                    |  Metric [polarity]▼|
|                                                                                       |  MinCompSize [ 16] |
|                     - Zoom tại tâm con trỏ chuột                                      |  Length1 [0] L2 [0]|
|                     - Pan kéo lia ảnh mượt mà                                         +--------------------+
|                     - Tương tác Gizmo (RectangleR, Polygon)                           | [Search Model]     |
|                                                                                       |  AngleStart [ 0.0] |
|             Lớp phủ vector độ nét cao:                                                |  AngleExtent[360.0]|
|             - Đường bao vector mô hình màu xanh lá (Steger Contour)                   |  ScaleMin   [ 1.0] |
|             - Hạt đặc trưng chẩn đoán màu đỏ (Feature Outliers)                       |  ScaleMax   [ 1.0] |
|             - Chữ thập đỏ tâm đối tượng kèm số thứ tự (+ 7, + 6,...)                  |  MinScore   [0.50] |
|                                                                                       |  MaxOverlap [0.50] |
|                                                                                       |  Greediness [0.75] |
|                                                                                       |  NumMatches [   0] |
|                                                                                       |  MaxDeform  [ 0  ]▼|
|                                                                                       |  LowestPyr  [ 1  ] |
|                                                                                       | [x]CrossBorder SMode|
|                                                                                       |  Draw [NoScore]▼SubP|
|                                                                                       | [ ]ROI [0.50] [10] |
|                                                                                       | [CLS]        [More]|
|                                                                                       +--------------------+
|                                                                                       | [LoadDst] [Create] |
|                                                                                       | [LoadSrc] [Search] |
|                                                                                       | [SearchBig][Multi] |
|                                                                                       | [LoadMdl] [SaveMdl]|
|                                                                                       | [ClearModels]      |
|                                                                                       +--------------------+
|  [HUD Canvas Overlay]                                                                 | [Model Preview]    |
|   W:2448, H:2048 | X:989, Y:44 | R:11, G:11, B:11                                     |    Checkerboard    |
|                                                                                       |   Isolate Contour  |
+---------------------------------------------------------------------------------------+--------------------+
| search model succeeded, 9 matches found in 10.3717 ms                                 | [Do]          [EN] |
+---------------------------------------------------------------------------------------+--------------------+
```

### 2.3. Vùng đồ họa chính (Main Graphics Viewport) và lớp phủ Heads-Up Display (HUD)

1. **Kiến trúc đồ họa Scene-View**:
   - Sử dụng cặp thành phần `QGraphicsView` và `QGraphicsScene`, cho phép quản lý hàng chục nghìn đối tượng vector mà không làm nghẽn luồng xử lý giao diện (GUI Thread).
   - Hệ thống item đồ họa tùy biến: `ImageItem` (kết xuất ảnh bitmap mức xám/màu), `DrawItem` (vẽ đường bao contour vector và chữ thập tâm), và `ControlItem` (các handle kéo thả, xoay góc tương tác của ROI).
2. **Thanh thông tin trực tiếp Heads-Up Display (HUD)**:
   - Được neo cứng tại góc dưới cùng bên trái của vùng đồ họa.
   - Định dạng chuỗi nội bộ trích xuất từ nhị phân: `W:%1,H:%2 | X:%3,Y:%4 | R:%5,G:%6,B:%7`.
   - Trong ảnh thực nghiệm, HUD hiển thị trực tiếp: `W:2448, H:2048 | X:989, Y:44 | R:11, G:11, B:11`.
   - **Giá trị thực tiễn**: Kỹ sư vận hành có thể di chuột đến bất kỳ vị trí nào trên biên hoặc nền ảnh để đọc ngay lập tức giá trị mức xám thực tế. Điều này giúp loại bỏ hoàn toàn việc "đoán mò" khi thiết lập ngưỡng tương phản `ContrastLow` và `ContrastHigh`.

### 2.4. Bảng điều khiển tham số dọc bên phải (Right Control Sidebar)

Bảng điều khiển chiếm khoảng $25\%$ chiều rộng giao diện, được phân chia thành 3 nhóm chức năng rõ ràng từ trên xuống dưới:

1. **Khối Tạo mô hình (Create Model Group)**:
   - Tập hợp toàn bộ tham số trích xuất biên hình học: `ContrastLow`, `ContrastHigh`, `NumLevels`, `MinContrast`, `Metric`, `MinCompSize`, `Length1`, `Length2`.
   - **Tích hợp 3 nút công cụ màu chỉ báo trạng thái tự động cân bằng**:
     * `toolBtnA1` (Màu Cyan / Xanh lơ): Nằm cạnh `ContrastLow`, kích hoạt thuật toán đo mức tương phản biên thấp.
     * `toolBtnA2` (Màu Green / Xanh lá): Nằm cạnh `ContrastHigh`, kích hoạt thuật toán đo mức tương phản biên cao.
     * `toolBtnA3` (Màu Red / Đỏ): Nằm cạnh `NumLevels`, kích hoạt tính toán số tầng kim tự tháp tối ưu.
2. **Khối Tìm kiếm mô hình (Search Model Group)**:
   - Tập hợp các tham số điều khiển không gian trạng thái: `AngleStart`, `AngleExtent`, `ScaleMin`, `ScaleMax`, `MinScore`, `MaxOverlap`, `Greediness`, `NumMatches`, `MaxDeformation`, `LowestPyramid`, `Cross Img Border`, `Draw`, `SubPixel`, `ScaleMode`.
   - Lưới nút bấm tác vụ $3 \times 3$ nằm ngay phía dưới, cho phép thao tác một chạm:
     * Hàng 1: `LoadDstImg` (Nạp ảnh kiểm tra), `CreateModel` (Tạo mô hình từ ROI).
     * Hàng 2: `LoadSrcImg` (Nạp ảnh mẫu gốc), `SearchModel` (Khớp mô hình đơn).
     * Hàng 3: `SearchModel(Big)` (Khớp trên ảnh siêu lớn), `SearchModels` (Khớp đa mẫu đồng thời).
     * Hàng 4: `LoadModel` (Nạp tệp `.shm`), `SaveModel` (Lưu tệp `.shm`).
     * Hàng 5: `ClearModels` (Giải phóng toàn bộ mô hình khỏi bộ nhớ RAM).

### 2.5. Khung xem trước mô hình biệt lập trên nền cờ ca-rô (Checkerboard Preview Widget)

Nằm ở góc dưới cùng bên phải của Sidebar là một `QGraphicsView` thu nhỏ độc lập. Khi một mô hình được tạo hoặc nạp từ đĩa, đường bao vector của mô hình được hiển thị biệt lập trên nền họa tiết cờ ca-rô xám-trắng (Alpha Checkerboard Pattern).  
**Ý nghĩa vận hành**: Giúp người dùng quan sát hình học thuần túy của mẫu đã trích xuất mà không bị phân tâm bởi các chi tiết phức tạp của ảnh nền phôi.

### 2.6. Thanh trạng thái công nghiệp và điều khiển chu kỳ máy

Thanh trạng thái trải dài dưới đáy màn hình với nền xanh dương công nghiệp nổi bật:
- Hiển thị văn bản kết quả định lượng: `search model succeeded, 9 matches found in 10.3717 ms`.
- Tích hợp 2 nút điều khiển nhanh ở góc phải:
  * Nút `Do` (`btnDo`): Chuyển đổi giữa chế độ kích hoạt đơn bước và chế độ kiểm tra lặp liên tục siêu tốc độ native (không có độ trễ nghỉ nhân tạo).
  * Nút `EN` / `中` (`btnLang`): Chuyển đổi song ngữ Anh - Trung ngay lập tức trong bộ nhớ mà không cần khởi động lại phần mềm.

---

## 3. KIẾN TRÚC GIAO DIỆN NGƯỜI DÙNG CỦA HỆ THỐNG TUNVISION

### 3.1. Triết lý thiết kế xưởng thị giác tích hợp (Multi-Tool Pipeline Studio)

Trái ngược với cách tiếp cận đơn mục tiêu của `TemplateMatchQt.exe`, `TunVision` được định vị là một **Xưởng phần mềm thị giác máy chuyên nghiệp (Integrated Vision Studio)**. TunVision giải quyết bài toán kiểm tra toàn diện trên dây chuyền, nơi một sản phẩm không chỉ cần định vị vị trí (Shape Alignment) mà còn cần kiểm tra kích thước ren ốc (Caliper), đo diện tích lớp keo dán (Blob), quét mã ma trận dữ liệu (Barcode/QR DataMatrix), và kết hợp các điều kiện logic để phân loại sản phẩm Đạt/Không Đạt (OK/NG Verdict).

### 3.2. Cấu trúc đồ thị DAG (Directed Acyclic Graph) và cơ chế luân chuyển dữ liệu

Không gian làm việc của TunVision được tổ chức xung quanh mô hình đồ thị có hướng không chu trình:
- Mỗi công cụ xử lý ảnh là một nút đồ thị (`GraphNode`), lưu trữ trong `TunVision.Core\Graph\PipelineGraph.cs`.
- Tọa độ đầu ra của node định vị `ShapeMatchNode` (bao gồm vị trí tâm $X, Y$ và góc xoay $\theta$) được tự động chuyển đổi thành ma trận biến đổi afin `VisionTransform` thông qua `PathResolver.cs` để bù trừ tọa độ cho các node đo lường phía sau (`CaliperNode`, `RoiRectangleNode`).

```
+--------------------------------------------------------------------------------------------------------------------+
| Smart Vision / TunVision Studio                                                                          _  []  X  |
+--------------------------------------------------------------------------------------------------------------------+
| [📄 Mới ▾] [📂 Mở] [💾 Lưu] [🖼 Mở ảnh] | [▶ Chạy F5] [⏹ Dừng] [⟳ Chạy liên tục] nghỉ [250] ms | [x]Chỉ overlay node |
| [✏ Vẽ ROI/caliper] [x]Tự chạy lại khi thả chuột | [Vừa khung] Zoom 100%               | [⚙ Cài đặt node] [🇻🇳 VI] [🇬🇧 EN]|
+-------------------+------------------------------------------+-----------------------------------------------------+
| CÔNG CỤ (Palette) | SƠ ĐỒ KHỐI DAG (GraphCanvasControl)      | XEM TRƯỚC ẢNH & THAO TÁC NGỮ CẢNH (VisionCanvas)   |
| [🔍 Tìm node...]  |                                          | Contour: (•)Chọn ROI ( )Kéo/Pan ( )Xóa cọ ( )Xóa khung |
| ▾ Phát hiện (2)   |   [SourceImage] ➔ [ShapeMatchNode]        | [Tạo template] [Hiện điểm] [Hoàn tác] [Khôi phục]  |
|   [Retainers]     |         |               |                    | [Đặt gốc] [◀ Ảnh trước] [Ảnh sau ▶]                |
|   [Cocoa]         |         v               v                    | +-------------------------------------------------+ |
| ▾ Đo lường (3)    |   [RoiRectNode]   [CaliperNode]          | |  VisionCanvas (WPF DrawingContext)              | |
|   [Caliper Ring]  |                         |                    | |  - Vẽ ảnh gốc BitmapSource                      | |
|   [Caliper Line]  |                         v                    | |  - Lớp phủ vector biên xanh lá / cyan           | |
| ▾ Nhị phân Blob   |                 [VerdictNode]            | |  - Hộp định vị vàng / cam                       | |
| ▾ Mã vạch / QR    |                                          | |  - Hệ trục tọa độ vật thể (Đỏ X, Xanh Y)       | |
|                   |                                          | |                                  [ OK  75 ms ]  | |
|                   | [HUD Graph] X: 521 - Y: 982              | +-------------------------------------------------+ |
|                   |                                     [⛶]  | BẢNG KẾT QUẢ NODE         | BẢNG SỐ ĐO NODE ĐANG CHỌN |
|                   |                                          | Node | KQ | ms | Ghi chú   | Số đo         | Giá trị   |
|                   |                                          | Align| OK | 18 | Score 0.98| Match.Score   | 0.979     |
+-------------------+------------------------------------------+----------------------------+---------------------------+
| Sẵn sàng                                                                                                              |
+-----------------------------------------------------------------------------------------------------------------------+
```

### 3.3. Vùng tương tác thị giác (VisionCanvas) và cơ chế kết xuất WPF DrawingContext

Tại `TunVision.UI\Controls\VisionCanvas.cs`, việc kết xuất đồ họa được tối ưu hóa ở mức thấp thông qua việc ghi đè phương thức `OnRender(DrawingContext dc)`:
- Chuyển đổi hệ tọa độ màn hình sang tọa độ ảnh thông qua ma trận `MatrixTransform` (hỗ trợ phóng to `ZoomLevel` và tịnh tiến `PanOffset`).
- Vẽ trực tiếp hình ảnh thông qua `dc.DrawImage(CurrentPreviewSource, rect)`.
- Kết xuất các vector kết quả của node được chọn:
  * Phân biệt rõ mẫu chính (Primary Match: Màu xanh lá cây `#00E676`) và các mẫu phụ (Secondary Matches: Màu Cyan `#00E5FF`).
  * Vẽ hệ trục tọa độ vật thể 2D hoàn chỉnh: Trục X màu đỏ, trục Y màu xanh lá cây, gốc tọa độ màu vàng.
  * Hiển thị hộp bao chữ nhật xoay (Rotated Bounding Box) màu cam.
  * Hiển thị nhãn thông tin dạng huy hiệu: `#1 0.98 (In:97%)`.
  * Hiển thị huy hiệu Verdict lớn ở góc trên bên phải: `OK 75 ms` hoặc `NG 75 ms`.

### 3.4. Thanh ngữ cảnh (Context Toolbar) và cửa sổ cấu hình thông số (NodeSettingsWindow)

1. **Thanh công cụ ngữ cảnh bên trên `VisionCanvas`**:
   - Tích hợp các nút thao tác nhanh: Bật chế độ vẽ ROI, công cụ xóa cọ (`BrushErase`), xóa khung (`BoxErase`), nút "Tạo template từ ROI", nút "Hoàn tác xóa (Ctrl+Z)", nút "Khôi phục điểm", và nút "Đặt gốc = kết quả khớp".
2. **Cửa sổ cấu hình tham số riêng biệt (`NodeSettingsWindow.xaml`)**:
   - Nháy đúp vào một thẻ node trên đồ thị sẽ mở ra cửa sổ cấu hình dạng modal tối màu (kích thước $440 \times 640$).
   - Với `ShapeMatchNode`, cửa sổ cung cấp các trường nhập liệu số cho: `ContrastLow`, `ContrastHigh`, `MinScore`, `MaxOverlap`, `Greediness`, `AngleStartDeg`, `AngleExtentDeg`, `ScaleMin`, `ScaleMax`, `NumLevels`.

### 3.5. Hệ thống bảng dữ liệu đo kiểm song song

Phía dưới `VisionCanvas`, TunVision bố trí hai bảng dữ liệu `DataGrid` song song:
- **Bảng trạng thái node (Node Results Table)**: Liệt kê toàn bộ các công cụ trong quy trình, trạng thái hoàn thành (OK/NG), thời gian xử lý chi tiết từng bước tính bằng mili-giây, và ghi chú vắn tắt.
- **Bảng số đo chi tiết (Measurement Table)**: Hiển thị danh sách cặp khóa-giá trị của node đang chọn (ví dụ: `Match.X`, `Match.Y`, `Match.AngleDeg`, `Match.Score`, `Match.InlierRatio`).

---

## 4. MA TRẬN ĐỐI CHIẾU TÍNH NĂNG TOÀN DIỆN 35 TIÊU CHÍ (COMPREHENSIVE FEATURE MATRIX)

Dưới đây là ma trận đối chiếu toàn diện 35 tiêu chí kỹ thuật giữa `TemplateMatchQt.exe` và `TunVision`, phân loại theo 5 nhóm năng lực cốt lõi:

| STT | Nhóm năng lực | Tiêu chí so sánh chi tiết | TemplateMatchQt.exe | TunVision (Hiện trạng) | Đánh giá khoảng cách kỹ thuật |
| :---: | :--- | :--- | :---: | :---: | :--- |
| **I** | **Bố cục & Tương tác Canvas** | | | | |
| 1 | | Zoom mượt tại vị trí con trỏ chuột (Mouse Wheel Centered) | ✅ Có | ✅ Có | **Tương đương**: Cả hai đều hỗ trợ zoom tỉ lệ chuẩn xác tại tâm chuột. |
| 2 | | Pan kéo lia ảnh mượt mà bằng chuột giữa hoặc giữ chuột trái | ✅ Có | ✅ Có | **Tương đương**: Tương tác lia khung nhìn mượt mà. |
| 3 | | Nút "Vừa khung" (Fit to Screen) | ✅ Có | ✅ Có | **Tương đương**: Tự động cân chỉnh ảnh trọn vẹn trong viewport. |
| 4 | | **Thanh Heads-Up Display (HUD) thông số pixel (W, H, X, Y, R, G, B)** | ✅ **Có sẵn** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G1)**: TunVision chỉ có tọa độ chuột trên đồ thị DAG, thiếu HUD trên ảnh. |
| 5 | | Thực đơn ngữ cảnh chuột phải trên Canvas (Context Menu) | ✅ Có | ❌ Chưa có | Chưa có menu chuột phải để nạp/lưu ảnh nhanh trên Canvas TunVision. |
| 6 | | **Khung xem trước mô hình biệt lập với nền cờ ca-rô (Preview Widget)** | ✅ **Có sẵn** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G2)**: TunVision vẽ đè lên ảnh gốc, thiếu khung preview hình học độc lập. |
| 7 | | Chuyển đổi ngôn ngữ giao diện tức thời (Bilingual Switching) | ✅ Có (Nút EN/中) | ✅ Có (Nút VI/EN) | **Tương đương**: Cả hai đều chuyển đổi ngôn ngữ tức thì trong bộ nhớ. |
| **II** | **Chế độ kiểm tra (Inspection Modes)** | | | | |
| 8 | | Kích hoạt kiểm tra đơn bước (Single-step Trigger) | ✅ Có (`SearchModel`) | ✅ Có (Phím F5 / Run) | **Tương đương**: Kích hoạt khớp ảnh đơn lẻ. |
| 9 | | Kiểm tra lặp liên tục thời gian thực (Continuous Live Loop) | ✅ Có (`btnDo`) | ✅ Có (`StartContinuous`) | **Khác biệt triết lý**: TemplateMatchQt chạy max tốc độ native; TunVision cho phép đặt trễ nghỉ ms. |
| 10 | | Tự động chạy lại khi buông chuột khỏi Gizmo (`AutoRerunOnMouseUp`)| ❌ Không có | ✅ **Có sẵn** | **TunVision vượt trội**: Kéo thả ROI xong buông chuột là tự chạy lại tức thì. |
| 11 | | **Khớp đa mô hình đồng thời (Multi-Template Matching)** | ✅ **Có (`SearchModels`)** | ⚠️ Hạn chế (Tách N node)| **KHOẢNG CÁCH LỚN (M0)**: TemplateMatchQt chia sẻ kim tự tháp tính toán 1 lần; TunVision tốn N lần CPU. |
| 12 | | **Khớp mẫu trên ảnh siêu lớn (Extra-Large Image / Tiled Matching)**| ✅ **Có (`SearchBig`)** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G8)**: TunVision thiếu cơ chế chia tile tự động cho ảnh 20MP+. |
| 13 | | **Khớp mô hình biến dạng đàn hồi/phối cảnh (Deformable Matching)** | ✅ **Có (`MaxDeformation`)**| ❌ **Chưa có** | **KHOẢNG CÁCH LỚN**: TemplateMatchQt hỗ trợ Planar Homography và Local TPS mesh. |
| 14 | | **Khớp co giãn bất đẳng hướng (Anisotropic Scaling)** | ✅ **Có (`cboScaleMode`)** | ❌ Chưa có (Chỉ Isotropic) | **KHOẢNG CÁCH LỚN**: TunVision chỉ hỗ trợ tỉ lệ scale đồng nhất trên 2 trục. |
| 15 | | Duyệt ảnh trước / sau nhanh trong thư mục (Image Stepping) | ❌ Không có | ✅ **Có sẵn** | **TunVision vượt trội**: Hai nút `Ảnh trước` và `Ảnh sau` duyệt bộ test rất tiện lợi. |
| **III**| **Quy trình tạo mẫu & ROI** | | | | |
| 16 | | Khung chọn hình chữ nhật đứng (Axis-Aligned Rectangle ROI) | ✅ Có | ✅ Có | **Tương đương**: Kéo khung chữ nhật song song trục tọa độ. |
| 17 | | **Khung chọn hình chữ nhật xoay tự do (Rotated Rectangle - RectangleR)**| ✅ **Có (`Length1, 2`)** | ⚠️ Chỉ có trong code | **KHOẢNG CÁCH LỚN (G5)**: TunVision thiếu gizmo kéo xoay trực tiếp trên canvas. |
| 18 | | **Vùng chọn đa giác tự do (Arbitrary Polygon ROI)** | ✅ **Có (`PolygonItem`)** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G5)**: TunVision chưa cho phép click tạo đa giác nhiều đỉnh. |
| 19 | | Vùng chọn hình vành khuyên (Annular Ring / Concentric Circle ROI) | ✅ Có | ❌ Chỉ có trong Caliper | Thiếu công cụ tạo vành khuyên trực tiếp để huấn luyện mẫu bánh răng, ổ bi. |
| 20 | | Lưu ảnh mẫu ROI đã cắt ra tệp hình ảnh | ✅ Có (`SaveRoiImg`) | ❌ Chưa có nút riêng | TemplateMatchQt cho phép trích xuất nhanh ảnh phôi con ra ổ cứng. |
| 21 | | **Hệ thống nút tự động cân bằng tham số màu (toolBtnA1/A2/A3)** | ✅ **Có (Cyan/Xanh/Đỏ)** | ⚠️ Chỉ có hàm lõi | **KHOẢNG CÁCH LỚN (G3)**: Thuật toán có sẵn trong lõi nhưng thiếu nút bấm kết nối trên UI. |
| 22 | | **Xem trước đường biên thời gian thực khi chỉnh Contrast** | ✅ **Có (`upon_update`)** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G4)**: TunVision phải bấm nút tạo mẫu mới nhìn thấy đường biên. |
| 23 | | Cọ xóa điểm đặc trưng tự do (Interactive Brush Erase) | ❌ Không có | ✅ **Có sẵn cực tốt** | **TunVision vượt trội**: Cho phép chọn cọ 2-120 px xóa chi tiết laser, vân xước. |
| 24 | | Khung chữ nhật xóa cụm đặc trưng (Box Erase Tool) | ❌ Không có | ✅ **Có sẵn** | **TunVision vượt trội**: Kéo khung xóa hàng loạt điểm biên không mong muốn. |
| 25 | | Ngăn xếp hoàn tác và khôi phục (Undo Ctrl+Z / Reset Snapshot) | ❌ Không có | ✅ **Có sẵn** | **TunVision vượt trội**: Quản lý snapshot mô hình an toàn, hỗ trợ Ctrl+Z. |
| 26 | | Thiết lập lại gốc tọa độ mô hình (Set Model Origin) | ✅ Có (`set_origin`) | ✅ Có (`SetOriginFromMatch`) | **Tương đương**: Cho phép dời tâm định vị khỏi trọng tâm hình học. |
| 27 | | **Lựa chọn chế độ cực tính tương phản (Metric Polarity)** | ✅ Có ComboBox Metric | ⚠️ Có trong lõi, thiếu UI | Thuộc tính `Metric` trong `ShapeModel.cs` chưa được đưa ra giao diện người dùng. |
| 28 | | Xuất / Nhập tệp mô hình nhị phân chuẩn công nghiệp (`.shm`) | ✅ Có | ✅ Có | **Tương đương**: Đều hỗ trợ lưu trữ mô hình ra tệp nhị phân độc lập. |
| **IV** | **Hiển thị kết quả & Đồ họa** | | | | |
| 29 | | Đường bao vector mô hình mượt mà (Steger Subpixel Contour) | ✅ Có (Xanh lá cây) | ✅ Có (Inlier Green) | **Tương đương**: Đường cong biên vector sắc nét ôm khít vật thể. |
| 30 | | Phân biệt màu sắc trực quan giữa mẫu chính và mẫu phụ | ❌ Không phân biệt | ✅ **Có (Green / Cyan)** | **TunVision vượt trội**: Mẫu số 1 xanh lá, các mẫu từ 2 trở đi màu cyan. |
| 31 | | Hệ trục tọa độ cục bộ của vật thể 2D (Local Coordinate Frame) | ❌ Chỉ có dấu cộng đỏ | ✅ **Có (Red X, Green Y)** | **TunVision vượt trội**: Giúp kỹ sư nhận diện hướng xoay sản phẩm tức thời. |
| 32 | | Hộp bao chữ nhật xoay ôm khít vật thể (Rotated Bounding Box) | ⚠️ Chỉ vẽ khi bật cờ | ✅ **Có sẵn (Màu cam)** | **TunVision vượt trội**: Trực quan hóa không gian chiếm dụng của phôi. |
| 33 | | **Tùy chọn chế độ hiển thị lớp phủ (Draw: NoScore / Score / All)** | ✅ **Có (`cboShowMatches`)**| ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G6)**: TunVision luôn vẽ tất cả, gây rối mắt khi có hàng trăm linh kiện. |
| 34 | | **Định vị đối tượng cắt tràn viền ảnh (Cross Image Border)** | ✅ **Có (`chkBorderShape`)**| ❌ **Chưa có** | **Khoảng cách thuật toán**: TemplateMatchQt tìm được đối tượng bị mất 1 phần ở mép. |
| 35 | | Lớp phủ tiếp tuyến điểm biên ngoại lai (Outlier Tangent Vectors) | ❌ Không có | ✅ **Có sẵn (Màu đỏ)** | **TunVision vượt trội**: Giúp chẩn đoán chính xác vị trí linh kiện bị mẻ/xước. |
| **V**  | **Chẩn đoán & Hiệu năng** | | | | |
| 36 | | Độ phân giải hiển thị thời gian thực thi (Execution Time Precision) | ✅ 4 chữ số (`%.4f ms`)| ⚠️ 1 chữ số (`{0:F1} ms`)| TemplateMatchQt phản ánh chính xác biến động độ trễ ở mức micro-giây. |
| 37 | | Chẩn đoán mã lỗi chi tiết khi khớp thất bại (Diagnostic Error Codes)| ✅ Có (`err = %d`, para)| ⚠️ Chuỗi thông báo chung | TemplateMatchQt in rõ vi phạm tham số giúp kỹ sư sửa lỗi nhanh. |
| 38 | | **Điều khiển tầng kim tự tháp kết thúc (`LowestPyramid`)** | ✅ **Có (SpinBox)** | ❌ **Chưa có** | **KHOẢNG CÁCH LỚN (G7)**: TunVision luôn chạy xuống tầng 0, không có chế độ định vị thô siêu tốc. |
| 39 | | Huy hiệu kết quả tổng thể trực quan (OK/NG Status Badge) | ❌ Không có | ✅ **Có sẵn (OK/NG ms)** | **TunVision vượt trội**: Huy hiệu nổi góc màn hình chuẩn giao diện công nghiệp. |
| 40 | | Bảng dữ liệu thống kê số đo hình học đa kênh (Measurement Tables) | ❌ Không có | ✅ **Có sẵn 2 DataGrid** | **TunVision vượt trội**: Xuất đầy đủ số liệu đo lường cho các hệ thống PLC/MES. |

---

## 5. PHÂN TÍCH CHUYÊN SÂU 8 LỖ HỔNG CHỨC NĂNG CỐT LÕI (G1 - G8)

Dựa trên kết quả đối chiếu ma trận tính năng, nhóm khảo sát xác định **8 khoảng cách chức năng trọng yếu (G1 đến G8)** mà TunVision đang thiếu hụt so với `TemplateMatchQt.exe`. Dưới đây là phân tích chi tiết nguyên nhân kỹ thuật, hệ quả vận hành và đề xuất giải pháp kiến trúc:

### 5.1. Lỗ hổng G1: Thiếu Heads-Up Display (HUD) hiển thị thông số điểm ảnh trên Canvas

- **Hiện trạng kỹ thuật**:  
  Trong `TunVision.UI\MainWindow.xaml`, giao diện chỉ có nhãn `GraphCursorText` (`X: 521 - Y: 982`) để đo tọa độ con trỏ chuột trên canvas sơ đồ khối DAG. Trên `VisionCanvas.cs`, hoàn toàn không có bất kỳ thành phần nào hiển thị kích thước ảnh nguồn, tọa độ pixel thực tế hoặc mức xám của điểm ảnh dưới con trỏ chuột.
- **Hệ quả vận hành**:  
  Khi chiếu sáng thực tế bị thay đổi (bóng đèn LED suy hao hoặc ánh sáng nhà xưởng rọi vào), kỹ sư không có cách nào biết được cường độ mức xám của vùng phôi và vùng nền là bao nhiêu. Người dùng buộc phải ước lượng cảm tính các ngưỡng tương phản, dẫn đến việc thiết lập sai thông số.
- **Giải pháp kiến trúc**:  
  Xây dựng một thanh HUD bán trong suốt ở góc dưới cùng bên trái của `VisionCanvas`:
  ```
  W: 2448, H: 2048 | X: 989, Y: 44 | Gray: 11 (R: 11, G: 11, B: 11)
  ```
  Trong sự kiện `OnMouseMove`, tính toán tọa độ ma trận ngược `imgPos = InverseTransform.Transform(mousePos)` và truy vấn trực tiếp giá trị pixel từ `BitmapSource` hoặc OpenCV `Mat` nguồn để cập nhật thuộc tính `HudPixelInfoText`.

### 5.2. Lỗ hổng G2: Thiếu khung xem trước mô hình mẫu biệt lập (Dedicated Model Preview Widget)

- **Hiện trạng kỹ thuật**:  
  TunVision hiện kết xuất mô hình mẫu đè trực tiếp lên ảnh nguồn đang hiển thị trên `VisionCanvas`.
- **Hệ quả vận hành**:  
  Khi ảnh mẫu được chụp trên nền kim loại có vân phay xước phức tạp hoặc nhiều chi tiết cơ khí rối rắm, người dùng rất khó phân biệt được đường biên màu xanh lá cây thuộc về mô hình mẫu hay thuộc về chi tiết ảnh nền bên dưới. Không có không gian biệt lập để thẩm định chất lượng hình học của mẫu.
- **Giải pháp kiến trúc**:  
  Bổ sung một UserControl nhỏ (`ModelPreviewWidget`) tại góc dưới thanh ngữ cảnh hoặc trong `NodeSettingsWindow`. Widget này kết xuất danh sách `ModelContours` trên nền họa tiết cờ ca-rô xám-trắng (Checkerboard pattern với chu kỳ $8 \times 8$ pixel), cô lập hoàn toàn hình học của mẫu khỏi ảnh chụp phôi.

### 5.3. Lỗ hổng G3: Thiếu hệ thống nút bấm tự động cân bằng tham số màu (toolBtnA1/A2/A3) trên UI

- **Hiện trạng kỹ thuật**:  
  Trong mã nguồn `TunVision.Core\ShapeMatching\ShapeTrainer.cs`, hàm `AutoEstimateContrast` đã được cài đặt thuật toán phân vị gradient rất thông minh:
  ```csharp
  // ShapeTrainer.cs
  int p25 = percentiles[25]; // MinContrast sàn nhiễu
  int p70 = percentiles[70]; // ContrastLow ngưỡng dưới
  int p85 = percentiles[85]; // ContrastHigh ngưỡng trên
  int numLevels = ComputeOptimalPyramidLevels(roiWidth, roiHeight);
  ```
  Tuy nhiên, thuật toán này **bị giấu kín hoàn toàn trong tầng Core**. Trên giao diện `NodeSettingsWindow.xaml`, các ô nhập liệu số vẫn bắt buộc người dùng gõ tay thủ công.
- **Hệ quả vận hành**:  
  Người dùng phổ thông không biết giá trị bao nhiêu là tối ưu, làm giảm giá trị của thuật toán lõi đã phát triển.
- **Giải pháp kiến trúc**:  
  Bố trí 3 nút bấm vuông nhỏ dạng icon màu sắc ngay cạnh các ô nhập liệu trong `NodeSettingsWindow.xaml`:
  * Nút Cyan cạnh `ContrastLow`: Bấm vào tự động gán giá trị phân vị $P_{70}$.
  * Nút Green cạnh `ContrastHigh`: Bấm vào tự động gán giá trị phân vị $P_{85}$.
  * Nút Red cạnh `NumLevels`: Bấm vào tự động gán số tầng kim tự tháp tối đa theo công thức $L = \text{round}(\log_2(\min/16)) + 1$.

### 5.4. Lỗ hổng G4: Thiếu cơ chế xem trước biên thời gian thực khi tinh chỉnh độ tương phản (Live Contrast Edge Preview)

- **Hiện trạng kỹ thuật**:  
  Trong TunVision, khi người dùng thay đổi giá trị `ContrastLow` hoặc `ContrastHigh`, hệ thống không có bất kỳ phản hồi nào. Muốn biết đường biên có bắt đúng hay không, người dùng bắt buộc phải bấm nút "Tạo template từ ROI" để chạy lại toàn bộ quy trình huấn luyện nặng nề.
- **Hệ quả vận hành**:  
  Nếu đặt ngưỡng sai, người dùng phải lặp lại chu trình "Sửa số $\to$ Bấm nút tạo $\to$ Nhìn kết quả" hàng chục lần, gây ức chế và lãng phí thời gian căn chỉnh dây chuyền.
- **Giải pháp kiến trúc**:  
  Cài đặt cơ chế xem trước tương tác: Khi người dùng kéo thanh trượt Slider `ContrastLow` / `ContrastHigh`, hệ thống kích hoạt luồng xử lý nhẹ (`Task.Run`) gọi bộ trích xuất biên topo nhanh (`TopologicalContourExtractor`) trên ROI hiện tại và vẽ đường biên tạm thời màu vàng cam trực tiếp lên Canvas mà không làm đông cứng giao diện.

### 5.5. Lỗ hổng G5: Hạn chế công cụ vẽ ROI tương tác (Thiếu Rotated Rectangle và Polygon trên Canvas)

- **Hiện trạng kỹ thuật**:  
  Trên `VisionCanvas.cs`, công cụ tương tác chuột duy nhất hiện tại là kéo khung chữ nhật đứng trục (`DrawTemplateRoi` với cờ `_isDraggingMarquee`). Mặc dù trong lõi có cấu trúc `RoiRectangleNode` hỗ trợ thuộc tính xoay `AngleDeg`, nhưng trên Canvas người dùng **không có công cụ gizmo nào để xoay trực tiếp hình chữ nhật bằng chuột**, đồng thời chưa có công cụ vẽ Đa giác tự do (Polygon ROI).
- **Hệ quả vận hành**:  
  Khi linh kiện mẫu nằm nghiêng (ví dụ chip nằm chéo $45^\circ$ trên khay chuyền), khung chữ nhật đứng trục buộc phải mở rất rộng, khiến cho các linh kiện lân cận hoặc vân nền xung quanh bị lọt vào vùng mẫu, làm giảm độ đặc trưng của mô hình.
- **Giải pháp kiến trúc**:  
  Xây dựng lớp điều khiển tương tác Gizmo hoàn chỉnh:
  1. **Rotated Rectangle Gizmo (`RectangleR`)**: Cho phép click chuột xác định tâm $(x_c, y_c)$, kéo xác định bán trục dài `Length1`, bán trục rộng `Length2`, và nắm handle xoay ở đỉnh để xoay góc $\phi$ trực quan.
  2. **Polygon ROI Gizmo**: Cho phép click chuột liên tiếp để định nghĩa các đỉnh $(x_k, y_k)$ của đa giác bao sát các chi tiết cơ khí phức tạp.

### 5.6. Lỗ hổng G6: Thiếu bộ lọc tùy biến lớp phủ kết quả (Draw Modes: NoScore / Score / All)

- **Hiện trạng kỹ thuật**:  
  `VisionCanvas.cs` luôn luôn kết xuất đồng thời tất cả các thành phần: Đường bao vector, hệ trục tọa độ X/Y, hộp bao chữ nhật, và huy hiệu điểm số văn bản.
- **Hệ quả vận hành**:  
  Trong các bài toán kiểm tra mật độ cao (ví dụ kiểm tra khay chip bán dẫn chứa 100 linh kiện IC như trong ảnh thực nghiệm), việc vẽ hàng trăm nhãn văn bản và khung chữ nhật sẽ làm chúng đè lấn lên nhau, biến màn hình thành một mớ hỗn độn (Visual Clutter), che khuất toàn bộ bề mặt ảnh của sản phẩm.
- **Giải pháp kiến trúc**:  
  Bổ sung ComboBox `Chế độ hiển thị lớp phủ (Overlay Draw Mode)` trên thanh công cụ:
  * `NoScore` (Tối giản): Chỉ vẽ đường cong biên vector xanh lá và dấu chữ thập tâm thanh mảnh (đây chính là chế độ mặc định được `TemplateMatchQt.exe` sử dụng trong screenshot thực tế).
  * `Score` (Tiêu chuẩn): Vẽ đường biên, tâm và nhãn điểm số ngắn gọn.
  * `All` (Toàn diện): Vẽ đầy đủ đường biên, tâm, điểm số, hệ trục tọa độ 2D và hộp bao chữ nhật xoay.

### 5.7. Lỗ hổng G7: Thiếu tham số điều khiển tầng kim tự tháp kết thúc (LowestPyramid / EndPyramidLevel)

- **Hiện trạng kỹ thuật**:  
  Thuật toán so khớp `ShapeMatcher.cs` của TunVision luôn duyệt vòng lặp coarse-to-fine xuống tận tầng đáy (Level 0 - ảnh gốc nguyên bản). Không có tham số nào trên giao diện cho phép dừng tìm kiếm ở các tầng thu nhỏ.
- **Hệ quả vận hành**:  
  Trong nhiều bài toán công nghiệp (ví dụ cánh tay robot gắp phôi thô từ thùng hàng), độ chính xác vị trí chỉ cần trong khoảng $\pm 1 \dots \pm 2$ pixel, nhưng chu kỳ xử lý bắt buộc phải cực nhanh ($< 3 \text{ ms}$). Việc TunVision luôn ép chạy xuống tầng 0 làm mất đi cơ hội tăng tốc độ xử lý từ $300\% \dots 400\%$.
- **Giải pháp kiến trúc**:  
  Thêm thuộc tính `LowestPyramid` (mặc định = 1, tương đương tầng 0) vào `AlignTool.cs` và đưa điều khiển SpinBox lên `NodeSettingsWindow.xaml`. Khi kỹ sư thiết lập `LowestPyramid = 2`, thuật toán sẽ dừng ngay tại tầng thu nhỏ $1/2$, giảm thiểu tối đa thời gian tính toán.

### 5.8. Lỗ hổng G8: Thiếu cơ chế phân vùng xử lý cho ảnh siêu độ phân giải (Tiled Matching / Division)

- **Hiện trạng kỹ thuật**:  
  TunVision chỉ tiếp nhận ảnh đầu vào toàn phần và tính toán ma trận đạo hàm trên toàn bộ khung hình.
- **Hệ quả vận hành**:  
  Khi làm việc với các hệ thống camera công nghiệp độ phân giải cao ($20\text{ MP} \dots 65\text{ MP}$ như camera quét dòng quang học PCB), việc tính toán Sobel và tích lũy điểm trên toàn bộ ma trận ảnh khổng lồ sẽ gây nghẽn băng thông bộ nhớ RAM, sụt giảm tốc độ khung hình nghiêm trọng.
- **Giải pháp kiến trúc**:  
  Phát triển cơ chế chia lưới ô cờ không gian (`Tile Division`) tương tự hàm `find_shape_model_xl` trong `mwwz.dll`. Chia ảnh thành lưới $M \times N$ khối nhỏ với khoảng chồng lấn biên an toàn $d_{\text{same}} = \text{Diameter}_{\text{model}}$, cho phép xử lý song song trên đa luồng CPU và giảm dung lượng đỉnh của bộ nhớ cache.

---

## 6. ĐỐI CHIẾU QUY TRÌNH KIỂM TRA (INSPECTION MODES & WORKFLOWS)

### 6.1. Quy trình kiểm tra đơn bước và vòng lặp kiểm tra liên tục

1. **Kiểm tra đơn bước (Single Trigger)**:
   - `TemplateMatchQt.exe`: Nhấn nút `SearchModel` $\to$ Động cơ thực thi ngay lập tức và in thời gian thực thi lên Status Bar.
   - `TunVision`: Nhấn phím `F5` hoặc nút `▶ Chạy (F5)` $\to$ Bộ điều phối `GraphExecutor` duyệt cây đồ thị DAG từ node nguồn đến node kết thúc.
   - **Đánh giá**: Cả hai đều đáp ứng hoàn hảo nhu cầu kiểm tra thử nghiệm từng ảnh chụp.
2. **Vòng lặp kiểm tra liên tục (Continuous Live Loop)**:
   - `TemplateMatchQt.exe`: Nút `Do` trên thanh trạng thái kích hoạt vòng lặp `while(do_flag)` chạy với tốc độ native tối đa của CPU (chu kỳ lặp $10 \text{ ms}$, đạt $\sim 100 \text{ FPS}$).
   - `TunVision`: Hỗ trợ nút `⟳ Chạy liên tục` kết hợp ô nhập độ trễ nghỉ `ContinuousDelayMs` (từ 20 ms đến 10,000 ms).
   - **Đánh giá**: TunVision linh hoạt hơn và an toàn hơn cho hệ thống đa công cụ (tránh chiếm dụng $100\%$ CPU), trong khi TemplateMatchQt tối ưu hóa tốc độ cực hạn cho các ứng dụng đo lường camera tốc độ cao.
3. **Cơ chế tự động chạy lại khi hiệu chỉnh (Auto Rerun on Edit)**:
   - TunVision sở hữu tính năng vượt bậc `AutoRerunOnMouseUp`: Người dùng dùng chuột kéo dịch chuyển một node ROI hoặc điều chỉnh thước kẹp Caliper, ngay khi nhả chuột (`MouseUp`), toàn bộ pipeline tự động chạy lại và cập nhật kết quả tức thời. Đây là trải nghiệm người dùng hiện đại mà TemplateMatchQt không có.

### 6.2. Quy trình so khớp đa mô hình đồng thời (Multi-Template Matching)

Một trong những khác biệt lớn nhất về quy trình xử lý giữa hai nền tảng:
- **Cơ chế của `TemplateMatchQt.exe` (`SearchModels`)**:
  * Gọi trực tiếp API native `find_shape_models(..., int* model_ids, int model_num, ...)`.
  * Ảnh hiện trường chỉ cần xây dựng tháp kim tự tháp và tính toán 16 kênh Response Maps **đúng một lần duy nhất**. Toàn bộ $N$ mô hình khác nhau được duyệt song song trên cùng một cấu trúc dữ liệu chia sẻ này.
- **Cơ chế hiện tại của `TunVision`**:
  * TunVision không có cơ chế `SearchModels` gom cụm. Nếu muốn tìm 5 linh kiện khác nhau trên cùng một bảng mạch, người dùng buộc phải tạo 5 thẻ `ShapeMatchNode` riêng biệt trên sơ đồ DAG.
  * Hệ quả: Ảnh kiểm tra bị tính toán lại kim tự tháp và đạo hàm 5 lần, tiêu tốn CPU gấp 5 lần và thời gian xử lý tăng tuyến tính theo số lượng mẫu.

### 6.3. Quy trình xử lý biến dạng và co giãn bất đẳng hướng

- **Xử lý co giãn bất đẳng hướng (Anisotropic Scaling)**:
  * Trong thực tế sản xuất, sản phẩm nhựa đúc nhiệt hoặc phôi kim loại dập nóng thường bị co ngót theo một phương duy nhất (ví dụ co ngót $2\%$ theo chiều dài nhưng giữ nguyên chiều rộng).
  * `TemplateMatchQt.exe` hỗ trợ hoàn hảo chế độ `anisotropic` thông qua việc quét độc lập tỉ lệ hàng `ScaleR` và tỉ lệ cột `ScaleC`.
  * `TunVision` hiện chỉ hỗ trợ co giãn đẳng hướng (Isotropic: tỉ lệ chiều dài và chiều rộng bắt buộc phải bằng nhau), dẫn đến việc rớt điểm số nghiêm trọng khi sản phẩm bị co ngót không đều.
- **Xử lý biến dạng phối cảnh và biến dạng cục bộ (Deformable Matching)**:
  * Khi tem nhãn dán trên bề mặt cong hoặc linh kiện bị dập uốn nghiêng, `TemplateMatchQt.exe` kích hoạt bộ so khớp phối cảnh phẳng (Planar Homography qua hàm `find_planar_uncalib_deformable_model`) hoặc biến dạng lưới tự do (Local Deformable qua hàm `find_local_deformable_model`).
  * TunVision hiện chỉ hỗ trợ chuyển đổi vật rắn cứng 2D (Rigid 2D: Tịnh tiến X/Y và Xoay $\theta$).

### 6.4. Đánh giá trải nghiệm người dùng biên tập và che mặt nạ mẫu (Feature Masking UX)

Đây là phương diện mà **TunVision thể hiện sự vượt trội áp đảo so với TemplateMatchQt**:
- **Cọ xóa đặc trưng tự do (`BrushErase`)**:
  * Trên `TemplateMatchQt.exe`, người dùng không có công cụ cọ vẽ. Muốn xóa đặc trưng thừa, người dùng phải loại bỏ từng điểm qua ID hoặc vẽ thêm ROI trừ vùng.
  * Trên `TunVision`, người dùng chỉ cần chọn công cụ Cọ xóa, lăn chuột chỉnh bán kính cọ từ $2 \dots 120$ px, và di chuột qua các nét xước hoặc các chữ số laser nhảy liên tục (như mã `AXP223` trên chip IC) để xóa sạch các điểm biên này khỏi mô hình mẫu chỉ trong vài giây.
- **Ngăn xếp hoàn tác (`UndoStack`)**:
  * TunVision hỗ trợ phím tắt chuẩn công nghiệp `Ctrl+Z` và nút "Hoàn tác", cho phép quay lui từng bước thao tác xóa.
  * `TemplateMatchQt.exe` không có tính năng hoàn tác; nếu xóa nhầm, người dùng buộc phải tạo lại mẫu từ đầu.

---

## 7. KẾT LUẬN VÀ ĐỊNH HƯỚNG NÂNG CẤP KIẾN TRÚC

Qua khảo sát toàn diện và đối chiếu chuyên sâu giữa `TemplateMatchQt.exe` và `TunVision`:

1. **Về kiến trúc nền tảng**:
   - `TunVision` sở hữu một nền tảng kiến trúc hiện đại, linh hoạt và có khả năng mở rộng cao hơn hẳn so với `TemplateMatchQt.exe`. Kiến trúc đồ thị DAG modular, khả năng phối hợp đa công cụ (Align $\to$ Caliper $\to$ Blob $\to$ Barcode), công cụ xóa đặc trưng bằng cọ tự do (`BrushErase`), cơ chế hoàn tác `Ctrl+Z`, và hệ trục tọa độ vật thể 2D trực quan là những ưu thế vượt bậc của TunVision.
2. **Về sự tinh gọn và tiện ích vận hành đơn tác vụ**:
   - `TemplateMatchQt.exe` lại là một hình mẫu chuẩn mực về việc tối ưu hóa trải nghiệm người dùng cho một tác vụ thị giác chuyên biệt:
     * Thanh HUD hiển thị thông số pixel trực tiếp trên Canvas.
     * Hệ thống nút bấm màu tự động cân bằng tham số (`toolBtnA1/A2/A3`) và xem trước đường biên tức thì (`Live Contrast Preview`).
     * Hỗ trợ đầy đủ các hình thái ROI xoay (`RectangleR`), đa giác tự do (`Polygon`), và vành khuyên.
     * Tùy chọn bộ lọc lớp phủ hiển thị (`Draw: NoScore`) giúp giao diện luôn thoáng đãng và trực quan.
     * Khả năng kiểm soát tầng dừng kim tự tháp (`LowestPyramid`) giúp tối ưu hóa thời gian chu kỳ máy.
3. **Định hướng chiến lược**:
   - Việc nâng cấp TunVision giải quyết triệt để **8 lỗ hổng chức năng từ G1 đến G8** (theo phương pháp thiết kế sạch Clean-Room trên C# .NET 8 / WPF / OpenCvSharp mà hoàn toàn không phụ thuộc vào `mwwz.dll`) sẽ giúp TunVision trở thành một nền tảng thị giác máy công nghiệp toàn diện: vừa sở hữu sức mạnh kết hợp đồ thị của một Vision Studio cao cấp, vừa đạt được sự tinh tế, tiện dụng và tốc độ vượt trội của một công cụ đo kiểm chuyên dụng hàng đầu thế giới.

---
*Tài liệu kết thúc.*