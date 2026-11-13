using NexusDesk.Modules.Ticketing.Application.Commands;
using NexusDesk.Modules.Ticketing.Domain.Enums;
using NexusDesk.SharedKernel.Primitives;
using NexusDesk.SharedKernel.Ticketing;

namespace NexusDesk.Modules.Ticketing.Application.Services;

public sealed class TicketCreationService : ITicketCreationService
{
    private readonly CreateTicketCommandHandler _createTicketHandler;

    public TicketCreationService(CreateTicketCommandHandler createTicketHandler)
    {
        _createTicketHandler = createTicketHandler;
    }

    public async Task<Result<Guid>> CreateTicketFromChatAsync(
        CreateTicketFromChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTicketCommand(
            request.Subject,
            request.Description,
            TicketPriority.Medium,
            TicketChannel.LiveChat,
            request.CustomerId,
            request.CreatedByUserId ?? request.CustomerId,
            false,
            ["live-chat"]);

        var result = await _createTicketHandler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        return Result.Success(result.Value.Id);
    }
}
