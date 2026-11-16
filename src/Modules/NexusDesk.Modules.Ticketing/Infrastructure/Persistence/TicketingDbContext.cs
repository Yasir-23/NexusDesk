using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Ticketing.Domain.Entities;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.Infrastructure.Persistence;

public class TicketingDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public Guid CurrentTenantId => _tenantContextAccessor.TenantContext?.TenantId ?? Guid.Empty;

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketActivityLog> TicketActivityLogs => Set<TicketActivityLog>();
    public DbSet<TicketTag> TicketTags => Set<TicketTag>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    public TicketingDbContext(
        DbContextOptions<TicketingDbContext> options,
        ITenantContextAccessor tenantContextAccessor,
        IDomainEventDispatcher? domainEventDispatcher = null) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
        _domainEventDispatcher = domainEventDispatcher;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregateRoots = ChangeTracker.Entries<AggregateRoot<Guid>>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var pendingEvents = aggregateRoots
            .SelectMany(a => a.DomainEvents)
            .ToList();

        if (Database.CurrentTransaction != null)
        {
            var result = await base.SaveChangesAsync(cancellationToken);

            foreach (var aggregateRoot in aggregateRoots)
            {
                aggregateRoot.ClearDomainEvents();
            }

            if (pendingEvents.Count > 0 && _domainEventDispatcher != null)
            {
                await _domainEventDispatcher.DispatchAsync(pendingEvents, cancellationToken);
            }

            return result;
        }

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

            var result = await base.SaveChangesAsync(cancellationToken);

            foreach (var aggregateRoot in aggregateRoots)
            {
                aggregateRoot.ClearDomainEvents();
            }

            if (pendingEvents.Count > 0 && _domainEventDispatcher != null)
            {
                await _domainEventDispatcher.DispatchAsync(pendingEvents, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("ticketing");

        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("tickets", "ticketing");
            b.HasKey(t => t.Id);

            b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(t => t.TicketNumber).HasColumnName("ticket_number").HasMaxLength(32).IsRequired();
            b.Property(t => t.Title).HasColumnName("title").HasMaxLength(Ticket.MaxTitleLength).IsRequired();
            b.Property(t => t.Description).HasColumnName("description").HasColumnType("text").IsRequired();
            b.Property(t => t.Status).HasColumnName("status").HasConversion<int>().IsRequired();
            b.Property(t => t.Priority).HasColumnName("priority").HasConversion<int>().IsRequired();
            b.Property(t => t.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
            b.Property(t => t.CustomerId).HasColumnName("customer_id").IsRequired();
            b.Property(t => t.AssignedAgentId).HasColumnName("assigned_agent_id");
            b.Property(t => t.ResolutionNotes).HasColumnName("resolution_notes").HasColumnType("text");
            b.Property(t => t.FirstResponseDueUtc).HasColumnName("first_response_due_utc");
            b.Property(t => t.ResolutionDueUtc).HasColumnName("resolution_due_utc");
            b.Property(t => t.FirstRespondedAtUtc).HasColumnName("first_responded_at_utc");
            b.Property(t => t.IsSlaBreached).HasColumnName("is_sla_breached").HasDefaultValue(false).IsRequired();
            b.Property(t => t.SlaBreachedAtUtc).HasColumnName("sla_breached_at_utc");
            b.Property(t => t.ResolvedAtUtc).HasColumnName("resolved_at_utc");
            b.Property(t => t.ClosedAtUtc).HasColumnName("closed_at_utc");
            b.Property(t => t.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(t => t.UpdatedAtUtc).HasColumnName("updated_at_utc");

            // PostgreSQL xmin system column mapped as optimistic concurrency token
            b.Property(t => t.RowVersion).IsRowVersion();

            b.Ignore(t => t.DomainEvents);

            b.HasIndex(t => new { t.TenantId, t.TicketNumber })
                .IsUnique()
                .HasDatabaseName("ix_tickets_tenant_ticket_number");

            b.HasIndex(t => new { t.TenantId, t.Status })
                .HasDatabaseName("ix_tickets_tenant_status");

            b.HasIndex(t => new { t.TenantId, t.AssignedAgentId })
                .HasDatabaseName("ix_tickets_tenant_assigned_agent");

            b.HasIndex(t => new { t.TenantId, t.CustomerId })
                .HasDatabaseName("ix_tickets_tenant_customer");

            b.HasIndex(t => new { t.TenantId, t.CreatedAtUtc })
                .HasDatabaseName("ix_tickets_tenant_created_at");

            b.HasMany(t => t.Comments)
                .WithOne()
                .HasForeignKey(c => c.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(t => t.Tags)
                .WithOne()
                .HasForeignKey(tag => tag.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(t => t.ActivityLogs)
                .WithOne()
                .HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.Cascade);
            b.Navigation(t => t.ActivityLogs).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TicketComment>(b =>
        {
            b.ToTable("ticket_comments", "ticketing");
            b.HasKey(c => c.Id);

            b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(c => c.TicketId).HasColumnName("ticket_id").IsRequired();
            b.Property(c => c.AuthorUserId).HasColumnName("author_user_id").IsRequired();
            b.Property(c => c.IsCustomer).HasColumnName("is_customer").IsRequired();
            b.Property(c => c.Type).HasColumnName("type").HasConversion<int>().IsRequired();
            b.Property(c => c.Body).HasColumnName("body").HasColumnType("text").IsRequired();
            b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(c => new { c.TenantId, c.TicketId })
                .HasDatabaseName("ix_ticket_comments_tenant_ticket");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TicketActivityLog>(b =>
        {
            b.ToTable("ticket_activity_logs", "ticketing");
            b.HasKey(a => a.Id);

            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(a => a.TicketId).HasColumnName("ticket_id").IsRequired();
            b.Property(a => a.PerformedByUserId).HasColumnName("performed_by_user_id").IsRequired();
            b.Property(a => a.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
            b.Property(a => a.OldValue).HasColumnName("old_value").HasColumnType("text");
            b.Property(a => a.NewValue).HasColumnName("new_value").HasColumnType("text");
            b.Property(a => a.TimestampUtc).HasColumnName("timestamp_utc").IsRequired();

            b.HasIndex(a => new { a.TenantId, a.TicketId })
                .HasDatabaseName("ix_ticket_activity_logs_tenant_ticket");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TicketTag>(b =>
        {
            b.ToTable("ticket_tags", "ticketing");
            b.HasKey(t => t.Id);

            b.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(t => t.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(t => t.TicketId).HasColumnName("ticket_id").IsRequired();
            b.Property(t => t.Name).HasColumnName("name").HasMaxLength(64).IsRequired();

            b.HasIndex(t => new { t.TenantId, t.TicketId })
                .HasDatabaseName("ix_ticket_tags_tenant_ticket");

            b.HasIndex(t => new { t.TicketId, t.Name })
                .IsUnique()
                .HasDatabaseName("ix_ticket_tags_ticket_name");

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TicketAttachment>(b =>
        {
            b.ToTable("ticket_attachments", "ticketing");
            b.HasKey(a => a.Id);

            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(a => a.TicketId).HasColumnName("ticket_id").IsRequired();
            b.Property(a => a.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();
            b.Property(a => a.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
            b.Property(a => a.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
            b.Property(a => a.SizeBytes).HasColumnName("size_bytes").IsRequired();
            b.Property(a => a.StoragePath).HasColumnName("storage_path").HasMaxLength(500).IsRequired();
            b.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(a => new { a.TenantId, a.TicketId })
                .HasDatabaseName("ix_ticket_attachments_tenant_ticket");

            b.HasIndex(a => a.TicketId)
                .HasDatabaseName("ix_ticket_attachments_ticket_id");

            b.HasOne<Ticket>()
                .WithMany()
                .HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(e => e.TenantId == CurrentTenantId);
        });
    }
}
