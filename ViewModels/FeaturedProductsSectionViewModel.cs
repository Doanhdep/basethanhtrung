using System.Collections.Generic;
using WebThuMuaPheLieu.Models;

namespace WebThuMuaPheLieu.ViewModels;

public class FeaturedProductsSectionViewModel
{
    public int? Count { get; set; }

    public string ViewName { get; set; } = "Pricing";

    public IReadOnlyList<ProductCardViewModel> Products { get; set; } = new List<ProductCardViewModel>();
}
