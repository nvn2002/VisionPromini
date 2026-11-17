# BÁO CÁO KỸ THUẬT CHUYÊN SÂU: CƠ CHẾ BẢNG THAM SỐ, NGUYÊN LÝ TỰ ĐỘNG CÂN BẰNG (AUTO-BALANCING), BẢN CHẤT TOÁN HỌC VÀ CHIẾN LƯỢC TỐI ƯU HÓA CÔNG NGHIỆP TRONG TEMPLATEMATCHQT & MWWZ.DLL

**Dự án**: Nghiên cứu & Dịch ngược Động cơ So khớp Mẫu Hình học Chuẩn Công nghiệp  
**Tài liệu**: Cơ chế Bảng Tham số và Thuật toán Tự động Cân bằng (Parameter Mechanics & Auto-Balancing)  
**Mã tài liệu**: `02_parameter_balancing_mechanics.md`  
**Đường dẫn lưu trữ**: `d:\tunvision\mwwz-samples\docs\02_parameter_balancing_mechanics.md`  
**Ngày phát hành**: 15/11/2026  
**Ngôn ngữ**: Tiếng Việt chuyên ngành Thị giác máy tính & Giải thuật tối ưu hóa công nghiệp  

---

## MỤC LỤC TỔNG QUAN

1. [TỔNG QUAN HỆ THỐNG VÀ BẢN ĐỒ THAM SỐ GIAO DIỆN](#1-tổng-quan-hệ-thống-và-bản-đồ-tham-số-giao-diện)
   - 1.1. Kiến trúc phân tầng và vị trí của bộ tham số trong quy trình đo kiểm
   - 1.2. Bản đồ offset bộ nhớ của các widget điều khiển trong `TemplateMatchQt.exe`
2. [DANH MỤC VÀ ĐẶC TẢ CHI TIẾT TOÀN BỘ 24 THAM SỐ](#2-danh-mục-và-đặc-tả-chi-tiết-toàn-bộ-24-tham-số)
   - 2.1. Nhóm tham số Tạo mô hình mẫu (Create Model Parameters - 8 tham số)
   - 2.2. Nhóm tham số Tìm kiếm và Không gian trạng thái (Search Model Parameters - 13 tham số)
   - 2.3. Nhóm tham số Vùng quan tâm và Chẩn đoán điểm đặc trưng (ROI & Diagnostic Parameters - 3 tham số)
   - 2.4. Bảng tổng hợp đối soát vi kiến trúc (UI Control vs Native C++ Engine Struct)
3. [CƠ CHẾ VÀ CÔNG THỨC TOÁN HỌC TỰ ĐỘNG CÂN BẰNG (AUTO-BALANCING MECHANICS)](#3-cơ-chế-và-công-thức-toán-học-tự-động-cân-bằng-auto-balancing-mechanics)
   - 3.1. Thuật toán tự động tính tầng Kim tự tháp tối ưu (Auto Pyramid Depth Selection)
   - 3.2. Thuật toán tự động ước lượng bộ tứ ngưỡng tương phản (Auto Contrast & Component Size Estimation)
   - 3.3. Quy tắc suy biến ngưỡng vi phân động qua các tầng kim tự tháp (Pyramid Halving Rule)
   - 3.4. Ràng buộc tương hỗ Bán kính mô hình và Bước góc quét tự thích ứng (Auto Angle Step Delta)
   - 3.5. Cơ chế cắt tỉa nhánh tham lam sớm Branch-and-Bound (Greediness Pruning)
   - 3.6. Cơ chế nới lỏng ngưỡng điểm tầng kim tự tháp thô (Coarse Pyramid Score Relaxation)
   - 3.7. Bộ lọc triệt tiêu cực đại cục bộ hai tầng (2-Stage Non-Maximum Suppression: Spatial 3x3 + Overlap IoU)
   - 3.8. Cơ chế phân phối nhánh động theo chế độ co giãn (ScaleMode Dynamic Dispatch)
4. [BẢN CHẤT TOÁN HỌC VÀ VAI TRÒ THUẬT TOÁN CỦA TỪNG THAM SỐ](#4-bản-chất-toán-học-và-vai-trò-thuật-toán-của-từng-tham-số)
   - 4.1. Giải thuật trích xuất biên Steger Facet 3x3 và khai triển trị riêng ma trận Hessian
   - 4.2. Lượng tử hóa hướng Gradient 16 Sector bằng bảng tỉ số tiếp tuyến số nguyên cố định (Fixed-point Tangent)
   - 4.3. Cấu trúc 16 kênh Response Maps và cơ chế tích vô hướng chuẩn hóa
   - 4.4. Tăng tốc phần cứng SIMD AVX2 với khả năng tích lũy 32 pixel/chu kỳ lệnh
   - 4.5. Các giải thuật tinh chỉnh vị trí dưới điểm ảnh (SubPixel Refinement: Paraboloid & Least-Squares)
   - 4.6. Chẩn đoán phân loại chất lượng từng hạt đặc trưng (Per-Feature Quality Diagnostics)
5. [HƯỚNG DẪN TINH CHỈNH VÀ TỐI ƯU HÓA TRONG MÔI TRƯỜNG DÂY CHUYỀN CÔNG NGHIỆP](#5-hướng-dẫn-tinh-chỉnh-và-tối-ưu-hóa-trong-môi-trường-dây-chuyền-công-nghiệp)
   - 5.1. Tối ưu hóa Tốc độ xử lý và Độ trễ chu kỳ máy (Latency Minimization)
   - 5.2. Triệt tiêu triệt để báo động giả trên phôi kim loại gia công (False-Positive Rejection)
   - 5.3. Xử lý dung sai góc xoay và định hướng linh kiện (Rotation Tolerance Tradeoff)
   - 5.4. Xử lý linh kiện biến đổi kích thước nhiệt và biến dạng bao bì mềm (Scale & Deformation Invariance)
   - 5.5. Khả năng kháng biến thiên cường độ chiếu sáng và tương phản (Illumination Robustness)
6. [KẾT LUẬN VÀ BÀI HỌC KINH NGHIỆM CHO HỆ THỐNG TUNVISION](#6-kết-luận-và-bài-học-kinh-nghiệm-cho-hệ-thống-tunvision)

---

## 1. TỔNG QUAN HỆ THỐNG VÀ BẢN ĐỒ THAM SỐ GIAO DIỆN

### 1.1. Kiến trúc phân tầng và vị trí của bộ tham số trong quy trình đo kiểm

Trong các hệ thống thị giác máy công nghiệp chuẩn Halcon/Cognex, thuật toán so khớp mẫu dựa trên hình học (Geometric Shape-Based Matching) không sử dụng phương pháp so khớp ma trận điểm ảnh xám đơn thuần (Normalized Cross Correlation - NCC). Thay vào đó, hệ thống trích xuất các **vector pháp tuyến vi phân tại các điểm biên dưới điểm ảnh (Subpixel Edge Gradient Vectors)**.

Để điều khiển cỗ máy toán học phức tạp này, giao diện người máy cung cấp một bảng tham số phân cấp. Trong ứng dụng chuẩn công nghiệp `TemplateMatchQt.exe`, bảng tham số bên phải đóng vai trò là bảng cầu chì điều phối toàn bộ các hành vi của nhân tính toán native `mwwz.dll`.

### 1.2. Bản đồ offset bộ nhớ của các widget điều khiển trong `TemplateMatchQt.exe`

Dựa trên kết quả dịch ngược tĩnh mã máy PE64 của `TemplateMatchQt.exe` tại hàm thiết lập giao diện `Ui::TemplateMatchQt::setupUi` (VA `0x140045e00` - `0x14004a500`), cấu trúc đối tượng `TemplateMatchQt` lưu trữ con trỏ đến từng widget điều khiển theo các địa chỉ offset cố định trong bộ nhớ heap của tiến trình:

```
TemplateMatchQt (QObject / QWidget Heap Object)
│
├── [Offset 0x058] : QSpinBox        *spinBoxContrastLow    // Ngưỡng tương phản thấp
├── [Offset 0x068] : QSpinBox        *spinBoxContrastHigh   // Ngưỡng tương phản cao
├── [Offset 0x078] : QSpinBox        *spinBoxNumLevel       // Số tầng kim tự tháp (0 = Auto)
├── [Offset 0x098] : QSpinBox        *spinBoxMinContrast    // Sàn nhiễu tương phản ảnh tìm kiếm
├── [Offset 0x0b8] : QComboBox       *cboMetric             // Chế độ cực tính (use/ignore polarity)
├── [Offset 0x0c8] : QSpinBox        *spinBoxMinContLen     // Chiều dài contour tối thiểu (MinCompSize)
│
├── [Offset 0x118] : QDoubleSpinBox  *dSpinBoxAngleStart    // Góc quét bắt đầu (Độ)
├── [Offset 0x128] : QDoubleSpinBox  *dSpinBoxAngleExtent   // Dải góc quét (Độ)
├── [Offset 0x138] : QDoubleSpinBox  *dSpinBoxScaleMin      // Tỉ lệ co giãn nhỏ nhất
├── [Offset 0x148] : QDoubleSpinBox  *dSpinBoxScaleMax      // Tỉ lệ co giãn lớn nhất
├── [Offset 0x158] : QDoubleSpinBox  *dSpinBoxMinScore      // Điểm số chấp nhận tối thiểu
├── [Offset 0x168] : QDoubleSpinBox  *dSpinBoxMaxOverlap    // Ngưỡng đè lấn diện tích NMS
├── [Offset 0x178] : QDoubleSpinBox  *dSpinBoxGreediness    // Hệ số tỉa nhánh Branch-and-bound
├── [Offset 0x188] : QSpinBox        *spinBoxNumMatches     // Số kết quả cần tìm (0 = Tất cả)
├── [Offset 0x1a8] : QComboBox       *cboShowMatchesMode    // Chế độ vẽ overlay (Draw: NoScore/Score/All)
├── [Offset 0x1b8] : QComboBox       *cboSubpixel           // Thuật toán tinh chỉnh subpixel
├── [Offset 0x1d0] : QCheckBox       *chkBorderShape        // Cho phép vật thể cắt tràn mép ảnh
├── [Offset 0x1e0] : QComboBox       *cboScaleMode          // Chế độ quét tỉ lệ (none/iso/aniso/deform)
├── [Offset 0x200] : QComboBox       *cboMaxDeformation     // Mức độ biến dạng đàn hồi
├── [Offset 0x210] : QSpinBox        *spinBoxNumLevel2      // Tầng dừng kim tự tháp (LowestPyramid)
│
├── [Offset 0x240] : QLineEdit       *lineEditLen1          // Nửa chiều dài mô hình XLD hình học
├── [Offset 0x248] : QLineEdit       *lineEditLen2          // Nửa chiều rộng mô hình XLD hình học
├── [Offset 0x2e8] : QCheckBox       *chkShowRoi            // Bật khung tương tác ROI
├── [Offset 0x300] : QDoubleSpinBox  *dSpinBoxScore         // Ngưỡng chấm điểm xanh/đỏ hạt feature
└── [Offset 0x308] : QSpinBox        *spinBoxMinMag         // Sàn lọc biên độ gradient chẩn đoán
```

Đặc biệt, bên cạnh 3 thông số huấn luyện cốt lõi, giao diện tích hợp 3 nút bấm trạng thái màu (Color Indicator ToolButtons):
- `toolBtnA1` (Màu Cyan / Xanh lơ): Nằm cạnh `ContrastLow`, gọi callback đo và gán giá trị ngưỡng thấp.
- `toolBtnA2` (Màu Green / Xanh lá): Nằm cạnh `ContrastHigh`, gọi callback đo và gán giá trị ngưỡng cao.
- `toolBtnA3` (Màu Red / Đỏ): Nằm cạnh `NumLevels`, kích hoạt tính toán tự động số tầng kim tự tháp và phát tín hiệu `upon_update_contrast()`.

---

## 2. DANH MỤC VÀ ĐẶC TẢ CHI TIẾT TOÀN BỘ 24 THAM SỐ

Dưới đây là thông số kỹ thuật chi tiết của toàn bộ 24 tham số được trích xuất trực tiếp từ các lệnh máy khởi tạo thuộc tính widget của Qt (`setMinimum`, `setMaximum`, `setSingleStep`, `setValue`, `setDecimals`):

### 2.1. Nhóm tham số Tạo mô hình mẫu (Create Model Parameters - 8 tham số)

#### 1. `ContrastLow` (Độ tương phản thấp)
- **Tên hiển thị trên UI**: `ContrastLow` (Nhãn Tiếng Trung: `对比度低` / `对比度(低)`)
- **Tên biến nội bộ**: `contrast[0]`, `contrast_low`, `spinBoxContrastLow`
- **Kiểu dữ liệu**: `int32` (Số nguyên có dấu 32-bit)
- **Đơn vị & Định dạng**: Không thứ nguyên (Thang đo độ lớn gradient), hiển thị số nguyên thập phân `%d`
- **Dải giá trị hợp lệ**:
  * Tối thiểu (`setMinimum`): **`1`**
  * Tối đa (`setMaximum`): **`255`** (`0xFF`)
  * Bước nhảy (`singleStep`): **`1`**
  * Mặc định khởi tạo (`setValue`): **`10`**
  * Giá trị thực nghiệm trên ảnh chip IC (`media_1794742572058_0c8a4605.png`): **`39`**
- **Ý nghĩa vận hành trong động cơ**: Là ngưỡng dưới trong giải thuật bám biên trễ (Hysteresis Tracking). Mọi điểm ảnh có độ lớn gradient $M(x, y) < \text{ContrastLow}$ sẽ bị loại bỏ ngay từ bước vi phân sơ cấp, ngăn chặn các vân xước bề mặt hoặc nhiễu nền mờ nhạt xâm nhập vào tập ứng viên biên yếu ($State = 1$).

#### 2. `ContrastHigh` (Độ tương phản cao)
- **Tên hiển thị trên UI**: `ContrastHigh` (Nhãn Tiếng Trung: `对比度高` / `对比度(高)`)
- **Tên biến nội bộ**: `contrast[1]`, `contrast_high`, `spinBoxContrastHigh`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Không thứ nguyên, hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**:
  * Tối thiểu: **`1`**
  * Tối đa: **`255`**
  * Bước nhảy: **`1`**
  * Mặc định: **`15`**
  * Giá trị thực nghiệm: **`52`**
- **Ý nghĩa vận hành trong động cơ**: Là ngưỡng kích hoạt hạt giống biên mạnh ($State = 2$). Các điểm có độ lớn gradient cực trị subpixel $M_{\text{sub}} \ge \text{ContrastHigh}$ được đưa vào hàng đợi ưu tiên để khởi phát quá trình bám biên Hysteresis hai chiều thuận nghịch.  
  *Ràng buộc bắt buộc*: Hệ thống kiểm tra điều kiện `ContrastLow <= ContrastHigh`. Nếu người dùng vi phạm, chuỗi lỗi `Para Error: [ContrastLow]=(%d) > [ContrastHigh]=(%d)` (RVA `0x97e1d`) sẽ kích hoạt từ chối tạo mẫu.

#### 3. `NumLevels` (Số tầng kim tự tháp khi huấn luyện)
- **Tên hiển thị trên UI**: `NumLevels` (Nhãn Tiếng Trung: `金字塔`)
- **Tên biến nội bộ**: `num_levels`, `spinBoxNumLevel`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Cấp độ phân giải, hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**:
  * Tối thiểu: **`0`** (0 đại diện cho chế độ Tự động tính toán - Auto-Calculate)
  * Tối đa: **`10`**
  * Bước nhảy: **`1`**
  * Mặc định: **`0`**
  * Giá trị thực nghiệm: **`0`**
- **Ý nghĩa vận hành trong động cơ**: Xác định số tầng kim tự tháp ảnh được sinh ra khi đóng gói mô hình `.shm`.
  * Khi đặt **`0`**: Động cơ gọi hàm `auto_shape_model_params` để tự động suy diễn số tầng tối ưu dựa trên kích thước khung bao nhỏ nhất: $L = \text{round}(\log_2(\min(W, H)/16)) + 1$.
  * Khi đặt $> 0$: Ép buộc động cơ tạo đúng số tầng yêu cầu (ví dụ $L = 4$).

#### 4. `Metric` (Đo lường cực tính tương phản)
- **Tên hiển thị trên UI**: `Metric` (Nhãn Tiếng Trung: `度量`)
- **Tên biến nội bộ**: `use_polarity` / `metric`, `cboMetric`
- **Kiểu dữ liệu**: `enum` / `int32`
- **Các tùy chọn ComboBox**:
  * Index 0: **`use polarity`** (Sử dụng cực tính — Mặc định)
  * Index 1: **`ignore polarity`** (Bỏ qua cực tính)
  * Index 2: **`ignore global`** (Bỏ qua tương phản toàn cục)
  * Index 3: **`ignore local`** (Bỏ qua tương phản cục bộ)
  * Giá trị thực nghiệm: **`use polarity`** (Index 0)
- **Ý nghĩa vận hành trong động cơ**: Quy định cách thức tính toán hàm đáp ứng góc gradient:
  * `use polarity = 1`: Tích vô hướng có dấu trên đoạn $[-128, 127]$. Điểm ảnh bị đảo chiều sáng-tối sẽ nhận điểm âm, loại bỏ các cạnh đối nghịch.
  * `ignore polarity = 0`: Lấy giá trị tuyệt đối $|R| \in [0, 127]$. Cho phép vật thể có màu sáng trên nền tối khớp thành công với mẫu tạo từ vật thể tối trên nền sáng.

#### 5. `Length1` & `Length2` (Kích thước mô hình hình học XLD)
- **Tên hiển thị trên UI**: `Length1`, `Length2`
- **Tên biến nội bộ**: `len1`, `len2`, `lineEditLen1`, `lineEditLen2`
- **Kiểu dữ liệu**: `double`
- **Đơn vị & Định dạng**: Pixel (px), hiển thị chuỗi float
- **Dải giá trị hợp lệ**: $[0.0, 10000.0]$, mặc định **`0`**
- **Ý nghĩa vận hành trong động cơ**: Cho phép khởi tạo mô hình hình học thuần túy (Parametric Synthetic Model) không cần ảnh mẫu chụp thật:
  * Khi `Length1 > 0` và `Length2 == 0`: Tự động gọi `create_shape_model_xld` sinh mô hình đường tròn bán kính $R = \text{Length1}$.
  * Khi `Length1 > 0` và `Length2 > 0`: Sinh mô hình khung chữ nhật có nửa chiều dài `Length1` và nửa chiều rộng `Length2`.
  * Trong ảnh thực nghiệm: Cả hai bằng `0`, nghĩa là tạo mẫu từ ảnh chụp thực tế (`pGray`).

#### 6. `MinContrast` (Độ tương phản tối thiểu)
- **Tên hiển thị trên UI**: `MinContrast` (Nhãn Tiếng Trung: `最小对比度`)
- **Tên biến nội bộ**: `contrast[3]`, `min_contrast`, `spinBoxMinContrast`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Không thứ nguyên, hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**:
  * Tối thiểu: **`1`**
  * Tối đa: **`255`**
  * Bước nhảy: **`1`**
  * Mặc định: **`5`**
  * Giá trị thực nghiệm: **`9`**
- **Ý nghĩa vận hành trong động cơ**: Thiết lập sàn lọc nhiễu tuyệt đối khi xử lý ảnh tìm kiếm thực tế. Tất cả các điểm ảnh trên ảnh kiểm tra có biên độ gradient vi phân Sobel $< \text{MinContrast}$ sẽ bị ép về $0$, giúp triệt tiêu hoàn toàn đáp ứng nhiễu từ các vùng phẳng, đồng thời tiết kiệm đáng kể chu kỳ tích lũy SIMD.

#### 7. `MinCompSize` (Kích thước thành phần liên thông tối thiểu)
- **Tên hiển thị trên UI**: `MinCompSize` (Mã nguồn UI: `spinBoxMinContLen`, Tiếng Trung: `最小组件尺寸`)
- **Tên biến nội bộ**: `contrast[2]`, `min_cont_len`, `min_component_size`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Pixel chiều dài cung (Arc-length px), hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**:
  * Tối thiểu: **`1`**
  * Tối đa: **`200`** (`0xC8`)
  * Bước nhảy: **`1`**
  * Mặc định: **`5`**
  * Giá trị thực nghiệm: **`16`**
- **Ý nghĩa vận hành trong động cơ**: Đây là tham số phòng thủ cốt lõi loại bỏ nhiễu hạt xước công nghiệp. Sau khi bám biên Hysteresis, tổng chiều dài cung tích lũy $L_{\text{arc}}$ của từng chuỗi contour được tính toán: nếu $L_{\text{arc}} < \text{MinCompSize}$, toàn bộ chuỗi điểm đó bị hủy bỏ ngay lập tức. Trong ảnh chip IC, giá trị `16` giúp loại bỏ hoàn toàn hàng trăm đốm sần sùi do tia laser tạo ra trong lòng linh kiện.

---

### 2.2. Nhóm tham số Tìm kiếm và Không gian trạng thái (Search Model Parameters - 13 tham số)

#### 8. `AngleStart` (Góc quét khởi đầu)
- **Tên hiển thị trên UI**: `AngleStart` (Nhãn Tiếng Trung: `起始角度`)
- **Tên biến nội bộ**: `angle_start`, `dSpinBoxAngleStart`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Độ (Degrees, $^\circ$), định dạng 1 chữ số thập phân `%.1f`
- **Dải giá trị hợp lệ**: $[-180.0, 180.0]$, bước nhảy `1.0`, mặc định **`0.0`**
- **Ý nghĩa vận hành trong động cơ**: Xác định góc biên dưới của dải tìm kiếm xoay.

#### 9. `AngleExtent` (Phạm vi góc quét)
- **Tên hiển thị trên UI**: `AngleExtent` (Nhãn Tiếng Trung: `角度范围`)
- **Tên biến nội bộ**: `angle_extent`, `dSpinBoxAngleExtent`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Độ ($^\circ$), định dạng `%.1f`
- **Dải giá trị hợp lệ**: $[0.0, 360.0]$, bước nhảy `1.0`, mặc định **`360.0`**
- **Ý nghĩa vận hành trong động cơ**: Xác định độ rộng góc quay được phép tìm kiếm, quét từ `AngleStart` đến `AngleStart + AngleExtent`.

#### 10. `ScaleMin` (Tỉ lệ co giãn tối thiểu)
- **Tên hiển thị trên UI**: `ScaleMin` (Nhãn Tiếng Trung: `最小比例`)
- **Tên biến nội bộ**: `scale_min`, `dSpinBoxScaleMin`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Tỉ số không thứ nguyên, định dạng 1 chữ số thập phân `%.1f`
- **Dải giá trị hợp lệ**: $[0.3, 2.0]$, bước nhảy `0.1`, mặc định **`1.0`**
- **Ý nghĩa vận hành trong động cơ**: Giới hạn tỉ lệ thu nhỏ tối thiểu của đối tượng so với mẫu gốc.

#### 11. `ScaleMax` (Tỉ lệ co giãn tối đa)
- **Tên hiển thị trên UI**: `ScaleMax` (Nhãn Tiếng Trung: `最大比例`)
- **Tên biến nội bộ**: `scale_max`, `dSpinBoxScaleMax`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Tỉ số không thứ nguyên, định dạng `%.1f`
- **Dải giá trị hợp lệ**: $[0.3, 2.0]$, bước nhảy `0.1`, mặc định **`1.0`**
- **Ý nghĩa vận hành trong động cơ**: Giới hạn tỉ lệ phóng to tối đa.  
  *Ghi chú tối ưu*: Khi `ScaleMin == 1.0` và `ScaleMax == 1.0` kết hợp cùng `ScaleMode = none`, động cơ tự động bỏ qua toàn bộ vòng lặp duyệt tỷ lệ, chạy với hiệu năng đỉnh.

#### 12. `MinScore` (Ngưỡng điểm tương đồng tối thiểu)
- **Tên hiển thị trên UI**: `MinScore` (Nhãn Tiếng Trung: `最小得分`)
- **Tên biến nội bộ**: `min_score`, `dSpinBoxMinScore`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Đoạn số thực $[0.0, 1.0]$, định dạng 2 chữ số thập phân `%.2f`
- **Dải giá trị hợp lệ**: $[0.1, 1.0]$, bước nhảy `0.05`, mặc định **`0.50`**
- **Ý nghĩa vận hành trong động cơ**: Ngưỡng tương quan tối thiểu để chấp nhận một vị trí là đối tượng hợp lệ. Ở tầng kim tự tháp thô nhất, ngưỡng này được động cơ tự động nhân tạm thời với hệ số $0.90$ ($0.50 \times 0.90 = 0.45$) để tránh bỏ sót ứng viên tiềm năng do sai số lượng tử hóa không gian.

#### 13. `MaxOverlap` (Độ đè lấn diện tích tối đa)
- **Tên hiển thị trên UI**: `MaxOverlap` (Nhãn Tiếng Trung: `最大重叠`)
- **Tên biến nội bộ**: `max_overlap`, `dSpinBoxMaxOverlap`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Tỉ lệ giao nhau trên diện tích nhỏ hơn $[0.0, 1.0]$, định dạng `%.2f`
- **Dải giá trị hợp lệ**: $[0.0, 1.0]$, bước nhảy `0.05`, mặc định **`0.50`**
- **Ý nghĩa vận hành trong động cơ**: Tham số điều khiển giải thuật Overlap NMS. Khi hai hộp bao có độ trùng lấn vượt quá `MaxOverlap`, đối tượng có điểm số tương đồng thấp hơn sẽ bị triệt tiêu, ngăn ngừa hiện tượng một phôi cơ khí bị phát hiện trùng lặp nhiều lần.

#### 14. `Greediness` (Độ tham lam tỉa nhánh sớm)
- **Tên hiển thị trên UI**: `Greediness` (Nhãn Tiếng Trung: `贪婪度`)
- **Tên biến nội bộ**: `greediness`, `dSpinBoxGreediness`
- **Kiểu dữ liệu**: `double` $\to$ `float32`
- **Đơn vị & Định dạng**: Hệ số tỉa nhánh $[0.0, 1.0]$, định dạng `%.2f`
- **Dải giá trị hợp lệ**: $[0.0, 1.0]$, bước nhảy `0.05`, mặc định **`0.75`**
- **Ý nghĩa vận hành trong động cơ**: Điều khiển thuật toán Branch-and-Bound cắt tỉa các điểm tích lũy dở dang. Giá trị `0.75` giúp loại bỏ tới $> 80\%$ các vị trí ứng viên không có khả năng đạt điểm số yêu cầu ngay từ $1/4$ chặng đường tính toán.

#### 15. `NumMatches` (Số lượng kết quả tìm kiếm tối đa)
- **Tên hiển thị trên UI**: `NumMatches` (Nhãn Tiếng Trung: `匹配数量`)
- **Tên biến nội bộ**: `num_matches`, `spinBoxNumMatches`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Số lượng đối tượng, hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**: $[0, 3000]$, bước nhảy `1`, mặc định **`0`** (Tìm tất cả)
- **Ý nghĩa vận hành trong động cơ**: Nếu đặt $> 0$, động cơ chỉ trả về tối đa $K$ kết quả có điểm số cao nhất sau khi đã sắp xếp. Đặt `0` tương đương với việc tìm toàn bộ các kết quả đạt chuẩn trên toàn bộ ảnh.

#### 16. `MaxDeformation` (Mức độ biến dạng tối đa)
- **Tên hiển thị trên UI**: `MaxDeformation`
- **Tên biến nội bộ**: `max_deformation`, `cboMaxDeformation`
- **Kiểu dữ liệu**: `enum` / `int32`
- **Dải giá trị hợp lệ**: ComboBox các mức độ biến dạng `0, 1, 2, ...`, mặc định **`0`**
- **Ý nghĩa vận hành trong động cơ**: Quy định biên độ dịch chuyển tối đa của lưới điều hòa cục bộ trong giải thuật `find_local_deformable_model`. Khi bằng `0`, mô hình hoạt động ở trạng thái vật rắn thuần túy (Rigid Shape Matching).

#### 17. `LowestPyramid` (Tầng kim tự tháp kết thúc tìm kiếm)
- **Tên hiển thị trên UI**: `LowestPyramid` (Nhãn Tiếng Trung: `终止金字塔`)
- **Tên biến nội bộ**: `numLevels[1]`, `stop_level`, `spinBoxNumLevel2`
- **Kiểu dữ liệu**: `int32`
- **Đơn vị & Định dạng**: Cấp kim tự tháp (Ký hiệu 1-based), hiển thị số nguyên `%d`
- **Dải giá trị hợp lệ**: $[1, 10]$, bước nhảy `1`, mặc định **`1`**
- **Ý nghĩa vận hành trong động cơ**: Tuân theo ký hiệu 1-based của phần mềm Halcon.  
  Trong mã máy của `mwwz.dll` tại địa chỉ `0x18004c71b`: lệnh `dec edi` trừ đi 1 ($1 \to 0$). Do đó, `LowestPyramid = 1` có nghĩa là: **Thuật toán chạy xuyên suốt từ tầng kim tự tháp thô nhất XUỐNG TẬN TẦNG 0 ẢNH GỐC NGUYÊN BẢN**. Nếu chọn `LowestPyramid = 2`, thuật toán sẽ dừng ở tầng thu nhỏ $1/2$ (tốc độ tăng gấp 3–4 lần nhưng độ chính xác vị trí giảm 1 nửa).

#### 18. `Cross Img Border` (Cho phép vượt biên ảnh)
- **Tên hiển thị trên UI**: `Cross Img Border`
- **Tên biến nội bộ**: `chkBorderShape`, gọi `set_system("border_shape_models", 1)`
- **Kiểu dữ liệu**: `bool` (Checkbox)
- **Dải giá trị**: `true` / `false`, mặc định: **`false`** (Unchecked)
- **Ý nghĩa vận hành trong động cơ**: Khi được bật, cho phép các đối tượng bị cắt một phần bởi viền ảnh camera vẫn được tính điểm và so khớp thành công.

#### 19. `ScaleMode` (Chế độ co giãn)
- **Tên hiển thị trên UI**: `ScaleMode` (Tô màu nổi bật Magenta)
- **Tên biến nội bộ**: `cboScaleMode`
- **Kiểu dữ liệu**: `enum` / `int32`
- **Các tùy chọn ComboBox**:
  * Index 0: **`none`** (Cố định tỷ lệ 1.0 — Mặc định)
  * Index 1: **`isotropic`** (Co giãn đẳng hướng $s_x = s_y$)
  * Index 2: **`anisotropic`** (Co giãn bất đẳng hướng $s_x \ne s_y$)
  * Index 3: **`local_deformable`** (Biến dạng phi tuyến tính cục bộ)
  * Index 4: **`planar_deformable`** (Biến dạng phối cảnh mặt phẳng tự do)
- **Ý nghĩa vận hành trong động cơ**: Điều phối bộ chuyển mạch (Switch-Case RVA `0x140063f06`) gọi đúng API tương ứng trong `mwwz.dll`.

#### 20. `SubPixel` (Độ phân giải dưới điểm ảnh)
- **Tên hiển thị trên UI**: `SubPixel` (Tô màu nổi bật Red)
- **Tên biến nội bộ**: `subpixel`, `cboSubpixel`
- **Kiểu dữ liệu**: `enum` / `int32`
- **Các tùy chọn ComboBox**:
  * Index 0: **`none`** (Tắt tinh chỉnh hậu kỳ — Mặc định)
  * Index 1: **`interpolation`** (Nội suy mặt đáp ứng song tuyến tính/song bậc ba)
  * Index 2: **`least-squares`** (Khớp đa thức Paraboloid 2D qua ma trận Hessian)
  * Index 3: **`speed-first`** (Nội suy parabol 1D theo hướng gradient)
- **Ý nghĩa vận hành trong động cơ**: Quyết định có áp dụng bước tối ưu cục bộ phi tuyến sau khi tìm thấy đỉnh cực đại thô hay không. Ở chế độ `none`, vị trí đỉnh lấy trực tiếp từ lưới kim tự tháp, nhưng độ chính xác vẫn rất cao nhờ mô hình biên vốn đã được trích xuất bằng giải tích Steger subpixel.

---

### 2.3. Nhóm tham số Vùng quan tâm và Chẩn đoán điểm đặc trưng (ROI & Diagnostic Parameters - 3 tham số)

#### 21. `Draw` (Chế độ hiển thị kết quả)
- **Tên hiển thị trên UI**: `Draw`
- **Tên biến nội bộ**: `cboShowMatchesMode`
- **Kiểu dữ liệu**: `enum` / `int32`
- **Các tùy chọn ComboBox**:
  * Index 0: **`All`** (Hiển thị đầy đủ: Contour + Crosshair + Điểm số)
  * Index 1: **`Cont`** (Chỉ hiển thị đường bao contour)
  * Index 2: **`Cross`** (Chỉ hiển thị tâm dấu chữ thập đỏ)
  * Index 3: **`Score`** (Hiển thị chữ thập và điểm số)
  * Index 4: **`NoScore`** (Hiển thị chữ thập và contour, ẩn điểm số văn bản — Đang dùng trong screenshot)

#### 22. `ROI` (Hiển thị/bật vùng quan tâm)
- **Tên hiển thị trên UI**: `ROI` (`chkShowRoi`)
- **Kiểu dữ liệu**: `bool` (Checkbox, mặc định Unchecked)
- **Ý nghĩa vận hành**: Bật khung đồ họa tương tác cho phép người dùng kéo thả hình chữ nhật, hình chữ nhật xoay (RotatedRect) hoặc đa giác để giới hạn vùng tìm kiếm hoặc vùng tạo mẫu.

#### 23. `dSpinBoxScore` (Ngưỡng phân loại hạt đặc trưng chẩn đoán)
- **Tên hiển thị trên UI**: SpinBox không tên cạnh ROI, hiển thị `0.50`
- **Kiểu dữ liệu**: `double` (Dải $[0.0, 1.0]$, Step `0.05`, Decimals 2)
- **Ý nghĩa vận hành**: Được truyền vào hàm nội suy `ShowMatchDetails` / `get_match_details`. Nếu tích vô hướng tại từng điểm đặc trưng riêng biệt $j$ trên đường biên của đối tượng tìm thấy có giá trị $\ge \text{dSpinBoxScore}$ thì điểm biên đó được vẽ **MÀU XANH LÁ CÂY (GREEN)**; nếu bị che khuất hoặc mờ nhạt $< \text{dSpinBoxScore}$ thì điểm biên đó được vẽ **MÀU ĐỎ (RED)**. Trong ảnh thực nghiệm, có thể thấy rõ các chấm đỏ xuất hiện lác đác tại các đoạn biên bị xước sần sùi của logo!

#### 24. `spinBoxMinMag` (Sàn lọc biên độ gradient chẩn đoán)
- **Tên hiển thị trên UI**: SpinBox cạnh ROI, hiển thị `10`
- **Kiểu dữ liệu**: `int32` (Dải $[1, 255]$, Mặc định `10`)
- **Ý nghĩa vận hành**: Đối số `minMag` trong API `get_match_details(pGray, w, h, minMag, ...)`. Bỏ qua không đánh giá các điểm biên mà độ lớn gradient trên ảnh thực tế quá yếu ($< 10$).

---

### 2.4. Bảng tổng hợp đối soát vi kiến trúc (UI Control vs Native C++ Engine Struct)

| STT | Tên tham số trên UI | Tên biến nội bộ Qt | Kiểu dữ liệu | Dải hợp lệ | Mặc định | Offset UI | API / Đối số tương ứng trong `mwwz.dll` |
|:---:|---|---|:---:|:---:|:---:|:---:|---|
| 1 | **ContrastLow** | `spinBoxContrastLow` | `int32` | 1 – 255 | 10 (Demo: 39) | `0x058` | `create_shape_model` (arg `contrast[0]`) |
| 2 | **ContrastHigh** | `spinBoxContrastHigh` | `int32` | 1 – 255 | 15 (Demo: 52) | `0x068` | `create_shape_model` (arg `contrast[1]`) |
| 3 | **NumLevels** | `spinBoxNumLevel` | `int32` | 0 – 10 | 0 (Auto) | `0x078` | `create_shape_model` (arg `num_levels`) |
| 4 | **Metric** | `cboMetric` | `enum` | 0 – 3 | 0 (use polarity) | `0x0b8` | `create_shape_model` (arg `use_polarity`) |
| 5 | **Length1** | `lineEditLen1` | `double` | 0 – 10000 | 0 | `0x240` | `create_shape_model_xld` (arg `len1`) |
| 6 | **Length2** | `lineEditLen2` | `double` | 0 – 10000 | 0 | `0x248` | `create_shape_model_xld` (arg `len2`) |
| 7 | **MinContrast** | `spinBoxMinContrast` | `int32` | 1 – 255 | 5 (Demo: 9) | `0x098` | `create_shape_model` (arg `contrast[3]`) |
| 8 | **MinCompSize** | `spinBoxMinContLen` | `int32` | 1 – 200 | 5 (Demo: 16) | `0x0c8` | `create_shape_model` (arg `contrast[2]`) |
| 9 | **AngleStart** | `dSpinBoxAngleStart` | `double` | -180.0 – 180.0 | 0.0 | `0x118` | `find_shape_model` (arg `angle_start`) |
| 10 | **AngleExtent** | `dSpinBoxAngleExtent` | `double` | 0.0 – 360.0 | 360.0 | `0x128` | `find_shape_model` (arg `angle_extent`) |
| 11 | **ScaleMin** | `dSpinBoxScaleMin` | `double` | 0.3 – 2.0 | 1.0 | `0x138` | `find_scaled_shape_model` (arg `scale_min`) |
| 12 | **ScaleMax** | `dSpinBoxScaleMax` | `double` | 0.3 – 2.0 | 1.0 | `0x148` | `find_scaled_shape_model` (arg `scale_max`) |
| 13 | **MinScore** | `dSpinBoxMinScore` | `double` | 0.10 – 1.00 | 0.50 | `0x158` | `find_shape_model` (arg `minScore`) |
| 14 | **MaxOverlap** | `dSpinBoxMaxOverlap` | `double` | 0.00 – 1.00 | 0.50 | `0x168` | `find_shape_model` (arg `maxOverLap`) |
| 15 | **Greediness** | `dSpinBoxGreediness` | `double` | 0.00 – 1.00 | 0.75 | `0x178` | `find_shape_model` (arg `greediness`) |
| 16 | **NumMatches** | `spinBoxNumMatches` | `int32` | 0 – 3000 | 0 (All) | `0x188` | `find_shape_model` (arg `numMatches`) |
| 17 | **MaxDeformation** | `cboMaxDeformation` | `enum` | 0 – 5 | 0 | `0x200` | `find_local_deformable_model` (arg `max_def`) |
| 18 | **LowestPyramid** | `spinBoxNumLevel2` | `int32` | 1 – 10 | 1 (Level 0) | `0x210` | `find_shape_model` (arg `numLevels[1]`) |
| 19 | **Cross Border** | `chkBorderShape` | `bool` | true / false | false | `0x1d0` | `set_system("border_shape_models", ...)` |
| 20 | **ScaleMode** | `cboScaleMode` | `enum` | 0 – 4 | 0 (none) | `0x1e0` | Switch dispatch: `find_shape_model_*` |
| 21 | **SubPixel** | `cboSubpixel` | `enum` | 0 – 3 | 0 (none) | `0x1b8` | `find_shape_model` (arg `subpixel`) |
| 22 | **Draw** | `cboShowMatchesMode`| `enum` | 0 – 4 | NoScore (4) | `0x1a8` | Quản lý cờ vẽ overlay `ShowMatches` |
| 23 | **Feature Score** | `dSpinBoxScore` | `double` | 0.00 – 1.00 | 0.50 | `0x300` | Ngưỡng đổi màu hạt Green/Red `ShowMatchDetails` |
| 24 | **MinMag** | `spinBoxMinMag` | `int32` | 1 – 255 | 10 | `0x308` | `get_match_details` (arg `minMag`) |

---

## 3. CƠ CHẾ VÀ CÔNG THỨC TOÁN HỌC TỰ ĐỘNG CÂN BẰNG (AUTO-BALANCING MECHANICS)

### 3.1. Thuật toán tự động tính tầng Kim tự tháp tối ưu (Auto Pyramid Depth Selection)

Khi người dùng đặt `NumLevels = 0` hoặc bấm nút màu đỏ `toolBtnA3`, động cơ `mwwz.dll` kích hoạt hàm `auto_shape_model_params` (RVA `0x5ebe3`):

```assembly
0x18005ec8b: cmp   esi, edi          ; So sánh w và h của ảnh mẫu
0x18005ec8d: cmovl edi, esi          ; edi = min(w, h)
0x18005ec90: vcvtsi2sd xmm0, xmm0, edi
0x18005ec94: vmulsd xmm0, xmm0, [rip + 0x6aec4] ; Nhân với 0.0625 (= 1/16.0)
0x18005ec9c: call  log2              ; Tính log2(min(w, h) / 16.0)
0x18005eca2: vxorpd xmm1, xmm1, xmm1
0x18005eca6: vmaxsd xmm0, xmm1, xmm0 ; Kẹp không âm: max(0.0, ...)
0x18005ecaa: vroundsd xmm0, xmm0, 9  ; Làm tròn số nguyên gần nhất
0x18005ecb0: vaddsd xmm0, xmm0, [1.0] ; Cộng thêm tầng gốc (+ 1.0)
0x18005ecb8: vcvttsd2si r14d, xmm0   ; r14d = Tổng số tầng L
```

**Công thức giải tích chuẩn hóa**:
$$\mathbf{L_{\text{optimal}} = \max\left(1, \; \text{round}\left( \log_2\left( \frac{\min(W_{\text{template}}, H_{\text{template}})}{16} \right) \right) + 1 \right)}$$

- **Nguyên lý chặn cận**: Kích thước vùng mẫu ở tầng thô nhất không được nhỏ hơn $16 \times 16$ pixel. Nếu nhỏ hơn 16 pixel, toán tử Sobel $3 \times 3$ sẽ bị triệt tiêu biên và không còn đủ thông tin hình học đặc trưng (cần tối thiểu $10 \dots 15$ điểm đặc trưng ở tầng đỉnh).
- **Ví dụ tính toán**:
  * Mẫu linh kiện $240 \times 180$ px: $\min(240, 180) = 180 \implies 180 / 16 = 11.25 \implies \log_2(11.25) \approx 3.49 \implies \text{round}(3.49) = 3 \implies L = 3 + 1 = \mathbf{4}$ tầng.
  * Mẫu vi mạch $64 \times 64$ px: $64 / 16 = 4 \implies \log_2(4) = 2 \implies L = 2 + 1 = \mathbf{3}$ tầng.

---

### 3.2. Thuật toán tự động ước lượng bộ tứ ngưỡng tương phản (Auto Contrast & Component Size Estimation)

Kích hoạt khi người dùng nạp ảnh mẫu mới, nhấn `toolBtnA1` (Cyan) hoặc `toolBtnA2` (Green):
1. **Xây dựng biểu đồ phân phối độ lớn Gradient (Gradient Magnitude Histogram)**:
   - Áp dụng toán tử Sobel $3 \times 3$ chuẩn hóa trên vùng ảnh mẫu ROI, tính độ lớn vi phân:
     $$M(x, y) = \frac{1}{4} \sqrt{g_x^2 + g_y^2}$$
   - Tính toán biểu đồ tần suất tích lũy $H(g) = \sum_{k=0}^g \text{count}(k)$.
2. **Suy diễn bộ tứ tham số `T[4]`**:
   - $\mathbf{T[0] = \text{ContrastLow}}$: Tương ứng phân vị dưới $P_{70}$ của dải gradient có nghĩa (vượt qua sàn nhiễu).
   - $\mathbf{T[1] = \text{ContrastHigh}}$: Tương ứng phân vị trên $P_{85}$ (thường bằng $1.33 \dots 1.5 \times \text{ContrastLow}$).
   - $\mathbf{T[2] = \text{MinCompSize}}$: Được tính toán từ đường kính cực đại $\text{Diameter}_{\max}$ của các contour khép kín sau bám biên:
     $$\text{MinCompSize} = \max\left(5, \; \text{round}\left( 0.5 \times \text{Diameter}_{\max} \right)\right)$$
   - $\mathbf{T[3] = \text{MinContrast}}$: Được suy diễn từ độ lệch chuẩn mức xám của vùng nền phẳng (Floor Noise Level), thường tương ứng phân vị $P_{25}$.
3. **Cập nhật ngược lên giao diện người dùng**:
   - Tại địa chỉ `0x14005e360` của `TemplateMatchQt.exe`, 4 giá trị này được tự động gán vào 4 ô nhập liệu:
     * `spinBoxContrastLow->setValue(T[0])` $\to$ **`39`**
     * `spinBoxContrastHigh->setValue(T[1])` $\to$ **`52`**
     * `spinBoxMinContrast->setValue(T[3])` $\to$ **`9`**
     * `spinBoxMinContLen->setValue(T[2])` $\to$ **`16`**

---

### 3.3. Quy tắc suy biến ngưỡng vi phân động qua các tầng kim tự tháp (Pyramid Halving Rule)

Khi tạo mô hình đa tầng $l = 0 \dots L-1$, ảnh mẫu bị giảm mẫu $2\times$ sau mỗi tầng bằng bộ lọc Gaussian $5 \times 5$ (`pyrDown`). Quá trình làm mịn làm sụt giảm biên độ gradient thực tế. Động cơ `mwwz.dll` cài đặt quy tắc **chia đôi ngưỡng động** (Dynamic Threshold Halving):

$$\text{ContrastLow}^{(l)} = \max\left(1, \; \left\lfloor \frac{\text{ContrastLow}^{(l-1)}}{2} \right\rfloor \right)$$

$$\text{MinCompSize}^{(l)} = \max\left(2, \; \left\lfloor \frac{\text{MinCompSize}^{(l-1)}}{2} \right\rfloor \right)$$

$$\text{MinContrast}^{(l)} = \text{MinContrast}^{(0)} \quad (\text{Giữ nguyên sàn nhiễu})$$

Nhờ cơ chế này, ở các tầng thô (nơi chi tiết bị mờ đi), thuật toán vẫn bám được đường bao hình học mà không làm đứt gãy mô hình.

---

### 3.4. Ràng buộc tương hỗ Bán kính mô hình và Bước góc quét tự thích ứng (Auto Angle Step Delta)

Người dùng trên giao diện chỉ cần nhập `AngleStart` và `AngleExtent`. Động cơ tự động tính toán bước góc tối ưu $\Delta\theta$ mà không cần người dùng can thiệp thủ công:

- **Bán kính mô hình cực đại**:
  $$R_{\max} = \max_{i=1 \dots N} \sqrt{(x_i - x_c)^2 + (y_i - y_c)^2}$$
- **Bước góc tầng nguyên bản (Level 0)**:
  $$\mathbf{\Delta\theta_0 = \frac{0.5}{R_{\max}} \text{ (radians)}} = \frac{0.5}{R_{\max}} \cdot \frac{180^\circ}{\pi}$$
  *Ý nghĩa hình học*: Bước quay làm dịch chuyển điểm xa tâm nhất một đoạn tối đa đúng $0.5$ pixel trên ảnh. Điều này bảo đảm không một điểm đặc trưng nào bị lệch quá nửa pixel giữa hai góc kề nhau, triệt tiêu nguy cơ bỏ lọt đỉnh tương quan.
- **Tự động mở rộng bước góc tại các tầng kim tự tháp thô**:
  $$\mathbf{\Delta\theta_l = \Delta\theta_0 \cdot 2^l}$$
  Ở tầng thô $l = 3$ (kích thước $1/8$), số lượng góc cần quét giảm đi $8$ lần! Đây là lý do chính giúp tốc độ quét toàn vòng $360^\circ$ chỉ mất vài mili-giây.

---

### 3.5. Cơ chế cắt tỉa nhánh tham lam sớm Branch-and-Bound (Greediness Pruning)

Hệ thống tính toán trước cận chặn dưới của điểm tương đồng tại các mốc kiểm tra đặc trưng $m \in \{ \frac{N}{4}, \frac{N}{2}, \frac{3N}{4} \}$:

$$\mathbf{S_{\text{threshold}}(m) = 128 \cdot \left[ \gamma_{\text{greediness}} \cdot N \cdot S_{\min} - (N - m) \right]}$$

- **Trong đó**:
  * $N$: Tổng số điểm đặc trưng của mô hình tại tầng đang xét.
  * $m$: Số điểm đặc trưng đã tích lũy tích vô hướng.
  * $N - m$: Số điểm đặc trưng còn lại chưa tính (giả định kịch bản hoàn hảo nhất là tất cả các điểm còn lại đều đạt điểm tuyệt đối $+128$).
  * $\gamma_{\text{greediness}}$: Giá trị nhập từ ô `Greediness` (mặc định $0.75$).
- **Điều kiện loại bỏ tức thời**: Nếu tại mốc kiểm tra $m$, tổng tích lũy hiện tại:
  $$S_{\text{accumulated}}(m) < S_{\text{threshold}}(m)$$
  $\implies$ **ỨNG VIÊN BỊ HỦY BỎ NGAY LẬP TỨC** (Pruned via fast pointer shift).
- **Tác động auto-balancing**:
  * Khi người dùng tăng `MinScore` lên cao (ví dụ $0.80$), $S_{\text{threshold}}(m)$ tự động tăng vọt, khiến thuật toán cắt tỉa tới $95\%$ ứng viên ngay từ vòng đầu.
  * Khi người dùng giảm `Greediness` về $0.0$, $S_{\text{threshold}}(m)$ trở thành cận an toàn tuyệt đối, tắt hoàn toàn việc tỉa nhánh (phù hợp khi tìm kiếm vật thể bị che khuất nặng).

---

### 3.6. Cơ chế nới lỏng ngưỡng điểm tầng kim tự tháp thô (Coarse Pyramid Score Relaxation)

Tại tầng kim tự tháp cao nhất (tầng thô nhất), do ảnh bị co nhỏ nhiều lần nên các vector gradient bị biến dạng và hướng gradient bị làm tròn. Để khắc phục hiện tượng này, tại địa chỉ `0x18004c8ee` trong `mwwz.dll`:

$$\mathbf{S_{\min}^{(\text{coarse})} = S_{\min} \times 0.90}$$

Ngưỡng điểm được tự động giảm đi $10\%$ tại tầng thô (ví dụ $0.50 \to 0.45$). Tất cả các ứng viên vượt qua ngưỡng $0.45$ ở tầng thô sẽ được theo dõi và tinh lọc dần khi chiếu tọa độ xuống các tầng dưới.

---

### 3.7. Bộ lọc triệt tiêu cực đại cục bộ hai tầng (2-Stage Non-Maximum Suppression)

Hệ thống kết hợp hai tầng lọc triệt tiêu cực đại cục bộ:
1. **Tầng 1 — Spatial 3x3 Response Map NMS** (`0x18003ce20`):  
   Ứng viên tại $(x, y, \theta)$ chỉ được giữ lại nếu điểm số lớn hơn cả 8 điểm lân cận trong cửa sổ không gian $3 \times 3$ trên ma trận đáp ứng 16-bit.
2. **Tầng 2 — Bounding Box Overlap IoU NMS** (`0x1800405ac`):  
   Sắp xếp tất cả các ứng viên theo điểm số giảm dần. Với mỗi ứng viên mới $B$, tính toán diện tích giao nhau với các ứng viên đã được chọn $A$:
   $$\text{OverlapRatio}(A, B) = \frac{\text{Area}(A \cap B)}{\min(\text{Area}(A), \text{Area}(B))}$$
   Nếu $\text{OverlapRatio}(A, B) > \text{MaxOverlap}$ (mặc định $0.50$) $\implies$ Ứng viên $B$ bị loại bỏ hoàn toàn.

---

### 3.8. Cơ chế phân phối nhánh động theo chế độ co giãn (ScaleMode Dynamic Dispatch)

Dựa vào lựa chọn của `ScaleMode` trên UI, hàm xử lý tại `0x140063f06` tự động phân phối luồng tính toán:

```
cboScaleMode->currentIndex()
│
├── Case 0 ("none"):
│     ScaleMin = 1.0, ScaleMax = 1.0
│     CALL mwwz.dll!find_shape_model(...)
│     -> Vô hiệu hóa vòng lặp scale, tốc độ cực đại.
│
├── Case 1 ("isotropic"):
│     ScaleMin != 1.0 hoặc ScaleMax != 1.0
│     CALL mwwz.dll!find_scaled_shape_model(...)
│     -> Bước scale tự động: delta_s = 0.5 / R_max.
│
├── Case 2 ("anisotropic"):
│     CALL mwwz.dll!find_aniso_shape_model(...)
│     -> Quét độc lập theo trục dòng (Row) và trục cột (Col).
│
├── Case 3 ("local_deformable"):
│     CALL mwwz.dll!find_local_deformable_model(...)
│     -> Khớp trường biến dạng hài hòa Laplace qua lặp Jacobi.
│
└── Case 4 ("planar_deformable"):
      CALL mwwz.dll!find_planar_uncalib_deformable_model(...)
      -> Khớp biến dạng phối cảnh 8 bậc tự do (Homography).
```

---

## 4. BẢN CHẤT TOÁN HỌC VÀ VAI TRÒ THUẬT TOÁN CỦA TỪNG THAM SỐ

### 4.1. Giải thuật trích xuất biên Steger Facet 3x3 và khai triển trị riêng ma trận Hessian

Thay vì sử dụng các phương pháp thô sơ như Canny hay binarization, `mwwz.dll` cài đặt mô hình giải tích mặt cong bậc hai Carsten Steger:
1. Tại mỗi lân cận $3 \times 3$, cường độ ảnh xám được khớp qua hệ đa thức trực giao Chebyshev rời rạc:
   $$z(u, v) = a_0 + a_1 u + a_2 v + a_3 \left(u^2 - \frac{2}{3}\right) + a_4 u v + a_5 \left(v^2 - \frac{2}{3}\right)$$
2. Ma trận Hessian của mặt cong vi phân:
   $$\mathbf{H} = \begin{bmatrix} 2 a_3 & a_4 \\ a_4 & 2 a_5 \end{bmatrix}$$
3. Khai triển trị riêng Jacobi tìm hướng pháp tuyến $\mathbf{n} = (\cos\theta, \sin\theta)$ ứng với trị riêng có độ lớn cực đại:
   $$\theta = \frac{1}{2} \text{atan2}(2 a_4, \; 2(a_3 - a_5))$$
4. Vị trí cực trị bậc nhất theo hướng pháp tuyến:
   $$t = -\frac{a_1 \cos\theta + a_2 \sin\theta}{2 a_3 \cos^2\theta + 2 a_4 \cos\theta \sin\theta + 2 a_5 \sin^2\theta}$$
5. **Điều kiện chấp nhận điểm biên Subpixel**:
   $$|t \cdot \cos\theta| \le 0.5 \quad \text{VÀ} \quad |t \cdot \sin\theta| \le 0.5$$
   Tọa độ chuẩn xác: $x_{\text{sub}} = x + t \cos\theta, \; y_{\text{sub}} = y + t \sin\theta$.

---

### 4.2. Lượng tử hóa hướng Gradient 16 Sector bằng bảng tỉ số tiếp tuyến số nguyên cố định (Fixed-point Tangent)

Để tối ưu hóa hiệu năng, góc gradient trên ảnh tìm kiếm không được tính bằng hàm `atan2` của thư viện toán học chuẩn (vốn rất chậm), mà được lượng tử hóa thành 16 sector ($22.5^\circ$ mỗi sector) bằng bảng tra tỉ số tiếp tuyến số nguyên cố định:

$$\text{Sector}(g_x, g_y) \in \{0, 1, 2, \dots, 15\}$$

Mỗi sector tương ứng với vector pháp tuyến chuẩn hóa $\mathbf{d}_k = (\cos(k \cdot 22.5^\circ), \sin(k \cdot 22.5^\circ))$.

---

### 4.3. Cấu trúc 16 kênh Response Maps và cơ chế tích vô hướng chuẩn hóa

Trước khi quét đối tượng, ảnh tìm kiếm được tiền xử lý thành **16 kênh bản đồ phản hồi (Response Maps)** dạng ảnh 8-bit:
- Giá trị tại pixel $(x, y)$ của kênh $k$ lưu trữ tích vô hướng được định lượng theo thang đo 128:
  $$R_k(x, y) = \text{round}\left( 128 \cdot \left( \mathbf{d}_k \cdot \frac{\nabla S(x, y)}{\|\nabla S(x, y)\|} \right) \right)$$
- Với chế độ `use polarity = 1`: $R_k \in [-128, 127]$ (Số nguyên có dấu `int8`).
- Với chế độ `ignore polarity = 0`: $R_k = |R_k| \in [0, 127]$ (Số nguyên không âm).
- Khi tính điểm cho tư thế ứng viên $(x_0, y_0, \theta)$, điểm số tương đồng chuẩn hóa được tính bằng:
  $$\mathbf{S(x_0, y_0, \theta) = \frac{1}{128 \cdot N} \sum_{i=1}^N R_{k_i'}(x_0 + x_i', \; y_0 + y_i') = \frac{1}{N} \sum_{i=1}^N \frac{\nabla S(\mathbf{x}_i) \cdot \mathbf{d}_i'}{\|\nabla S(\mathbf{x}_i)\|}}$$

---

### 4.4. Tăng tốc phần cứng SIMD AVX2 với khả năng tích lũy 32 pixel/chu kỳ lệnh

Tại địa chỉ `0x180030746` trong `mwwz.dll`, vòng lặp tính điểm được vector hóa bằng tập lệnh AVX2:
```assembly
vpmovsxbw ymm0, [rcx + rdx]        ; Nạp 16 byte int8 từ kênh Response Map, mở rộng dấu thành 16 word int16
vpmovsxbw ymm1, [rcx + rdx + 10h]   ; Nạp tiếp 16 byte int8 kế tiếp thành 16 word int16
vpaddsw   ymm0, ymm1, [rax + rsi]   ; Cộng dồn bão hòa 16-bit vào accumulator
vmovdqa   [rax + rsi], ymm0        ; Lưu kết quả
add       rax, 20h                 ; Xử lý song song 32 pixel trong một chu kỳ lệnh!
```
Nhờ cơ chế này, toàn bộ việc tính điểm cho một khung hình lớn $2448 \times 2048$ trên 9 phôi linh kiện chỉ tiêu tốn đúng **10.37 mili-giây**!

---

### 4.5. Các giải thuật tinh chỉnh vị trí dưới điểm ảnh (SubPixel Refinement)

Khi chọn `SubPixel` khác `none`, sau khi định vị được tọa độ nguyên đỉnh $(x^*, y^*, \theta^*)$:
- **Chế độ `interpolation` (1)**: Sử dụng nội suy parabol 1 chiều độc lập theo 3 trục $x, y, \theta$ từ các giá trị lân cận.
- **Chế độ `least-squares` (2)**: Khớp mặt Paraboloid 2D bậc hai tổng quát:
  $$f(\Delta x, \Delta y) = c_0 + c_1 \Delta x + c_2 \Delta y + c_3 \Delta x^2 + c_4 \Delta x \Delta y + c_5 \Delta y^2$$
  Nghiệm cực trị xác định độ dời dưới điểm ảnh chuẩn xác:
  $$\begin{bmatrix} \Delta x^* \\ \Delta y^* \end{bmatrix} = -\begin{bmatrix} 2 c_3 & c_4 \\ c_4 & 2 c_5 \end{bmatrix}^{-1} \begin{bmatrix} c_1 \\ c_2 \end{bmatrix}$$
  Đạt độ chính xác vị trí tới $\frac{1}{20} \dots \frac{1}{50}$ pixel.

---

### 4.6. Chẩn đoán phân loại chất lượng từng hạt đặc trưng (Per-Feature Quality Diagnostics)

API `get_match_details` xuất khẩu cấu trúc `MatchDetails` chứa mảng `float* scores` lưu điểm số của từng feature riêng rẽ:
- Tham số `spinBoxMinMag` (mặc định 10): Bỏ qua các điểm ảnh có gradient mờ nhạt.
- Tham số `dSpinBoxScore` (mặc định 0.50): Phân ngưỡng màu hiển thị:
  * Nếu $\text{score}_j \ge 0.50$: Hiển thị màu **Xanh lá cây** (Đoạn biên trùng khớp hoàn hảo).
  * Nếu $\text{score}_j < 0.50$: Hiển thị màu **Đỏ** (Đoạn biên bị khuyết tật, xước vỡ hoặc bị che khuất).

---

## 5. HƯỚNG DẪN TINH CHỈNH VÀ TỐI ƯU HÓA TRONG MÔI TRƯỜNG DÂY CHUYỀN CÔNG NGHIỆP

### 5.1. Tối ưu hóa Tốc độ xử lý và Độ trễ chu kỳ máy (Latency Minimization)

| Tham số điều chỉnh | Hướng tinh chỉnh | Cơ chế tăng tốc | Đánh đổi kỹ thuật |
|---|:---:|---|---|
| **LowestPyramid** | Tăng từ `1` lên `2` hoặc `3` | Dừng tìm kiếm ở tầng thu nhỏ ($1/2$ hoặc $1/4$), giảm $75\% - 93\%$ số phép tính | Độ chính xác vị trí giảm xuống $\pm 1 \dots \pm 2$ pixel |
| **ScaleMode** | Đặt cố định `none` | Triệt tiêu hoàn toàn vòng lặp quét tỷ lệ kích thước | Vật thể bị co giãn sẽ không tìm thấy |
| **AngleExtent** | Thu hẹp từ $360^\circ$ xuống $\pm 15^\circ$ | Giảm số lượng góc cần quét từ $\sim 720$ góc xuống còn $60$ góc (tăng tốc $\sim 12\times$) | Chỉ áp dụng khi phôi đã được định hướng cơ khí sơ bộ |
| **Greediness** | Tăng từ `0.75` lên `0.90 – 0.95` | Cắt tỉa nhánh sớm hơn và mạnh hơn trên cây tìm kiếm | Dễ bỏ sót đối tượng nếu bị che khuất một phần |
| **MinScore** | Tăng từ `0.50` lên `0.70 – 0.80` | Nâng cao cận chặn dưới, giúp giải thuật Branch-and-Bound loại bỏ ứng viên tức thời | Không phát hiện được sản phẩm bị mờ hoặc tương phản kém |
| **SubPixel** | Đặt `none` hoặc `speed-first` | Loại bỏ bước giải phương trình ma trận nghịch đảo vi phân | Độ lặp lại vị trí dừng ở mức pixel nguyên |

---

### 5.2. Triệt tiêu triệt để báo động giả trên phôi kim loại gia công (False-Positive Rejection)

Trong môi trường có phôi kim loại gia công cơ khí hoặc bề mặt sần sùi (như ảnh chip IC):
1. **Thiết lập `MinCompSize = 16 – 32` px**: Đây là tham số quyết định nhất. Loại bỏ triệt để toàn bộ các đốm xước ngắn, chỉ giữ lại các đường nét hình học cốt lõi của linh kiện.
2. **Kích hoạt `Metric = use polarity`**: Bắt buộc chiều chuyển màu sáng-sang-tối phải đúng tuyệt đối với mẫu, ngăn chặn việc nhận nhầm các cạnh viền đối xứng của bóng đổ hoặc khe hở cơ khí.
3. **Tăng `ContrastLow` và `ContrastHigh`**: Đặt `ContrastLow = 30 – 40`, `ContrastHigh = 50 – 60` để lọc sạch các vân xước tế vi của vật liệu.
4. **Giảm `MaxOverlap` xuống `0.20 – 0.30`**: Loại bỏ hiện tượng phát hiện đúp khi các chi tiết dạng lưới hoặc ren ốc nằm sát nhau.

---

### 5.3. Xử lý dung sai góc xoay và định hướng linh kiện (Rotation Tolerance Tradeoff)

- Khi linh kiện cấp rời tự do trên mâm rung (Bowl Feeder): Đặt `AngleStart = 0.0`, `AngleExtent = 360.0`. Động cơ sẽ tự động chia đều các bước góc $\Delta\theta_0 = 0.5 / R_{\max}$.
- Khi linh kiện trên băng chuyền có rãnh dẫn hướng: Giới hạn `AngleStart = -15.0`, `AngleExtent = 30.0` để tối ưu hóa thời gian xử lý chu kỳ máy (Cycle Time).

---

### 5.4. Xử lý linh kiện biến đổi kích thước nhiệt và biến dạng bao bì mềm (Scale & Deformation Invariance)

- **Khi camera gắn cố định vuông góc**: Luôn để `ScaleMode = none` (`ScaleMin = ScaleMax = 1.0`).
- **Khi khoảng cách làm việc thay đổi (Working Distance Drift)**:
  * Chọn `ScaleMode = isotropic`.
  * Đặt dải hẹp quanh 1.0: `ScaleMin = 0.90`, `ScaleMax = 1.10`.
  * Không đặt dải quá rộng (ví dụ $0.3 - 2.0$) trừ khi thực sự cần thiết, vì không gian tìm kiếm sẽ tăng gấp số lần tỷ lệ quét.
- **Khi bề mặt phôi bị uốn cong hoặc nhăn (Bao bì, màng nhựa)**: Chuyển sang `ScaleMode = local_deformable` và đặt `MaxDeformation = 1 – 3`.

---

### 5.5. Khả năng kháng biến thiên cường độ chiếu sáng và tương phản (Illumination Robustness)

Về mặt toán học, phép tích vô hướng chuẩn hóa giữa hai vector gradient đơn vị:

$$\frac{\nabla I_1}{\|\nabla I_1\|} \cdot \frac{\nabla I_2}{\|\nabla I_2\|}$$

hoàn toàn **bất biến với mọi phép biến đổi độ sáng tuyến tính** dạng $I'(x, y) = \alpha \cdot I(x, y) + \beta$ (do hệ số tỷ lệ $\alpha$ ở tử số và mẫu số tự triệt tiêu).
- Để bảo vệ hệ thống trước sự suy giảm ánh sáng đèn LED theo thời gian:
  * Đặt `MinContrast = 5 – 9` để bảo đảm tín hiệu biên không bị chìm xuống dưới sàn nhiễu.
  * Sử dụng hàm chuẩn hóa động `to8U_AutoContrast_MinMax` đối với ảnh 16-bit trước khi đưa vào pipeline so khớp.

---

## 6. KẾT LUẬN VÀ BÀI HỌC KINH NGHIỆM CHO HỆ THỐNG TUNVISION

1. Bảng tham số của hệ thống được thiết kế theo tư duy Halcon công nghiệp, với sự phân tách mạch lạc giữa tham số tạo mô hình (đặc trưng hình học, lọc nhiễu Steger) và tham số tìm kiếm (không gian trạng thái, tỉa nhánh greediness, tầng dừng kim tự tháp).
2. Tính năng tự động cân bằng (Auto-Balancing) là chìa khóa then chốt giúp phần mềm chạy mượt mà trên các ảnh công nghiệp khó:
   * Tự động suy diễn số tầng kim tự tháp: $L = \text{round}(\log_2(\min(W, H)/16)) + 1$.
   * Tự động tính toán bộ tứ ngưỡng vi phân Hysteresis và lọc chiều dài contour $L_{\text{arc}} \ge 16$ px từ biểu đồ phân phối gradient.
   * Tự động mở rộng bước góc theo tầng kim tự tháp: $\Delta\theta_l = (0.5 / R_{\max}) \cdot 2^l$.
   * Tự động nới lỏng ngưỡng tầng thô $S_{\min} \times 0.90$.
3. Đề xuất kiến trúc cho TunVision (Clean-room .NET 8):
   * Thay thế việc phân ngưỡng thủ công bằng module `TopologicalContourExtractor` tích hợp lọc `MinComponentSize` (mặc định 16 px).
   * Bổ sung cấu trúc 16 Response Maps và bộ tích lũy SIMD AVX2.
   * Thêm cơ chế chẩn đoán chất lượng hạt feature (tô màu Green/Red dựa trên `dSpinBoxScore` và `minMag`) để nâng cao trải nghiệm người dùng trong TunVision Studio.

---
*Tài liệu kết thúc.*