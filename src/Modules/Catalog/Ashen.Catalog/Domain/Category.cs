using Ashen.BuildingBlocks.Domain;

namespace Ashen.Catalog.Domain;

/// <summary>A named grouping of products, e.g. Jackets or Shoes.</summary>
internal sealed class Category : Entity
{
    private Category() { }

    public Category(string name, string slug, int sortOrder)
    {
        Name = name;
        Slug = slug;
        SortOrder = sortOrder;
    }

    /// <summary>Renames; the slug stays the same so category links keep working.</summary>
    public void Rename(string name) => Name = name;

    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public int SortOrder { get; private set; }
}
