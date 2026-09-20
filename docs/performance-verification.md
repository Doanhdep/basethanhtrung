# Performance verification

## Build validation

- Debug build passed with alternate output directory to avoid locked development DLLs.
- Release build passed with alternate output directory after removing production runtime-compilation dependency from the Release graph.
- Remaining package audit warning: `SixLabors.ImageSharp` 3.1.6 is reported by NuGet as vulnerable. Upgrade when a compatible patched version is available in the project target framework.

## Browser waterfall checklist

Run these checks in an incognito browser with cache disabled, then repeat with cache enabled:

1. Open `/` and verify the preloader disappears shortly after DOM is interactive.
2. Confirm the LCP image is one of the medium WebP hero/banner images and is preloaded.
3. Confirm initial home HTML does not contain the recent-news and scrap-service card markup; those sections should appear after the lazy placeholder enters the viewport.
4. Confirm `/Home/LazySection/recent-news` and `/Home/LazySection/scrap-services` return cached HTML after the first request.
5. Confirm `/Product/GetProductsCursor` and `/Blog/GetBlogsCursor` return `nextCursor` values and no longer require offset paging for load-more UX.
6. Confirm Swiper CSS/JS only loads on pages that set `NeedsSwiper`.

## Metrics to record before/after deployment

| Metric | Before | After | Notes |
| --- | ---: | ---: | --- |
| TTFB `/` cold | | | Use the Network panel document request. |
| TTFB `/` warm | | | Repeat after output cache is primed. |
| DOMContentLoaded | | | Use Performance panel or Navigation Timing. |
| LCP | | | Use Lighthouse or Performance Insights. |
| Initial transferred bytes | | | Network panel, cache disabled. |
| Initial image transferred bytes | | | Filter by Img. |
| Product load-more API TTFB | | | `/Product/GetProductsCursor`. |
| Blog load-more API TTFB | | | `/Blog/GetBlogsCursor`. |

## SQL validation after running indexes

After running `database/performance-indexes.sql`, verify the important indexes exist:

```sql
SELECT name
FROM sys.indexes
WHERE object_id IN (
    OBJECT_ID('dbo.products'),
    OBJECT_ID('dbo.product_images'),
    OBJECT_ID('dbo.blog_posts'),
    OBJECT_ID('dbo.blog_post_categories'),
    OBJECT_ID('dbo.blog_categories'),
    OBJECT_ID('dbo.site_settings')
)
AND name LIKE 'IX_%'
ORDER BY name;
```

Then capture actual execution plans for cursor endpoints and confirm index seeks/scans use the new cursor/filter indexes instead of broad clustered scans.
