# Kế hoạch khắc phục web load chậm và chuyển sang cursor pagination

## 1. Mục tiêu

- Giảm cảm giác trắng màn hình/xoay vòng lâu khi tải lần đầu.
- Giảm số truy vấn và dữ liệu render trong request trang chủ.
- Giảm dung lượng ảnh, CSS, JS tải ban đầu.
- Chuyển phân trang offset ở danh sách sản phẩm và blog sang cursor pagination để load mượt hơn khi dữ liệu tăng.
- Bổ sung đo kiểm để so sánh trước/sau tối ưu.

## 2. Kết luận nguyên nhân chính

### 2.1. Preloader che toàn màn hình quá lâu

- Preloader được render tại [`P_Footer.cshtml`](Views/Shared/P_Footer.cshtml:257).
- CSS [`#preloader`](wwwroot/assets/css/main.css:205) phủ toàn màn hình với lớp nền trắng và z-index rất cao.
- Script hiện tại chỉ xóa preloader khi [`window.addEventListener()`](wwwroot/assets/js/main.js:15) nhận sự kiện tải xong toàn bộ tài nguyên, sau đó gọi [`HTMLElement.remove()`](wwwroot/assets/js/main.js:16).
- Vì sự kiện này chờ ảnh, font, CSS, JS và script bên ngoài, người dùng có thể thấy vòng xoay 5-10 giây dù HTML đã có thể render trước đó.

### 2.2. Trang chủ chạy nhiều truy vấn trong request đầu

Các nguồn truy vấn/cached data chính:

- [`HomeController.Index()`](Controllers/HomeController.cs:29) lấy banner, contact và sản phẩm.
- [`BannerInjectHelper.GetActiveBannersAsync()`](helpper/BannerInjectHelper.cs:22) lấy banner active.
- [`ContactInfoHelper.GetContactInfoAsync()`](helpper/ContactInfoHelper.cs:21) lấy contact settings.
- [`SeoSettingHelper.GetSeoSettingsAsync()`](helpper/SeoSettingHelper.cs:30) lấy SEO settings từ layout.
- [`LayoutDataHelper.GetServiceMenuItemsAsync()`](helpper/LayoutDataHelper.cs:21) lấy menu dịch vụ header.
- [`LayoutDataHelper.GetFooterFeaturedProductsAsync()`](helpper/LayoutDataHelper.cs:45) lấy sản phẩm footer.
- [`LayoutDataHelper.GetFooterTopServicePostsAsync()`](helpper/LayoutDataHelper.cs:67) lấy bài dịch vụ footer.
- [`RecentNewsSectionComponentViewComponent.InvokeAsync()`](Components/RecentNewsSectionComponentViewComponent.cs:25) lấy tin tức gần đây.
- [`ScrapPurchaseServicesSectionComponentViewComponent.InvokeAsync()`](Components/ScrapPurchaseServicesSectionComponentViewComponent.cs:25) lấy dịch vụ thu mua.
- [`StructuredDataService.BuildHomeSchemas()`](Services/StructuredDataService.cs:48) tạo structured data và đọc site settings qua [`StructuredDataService.BuildSiteContext()`](Services/StructuredDataService.cs:275).

### 2.3. Trang chủ load toàn bộ sản phẩm active

- Truy vấn sản phẩm trang chủ bắt đầu tại [`HomeController.cs`](Controllers/HomeController.cs:50).
- Truy vấn lọc theo status tại [`HomeController.cs`](Controllers/HomeController.cs:52).
- Truy vấn gọi [`EntityFrameworkQueryableExtensions.ToListAsync()`](Controllers/HomeController.cs:79) nhưng chưa giới hạn số lượng.
- View render product card trong vòng lặp tại [`Index.cshtml`](Views/Home/Index.cshtml:418).

### 2.4. Ảnh và tài nguyên tĩnh nặng

