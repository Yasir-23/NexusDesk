using Xunit;

namespace NexusDesk.IntegrationTests;

/// <summary>
/// Serializes all integration test classes sharing the live PostgreSQL and Redis instances
/// to prevent parallel race conditions on shared tenant state (e.g., beta tenant status).
/// </summary>
[CollectionDefinition("DatabaseIntegrationCollection", DisableParallelization = true)]
public sealed class DatabaseIntegrationCollection
{
}
