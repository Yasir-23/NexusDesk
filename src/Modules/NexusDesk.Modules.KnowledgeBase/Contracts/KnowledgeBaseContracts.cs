namespace NexusDesk.Modules.KnowledgeBase.Contracts;

public sealed record KbCategoryDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Slug,
    string? Description,
    int DisplayOrder,
    DateTimeOffset CreatedAtUtc,
    int PublishedArticlesCount);

public sealed record CreateCategoryRequest(
    string Name,
    string? Slug = null,
    string? Description = null,
    int? DisplayOrder = null);

public sealed record KbArticleDto(
    Guid Id,
    Guid TenantId,
    Guid CategoryId,
    string? CategoryName,
    string Title,
    string Slug,
    string? Summary,
    string BodyMarkdown,
    IReadOnlyList<string> Tags,
    string Status,
    Guid AuthorUserId,
    int ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateArticleRequest(
    Guid CategoryId,
    string Title,
    string? Slug = null,
    string? Summary = null,
    string BodyMarkdown = "",
    List<string>? Tags = null,
    bool? PublishImmediately = false);

public sealed record UpdateArticleRequest(
    string Title,
    string? Slug,
    string? Summary,
    string BodyMarkdown,
    Guid CategoryId,
    List<string>? Tags);

public sealed record PaginatedArticlesResponse(
    IReadOnlyList<KbArticleDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record DeflectionSearchResultDto(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string CategoryName,
    double RelevanceScore,
    int HelpfulCount);

public sealed record ArticleFeedbackRequest(
    bool IsHelpful);

public sealed record ArticleFeedbackResponse(
    Guid ArticleId,
    int HelpfulCount,
    int NotHelpfulCount);
