using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.SharedKernel.Ticketing;

public sealed record CreateTicketFromChatRequest(
    Guid TenantId,
    Guid CustomerId,
    string Subject,
    string Description,
    Guid? CreatedByUserId = null);

public interface ITicketCreationService
{
    Task<Result<Guid>> CreateTicketFromChatAsync(
        CreateTicketFromChatRequest request,
        CancellationToken cancellationToken = default);
}
