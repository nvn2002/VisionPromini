# Original User Request

## Initial Request — 2026-11-13T02:54:10Z

Nghiên cứu, phân tích và dịch ngược ứng dụng demo mẫu MFC C++ (TemplateMatch.exe, mwwz.dll, mwwzcv.dll) để tìm hiểu tại sao app demo tìm vật thể tốt, so sánh đối chiếu chi tiết với thuật toán hiện tại trong TunVision.Core\ShapeMatching\, chỉ ra các nguyên nhân cốt lõi khiến TunVision kém chất lượng hơn và đề xuất kế hoạch khắc phục, nâng cấp chi tiết.

Working directory: d:\tunvision\mwwz-samples
Integrity mode: development

## Requirements

### R1. Phân tích và dịch ngược thuật toán của ứng dụng demo mẫu
Nghiên cứu, phân tích và dịch ngược các thành phần của ứng dụng demo mẫu tại `c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.10.22\demo` và `d:\tunvision\mwwz-samples` (bao gồm `TemplateMatch.exe`, `mwwz.dll`, `mwwzcv.dll`) để làm rõ:
- Thuật toán trích xuất biên mẫu (training / contour extraction: gradient, canny, sobel, thresholding, lọc hướng đạo hàm, xây dựng các cấp pyramid).
- Thuật toán khớp mẫu (matching / search engine: công thức tính điểm tương đồng score, tinh chỉnh vị trí subpixel, chiến lược tìm kiếm góc xoay và tỉ lệ scale, cơ chế tỉa nhánh đa tầng coarse-to-fine pruning, greediness).

### R2. So sánh đối chiếu với thuật toán hiện tại của TunVision
Đối chiếu chi tiết từng bước thuật toán được tìm thấy từ app demo với mã nguồn hiện tại của TunVision tại `d:\tunvision\mwwz-samples\TunVision\TunVision.Core\ShapeMatching\` (ShapeMatcher.cs, ShapeModel.cs, ContourExtractor.cs, v.v.) và các node liên quan trong `d:\tunvision\mwwz-samples\TunVision\TunVision.Core\Graph\Nodes.cs`:
- Chỉ rõ các khác biệt cốt lõi giải thích vì sao demo mẫu khớp tốt, nhạy, chính xác và ổn định, trong khi TunVision hiện tại lại kém chất lượng hơn.
- Phân tích cụ thể các thiếu sót hoặc điểm chưa tối ưu trong TunVision về xử lý gradient, ngưỡng biên, hướng gradient, cấu trúc kim tự tháp ảnh (pyramid levels), chiến lược coarse-to-fine, hàm tính score, và tinh chỉnh subpixel.

### R3. Báo cáo phân tích chuyên sâu và kế hoạch nâng cấp chi tiết
Lập một tài liệu báo cáo phân tích toàn diện lưu tại `d:\tunvision\mwwz-samples` bao gồm:
- Toàn bộ kết quả phân tích thuật toán của demo mẫu và đối chiếu với TunVision.
- Kế hoạch và lộ trình kỹ thuật chi tiết từng bước để nâng cấp `TunVision.Core\ShapeMatching\` nhằm đạt chất lượng nhận diện tương đương hoặc vượt trội so với demo mẫu.

## Verification Resources
- Tập ảnh mẫu và dữ liệu kiểm thử có sẵn tại: `d:\tunvision\mwwz-samples\imgs` và `d:\tunvision\mwwz-samples\data`.

## Acceptance Criteria

### Báo cáo phân tích thuật toán
- [ ] Báo cáo ghi nhận đầy đủ kiến trúc và công thức toán học/logic trích xuất biên mẫu từ demo mẫu (gradient, ngưỡng lọc biên, hướng vector biên, xử lý đa tầng pyramid).
- [ ] Báo cáo ghi nhận đầy đủ thuật toán khớp mẫu từ demo mẫu (công thức tính score, tìm kiếm đa góc/tỉ lệ, chiến lược coarse-to-fine, điều kiện dừng sớm/greediness, subpixel refinement).
- [ ] Bảng so sánh đối chiếu từng bước (side-by-side) giữa demo mẫu và TunVision.Core\ShapeMatching\, nêu rõ từng nguyên nhân kỹ thuật khiến TunVision kém chất lượng hơn.
- [ ] Báo cáo chỉ rõ kết quả đối chiếu trên các mẫu ảnh thực tế từ `d:\tunvision\mwwz-samples\imgs`.
- [ ] Kế hoạch khắc phục và lộ trình nâng cấp chi tiết cho TunVision với các giải pháp kỹ thuật cụ thể và khả thi.
