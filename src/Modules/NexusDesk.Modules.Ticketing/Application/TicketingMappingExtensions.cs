using NexusDesk.Modules.Ticketing.Application.Contracts;
using NexusDesk.Modules.Ticketing.Domain.Entities;

namespace NexusDesk.Modules.Ticketing.Application;

public static class TicketingMappingExtensions
{
    public static TicketSummaryResponse ToSummaryResponse(this Ticket ticket)
    {
        return new TicketSummaryResponse(
            ticket.Id,
            ticket.TenantId,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Status,
            ticket.Priority,
            ticket.Channel,
            ticket.CustomerId,
            ticket.AssignedAgentId,
            ticket.FirstResponseDueUtc,
            ticket.ResolutionDueUtc,
            ticket.FirstRespondedAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.CreatedAtUtc,
            ticket.UpdatedAtUtc,
            ticket.RowVersion,
            ticket.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
            ticket.IsSlaBreached,
            ticket.SlaBreachedAtUtc);
    }

    public static TicketDetailsResponse ToDetailsResponse(this Ticket ticket, bool isCustomer)
    {
        var comments = ticket.GetVisibleComments(isCustomer)
            .Select(c => c.ToDto())
            .ToList();

        var activityLogs = isCustomer
            ? new List<TicketActivityLogDto>()
            : ticket.ActivityLogs
                .OrderBy(a => a.TimestampUtc)
                .Select(a => new TicketActivityLogDto(
                    a.Id,
                    a.PerformedByUserId,
                    a.Action,
                    a.OldValue,
                    a.NewValue,
                    a.TimestampUtc))
                .ToList();

        return new TicketDetailsResponse(
            ticket.Id,
            ticket.TenantId,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Description,
            ticket.Status,
            ticket.Priority,
            ticket.Channel,
            ticket.CustomerId,
            ticket.AssignedAgentId,
            ticket.ResolutionNotes,
            ticket.FirstResponseDueUtc,
            ticket.ResolutionDueUtc,
            ticket.FirstRespondedAtUtc,
            ticket.ResolvedAtUtc,
            ticket.ClosedAtUtc,
            ticket.CreatedAtUtc,
            ticket.UpdatedAtUtc,
            ticket.RowVersion,
            ticket.Tags.Select(t => t.Name).OrderBy(n => n).ToList(),
            comments,
            activityLogs,
            ticket.IsSlaBreached,
            ticket.SlaBreachedAtUtc);
    }

    public static TicketCommentDto ToDto(this TicketComment comment)
    {
        return new TicketCommentDto(
            comment.Id,
            comment.AuthorUserId,
            comment.IsCustomer,
            comment.Type,
            comment.Body,
            comment.CreatedAtUtc);
    }
}