- Thư mục [`wwwroot`](wwwroot) có khoảng 144 MB tài nguyên tĩnh.
- Thư mục [`assets/images/blogs`](wwwroot/assets/images/blogs) khoảng 64 MB.
- Thư mục [`assets/images/products`](wwwroot/assets/images/products) khoảng 37 MB.
- Logo [`logo2.png`](wwwroot/assets/img/logo2.png) khoảng 518 KB, nên giảm xuống dưới 80 KB.
- Logo [`logo01.png`](wwwroot/assets/img/logo01.png) khoảng 466 KB, nên giảm xuống dưới 80 KB.
- Ảnh hero [`satthep.png`](wwwroot/assets/img/satthep.png) khoảng 545 KB, nên có bản WebP/AVIF tối ưu.

### 2.5. Layout nạp nhiều thư viện chung

- CSS được nạp tại [`_HomeLayout.cshtml`](Views/Shared/_HomeLayout.cshtml:81).
- JS được nạp tại [`_HomeLayout.cshtml`](Views/Shared/_HomeLayout.cshtml:113).
- Một số thư viện như lightbox, isotope, counter hoặc validate không phải trang nào cũng cần tải ngay.

## 3. Lộ trình triển khai ưu tiên

### Giai đoạn 1 — Sửa preloader

Mục tiêu: hiển thị nội dung sớm, không để preloader che trang đến khi toàn bộ ảnh/script tải xong.

Checklist:

- Sửa logic preloader trong [`main.js`](wwwroot/assets/js/main.js:13).
- Không phụ thuộc vào sự kiện tại [`main.js`](wwwroot/assets/js/main.js:15) để xóa preloader.
- Xóa preloader sau khi DOM sẵn sàng và đặt timeout dự phòng.
- Có class fade-out để trải nghiệm mượt hơn, sau đó gọi [`HTMLElement.remove()`](wwwroot/assets/js/main.js:16).
- Kiểm tra trường hợp script lỗi để [`#preloader`](wwwroot/assets/css/main.css:205) không che trang mãi.

Tiêu chí hoàn thành:

- Nội dung trang hiện ra trong 1-2 giây nếu HTML đã trả về.
- Không còn cảm giác xoay vòng trắng màn hình 10 giây do chờ toàn bộ ảnh tải xong.

### Giai đoạn 2 — Tối ưu ảnh và tạo thumbnail

Mục tiêu: giảm dung lượng ảnh tải ban đầu và giảm thời gian tải toàn trang.

Checklist:

- Tối ưu [`logo2.png`](wwwroot/assets/img/logo2.png) và [`logo01.png`](wwwroot/assets/img/logo01.png).
- Tạo bản WebP/AVIF cho ảnh hero như [`satthep.png`](wwwroot/assets/img/satthep.png).
- Tạo thumbnail cho sản phẩm trong [`assets/images/products`](wwwroot/assets/images/products).
- Tạo thumbnail cho blog trong [`assets/images/blogs`](wwwroot/assets/images/blogs).
- Sửa component tin tức để ưu tiên thumbnail tại [`RecentNewsSectionComponentViewComponent.cs`](Components/RecentNewsSectionComponentViewComponent.cs:47).
- Sửa component dịch vụ để ưu tiên thumbnail tại [`ScrapPurchaseServicesSectionComponentViewComponent.cs`](Components/ScrapPurchaseServicesSectionComponentViewComponent.cs:47).
- Cập nhật view card ảnh tại [`Default.cshtml`](Views/Shared/Components/RecentNewsSectionComponent/Default.cshtml:97) và [`Default.cshtml`](Views/Shared/Components/ScrapPurchaseServicesSectionComponent/Default.cshtml:97).

Tiêu chí hoàn thành:

- Logo nhỏ hơn 80 KB.
- Ảnh card nhỏ hơn 100-180 KB tùy kích thước.
- Ảnh hero nhỏ hơn 250-350 KB nếu có thể.

### Giai đoạn 3 — Cache và giảm truy vấn trang chủ

Mục tiêu: request đầu nhẹ hơn và request sau lấy từ cache nhiều hơn.

Checklist:

