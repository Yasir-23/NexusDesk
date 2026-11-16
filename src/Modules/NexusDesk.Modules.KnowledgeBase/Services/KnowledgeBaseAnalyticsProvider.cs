using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.Modules.KnowledgeBase.Persistence;
using NexusDesk.SharedKernel.Analytics;

namespace NexusDesk.Modules.KnowledgeBase.Services;

public sealed class KnowledgeBaseAnalyticsProvider : IKnowledgeBaseAnalyticsProvider
{
    private readonly KnowledgeBaseDbContext _kbDb;

    public KnowledgeBaseAnalyticsProvider(KnowledgeBaseDbContext kbDb)
    {
        _kbDb = kbDb;
    }

    public async Task<int> GetPublishedArticlesCountAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _kbDb.KbArticles
            .AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.Status == KbArticleStatus.Published, cancellationToken);
    }
}
