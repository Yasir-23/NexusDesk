using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Domain.Entities;

public sealed class TicketAttachment : Entity<Guid>, ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string StoragePath { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public TicketAttachment(
        Guid id,
        Guid tenantId,
        Guid ticketId,
        Guid uploadedByUserId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storagePath,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        TicketId = ticketId;
        UploadedByUserId = uploadedByUserId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? "application/octet-stream";
        SizeBytes = sizeBytes;
        StoragePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        CreatedAtUtc = createdAtUtc;
    }

    private TicketAttachment() : base()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        StoragePath = string.Empty;
    }
}
