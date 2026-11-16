namespace NexusDesk.SharedKernel.Ticketing;

public sealed record TicketAiContextDto(
    Guid Id,
    Guid TenantId,
    string TicketNumber,
    string Title,
    string Description,
    IReadOnlyList<string> Comments);

public interface ITicketDataProvider
{
    Task<TicketAiContextDto?> GetTicketAiContextAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken = default);
}
