using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.KnowledgeBase.Contracts;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.KnowledgeBase.Endpoints;

public static class KnowledgeBaseEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeBaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/kb")
            .WithTags("Knowledge Base & Ticket Deflection");

        // 1. GET /api/v1/kb/categories (Lists tenant KB categories ordered by DisplayOrder, Name + published articles count)
        group.MapGet("/categories", async (
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            var categories = await dbContext.KbCategories
                .AsNoTracking()
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .Select(c => new KbCategoryDto(
                    c.Id,
                    c.TenantId,
                    c.Name,
                    c.Slug,
                    c.Description,
                    c.DisplayOrder,
                    c.CreatedAtUtc,
                    dbContext.KbArticles.Count(a => a.CategoryId == c.Id && a.Status == KbArticleStatus.Published)))
                .ToListAsync(cancellationToken);

            return Results.Ok(categories);
        })
        .Produces<List<KbCategoryDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithName("GetKbCategories");

        // 2. POST /api/v1/kb/categories (kb:manage / TenantAdmin)
        group.MapPost("/categories", async (
            [FromBody] CreateCategoryRequest request,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!CheckStaffOrManagePermission(httpContext.User, httpContext, out var authError))
            {
                return authError!;
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Category name is required.",
                    "KB_CATEGORY_NAME_REQUIRED",
                    httpContext);
            }

            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.Generate(request.Name)
                : SlugHelper.Normalize(request.Slug);

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;

            var slugExists = await dbContext.KbCategories
                .AnyAsync(c => c.Slug == slug, cancellationToken);

            if (slugExists)
            {
                return CreateProblemResult(
                    StatusCodes.Status409Conflict,
                    "Duplicate Category Slug",
                    $"A category with slug '{slug}' already exists in this tenant.",
                    "KB_CATEGORY_SLUG_CONFLICT",
                    httpContext);
            }

            var category = new KbCategory(
                Guid.NewGuid(),
                tenantId,
                request.Name,
                slug,
                request.Description,
                request.DisplayOrder ?? 0,
                DateTimeOffset.UtcNow);

            dbContext.KbCategories.Add(category);
            await dbContext.SaveChangesAsync(cancellationToken);

            var dto = new KbCategoryDto(
                category.Id,
                category.TenantId,
                category.Name,
                category.Slug,
                category.Description,
                category.DisplayOrder,
                category.CreatedAtUtc,
                0);

            return Results.Created($"/api/v1/kb/categories/{category.Id}", dto);
        })
        .Produces<KbCategoryDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("CreateKbCategory");

        // 3. POST /api/v1/kb/articles (kb:manage)
        group.MapPost("/articles", async (
            [FromBody] CreateArticleRequest request,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!CheckStaffOrManagePermission(httpContext.User, httpContext, out var authError))
            {
                return authError!;
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Article title is required.",
                    "KB_ARTICLE_TITLE_REQUIRED",
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(request.BodyMarkdown))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Article body in Markdown format is required.",
                    "KB_ARTICLE_BODY_REQUIRED",
                    httpContext);
            }

            var categoryExists = await dbContext.KbCategories
                .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Category Not Found",
                    $"Category '{request.CategoryId}' does not exist.",
                    "KB_CATEGORY_NOT_FOUND",
                    httpContext);
            }

            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.Generate(request.Title)
                : SlugHelper.Normalize(request.Slug);

            var slugExists = await dbContext.KbArticles
                .AnyAsync(a => a.Slug == slug, cancellationToken);

            if (slugExists)
            {
                return CreateProblemResult(
                    StatusCodes.Status409Conflict,
                    "Duplicate Article Slug",
                    $"An article with slug '{slug}' already exists in this tenant.",
                    "KB_ARTICLE_SLUG_CONFLICT",
                    httpContext);
            }

            TryGetCallerUserId(httpContext.User, out var authorUserId);
            if (authorUserId == Guid.Empty)
            {
                authorUserId = Guid.NewGuid();
            }

            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var publishImmediately = request.PublishImmediately == true;

            var article = KbArticle.Create(
                tenantId,
                request.CategoryId,
                authorUserId,
                request.Title,
                slug,
                request.Summary,
                request.BodyMarkdown,
                request.Tags,
                publishImmediately);

            dbContext.KbArticles.Add(article);
            await dbContext.SaveChangesAsync(cancellationToken);

            var category = await dbContext.KbCategories.FindAsync([article.CategoryId], cancellationToken);
            var dto = ToDto(article, category?.Name);

            return Results.Created($"/api/v1/kb/articles/{article.Id}", dto);
        })
        .Produces<KbArticleDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("CreateKbArticle");

        // 4. GET /api/v1/kb/articles (kb:read or Customer)
        group.MapGet("/articles", async (
            [FromQuery] Guid? categoryId,
            [FromQuery] KbArticleStatus? status,
            [FromQuery] string? tag,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            var isStaff = IsStaffCaller(httpContext.User);

            // Customers / unauthenticated callers MUST NEVER see Draft or Archived articles
            KbArticleStatus? effectiveStatus;
            if (!isStaff)
            {
                effectiveStatus = KbArticleStatus.Published;
            }
            else
            {
                effectiveStatus = status;
            }

            var currentPage = Math.Max(1, page ?? 1);
            var currentPageSize = Math.Clamp(pageSize ?? 20, 1, 100);

            var query = dbContext.KbArticles
                .Include(a => a.Category)
                .AsNoTracking()
                .AsQueryable();

            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
            {
                query = query.Where(a => a.CategoryId == categoryId.Value);
            }

            if (effectiveStatus.HasValue)
            {
                query = query.Where(a => a.Status == effectiveStatus.Value);
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                var normalizedTag = tag.Trim().ToLowerInvariant();
                query = query.Where(a => a.Tags.Contains(normalizedTag));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = (int)Math.Ceiling(totalCount / (double)currentPageSize);

            var articles = await query
                .OrderByDescending(a => a.PublishedAtUtc ?? a.CreatedAtUtc)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .ToListAsync(cancellationToken);

            var items = articles.Select(a => ToDto(a, a.Category?.Name)).ToList();

            var response = new PaginatedArticlesResponse(
                items,
                totalCount,
                currentPage,
                currentPageSize,
                totalPages);

            return Results.Ok(response);
        })
        .Produces<PaginatedArticlesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithName("GetKbArticles");

        // 5. GET /api/v1/kb/articles/{idOrSlug} (kb:read or Customer)
        group.MapGet("/articles/{idOrSlug}", async (
            string idOrSlug,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            var isGuid = Guid.TryParse(idOrSlug, out var articleId);
            var normalizedSlug = isGuid ? string.Empty : SlugHelper.Normalize(idOrSlug);

            var article = await dbContext.KbArticles
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => isGuid ? a.Id == articleId : a.Slug == normalizedSlug, cancellationToken);

            if (article == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Article Not Found",
                    $"Article '{idOrSlug}' was not found.",
                    "KB_ARTICLE_NOT_FOUND",
                    httpContext);
            }

            var isStaff = IsStaffCaller(httpContext.User);

            // Customers / unauthenticated callers MUST NEVER see Draft or Archived articles
            if (!isStaff)
            {
                if (article.Status != KbArticleStatus.Published)
                {
                    return CreateProblemResult(
                        StatusCodes.Status404NotFound,
                        "Article Not Found",
                        $"Article '{idOrSlug}' was not found.",
                        "KB_ARTICLE_NOT_FOUND",
                        httpContext);
                }

                // Increment ViewCount when viewed by a Customer
                article.IncrementViewCount();
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var dto = ToDto(article, article.Category?.Name);
            return Results.Ok(dto);
        })
        .Produces<KbArticleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("GetKbArticleByIdOrSlug");

        // 6. PUT /api/v1/kb/articles/{id} (kb:manage)
        group.MapPut("/articles/{id:guid}", async (
            Guid id,
            [FromBody] UpdateArticleRequest request,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!CheckStaffOrManagePermission(httpContext.User, httpContext, out var authError))
            {
                return authError!;
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Article title is required.",
                    "KB_ARTICLE_TITLE_REQUIRED",
                    httpContext);
            }

            if (string.IsNullOrWhiteSpace(request.BodyMarkdown))
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Validation Failed",
                    "Article body in Markdown format is required.",
                    "KB_ARTICLE_BODY_REQUIRED",
                    httpContext);
            }

            var article = await dbContext.KbArticles
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (article == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Article Not Found",
                    $"Article with ID '{id}' was not found.",
                    "KB_ARTICLE_NOT_FOUND",
                    httpContext);
            }

            var categoryExists = await dbContext.KbCategories
                .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
            {
                return CreateProblemResult(
                    StatusCodes.Status400BadRequest,
                    "Category Not Found",
                    $"Category '{request.CategoryId}' does not exist.",
                    "KB_CATEGORY_NOT_FOUND",
                    httpContext);
            }

            var slug = string.IsNullOrWhiteSpace(request.Slug)
                ? SlugHelper.Generate(request.Title)
                : SlugHelper.Normalize(request.Slug);

            var slugConflict = await dbContext.KbArticles
                .AnyAsync(a => a.Slug == slug && a.Id != id, cancellationToken);

            if (slugConflict)
            {
                return CreateProblemResult(
                    StatusCodes.Status409Conflict,
                    "Duplicate Article Slug",
                    $"An article with slug '{slug}' already exists in this tenant.",
                    "KB_ARTICLE_SLUG_CONFLICT",
                    httpContext);
            }

            article.Update(
                request.Title,
                slug,
                request.Summary,
                request.BodyMarkdown,
                request.CategoryId,
                request.Tags);

            await dbContext.SaveChangesAsync(cancellationToken);

            var category = await dbContext.KbCategories.FindAsync([article.CategoryId], cancellationToken);
            return Results.Ok(ToDto(article, category?.Name));
        })
        .Produces<KbArticleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("UpdateKbArticle");

        // 7. POST /api/v1/kb/articles/{id}/publish (kb:manage)
        group.MapPost("/articles/{id:guid}/publish", async (
            Guid id,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!CheckStaffOrManagePermission(httpContext.User, httpContext, out var authError))
            {
                return authError!;
            }

            var article = await dbContext.KbArticles
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (article == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Article Not Found",
                    $"Article with ID '{id}' was not found.",
                    "KB_ARTICLE_NOT_FOUND",
                    httpContext);
            }

            article.Publish();
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToDto(article, article.Category?.Name));
        })
        .Produces<KbArticleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("PublishKbArticle");

        // 8. POST /api/v1/kb/articles/{id}/archive (kb:manage)
        group.MapPost("/articles/{id:guid}/archive", async (
            Guid id,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (!CheckStaffOrManagePermission(httpContext.User, httpContext, out var authError))
            {
                return authError!;
            }

            var article = await dbContext.KbArticles
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

            if (article == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Article Not Found",
                    $"Article with ID '{id}' was not found.",
                    "KB_ARTICLE_NOT_FOUND",
                    httpContext);
            }

            article.Archive();
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToDto(article, article.Category?.Name));
        })
        .Produces<KbArticleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("ArchiveKbArticle");

        // 9. GET /api/v1/kb/search?q={query}&limit=5 (kb:read or Customer)
        group.MapGet("/search", async (
            [FromQuery] string? q,
            [FromQuery] int? limit,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<DeflectionSearchResultDto>());
            }

            var effectiveLimit = Math.Clamp(limit ?? 5, 1, 50);
            var tenantId = tenantContextAccessor.TenantContext!.TenantId!.Value;
            var cleanQuery = q.Trim();

            const string searchSql = """
                SELECT
                    a.id,
                    a.title,
                    a.slug,
                    a.summary,
                    c.name AS category_name,
                    ROUND((
                        ts_rank_cd(to_tsvector('english', coalesce(a.title, '') || ' ' || coalesce(a.summary, '') || ' ' || coalesce(a.body_markdown, '')), plainto_tsquery('english', @query)) * 10.0
                        + (CASE WHEN a.title ILIKE @likeQuery THEN 5.0 ELSE 0.0 END)
                        + (CASE WHEN a.summary ILIKE @likeQuery THEN 2.0 ELSE 0.0 END)
                        + (CASE WHEN a.body_markdown ILIKE @likeQuery THEN 1.0 ELSE 0.0 END)
                        + (CASE WHEN @query = ANY(a.tags) THEN 4.0 ELSE 0.0 END)
                        + (LEAST(a.helpful_count, 100) * 0.05)
                    )::numeric, 2) AS relevance_score,
                    a.helpful_count
                FROM kb.kb_articles a
                JOIN kb.kb_categories c ON a.category_id = c.id
                WHERE a.tenant_id = @tenantId
                  AND a.status = 'Published'
                  AND (
                    to_tsvector('english', coalesce(a.title, '') || ' ' || coalesce(a.summary, '') || ' ' || coalesce(a.body_markdown, '')) @@ plainto_tsquery('english', @query)
                    OR a.title ILIKE @likeQuery
                    OR a.summary ILIKE @likeQuery
                    OR a.body_markdown ILIKE @likeQuery
                    OR @query = ANY(a.tags)
                    OR EXISTS (SELECT 1 FROM unnest(a.tags) t WHERE t ILIKE @likeQuery)
                  )
                ORDER BY relevance_score DESC, a.helpful_count DESC, a.published_at_utc DESC
                LIMIT @limit;
                """;

            var connection = dbContext.Database.GetDbConnection();
            var shouldClose = false;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                shouldClose = true;
            }

            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = searchSql;

                var pQuery = cmd.CreateParameter();
                pQuery.ParameterName = "@query";
                pQuery.Value = cleanQuery;
                cmd.Parameters.Add(pQuery);

                var pLike = cmd.CreateParameter();
                pLike.ParameterName = "@likeQuery";
                pLike.Value = $"%{cleanQuery}%";
                cmd.Parameters.Add(pLike);

                var pTenant = cmd.CreateParameter();
                pTenant.ParameterName = "@tenantId";
                pTenant.Value = tenantId;
                cmd.Parameters.Add(pTenant);

                var pLimit = cmd.CreateParameter();
                pLimit.ParameterName = "@limit";
                pLimit.Value = effectiveLimit;
                cmd.Parameters.Add(pLimit);

                var results = new List<DeflectionSearchResultDto>();
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new DeflectionSearchResultDto(
                        Id: reader.GetGuid(0),
                        Title: reader.GetString(1),
                        Slug: reader.GetString(2),
                        Summary: reader.IsDBNull(3) ? null : reader.GetString(3),
                        CategoryName: reader.GetString(4),
                        RelevanceScore: Convert.ToDouble(reader.GetValue(5)),
                        HelpfulCount: reader.GetInt32(6)
                    ));
                }

                return Results.Ok(results);
            }
            finally
            {
                if (shouldClose && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        })
        .Produces<List<DeflectionSearchResultDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithName("SearchKbArticlesForDeflection");

        // 10. POST /api/v1/kb/articles/{id}/feedback (kb:read or Customer)
        group.MapPost("/articles/{id:guid}/feedback", async (
            Guid id,
            [FromBody] ArticleFeedbackRequest request,
            KnowledgeBaseDbContext dbContext,
            ITenantContextAccessor tenantContextAccessor,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!TryEnsureTenantResolved(tenantContextAccessor, httpContext, out var tenantError))
            {
                return tenantError!;
            }

            var article = await dbContext.KbArticles
                .FirstOrDefaultAsync(a => a.Id == id && a.Status == KbArticleStatus.Published, cancellationToken);

            if (article == null)
            {
                return CreateProblemResult(
                    StatusCodes.Status404NotFound,
                    "Article Not Found",
                    $"Published article with ID '{id}' was not found.",
                    "KB_ARTICLE_NOT_FOUND",
                    httpContext);
            }

            article.RecordFeedback(request.IsHelpful);
            await dbContext.SaveChangesAsync(cancellationToken);

            var response = new ArticleFeedbackResponse(
                article.Id,
                article.HelpfulCount,
                article.NotHelpfulCount);

            return Results.Ok(response);
        })
        .Produces<ArticleFeedbackResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("RecordKbArticleFeedback");

        return app;
    }

    private static KbArticleDto ToDto(KbArticle article, string? categoryName)
    {
        return new KbArticleDto(
            article.Id,
            article.TenantId,
            article.CategoryId,
            categoryName ?? article.Category?.Name,
            article.Title,
            article.Slug,
            article.Summary,
            article.BodyMarkdown,
            article.Tags,
            article.Status.ToString(),
            article.AuthorUserId,
            article.ViewCount,
            article.HelpfulCount,
            article.NotHelpfulCount,
            article.PublishedAtUtc,
            article.CreatedAtUtc,
            article.UpdatedAtUtc);
    }

    private static bool TryEnsureTenantResolved(
        ITenantContextAccessor accessor,
        HttpContext httpContext,
        out IResult? errorResult)
    {
        var tenantContext = accessor.TenantContext;
        if (tenantContext == null || !tenantContext.IsResolved || !tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            errorResult = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                "Tenant Not Resolved",
                "A valid tenant identifier (X-Tenant-Subdomain, X-Tenant-Id, or Host subdomain) is required for Knowledge Base endpoints.",
                "TENANT_NOT_RESOLVED",
                httpContext);
            return false;
        }

        errorResult = null;
        return true;
    }

    private static bool IsStaffCaller(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.HasClaim("permission", IdentityPermissions.KnowledgeBaseManage))
        {
            return true;
        }

        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role") ?? string.Empty;
        return string.Equals(role, IdentityRoles.Agent, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, IdentityRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckStaffOrManagePermission(
        ClaimsPrincipal user,
        HttpContext httpContext,
        out IResult? authError)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            authError = CreateProblemResult(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authentication is required to perform this action.",
                "KB_UNAUTHORIZED",
                httpContext);
            return false;
        }

        if (!IsStaffCaller(user))
        {
            authError = CreateProblemResult(
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Permission 'kb:manage' or Staff role is required for this action.",
                "KB_FORBIDDEN",
                httpContext);
            return false;
        }

        authError = null;
        return true;
    }

    private static bool TryGetCallerUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = Guid.Empty;
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(userIdStr, out userId) && userId != Guid.Empty;
    }

    private static IResult CreateProblemResult(
        int statusCode,
        string title,
        string detail,
        string code,
        HttpContext httpContext)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://nexusdesk.io/errors/{code.ToLowerInvariant().Replace('.', '-').Replace('_', '-')}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["errorCode"] = code;

        return Results.Problem(
            detail: problemDetails.Detail,
            instance: problemDetails.Instance,
            statusCode: statusCode,
            title: problemDetails.Title,
            type: problemDetails.Type,
            extensions: problemDetails.Extensions);
    }
}
