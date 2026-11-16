using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NexusDesk.Modules.Tenants.Caching;
using NexusDesk.Modules.Tenants.Domain;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Tenants.Endpoints;

public record ProvisionTenantRequest(
    string OrganizationName,
    string Subdomain,
    string AdminEmail,
    string AdminFullName,
    string PlanTier,
    string? AdminPassword = null);

public record PlanLimitsDto(int MaxAgents, int MaxTicketsPerMonth, long MaxStorageBytes);

public record ProvisionTenantResponse(
    Guid TenantId,
    string OrganizationName,
    string Subdomain,
    string PortalUrl,
    string Status,
    string IsolationStrategy,
    string PlanTier,
    PlanLimitsDto PlanLimits,
    DateTimeOffset CreatedAtUtc);

public record UpdateTenantStatusRequest(string Status, string Reason);

public record CreateSettingItemRequest(string Key, string Value);

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1");

        // 1. Provisioning endpoint & platform alias
        var provisionHandler = async (
            [FromBody] ProvisionTenantRequest request,
            CatalogDbContext catalogDb,
            ITenantDatabaseProvisioner provisioner,
            ITenantConnectionResolver connectionResolver,
            ITenantCache tenantCache,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(request.OrganizationName))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Organization Name",
                    Detail = "Organization name is required.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (!SubdomainValidator.IsValid(request.Subdomain, out var subError))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Subdomain",
                    Detail = subError,
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var cleanSubdomain = request.Subdomain.Trim().ToLowerInvariant();

            if (!Enum.TryParse<TenantPlanTier>(request.PlanTier, true, out var planTier))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Plan Tier",
                    Detail = $"Plan tier '{request.PlanTier}' is not recognized. Valid options: Free, Pro, Enterprise.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var exists = await catalogDb.Tenants.AnyAsync(t => t.Subdomain == cleanSubdomain);
            if (exists)
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Subdomain Conflict",
                    Detail = $"Subdomain '{cleanSubdomain}' is already taken.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            var isolationStrategy = planTier == TenantPlanTier.Enterprise
                ? IsolationStrategy.DedicatedDatabase
                : IsolationStrategy.SharedDatabase;

            var tenantId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var tenant = new Tenant(tenantId, request.OrganizationName, cleanSubdomain, planTier, isolationStrategy, now);

            string? dedicatedRawConn = null;
            if (isolationStrategy == IsolationStrategy.DedicatedDatabase)
            {
                var provResult = await provisioner.ProvisionDedicatedDatabaseAsync(cleanSubdomain, httpContext.RequestAborted);
                tenant.SetCustomConnectionString(provResult.ProtectedConnectionString, now);
                dedicatedRawConn = provResult.RawConnectionString;
            }

            catalogDb.Tenants.Add(tenant);
            catalogDb.TenantAuditLogs.Add(new TenantAuditLog(
                Guid.NewGuid(),
                tenant.Id,
                "Provisioned",
                $"Tenant '{tenant.Subdomain}' provisioned with plan '{tenant.PlanTier}' and isolation '{tenant.IsolationStrategy}'.",
                "System",
                now));

            await catalogDb.SaveChangesAsync(httpContext.RequestAborted);

            // Seed initial admin membership and default settings
            var adminUserId = Guid.NewGuid();
            var initialMembership = new TenantMembership(
                Guid.NewGuid(),
                tenant.Id,
                adminUserId,
                "TenantAdmin",
                request.AdminEmail,
                request.AdminFullName,
                now);

            var initialSetting = new TenantSetting(
                Guid.NewGuid(),
                tenant.Id,
                "General.Theme",
                "SystemDefault",
                now);

            var adminPassword = string.IsNullOrWhiteSpace(request.AdminPassword) ? "Password123!" : request.AdminPassword;
            var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<string>();
            var adminPasswordHash = hasher.HashPassword(request.AdminEmail, adminPassword);
            var normalizedAdminEmail = request.AdminEmail.Trim().ToUpperInvariant();

            if (isolationStrategy == IsolationStrategy.DedicatedDatabase && dedicatedRawConn != null)
            {
                await using var dedicatedConn = new NpgsqlConnection(dedicatedRawConn);
                await dedicatedConn.OpenAsync(httpContext.RequestAborted);

                var insertMembershipSql = """
                    INSERT INTO tenants.tenant_memberships (id, tenant_id, user_id, role, email, full_name, created_at_utc)
                    VALUES (@id, @tenantId, @userId, @role, @email, @fullName, @createdAtUtc);
                    """;
                await using var cmdM = new NpgsqlCommand(insertMembershipSql, dedicatedConn);
                cmdM.Parameters.AddWithValue("id", initialMembership.Id);
                cmdM.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdM.Parameters.AddWithValue("userId", initialMembership.UserId);
                cmdM.Parameters.AddWithValue("role", initialMembership.Role);
                cmdM.Parameters.AddWithValue("email", initialMembership.Email);
                cmdM.Parameters.AddWithValue("fullName", initialMembership.FullName);
                cmdM.Parameters.AddWithValue("createdAtUtc", initialMembership.CreatedAtUtc);
                await cmdM.ExecuteNonQueryAsync(httpContext.RequestAborted);

                var insertUserSql = """
                    INSERT INTO identity.users (
                        id, tenant_id, email, normalized_email, password_hash, full_name,
                        role, is_active, is_anonymized, created_at_utc
                    ) VALUES (
                        @id, @tenantId, @email, @normalizedEmail, @passwordHash, @fullName,
                        'TenantAdmin', true, false, @createdAtUtc
                    ) ON CONFLICT (tenant_id, normalized_email) DO NOTHING;

                    INSERT INTO identity.user_role_assignments (id, user_id, tenant_id, role, created_at_utc)
                    VALUES (@roleId, @id, @tenantId, 'TenantAdmin', @createdAtUtc)
                    ON CONFLICT (user_id, role) DO NOTHING;
                    """;
                await using var cmdU = new NpgsqlCommand(insertUserSql, dedicatedConn);
                cmdU.Parameters.AddWithValue("id", initialMembership.UserId);
                cmdU.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdU.Parameters.AddWithValue("email", request.AdminEmail);
                cmdU.Parameters.AddWithValue("normalizedEmail", normalizedAdminEmail);
                cmdU.Parameters.AddWithValue("passwordHash", adminPasswordHash);
                cmdU.Parameters.AddWithValue("fullName", request.AdminFullName);
                cmdU.Parameters.AddWithValue("createdAtUtc", initialMembership.CreatedAtUtc);
                cmdU.Parameters.AddWithValue("roleId", Guid.NewGuid());
                await cmdU.ExecuteNonQueryAsync(httpContext.RequestAborted);

                var insertSettingSql = """
                    INSERT INTO tenants.tenant_settings (id, tenant_id, key, value, created_at_utc)
                    VALUES (@id, @tenantId, @key, @value, @createdAtUtc);
                    """;
                await using var cmdS = new NpgsqlCommand(insertSettingSql, dedicatedConn);
                cmdS.Parameters.AddWithValue("id", initialSetting.Id);
                cmdS.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdS.Parameters.AddWithValue("key", initialSetting.Key);
                cmdS.Parameters.AddWithValue("value", initialSetting.Value);
                cmdS.Parameters.AddWithValue("createdAtUtc", initialSetting.CreatedAtUtc);
                await cmdS.ExecuteNonQueryAsync(httpContext.RequestAborted);
            }
            else
            {
                // Seed into nexusdesk_shared
                var sharedConnStr = connectionResolver.GetDefaultConnectionString();
                await using var sharedConn = new NpgsqlConnection(sharedConnStr);
                await sharedConn.OpenAsync(httpContext.RequestAborted);

                // Set session variable so RLS allows initial insert
                await using (var rlsCmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @tId, false);", sharedConn))
                {
                    rlsCmd.Parameters.AddWithValue("tId", tenant.Id.ToString());
                    await rlsCmd.ExecuteNonQueryAsync(httpContext.RequestAborted);
                }

                var insertMembershipSql = """
                    INSERT INTO tenants.tenant_memberships (id, tenant_id, user_id, role, email, full_name, created_at_utc)
                    VALUES (@id, @tenantId, @userId, @role, @email, @fullName, @createdAtUtc);
                    """;
                await using var cmdM = new NpgsqlCommand(insertMembershipSql, sharedConn);
                cmdM.Parameters.AddWithValue("id", initialMembership.Id);
                cmdM.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdM.Parameters.AddWithValue("userId", initialMembership.UserId);
                cmdM.Parameters.AddWithValue("role", initialMembership.Role);
                cmdM.Parameters.AddWithValue("email", initialMembership.Email);
                cmdM.Parameters.AddWithValue("fullName", initialMembership.FullName);
                cmdM.Parameters.AddWithValue("createdAtUtc", initialMembership.CreatedAtUtc);
                await cmdM.ExecuteNonQueryAsync(httpContext.RequestAborted);

                var insertUserSql = """
                    INSERT INTO identity.users (
                        id, tenant_id, email, normalized_email, password_hash, full_name,
                        role, is_active, is_anonymized, created_at_utc
                    ) VALUES (
                        @id, @tenantId, @email, @normalizedEmail, @passwordHash, @fullName,
                        'TenantAdmin', true, false, @createdAtUtc
                    ) ON CONFLICT (tenant_id, normalized_email) DO NOTHING;

                    INSERT INTO identity.user_role_assignments (id, user_id, tenant_id, role, created_at_utc)
                    VALUES (@roleId, @id, @tenantId, 'TenantAdmin', @createdAtUtc)
                    ON CONFLICT (user_id, role) DO NOTHING;
                    """;
                await using var cmdU = new NpgsqlCommand(insertUserSql, sharedConn);
                cmdU.Parameters.AddWithValue("id", initialMembership.UserId);
                cmdU.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdU.Parameters.AddWithValue("email", request.AdminEmail);
                cmdU.Parameters.AddWithValue("normalizedEmail", normalizedAdminEmail);
                cmdU.Parameters.AddWithValue("passwordHash", adminPasswordHash);
                cmdU.Parameters.AddWithValue("fullName", request.AdminFullName);
                cmdU.Parameters.AddWithValue("createdAtUtc", initialMembership.CreatedAtUtc);
                cmdU.Parameters.AddWithValue("roleId", Guid.NewGuid());
                await cmdU.ExecuteNonQueryAsync(httpContext.RequestAborted);

                var insertSettingSql = """
                    INSERT INTO tenants.tenant_settings (id, tenant_id, key, value, created_at_utc)
                    VALUES (@id, @tenantId, @key, @value, @createdAtUtc);
                    """;
                await using var cmdS = new NpgsqlCommand(insertSettingSql, sharedConn);
                cmdS.Parameters.AddWithValue("id", initialSetting.Id);
                cmdS.Parameters.AddWithValue("tenantId", tenant.Id);
                cmdS.Parameters.AddWithValue("key", initialSetting.Key);
                cmdS.Parameters.AddWithValue("value", initialSetting.Value);
                cmdS.Parameters.AddWithValue("createdAtUtc", initialSetting.CreatedAtUtc);
                await cmdS.ExecuteNonQueryAsync(httpContext.RequestAborted);
            }

            await tenantCache.InvalidateAsync(tenant.Id, tenant.Subdomain, httpContext.RequestAborted);

            var portalUrl = $"http://{tenant.Subdomain}.localhost:56100";
            var response = new ProvisionTenantResponse(
                TenantId: tenant.Id,
                OrganizationName: tenant.Name,
                Subdomain: tenant.Subdomain,
                PortalUrl: portalUrl,
                Status: tenant.Status.ToString(),
                IsolationStrategy: tenant.IsolationStrategy.ToString(),
                PlanTier: tenant.PlanTier.ToString(),
                PlanLimits: new PlanLimitsDto(tenant.MaxAgents, tenant.MaxTicketsPerMonth, tenant.MaxStorageBytes),
                CreatedAtUtc: tenant.CreatedAtUtc);

            return Results.Created($"/api/v1/admin/tenants/{tenant.Id}", response);
        };

        group.MapPost("/tenants/provision", provisionHandler)
            .WithName("ProvisionTenant")
            .WithTags("Tenants");

        group.MapPost("/platform/tenants", provisionHandler)
            .WithName("PlatformProvisionTenant")
            .WithTags("PlatformAdmin");

        // 2. Admin: List all tenants & platform alias
        var listTenantsHandler = async (
            CatalogDbContext catalogDb,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var p = Math.Max(1, page);
            var ps = Math.Clamp(pageSize, 1, 100);

            var total = await catalogDb.Tenants.CountAsync();
            var items = await catalogDb.Tenants
                .AsNoTracking()
                .OrderBy(t => t.CreatedAtUtc)
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Subdomain,
                    PlanTier = t.PlanTier.ToString(),
                    IsolationStrategy = t.IsolationStrategy.ToString(),
                    Status = t.Status.ToString(),
                    t.MaxAgents,
                    t.MaxTicketsPerMonth,
                    t.MaxStorageBytes,
                    t.CreatedAtUtc
                })
                .ToListAsync();

            return Results.Ok(new
            {
                page = p,
                pageSize = ps,
                totalCount = total,
                items
            });
        };

        group.MapGet("/admin/tenants", listTenantsHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("ListTenants")
            .WithTags("PlatformAdmin");

        group.MapGet("/platform/tenants", listTenantsHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("PlatformListTenants")
            .WithTags("PlatformAdmin");

        // 3. Admin: Get tenant by id with audit logs & platform alias
        var getTenantByIdHandler = async (
            Guid tenantId,
            CatalogDbContext catalogDb) =>
        {
            var tenant = await catalogDb.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Tenant Not Found",
                    Detail = $"Tenant with ID '{tenantId}' was not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var auditLogs = await catalogDb.TenantAuditLogs
                .AsNoTracking()
                .Where(l => l.TenantId == tenantId)
                .OrderByDescending(l => l.TimestampUtc)
                .ToListAsync();

            return Results.Ok(new
            {
                tenant = new
                {
                    tenant.Id,
                    tenant.Name,
                    tenant.Subdomain,
                    PlanTier = tenant.PlanTier.ToString(),
                    IsolationStrategy = tenant.IsolationStrategy.ToString(),
                    Status = tenant.Status.ToString(),
                    tenant.MaxAgents,
                    tenant.MaxTicketsPerMonth,
                    tenant.MaxStorageBytes,
                    tenant.CreatedAtUtc,
                    tenant.UpdatedAtUtc
                },
                auditLogs
            });
        };

        group.MapGet("/admin/tenants/{tenantId:guid}", getTenantByIdHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("GetTenantById")
            .WithTags("PlatformAdmin");

        group.MapGet("/platform/tenants/{tenantId:guid}", getTenantByIdHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("PlatformGetTenantById")
            .WithTags("PlatformAdmin");

        // 4. Admin: Update tenant status (Suspend / Activate) & platform alias
        var updateTenantStatusHandler = async (
            Guid tenantId,
            [FromBody] UpdateTenantStatusRequest request,
            CatalogDbContext catalogDb,
            ITenantCache tenantCache,
            HttpContext httpContext) =>
        {
            var tenant = await catalogDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Tenant Not Found",
                    Detail = $"Tenant with ID '{tenantId}' was not found.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            if (!Enum.TryParse<TenantStatus>(request.Status, true, out var newStatus))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Title = "Invalid Status",
                    Detail = $"Status '{request.Status}' is invalid. Supported: Active, Suspended.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var now = DateTimeOffset.UtcNow;
            if (newStatus == TenantStatus.Suspended)
            {
                tenant.Suspend(request.Reason, now);
            }
            else if (newStatus == TenantStatus.Active)
            {
                tenant.Activate(request.Reason, now);
            }

            catalogDb.TenantAuditLogs.Add(new TenantAuditLog(
                Guid.NewGuid(),
                tenant.Id,
                $"StatusChangedTo{newStatus}",
                request.Reason ?? "Status updated by admin",
                "PlatformAdmin",
                now));

            await catalogDb.SaveChangesAsync(httpContext.RequestAborted);
            await tenantCache.InvalidateAsync(tenant.Id, tenant.Subdomain, httpContext.RequestAborted);

            return Results.Ok(new
            {
                tenant.Id,
                tenant.Subdomain,
                Status = tenant.Status.ToString(),
                tenant.UpdatedAtUtc
            });
        };

        group.MapPatch("/admin/tenants/{tenantId:guid}/status", updateTenantStatusHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("UpdateTenantStatus")
            .WithTags("PlatformAdmin");

        group.MapPatch("/platform/tenants/{tenantId:guid}/status", updateTenantStatusHandler)
            .RequireAuthorization("PlatformSuperAdminOnly")
            .WithName("PlatformUpdateTenantStatus")
            .WithTags("PlatformAdmin");

        // 5. Tenant-Scoped: Get Current Tenant Context & Verification Data
        group.MapGet("/tenant/current", async (
            ITenantContextAccessor accessor,
            TenantScopedDbContext tenantScopedDb,
            HttpContext httpContext) =>
        {
            var context = accessor.TenantContext;
            if (!context.IsResolved || !context.TenantId.HasValue)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Tenant Not Found",
                    Detail = "No active tenant could be resolved from request headers or host.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            // Execute SQL probes to prove physical database routing and RLS session state
            string activeDatabase = "unknown";
            string rlsSessionTenantId = "none";

            await tenantScopedDb.Database.OpenConnectionAsync(httpContext.RequestAborted);
            try
            {
                var conn = tenantScopedDb.Database.GetDbConnection();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT current_database();";
                    var dbName = await cmd.ExecuteScalarAsync(httpContext.RequestAborted);
                    if (dbName != null) activeDatabase = dbName.ToString()!;
                }

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT coalesce(current_setting('app.current_tenant_id', true), '');";
                    var rlsVal = await cmd.ExecuteScalarAsync(httpContext.RequestAborted);
                    if (rlsVal != null) rlsSessionTenantId = rlsVal.ToString()!;
                }
            }
            finally
            {
                await tenantScopedDb.Database.CloseConnectionAsync();
            }

            var settings = await tenantScopedDb.Settings.AsNoTracking().ToListAsync(httpContext.RequestAborted);
            var memberships = await tenantScopedDb.Memberships.AsNoTracking().ToListAsync(httpContext.RequestAborted);

            return Results.Ok(new
            {
                tenantContext = new
                {
                    context.TenantId,
                    context.Subdomain,
                    PlanTier = context.PlanTier?.ToString(),
                    IsolationStrategy = context.IsolationStrategy?.ToString(),
                    Status = context.Status?.ToString(),
                    context.IsResolved
                },
                activeDatabase,
                rlsSessionTenantId,
                settingsCount = settings.Count,
                settings = settings.Select(s => new { s.Id, s.Key, s.Value, s.TenantId }),
                membershipsCount = memberships.Count,
                memberships = memberships.Select(m => new { m.Id, m.UserId, m.Role, m.Email, m.FullName, m.TenantId })
            });
        })
        .WithName("GetCurrentTenant")
        .WithTags("TenantScoped");

        // 6. Tenant-Scoped: Create and list settings items (to verify read/write isolation)
        group.MapPost("/tenant/settings/items", async (
            [FromBody] CreateSettingItemRequest request,
            ITenantContextAccessor accessor,
            TenantScopedDbContext tenantScopedDb,
            HttpContext httpContext) =>
        {
            var currentTenant = accessor.TenantContext;
            if (!currentTenant.IsResolved || !currentTenant.TenantId.HasValue)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Title = "Tenant Not Found",
                    Detail = "Tenant resolution required.",
                    Status = StatusCodes.Status404NotFound
                });
            }

            var setting = new TenantSetting(
                Guid.NewGuid(),
                currentTenant.TenantId.Value,
                request.Key,
                request.Value,
                DateTimeOffset.UtcNow);

            tenantScopedDb.Settings.Add(setting);
            await tenantScopedDb.SaveChangesAsync(httpContext.RequestAborted);

            return Results.Created($"/api/v1/tenant/settings/items/{setting.Id}", new
            {
                setting.Id,
                setting.TenantId,
                setting.Key,
                setting.Value,
                setting.CreatedAtUtc
            });
        })
        .RequireAuthorization("TenantAdminOrHigher")
        .WithName("CreateTenantSettingItem")
        .WithTags("TenantScoped");

        group.MapGet("/tenant/settings/items", async (
            TenantScopedDbContext tenantScopedDb,
            HttpContext httpContext) =>
        {
            var items = await tenantScopedDb.Settings
                .AsNoTracking()
                .OrderBy(s => s.Key)
                .ToListAsync(httpContext.RequestAborted);

            return Results.Ok(items.Select(s => new
            {
                s.Id,
                s.TenantId,
                s.Key,
                s.Value,
                s.CreatedAtUtc
            }));
        })
        .WithName("ListTenantSettingItems")
        .WithTags("TenantScoped");

        return app;
    }
}
