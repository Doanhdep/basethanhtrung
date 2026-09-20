namespace WebThuMuaPheLieu.helpper;

public interface ILayoutDataHelper
{
    Task<IReadOnlyList<LayoutBlogLinkItem>> GetServiceMenuItemsAsync();

    Task<IReadOnlyList<LayoutProductLinkItem>> GetFooterFeaturedProductsAsync();

    Task<IReadOnlyList<LayoutBlogLinkItem>> GetFooterTopServicePostsAsync();
}

public sealed class LayoutBlogLinkItem
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public string? Slug { get; init; }
}

public sealed class LayoutProductLinkItem
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public string? Slug { get; init; }
}