- Thêm key home products vào [`SiteCacheKeys`](Services/SiteCacheKeys.cs:3).
- Cache danh sách sản phẩm trang chủ trong [`HomeController.Index()`](Controllers/HomeController.cs:29).
- Giới hạn số sản phẩm trang chủ, ví dụ 12 hoặc 16 item.
- Không gọi [`EntityFrameworkQueryableExtensions.ToListAsync()`](Controllers/HomeController.cs:79) trên toàn bộ sản phẩm active.
- Gộp data header/footer thành một cache snapshot thay vì gọi rải rác nhiều hàm trong [`P_Header.cshtml`](Views/Shared/P_Header.cshtml:5) và [`P_Footer.cshtml`](Views/Shared/P_Footer.cshtml:6).
- Thêm output cache policy cho trang chủ ở [`Program.cs`](Program.cs:22).

Tiêu chí hoàn thành:

- Request trang chủ sau cache nhanh ổn định.
- Cache miss không còn chạy quá nhiều truy vấn nhỏ lẻ.

### Giai đoạn 4 — Giảm payload HTML trang chủ

Mục tiêu: giảm dung lượng HTML và JS inline trong lần tải đầu.

Checklist:

- Giữ hero và phần nội dung quan trọng trong HTML ban đầu tại [`Index.cshtml`](Views/Home/Index.cshtml:85).
- Lazy-load section tin tức tại [`Index.cshtml`](Views/Home/Index.cshtml:1087).
- Lazy-load section dịch vụ tại [`Index.cshtml`](Views/Home/Index.cshtml:1184).
- Đưa script inline lớn tại [`Index.cshtml`](Views/Home/Index.cshtml:470) ra file tĩnh để browser cache được.
- Giảm số card render server-side trong lần đầu.

Tiêu chí hoàn thành:

- HTML trang chủ giảm rõ rệt.
- DOMContentLoaded nhanh hơn.
- LCP không bị cạnh tranh bởi nhiều section dưới fold.

## 4. Cursor pagination cho Product

### 4.1. Hiện trạng

Product hiện dùng offset pagination trong [`ProductController.BuildProductIndexViewModelAsync()`](Controllers/ProductController.cs:59):

- [`Queryable.Skip()`](Controllers/ProductController.cs:82).
- [`Queryable.Take()`](Controllers/ProductController.cs:83).

Khi dữ liệu tăng, offset page sâu sẽ chậm hơn vì database vẫn phải bỏ qua nhiều dòng.

### 4.2. Sort chuẩn đề xuất

Dùng thứ tự ổn định:

- `IsFeatured` giảm dần theo logic tại [`ProductController.cs`](Controllers/ProductController.cs:68).
- `Name` tăng dần theo logic tại [`ProductController.cs`](Controllers/ProductController.cs:69).
- `Id` tăng dần để làm tie-breaker.

Cursor product cần chứa:

- `isFeatured`.
- `name`.
- `id`.

### 4.3. Endpoint đề xuất

Tạo endpoint mới cạnh [`ProductController.GetPagedProducts()`](Controllers/ProductController.cs:30):

- [`ProductController.GetProductsCursor()`](Controllers/ProductController.cs:30).
- Route đề xuất: `/Product/GetProductsCursor`.
- Query đề xuất: `cursor`, `category`, `limit`.

Response nên có:

- `products`.
- `nextCursor`.
- `hasMore`.
- `limit`.

### 4.4. Điều kiện lấy batch tiếp theo

Với sort `IsFeatured DESC`, `Name ASC`, `Id ASC`, item sau cursor là:

- `IsFeatured` nhỏ hơn cursor, hoặc
- `IsFeatured` bằng cursor và `Name` lớn hơn cursor, hoặc
- `IsFeatured` bằng cursor và `Name` bằng cursor và `Id` lớn hơn cursor.

### 4.5. UI đề xuất

- Thay phân trang số trong [`Index.cshtml`](Views/Product/Index.cshtml:349) bằng nút “Xem thêm”.
- Khi click, gọi endpoint cursor và append card mới.
- Vẫn giữ fallback link page cũ trong giai đoạn chuyển đổi nếu cần.

