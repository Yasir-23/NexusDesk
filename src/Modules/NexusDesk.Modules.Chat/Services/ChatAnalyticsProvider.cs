using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Chat.Domain;
using NexusDesk.Modules.Chat.Persistence;
using NexusDesk.SharedKernel.Analytics;

namespace NexusDesk.Modules.Chat.Services;

public sealed class ChatAnalyticsProvider : IChatAnalyticsProvider
{
    private readonly ChatDbContext _chatDb;

    public ChatAnalyticsProvider(ChatDbContext chatDb)
    {
        _chatDb = chatDb;
    }

    public async Task<int> GetActiveChatSessionsCountAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _chatDb.ChatSessions
            .AsNoTracking()
            .CountAsync(s => s.TenantId == tenantId &&
                             (s.Status == ChatSessionStatus.Active || s.Status == ChatSessionStatus.Waiting),
                        cancellationToken);
    }
}
