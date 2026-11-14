using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.KnowledgeBase.Domain;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.KnowledgeBase.Persistence;

public class KnowledgeBaseDbContext : DbContext
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public KnowledgeBaseDbContext(
        DbContextOptions<KnowledgeBaseDbContext> options,
        ITenantContextAccessor tenantContextAccessor) : base(options)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public DbSet<KbCategory> KbCategories => Set<KbCategory>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("kb");

        // 1. KbCategory Entity Configuration
        modelBuilder.Entity<KbCategory>(b =>
        {
            b.ToTable("kb_categories");
            b.HasKey(c => c.Id);

            b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(c => c.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(c => c.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            b.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(150).IsRequired();
            b.Property(c => c.Description).HasColumnName("description").HasMaxLength(500);
            b.Property(c => c.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0).IsRequired();
            b.Property(c => c.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            b.HasIndex(c => c.TenantId).HasDatabaseName("ix_kb_categories_tenant_id");
            b.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique().HasDatabaseName("ix_kb_categories_tenant_slug");
            b.HasIndex(c => new { c.TenantId, c.DisplayOrder, c.Name }).HasDatabaseName("ix_kb_categories_tenant_order_name");

            b.HasMany(c => c.Articles)
                .WithOne(a => a.Category)
                .HasForeignKey(a => a.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Navigation(c => c.Articles).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasQueryFilter(c => c.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });

        // 2. KbArticle Entity Configuration
        modelBuilder.Entity<KbArticle>(b =>
        {
            b.ToTable("kb_articles");
            b.HasKey(a => a.Id);

            b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(a => a.TenantId).HasColumnName("tenant_id").IsRequired();
            b.Property(a => a.CategoryId).HasColumnName("category_id").IsRequired();
            b.Property(a => a.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            b.Property(a => a.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
            b.Property(a => a.Summary).HasColumnName("summary").HasMaxLength(500);
            b.Property(a => a.BodyMarkdown).HasColumnName("body_markdown").IsRequired();
            b.Property(a => a.Tags).HasColumnName("tags").HasColumnType("text[]").IsRequired();
            b.Property(a => a.Status)
                .HasColumnName("status")
                .HasMaxLength(30)
                .HasConversion<string>()
                .IsRequired();
            b.Property(a => a.AuthorUserId).HasColumnName("author_user_id").IsRequired();
            b.Property(a => a.ViewCount).HasColumnName("view_count").HasDefaultValue(0).IsRequired();
            b.Property(a => a.HelpfulCount).HasColumnName("helpful_count").HasDefaultValue(0).IsRequired();
            b.Property(a => a.NotHelpfulCount).HasColumnName("not_helpful_count").HasDefaultValue(0).IsRequired();
            b.Property(a => a.PublishedAtUtc).HasColumnName("published_at_utc");
            b.Property(a => a.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            b.Property(a => a.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            b.HasIndex(a => a.TenantId).HasDatabaseName("ix_kb_articles_tenant_id");
            b.HasIndex(a => new { a.TenantId, a.Slug }).IsUnique().HasDatabaseName("ix_kb_articles_tenant_slug");
            b.HasIndex(a => new { a.TenantId, a.CategoryId }).HasDatabaseName("ix_kb_articles_tenant_category");
            b.HasIndex(a => new { a.TenantId, a.Status }).HasDatabaseName("ix_kb_articles_tenant_status");
            b.HasIndex(a => new { a.TenantId, a.Status, a.PublishedAtUtc }).HasDatabaseName("ix_kb_articles_tenant_status_published");

            b.HasQueryFilter(a => a.TenantId == _tenantContextAccessor.TenantContext.TenantId);
        });
    }
}
