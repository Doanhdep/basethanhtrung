using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.Components
{
    public class RelatedProductsCarouselViewComponent : ViewComponent
    {
        private readonly AppDbContext _context;

        public RelatedProductsCarouselViewComponent(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(int count = 8, IEnumerable<int>? excludeIds = null)
        {
            var excludedIdSet = (excludeIds ?? Enumerable.Empty<int>())
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            var rows = await _context.Products
                .AsNoTracking()
                .Where(product => product.Status == "active" && !excludedIdSet.Contains(product.Id))
                .OrderByDescending(product => product.IsFeatured)
                .ThenByDescending(product => product.CreatedAt)
                .Take(count > 0 ? count : 8)
                .Select(product => new
                {
                    product.Id,
                    product.Slug,
                    product.Name,
                    product.IsFeatured,
                    product.ShortDescription,
                    product.Description,
                    product.PriceLabel,
                    product.PriceValue,
                    product.Unit,
                    product.PrimaryImage,
                    ProductImages = product.ProductImages
                        .OrderBy(image => image.OrderIndex ?? int.MaxValue)
                        .ThenBy(image => image.Id)
                        .Take(4)
                        .Select(image => image.ImageUrl)
                        .ToList(),
                    CategoryName = product.Category == null ? null : product.Category.Name
                })
                .ToListAsync();

            var products = rows
                .Select(product =>
                {
                    var productImages = BuildProductImageList(product.PrimaryImage, product.ProductImages);

                    return new ProductCardViewModel
                    {
                        Id = product.Id,
                        Slug = product.Slug,
                        Name = product.Name ?? string.Empty,
                        CategoryName = product.CategoryName,
                        ShortDescription = product.ShortDescription,
                        Description = product.Description,
                        PriceLabel = product.PriceLabel,
                        PriceValue = product.PriceValue,
                        Unit = product.Unit,
                        PrimaryImage = productImages.FirstOrDefault() ?? ImagePathHelper.DefaultProductImage,
                        IsFeatured = product.IsFeatured ?? false,
                        Images = productImages
                    };
                })
                .ToList();

            return View(products);
        }

        private static List<string> BuildProductImageList(string? primaryImage, IEnumerable<string?>? galleryImages = null)
        {
            var sourceImages = new List<string?> { primaryImage };
            if (galleryImages is not null)
            {
                sourceImages.AddRange(galleryImages);
            }

            var images = ImagePathHelper.ResolveVariantSequence(
                    sourceImages,
                    SiteImageVariant.Thumbnail,
                    ImagePathHelper.DefaultProductImage)
                .ToList();

            return images.Count > 0
                ? images
                : [ImagePathHelper.DefaultProductImage];
        }
    }
}
