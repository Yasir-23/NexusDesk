using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.KnowledgeBase.Domain;

public sealed class KbArticle : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public string Slug { get; private set; }
    public string ContentMarkdown { get; private set; }
    public bool IsPublished { get; private set; }

    public KbArticle(Guid id, Guid tenantId, string title, string slug, string contentMarkdown, bool isPublished) : base(id)
    {
        TenantId = tenantId;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Slug = slug ?? throw new ArgumentNullException(nameof(slug));
        ContentMarkdown = contentMarkdown ?? throw new ArgumentNullException(nameof(contentMarkdown));
        IsPublished = isPublished;
    }

    private KbArticle() : base()
    {
        Title = string.Empty;
        Slug = string.Empty;
        ContentMarkdown = string.Empty;
    }
}
