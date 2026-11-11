using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Domain;
using NexusDesk.Modules.Tenants.Persistence.Interceptors;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.MultiTenancy.Exceptions;
using Xunit;

namespace NexusDesk.UnitTests.MultiTenancy;

public class TenantAuditSaveChangesInterceptorTests
{
    private class TestDbContext : DbContext
    {
        public DbSet<TenantSetting> Settings => Set<TenantSetting>();

        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TenantSetting>(b =>
            {
                b.HasKey(s => s.Id);
            });
        }
    }

    [Fact]
    public async Task SaveChanges_ShouldReject_WriteWithDifferentTenantId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var accessor = new TenantContextAccessor
        {
            TenantContext = new TenantContext
            {
                TenantId = tenantA,
                Subdomain = "tenant-a",
                Status = TenantStatus.Active
            }
        };

        var interceptor = new TenantAuditSaveChangesInterceptor(accessor);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql("Host=localhost;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres")
            .AddInterceptors(interceptor)
            .Options;

        using var db = new TestDbContext(options);

        // Attempt to insert an entity explicitly stamped with Tenant B while context is Tenant A
        var foreignEntity = new TenantSetting(Guid.NewGuid(), tenantB, "Key1", "Value1");
        db.Settings.Add(foreignEntity);

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantIsolationViolationException>()
            .WithMessage("*Cross-tenant write or modification violation*");
    }

    [Fact]
    public void SaveChanges_ShouldAutoStampTenantId_WhenTenantIdIsEmpty()
    {
        var currentTenantId = Guid.NewGuid();

        var accessor = new TenantContextAccessor
        {
            TenantContext = new TenantContext
            {
                TenantId = currentTenantId,
                Subdomain = "tenant-a",
                Status = TenantStatus.Active
            }
        };

        var interceptor = new TenantAuditSaveChangesInterceptor(accessor);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql("Host=localhost;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres")
            .AddInterceptors(interceptor)
            .Options;

        using var db = new TestDbContext(options);

        // Add entity with Guid.Empty
        var entity = new TenantSetting(Guid.NewGuid(), Guid.Empty, "Theme", "Dark");
        db.Settings.Add(entity);

        try
        {
            db.SaveChanges();
        }
        catch
        {
            // Connection error expected if database is not queried, but interceptor executes first
        }

        entity.TenantId.Should().Be(currentTenantId);
    }
}
