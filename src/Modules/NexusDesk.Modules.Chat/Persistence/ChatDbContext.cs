using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Chat.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Chat.Persistence;

public class ChatDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public ChatDbContext(
        DbContextOptions<ChatDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("chat");

        // 1. ChatSession Entity Configuration
        modelBuilder.Entity<ChatSession>(b =>
        {
            b.ToTable("chat_sessions");
            b.HasKey(s => s.Id);

            b.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(s => s.CustomerId).HasColumnName("customer_id").IsRequired();
            b.Property(s => s.AssignedAgentId).HasColumnName("assigned_agent_id");
            b.Property(s => s.Subject).HasColumnName("subject").HasMaxLength(250).IsRequired();
            b.Property(s => s.Status)
                .HasColumnName("status")
                .HasMaxLength(30)
                .HasConversion<string>()
                .IsRequired();
            b.Property(s => s.ConvertedTicketId).HasColumnName("converted_ticket_id");
            b.Property(s => s.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(s => s.ClosedAtUtc).HasColumnName("closed_at_utc");

            b.HasIndex(s => s.TenantId).HasDatabaseName("ix_chat_sessions_tenant_id");
            b.HasIndex(s => new { s.TenantId, s.CustomerId }).HasDatabaseName("ix_chat_sessions_tenant_customer");
            b.HasIndex(s => new { s.TenantId, s.Status }).HasDatabaseName("ix_chat_sessions_tenant_status");

            b.HasMany(s => s.Messages)
                .WithOne()
                .HasForeignKey(m => m.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(s => s.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasQueryFilter(s => s.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 2. ChatMessage Entity Configuration
        modelBuilder.Entity<ChatMessage>(b =>
        {
            b.ToTable("chat_messages");
            b.HasKey(m => m.Id);

            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(m => m.ChatSessionId).HasColumnName("chat_session_id").IsRequired();
            b.Property(m => m.SenderUserId).HasColumnName("sender_user_id").IsRequired();
            b.Property(m => m.SenderRole).HasColumnName("sender_role").HasMaxLength(30).IsRequired();
            b.Property(m => m.Content).HasColumnName("content").IsRequired();
            b.Property(m => m.SentAtUtc).HasColumnName("sent_at_utc").IsRequired();

            b.HasIndex(m => m.TenantId).HasDatabaseName("ix_chat_messages_tenant_id");
            b.HasIndex(m => new { m.TenantId, m.ChatSessionId }).HasDatabaseName("ix_chat_messages_tenant_session");

            b.HasQueryFilter(m => m.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });
    }
}
