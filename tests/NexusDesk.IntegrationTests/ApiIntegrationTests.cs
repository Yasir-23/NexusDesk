using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace NexusDesk.IntegrationTests;

[Collection("DatabaseIntegrationCollection")]
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    [Fact]
    public async Task GetHealthLive_ShouldReturnOk_AndHealthyStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);
        jsonDoc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealthReady_ShouldReturnOk_WithHealthyPostgresAndRedis()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        var entries = jsonDoc.RootElement.GetProperty("entries");

        entries.TryGetProperty("postgresql", out var pgEntry).Should().BeTrue();
        pgEntry.GetProperty("status").GetString().Should().Be("Healthy");

        entries.TryGetProperty("redis", out var redisEntry).Should().BeTrue();
        redisEntry.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task PostgreSql_ShouldHaveVectorExtension_AndSixRequiredSchemas()
    {
        await using var connection = new NpgsqlConnection(PostgresConnectionString);
        await connection.OpenAsync();

        // 1. Verify 'vector' and 'uuid-ossp' extensions
        await using (var cmd = new NpgsqlCommand("SELECT extname FROM pg_extension WHERE extname IN ('vector', 'uuid-ossp');", connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            var extensions = new List<string>();
            while (await reader.ReadAsync())
            {
                extensions.Add(reader.GetString(0));
            }
            extensions.Should().Contain("vector");
            extensions.Should().Contain("uuid-ossp");
        }

        // 2. Verify required schemas
        var expectedSchemas = new[] { "identity", "tenants", "ticketing", "knowledge", "automation", "billing", "analytics" };
        await using (var cmd = new NpgsqlCommand("SELECT schema_name FROM information_schema.schemata WHERE schema_name = ANY(@schemas);", connection))
        {
            cmd.Parameters.AddWithValue("schemas", expectedSchemas);
            await using var reader = await cmd.ExecuteReaderAsync();
            var schemas = new List<string>();
            while (await reader.ReadAsync())
            {
                schemas.Add(reader.GetString(0));
            }
            schemas.Should().BeEquivalentTo(expectedSchemas);
        }
    }

    [Fact]
    public async Task GetSystemInfo_ShouldReturnOk_WithAllEightModulesRegistered()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(content);

        jsonDoc.RootElement.GetProperty("application").GetString().Should().Be("NexusDesk.Api");

        var modules = jsonDoc.RootElement.GetProperty("loadedModules")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        modules.Should().BeEquivalentTo(
        [
            "Tenants",
            "Identity",
            "Ticketing",
            "Chat",
            "KnowledgeBase",
            "Automation",
            "Billing",
            "Analytics"
        ]);

        var connectivity = jsonDoc.RootElement.GetProperty("connectivity");
        connectivity.GetProperty("overall").GetString().Should().Be("Healthy");
        connectivity.GetProperty("postgresql").GetString().Should().Be("Healthy");
        connectivity.GetProperty("redis").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task SwaggerEndpoint_ShouldReturnOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("NexusDesk API");
        content.Should().Contain("Bearer");
        content.Should().Contain("X-Tenant-Subdomain");

        using var doc = JsonDocument.Parse(content);
        var paths = doc.RootElement.GetProperty("paths");

        var authorizePost = paths.GetProperty("/connect/authorize").GetProperty("post");
        authorizePost.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _).Should().BeTrue();

        var tokenPost = paths.GetProperty("/connect/token").GetProperty("post");
        tokenPost.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _).Should().BeTrue();

        var ssoPost = paths.GetProperty("/connect/sso/{provider}").GetProperty("post");
        ssoPost.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _).Should().BeTrue();

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("AuthorizeRequestModel", out _).Should().BeTrue();
        schemas.TryGetProperty("TokenRequestModel", out _).Should().BeTrue();
        schemas.TryGetProperty("SsoRequestModel", out _).Should().BeTrue();
    }
}
