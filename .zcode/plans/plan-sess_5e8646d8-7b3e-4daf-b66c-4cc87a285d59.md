# Nâng cấp giao diện — bỏ "cảm giác AI", đồng bộ kiểu chữ (giữ nguyên 100% bố cục)

## Nguyên tắc bất di bất dịch
- **KHÔNG thay đổi bố cục**: không đụng đến cấu trúc HTML/section/grid của bất kỳ trang nào (đặc biệt Admin). Chỉ chỉnh CSS: font, màu, bo góc, bóng đổ, hiệu ứng.
- Chỉnh sửa **tại chỗ** trong các khối `<style>` hiện có của view (không tách file, không refactor markup) để hạn chế rủi ro.

## Hệ định hướng mới (client)
- **Font theo vai trò** (áp dụng thống nhất toàn project):
  - **Tiêu đề / Header / Nav / Nút / Badge / Số liệu**: `Be Vietnam Pro` (600–800) — font thiết kế riêng cho tiếng Việt, đang có sẵn ở trang Detail.
  - **Nội dung / Đoạn văn / Bảng / Form**: `Inter` (400–600) — trùng với font Admin đang dùng → client và admin đồng bộ với nhau.
  - **Bỏ Bungee, Manrope, Plus Jakarta Sans, Work Sans, Open Sans, Roboto** → hết cảnh 5+ font trộn lẫn.
- **Bo góc chuẩn hóa**: nút/input `8px`, card `12px`, khối lớn/section panel `16px`, chỉ badge/tag giữ `999px` (pill), avatar `50%`. Xóa các giá trị lẻ 2/4/7/13/18/22/24/28/30/32/50px.
- **Bỏ hiệu ứng "AI"**: xóa animation `ctaPulse` nhấp nháy vô hạn, bỏ glow nhiều lớp màu, bỏ sheen kính mờ (`backdrop-filter`, `::before` bóng trắng), gradient tinh giản (header/hero giữ 1 gradient xanh lá dịu, còn lại màu phẳng), bóng đổ 1 lớp tự nhiên.
- **Màu**: giữ nhận diện xanh lá + vàng, quy về 1 bộ token duy nhất (hết cảnh mỗi trang một tông xanh).

---

## Giai đoạn 1 — Commit backup + hệ token nền tảng
1. Commit trạng thái hiện tại lên git làm điểm rollback an toàn.
2. Tạo `wwwroot/assets/css/theme.css` (load **sau** main.css trong `_HomeLayout`):
   - Token font: `--font-heading` / `--font-body`, đồng thời ghi đè `--font-default/--font-primary/--font-secondary` cũ.
   - Token màu: scale xanh lá (900→50), vàng nhấn, slate cho chữ; map cả 3 hệ token cũ (`--color-primary #feb900`, `--scrap-*` v1, `--scrap-*` v2) về cùng giá trị → hết xung đột.
   - Token bo góc `--radius-sm/md/lg` + bóng `--shadow-sm/md/lg`.
3. Sửa `_HomeLayout.cshtml`: thay link Google Fonts (Open Sans/Roboto/Work Sans) bằng 1 link duy nhất Be Vietnam Pro + Inter; bỏ điều kiện load Bungee (`NeedsBungee`); thêm thẻ load theme.css.
4. Sửa 3 khối `:root` trong `wwwroot/assets/css/main.css` (dòng ~13, ~2664, ~7037) về giá trị token thống nhất.

## Giai đoạn 2 — De-AI main.css (9.733 dòng, chỉ sửa selector đang dùng)
- Bỏ `ctaPulse`, glow nhiều lớp → `--shadow-*`; bỏ sheen/backdrop-filter ở button/header CTA.
- Header: gradient xanh lá đơn giản hơn (hoặc màu phẳng đậm); hero: giảm blob/gradient mạnh.
- Quy đồng bo góc trong main.css về token (cards 16px, nút 8–10px, pill chỉ cho badge).
- Không xóa các section CSS của template cũ không dùng (rủi ro cascade) — chỉ sửa giá trị.

## Giai đoạn 3 — Đồng bộ từng trang client (sửa `<style>` trong view, không đụng markup)
Với **mỗi** trang sau: bỏ import font riêng của trang, map hex cứng về token, quy bo góc, thay glow bằng shadow token, bỏ animation pulse:
- `Views/Home/Index.cshtml` (~246 dòng CSS)
- `Views/Home/Detail.cshtml` + `wwwroot/css/detail.css` (đã dùng Be Vietnam Pro + Material Symbols — chỉ cần map font/màu về token chung)
- `Views/Home/About.cshtml` (~502 dòng)
- `Views/Product/Index.cshtml` (~244 dòng)
- `Views/Pricing/Index.cshtml` (~780 dòng): giữ nguyên tên biến `--pricing-*` nhưng đổi **giá trị** về token chung (an toàn tuyệt đối, không phải sửa 1.000 tham chiếu)
- `Views/Blog/Index.cshtml` (~354 dòng), `Views/Blog/Detail.cshtml` (~1.739 dòng)
- `Views/Contact/Index.cshtml`: bỏ `:root` khai báo đè, dùng token chung
- 4 ViewComponent: FeaturedProducts (+Pricing), RecentNews, RelatedProducts, ScrapServices
- `Views/Shared/P_Header.cshtml` + `P_Footer.cshtml` (header: nút CTA bỏ glow/pulse; footer toggle bo 6px → 8px)

## Giai đoạn 4 — Admin (bố cục nguyên vẹn, chỉ vệ sinh thị giác)
1. `wwwroot/adminwebsite/css/admin-dashboard.css`: quy 14 giá trị bo góc về token có sẵn (10/13/14→12, 16/18→16, 20/22→20, 24/28/30→24); thêm `--font-heading: Be Vietnam Pro` cho tiêu đề để đồng bộ với client (thêm Be Vietnam Pro vào link font của `_AdminLayout`).
2. `Views/AdminAuth/Index.cshtml`: **thêm link Google Fonts Inter** (đang khai báo Inter nhưng không load → rơi về Arial).
3. TinyMCE (`Products.cshtml:1652`, `Posts.cshtml:2998`): đổi `content_style` font từ "Plus Jakarta Sans" (không tồn tại) → "Inter".
4. `Views/Admin/Prices.cshtml`: đổi tông teal/tím lạc quẻ → token xanh primary của hệ.
5. Kiểm tra `Marketing.cshtml` (trang admin có load main.css) hoạt động tốt sau khi sửa main.css.

## Giai đoạn 5 — Kiểm chứng
1. `dotnet build` — đảm bảo không lỗi Razor.
2. Chạy site, chụp màn hình các trang chính (Home, Detail, Pricing, Blog, Blog Detail, Contact, About, Product + Admin Overview/Prices/Posts/Login) để đối chiếu: font đúng vai trò, bo góc nhất quán, không còn pulse/glow, **bố cục không xê dịch**.
3. Chạy visual judge trên các trang đã render để bắt lỗi thị giác trước khi bàn giao.
4. Commit kết quả.

## Rủi ro & cách xử lý
- `main.css` dài 9.733 dòng, cascade phức tạp → theme.css load sau để thắng specificity; các sửa trong main.css chỉ tính toán giá trị, không xóa selector.
- File `.br/.gz` nén sẵn trong wwwroot/publish-host là of bản deploy cũ, không ảnh hưởng môi trường dev; khi deploy thật cần publish lại.
- Không đụng `Views/Shared/_Layout.cshtml` (trang Error) và các file không liên quan.