using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NexusDesk.SharedKernel.MultiTenancy;
using Npgsql;

namespace NexusDesk.Modules.Tenants.Persistence.Interceptors;

public sealed class PostgresRlsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContextAccessor _tenantContextAccessor;

    public PostgresRlsConnectionInterceptor(ITenantContextAccessor tenantContextAccessor)
    {
        _tenantContextAccessor = tenantContextAccessor;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetRlsSessionVariable(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetRlsSessionVariableAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void SetRlsSessionVariable(DbConnection connection)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, false);";
        var param = command.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.Value.ToString();
        command.Parameters.Add(param);
        command.ExecuteNonQuery();
    }

    private async Task SetRlsSessionVariableAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContextAccessor.TenantContext?.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, false);";
        var param = command.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.Value.ToString();
        command.Parameters.Add(param);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