## 5. Cursor pagination cho Blog

### 5.1. Hiện trạng

Blog hiện dùng offset pagination trong [`BlogController.BuildBlogIndexViewModelAsync()`](Controllers/BlogController.cs:261):

- [`Queryable.Skip()`](Controllers/BlogController.cs:307).
- [`Queryable.Take()`](Controllers/BlogController.cs:308).

### 5.2. Sort chuẩn đề xuất

Thứ tự hiện tại:

- `PublishedAt ?? CreatedAt` giảm dần tại [`BlogController.cs`](Controllers/BlogController.cs:305).
- `Id` giảm dần tại [`BlogController.cs`](Controllers/BlogController.cs:306).

Cursor blog cần chứa:

- `sortDate`.
- `id`.

### 5.3. Endpoint đề xuất

Tạo endpoint mới cạnh [`BlogController.GetPagedBlogs()`](Controllers/BlogController.cs:181):

- [`BlogController.GetBlogsCursor()`](Controllers/BlogController.cs:181).
- Route đề xuất: `/Blog/GetBlogsCursor`.
- Query đề xuất: `cursor`, `searchTerm`, `categoryName`, `limit`.

Response nên có:

- `posts`.
- `nextCursor`.
- `hasMore`.
- `limit`.

### 5.4. Điều kiện lấy batch tiếp theo

Với sort `SortDate DESC`, `Id DESC`, item sau cursor là:

- `SortDate` nhỏ hơn cursor, hoặc
- `SortDate` bằng cursor và `Id` nhỏ hơn cursor.

### 5.5. UI đề xuất

- Thay phân trang số trong [`Index.cshtml`](Views/Blog/Index.cshtml:502) bằng nút “Xem thêm” hoặc infinite scroll.
- Tái sử dụng logic render card hiện đang có trong script blog tại [`Index.cshtml`](Views/Blog/Index.cshtml:899).
- Khi filter/search thay đổi, reset cursor và load batch đầu.

## 6. Chuẩn hóa cursor token

Mục tiêu: cursor ngắn, an toàn cơ bản, dễ parse.

Checklist:

- Tạo helper encode/decode Base64 URL-safe JSON.
- Product cursor map sang DTO riêng.
- Blog cursor map sang DTO riêng.
- Nếu cursor invalid, trả batch đầu tiên thay vì lỗi 500.
- Nếu muốn chống sửa cursor, bổ sung chữ ký HMAC ở giai đoạn sau.

Vị trí triển khai đề xuất:

- Helper chung trong [`Services`](Services) hoặc private helper trong [`ProductController`](Controllers/ProductController.cs:10) và [`BlogController`](Controllers/BlogController.cs:13) giai đoạn đầu.

## 7. Index SQL cần bổ sung

### 7.1. Product

Phục vụ filter/sort ở [`ProductController`](Controllers/ProductController.cs:10) và trang chủ ở [`HomeController`](Controllers/HomeController.cs:10):

- Index cho `status`, `is_featured`, `name`, `id`.
- Nếu lọc theo category, nên ưu tiên `category_id` thay vì tên category.
- Index bổ sung cho `category_id`, `status`, `is_featured`, `name`, `id` nếu filter category phổ biến.

### 7.2. Blog

Phục vụ query ở [`BlogController`](Controllers/BlogController.cs:13), [`RecentNewsSectionComponentViewComponent`](Components/RecentNewsSectionComponentViewComponent.cs:12) và [`ScrapPurchaseServicesSectionComponentViewComponent`](Components/ScrapPurchaseServicesSectionComponentViewComponent.cs:12):

- Index cho `status`, `published_at`, `created_at`, `id`.
- Index cho mapping `category_id`, `post_id` trong bảng blog-post-category.
- Nếu search bằng pattern chứa keyword ở giữa, cân nhắc full-text search sau.

### 7.3. Site settings và layout data

