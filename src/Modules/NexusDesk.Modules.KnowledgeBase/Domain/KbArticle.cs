using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.KnowledgeBase.Domain;

public sealed class KbArticle : AggregateRoot<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Title { get; private set; }
    public string Slug { get; private set; }
    public string? Summary { get; private set; }
    public string BodyMarkdown { get; private set; }
    public List<string> Tags { get; private set; } = [];
    public KbArticleStatus Status { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public int ViewCount { get; private set; }
    public int HelpfulCount { get; private set; }
    public int NotHelpfulCount { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public KbCategory? Category { get; private set; }

    public KbArticle(
        Guid id,
        Guid tenantId,
        Guid categoryId,
        Guid authorUserId,
        string title,
        string slug,
        string? summary,
        string bodyMarkdown,
        IEnumerable<string>? tags,
        KbArticleStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? publishedAtUtc = null) : base(id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Article Id cannot be empty.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        if (categoryId == Guid.Empty) throw new ArgumentException("CategoryId cannot be empty.", nameof(categoryId));
        if (authorUserId == Guid.Empty) throw new ArgumentException("AuthorUserId cannot be empty.", nameof(authorUserId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Article Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Article Slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(bodyMarkdown)) throw new ArgumentException("Article BodyMarkdown is required.", nameof(bodyMarkdown));

        TenantId = tenantId;
        CategoryId = categoryId;
        AuthorUserId = authorUserId;
        Title = title.Trim();
        Slug = SlugHelper.Normalize(slug);
        Summary = summary?.Trim();
        BodyMarkdown = bodyMarkdown;
        Tags = tags != null
            ? tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList()
            : [];
        Status = status;
        CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc;
        UpdatedAtUtc = CreatedAtUtc;
        PublishedAtUtc = status == KbArticleStatus.Published ? (publishedAtUtc ?? CreatedAtUtc) : null;
        ViewCount = 0;
        HelpfulCount = 0;
        NotHelpfulCount = 0;
    }

    private KbArticle() : base()
    {
        Title = string.Empty;
        Slug = string.Empty;
        BodyMarkdown = string.Empty;
    }

    public KbArticle(Guid id, Guid tenantId, string title, string slug, string contentMarkdown, bool isPublished)
        : this(id, tenantId, Guid.NewGuid(), Guid.NewGuid(), title, slug, null, contentMarkdown, null, isPublished ? KbArticleStatus.Published : KbArticleStatus.Draft, DateTimeOffset.UtcNow)
    {
    }

    public static KbArticle Create(
        Guid tenantId,
        Guid categoryId,
        Guid authorUserId,
        string title,
        string? slug,
        string? summary,
        string bodyMarkdown,
        IEnumerable<string>? tags,
        bool publishImmediately = false)
    {
        var finalSlug = string.IsNullOrWhiteSpace(slug) ? SlugHelper.Generate(title) : SlugHelper.Normalize(slug);
        var status = publishImmediately ? KbArticleStatus.Published : KbArticleStatus.Draft;
        var now = DateTimeOffset.UtcNow;

        return new KbArticle(
            Guid.NewGuid(),
            tenantId,
            categoryId,
            authorUserId,
            title,
            finalSlug,
            summary,
            bodyMarkdown,
            tags,
            status,
            now,
            publishImmediately ? now : null);
    }

    public void Update(
        string title,
        string? slug,
        string? summary,
        string bodyMarkdown,
        Guid categoryId,
        IEnumerable<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Article Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(bodyMarkdown)) throw new ArgumentException("Article BodyMarkdown is required.", nameof(bodyMarkdown));
        if (categoryId == Guid.Empty) throw new ArgumentException("CategoryId cannot be empty.", nameof(categoryId));

        Title = title.Trim();
        Slug = string.IsNullOrWhiteSpace(slug) ? SlugHelper.Generate(title) : SlugHelper.Normalize(slug);
        Summary = summary?.Trim();
        BodyMarkdown = bodyMarkdown;
        CategoryId = categoryId;
        Tags = tags != null
            ? tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList()
            : [];
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Publish()
    {
        Status = KbArticleStatus.Published;
        PublishedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = PublishedAtUtc.Value;
    }

    public void Archive()
    {
        Status = KbArticleStatus.Archived;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void IncrementViewCount()
    {
        ViewCount++;
    }

    public void RecordFeedback(bool isHelpful)
    {
        if (isHelpful)
        {
            HelpfulCount++;
        }
        else
        {
            NotHelpfulCount++;
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
