using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.KnowledgeBase.Domain;

public sealed class KbCategory : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<KbArticle> Articles => _articles.AsReadOnly();
    private readonly List<KbArticle> _articles = [];

    public KbCategory(
        Guid id,
        Guid tenantId,
        string name,
        string slug,
        string? description,
        int displayOrder,
        DateTimeOffset createdAtUtc) : base(id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Category Id cannot be empty.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Category Slug is required.", nameof(slug));

        TenantId = tenantId;
        Name = name.Trim();
        Slug = SlugHelper.Normalize(slug);
        Description = description?.Trim();
        DisplayOrder = displayOrder;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
    }

    private KbCategory() : base()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public void Update(string name, string slug, string? description, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Category Slug is required.", nameof(slug));

        Name = name.Trim();
        Slug = SlugHelper.Normalize(slug);
        Description = description?.Trim();
        DisplayOrder = displayOrder;
    }
}