- Đảm bảo unique index cho `setting_key` như khai báo tại [`databasevechai.sql`](database/databasevechai.sql:328).
- Đảm bảo index cho banner active/order phục vụ [`BannerInjectHelper.GetActiveBannersAsync()`](helpper/BannerInjectHelper.cs:28).

## 8. Tối ưu CSS/JS theo trang

Mục tiêu: giảm tài nguyên tải ban đầu trong layout.

Checklist:

- Chỉ nạp lightbox khi trang cần [`glightbox.min.js`](wwwroot/assets/vendor/glightbox/js/glightbox.min.js).
- Chỉ nạp isotope khi trang có portfolio/filter [`isotope.pkgd.min.js`](wwwroot/assets/vendor/isotope-layout/isotope.pkgd.min.js).
- Chỉ nạp counter khi trang có counter [`purecounter_vanilla.js`](wwwroot/assets/vendor/purecounter/purecounter_vanilla.js).
- Chỉ nạp validate khi trang có form tương ứng [`validate.js`](wwwroot/assets/vendor/php-email-form/validate.js).
- Thêm `defer` cho script local ở [`_HomeLayout.cshtml`](Views/Shared/_HomeLayout.cshtml:113) nếu không cần chạy blocking.
- Tách critical CSS cho hero/header, phần còn lại tải sau.

## 9. Cold start và production

Checklist:

- Nếu production không sửa view runtime, loại package [`Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation`](WebThuMuaPheLieu.csproj:18).
- Đảm bảo chạy Release build.
- Nếu deploy IIS, bật Always On hoặc preload app.
- Mở rộng warmup hiện có trong [`DatabaseConnectionWarmupService`](Program.cs:197):
  - Warmup SEO settings.
  - Warmup contact info.
  - Warmup banner.
  - Warmup layout data.
  - Warmup home products.

## 10. Đo kiểm trước/sau

Checklist đo server-side:

- Log timing trong [`HomeController.Index()`](Controllers/HomeController.cs:29).
- Log timing trong [`RecentNewsSectionComponentViewComponent.InvokeAsync()`](Components/RecentNewsSectionComponentViewComponent.cs:25).
- Log timing trong [`ScrapPurchaseServicesSectionComponentViewComponent.InvokeAsync()`](Components/ScrapPurchaseServicesSectionComponentViewComponent.cs:25).
- Log cache hit/miss cho các helper chính.

Checklist đo browser:

- TTFB.
- DOMContentLoaded.
- Load.
- LCP.
- Tổng dung lượng ảnh tải ban đầu.
- Waterfall các request chậm nhất.

## 11. Thứ tự triển khai khuyến nghị

1. Sửa preloader trong [`main.js`](wwwroot/assets/js/main.js:13).
2. Tối ưu logo và hero image.
3. Cache và giới hạn sản phẩm trang chủ trong [`HomeController.Index()`](Controllers/HomeController.cs:29).
4. Thêm output cache cho trang chủ ở [`Program.cs`](Program.cs:22).
5. Thêm cursor endpoint cho Product trong [`ProductController`](Controllers/ProductController.cs:10).
6. Cập nhật UI Product dùng “Xem thêm”.
7. Thêm cursor endpoint cho Blog trong [`BlogController`](Controllers/BlogController.cs:13).
8. Cập nhật UI Blog dùng “Xem thêm”.
9. Thêm index SQL tương ứng.
10. Tách/lazy-load JS/CSS không cần thiết trong [`_HomeLayout.cshtml`](Views/Shared/_HomeLayout.cshtml:81).
11. Warmup cache production và đo benchmark.

## 12. Checklist nghiệm thu

- Trang không còn bị preloader che quá 1-2 giây khi HTML đã sẵn sàng.
- LCP trang chủ giảm rõ rệt.
- Tài nguyên ảnh tải ban đầu giảm ít nhất 40-60%.
- Request trang chủ cache hit nhanh và ổn định.
- Product load thêm bằng cursor không dùng offset sâu.
- Blog load thêm bằng cursor không dùng offset sâu.
- Các query cursor có index hỗ trợ.
- Waterfall không còn ảnh/logo cực nặng trong critical path.
