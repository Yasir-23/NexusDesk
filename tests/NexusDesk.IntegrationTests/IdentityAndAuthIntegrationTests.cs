using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.Identity.Security;
using NexusDesk.Modules.Tenants.Endpoints;
using Npgsql;
using Xunit;

namespace NexusDesk.IntegrationTests;

[Collection("DatabaseIntegrationCollection")]
public class IdentityAndAuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    public IdentityAndAuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        });
    }

    private async Task EnsureTenantsProvisionedAsync()
    {
        var rootClient = _factory.CreateClient();

        // Ensure Acme (Pro), Beta (Free), Globex (Enterprise) are provisioned
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Acme Corporation", "acme", "admin@acme.com", "Acme Admin", "Pro"));

        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Beta Innovations", "beta", "admin@beta.com", "Beta Admin", "Free"));

        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Globex Global", "globex", "admin@globex.com", "Globex Admin", "Enterprise"));

        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var listRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        if (listRes.IsSuccessStatusCode)
        {
            var doc = await listRes.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var item in doc.GetProperty("items").EnumerateArray())
            {
                var tid = Guid.Parse(item.GetProperty("id").GetString()!);
                var status = item.GetProperty("status").GetString();
                if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{tid}/status",
                        new UpdateTenantStatusRequest("Active", "Baseline test reset"));
                }
            }
        }
    }

    [Fact]
    public async Task OAuth2_AuthorizationCodeAndPkceFlow_ShouldSucceed_AndRejectInvalidVerifier()
    {
        await EnsureTenantsProvisionedAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        // 1. Generate cryptographic PKCE code verifier and S256 challenge
        var codeVerifier = PkceHelper.GenerateCodeVerifier(32);
        var codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);

        // 2. Request authorization code from /connect/authorize
        var authRequest = new
        {
            client_id = "nexusdesk-spa",
            redirect_uri = "https://acme.nexusdesk.local/oauth/callback",
            response_type = "code",
            code_challenge = codeChallenge,
            code_challenge_method = "S256",
            scope = "openid profile email",
            state = "xyz-state-12345",
            username = "admin@acme.com",
            password = "Password123!"
        };

        var authRes = await client.PostAsJsonAsync("/connect/authorize", authRequest);
        authRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var authJson = await authRes.Content.ReadFromJsonAsync<JsonElement>();
        var authCode = authJson.GetProperty("code").GetString();
        authCode.Should().NotBeNullOrWhiteSpace();

        // Verify code_challenge and code_challenge_method are persisted in PostgreSQL
        await using (var conn = new NpgsqlConnection(PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT code_challenge, code_challenge_method FROM identity.oauth_authorization_codes WHERE code_hash = @hash;",
                conn);
            cmd.Parameters.AddWithValue("hash", PkceHelper.HashToken(authCode!));
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue("OAuthAuthorizationCode record must be persisted.");
            reader.GetString(0).Should().Be(codeChallenge);
            reader.GetString(1).Should().Be("S256");
        }

        // 3. Negative test: Exchange code with an INVALID code_verifier -> Must return 400 Bad Request
        var invalidTokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode!,
            ["code_verifier"] = "invalid-code-verifier-tampered-1234567890",
            ["redirect_uri"] = "https://acme.nexusdesk.local/oauth/callback",
            ["client_id"] = "nexusdesk-spa"
        };

        var invalidTokenRes = await client.PostAsJsonAsync("/connect/token", invalidTokenRequest);
        invalidTokenRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. Positive test: Request fresh auth code (using camelCase & defaulted S256) and exchange with VALID code_verifier -> Must return 200 OK with JWT
        var camelCaseAuthRequest = new
        {
            clientId = "nexusdesk-spa",
            redirectUri = "https://acme.nexusdesk.local/oauth/callback",
            responseType = "code",
            codeChallenge = codeChallenge,
            scope = "openid profile email",
            state = "xyz-state-67890",
            username = "admin@acme.com",
            password = "Password123!"
        };
        var authRes2 = await client.PostAsJsonAsync("/connect/authorize", camelCaseAuthRequest);
        authRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var authCode2 = (await authRes2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

        var validTokenRequest = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authCode2!,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = "https://acme.nexusdesk.local/oauth/callback",
            ["client_id"] = "nexusdesk-spa"
        };

        var validTokenRes = await client.PostAsJsonAsync("/connect/token", validTokenRequest);
        validTokenRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenJson = await validTokenRes.Content.ReadFromJsonAsync<JsonElement>();
        tokenJson.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        tokenJson.GetProperty("refresh_token").GetString().Should().NotBeNullOrWhiteSpace();
        tokenJson.GetProperty("token_type").GetString().Should().Be("Bearer");
        tokenJson.GetProperty("expires_in").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OAuth2_Rfc7636ExactHttpJsonPair_ShouldReturn200Ok_WithJwtToken()
    {
        await EnsureTenantsProvisionedAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        const string rfc7636Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string rfc7636Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        // Step 1: POST /connect/authorize with JSON body
        var authorizeJson = """
            {
              "client_id": "nexusdesk-web",
              "redirect_uri": "http://localhost:56100/callback",
              "response_type": "code",
              "scope": "openid profile email offline_access",
              "code_challenge": "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
              "code_challenge_method": "S256",
              "state": "xyz123",
              "username": "admin@acme.com",
              "password": "Password123!"
            }
            """;

        using var authorizeContent = new StringContent(authorizeJson, System.Text.Encoding.UTF8, "application/json");
        var authorizeRes = await client.PostAsync("/connect/authorize", authorizeContent);
        authorizeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var authorizeDoc = await authorizeRes.Content.ReadFromJsonAsync<JsonElement>();
        var code = authorizeDoc.GetProperty("code").GetString();
        code.Should().NotBeNullOrWhiteSpace();
        rfc7636Challenge.Should().Be(PkceHelper.GenerateCodeChallenge(rfc7636Verifier));

        // Step 2: POST /connect/token with JSON body
        var tokenJsonPayload = JsonSerializer.Serialize(new
        {
            grant_type = "authorization_code",
            code,
            redirect_uri = "http://localhost:56100/callback",
            client_id = "nexusdesk-web",
            code_verifier = rfc7636Verifier
        });

        using var tokenContent = new StringContent(tokenJsonPayload, System.Text.Encoding.UTF8, "application/json");
        var tokenRes = await client.PostAsync("/connect/token", tokenContent);
        tokenRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenDoc = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
        tokenDoc.GetProperty("access_token").GetString().Should().NotBeNullOrWhiteSpace();
        tokenDoc.GetProperty("refresh_token").GetString().Should().NotBeNullOrWhiteSpace();
        tokenDoc.GetProperty("token_type").GetString().Should().Be("Bearer");

        // Negative verification: 1-character challenge mismatch ("FrGMT..." instead of "FrEMT...") must fail with 400 invalid_grant
        var mismatchedAuthorizeJson = authorizeJson.Replace("FrEMTJgu", "FrGMTJgu");
        using var mismatchContent = new StringContent(mismatchedAuthorizeJson, System.Text.Encoding.UTF8, "application/json");
        var mismatchAuthRes = await client.PostAsync("/connect/authorize", mismatchContent);
        mismatchAuthRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var mismatchedCode = (await mismatchAuthRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

        var mismatchTokenPayload = JsonSerializer.Serialize(new
        {
            grant_type = "authorization_code",
            code = mismatchedCode,
            redirect_uri = "http://localhost:56100/callback",
            client_id = "nexusdesk-web",
            code_verifier = rfc7636Verifier
        });
        using var mismatchTokenContent = new StringContent(mismatchTokenPayload, System.Text.Encoding.UTF8, "application/json");
        var mismatchTokenRes = await client.PostAsync("/connect/token", mismatchTokenContent);
        mismatchTokenRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errDoc = await mismatchTokenRes.Content.ReadFromJsonAsync<JsonElement>();
        errDoc.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task SuspendedTenantLoginBlock_ShouldReturn403_AndSucceedWhenReactivated()
    {
        await EnsureTenantsProvisionedAsync();

        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);

        // 1. Get Beta tenant ID
        var listRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        var betaItem = doc.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("subdomain").GetString() == "beta");
        var betaTenantId = Guid.Parse(betaItem.GetProperty("id").GetString()!);

        // Ensure Beta starts Active
        var initActiveRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status",
            new UpdateTenantStatusRequest("Active", "Ensure active baseline"));
        initActiveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var betaClient = _factory.CreateClient();
        betaClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

        try
        {
            // 2. Suspend Beta
            var suspendRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status",
                new UpdateTenantStatusRequest("Suspended", "Non-payment test"));
            suspendRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Attempt to log in as Beta admin while suspended -> Must be rejected with 403 Forbidden & TENANT_SUSPENDED
            var loginRes = await betaClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@beta.com", "Password123!"));
            loginRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var problem = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
            var loginErrCode = problem.TryGetProperty("code", out var lc) ? lc.GetString()
                : problem.TryGetProperty("errorCode", out var lec) ? lec.GetString() : null;
            loginErrCode.Should().Be("TENANT_SUSPENDED");

            // Also test /connect/token password grant on suspended tenant
            var tokenRes = await betaClient.PostAsJsonAsync("/connect/token", new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = "admin@beta.com",
                ["password"] = "Password123!"
            });
            tokenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var tokenProblem = await tokenRes.Content.ReadFromJsonAsync<JsonElement>();
            var tokenErrCode = tokenProblem.TryGetProperty("code", out var tc) ? tc.GetString()
                : tokenProblem.TryGetProperty("errorCode", out var tec) ? tec.GetString() : null;
            tokenErrCode.Should().Be("TENANT_SUSPENDED");
        }
        finally
        {
            // 4. Reactivate Beta unconditionally
            var reactivateRes = await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status",
                new UpdateTenantStatusRequest("Active", "Account restored"));
            reactivateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 5. Login now succeeds
        var retryLoginRes = await betaClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@beta.com", "Password123!"));
        retryLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var retryJson = await retryLoginRes.Content.ReadFromJsonAsync<JsonElement>();
        retryJson.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CrossTenantTokenSpoofing_ShouldRejectWith403_CrossTenantAccessDenied()
    {
        await EnsureTenantsProvisionedAsync();

        // 1. Log in as Acme admin to receive Acme JWT
        var acmeClient = _factory.CreateClient();
        acmeClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        var acmeLoginRes = await acmeClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@acme.com", "Password123!"));
        acmeLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeJson = await acmeLoginRes.Content.ReadFromJsonAsync<JsonElement>();
        var acmeToken = acmeJson.GetProperty("accessToken").GetString()!;

        // 2. Send request to Beta tenant boundary while attaching Acme JWT
        var spoofingClient = _factory.CreateClient();
        spoofingClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acmeToken);
        spoofingClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

        var res = await spoofingClient.GetAsync("/api/v1/users");

        // Assert: Cross-Tenant Token Guard short-circuits with 403 Forbidden
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var errorDoc = await res.Content.ReadFromJsonAsync<JsonElement>();
        errorDoc.GetProperty("code").GetString().Should().Be("CROSS_TENANT_ACCESS_DENIED");
    }

    [Fact]
    public async Task PlanAgentSeatQuotaLimit_FreePlanMaxTwoAgents_ShouldEnforceBoundary()
    {
        await EnsureTenantsProvisionedAsync();

        var adminClient = AuthTestTokenHelper.CreateSuperAdminClient(_factory);
        var listRes = await adminClient.GetAsync("/api/v1/admin/tenants");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        var betaItem = doc.GetProperty("items").EnumerateArray()
            .First(t => t.GetProperty("subdomain").GetString() == "beta");
        var betaTenantId = Guid.Parse(betaItem.GetProperty("id").GetString()!);

        await adminClient.PatchAsJsonAsync($"/api/v1/admin/tenants/{betaTenantId}/status",
            new UpdateTenantStatusRequest("Active", "Ensure active for seat quota test"));

        // Reset Beta agents to just admin@beta.com so test is idempotent across repeated runs
        await using (var conn = new NpgsqlConnection(PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM identity.users WHERE tenant_id = @tid AND normalized_email <> 'ADMIN@BETA.COM' AND role IN ('Agent', 'TenantAdmin');",
                conn);
            cmd.Parameters.AddWithValue("tid", betaTenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 1. Log in as Beta Admin (Free tier: max 2 agents)
            var betaClient = _factory.CreateClient();
            betaClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

            var loginRes = await betaClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin@beta.com", "Password123!"));
            loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var token = (await loginRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

            var authedBetaClient = _factory.CreateClient();
            authedBetaClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            authedBetaClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "beta");

            // 2. Count existing agents in Beta (admin@beta.com is 1 active agent)
            var listUsersRes = await authedBetaClient.GetAsync("/api/v1/users");
            listUsersRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Create 1 new agent (Seat 2 of 2 used)
            var agent2Email = $"beta.agent2.{Guid.NewGuid():N}@beta.com";
            var agent2Res = await authedBetaClient.PostAsJsonAsync("/api/v1/users/agents", new CreateAgentRequest(
                agent2Email,
                "Beta Agent Two",
                "Password123!",
                "+1-800-555-0302"));

            agent2Res.StatusCode.Should().Be(HttpStatusCode.Created);

            // 4. Attempt to create a 3rd agent -> Must be blocked by quota with 422 & PLAN_AGENT_LIMIT_EXCEEDED
            var agent3Email = $"beta.agent3.{Guid.NewGuid():N}@beta.com";
            var agent3Res = await authedBetaClient.PostAsJsonAsync("/api/v1/users/agents", new CreateAgentRequest(
                agent3Email,
                "Beta Agent Three",
                "Password123!",
                "+1-800-555-0303"));

            agent3Res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var quotaProblem = await agent3Res.Content.ReadFromJsonAsync<JsonElement>();
            quotaProblem.GetProperty("code").GetString().Should().Be("PLAN_AGENT_LIMIT_EXCEEDED");
        }
        finally
        {
            // Clean up synthetic test agent so Beta retains 1 active agent seat for future runs
            await using var conn = new NpgsqlConnection(PostgresConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM identity.users WHERE tenant_id = @tid AND normalized_email LIKE 'BETA.AGENT%';",
                conn);
            cmd.Parameters.AddWithValue("tid", betaTenantId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task SsoLoginAndPiiEncryption_ShouldEncryptInDatabase_AndDecryptInApi()
    {
        await EnsureTenantsProvisionedAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        var uniqueEmail = $"sso.user.{Guid.NewGuid():N}@acme.com";
        const string rawPhone = "+1-555-987-6543";

        // 1. Authenticate via Google SSO
        var ssoReq = new SsoLoginRequest(
            IdToken: "mock-google-id-token-xyz",
            Email: uniqueEmail,
            FullName: "SSO Customer User",
            PhoneNumber: rawPhone);

        var ssoRes = await client.PostAsJsonAsync("/api/v1/auth/sso/google", ssoReq);
        ssoRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ssoDoc = await ssoRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = ssoDoc.GetProperty("accessToken").GetString()!;

        // 2. Query PostgreSQL directly to prove phone number is encrypted ciphertext on disk
        await using (var connection = new NpgsqlConnection(PostgresConnectionString))
        {
            await connection.OpenAsync();
            var checkSql = "SELECT encrypted_phone_number FROM identity.users WHERE normalized_email = @email;";
            await using var cmd = new NpgsqlCommand(checkSql, connection);
            cmd.Parameters.AddWithValue("email", uniqueEmail.ToUpperInvariant());
            var storedValue = (string?)await cmd.ExecuteScalarAsync();

            storedValue.Should().NotBeNullOrWhiteSpace();
            storedValue.Should().NotBe(rawPhone, "Encrypted phone number column on PostgreSQL disk must NOT equal plaintext phone.");
        }

        // 3. Call GET /api/v1/auth/me to prove API transparently returns the decrypted plaintext phone number
        var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        authedClient.DefaultRequestHeaders.Add("X-Tenant-Subdomain", "acme");

        var meRes = await authedClient.GetAsync("/api/v1/auth/me");
        meRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var meDoc = await meRes.Content.ReadFromJsonAsync<JsonElement>();
        meDoc.GetProperty("phoneNumber").GetString().Should().Be(rawPhone, "Decrypted phone number returned by /me must match original plaintext.");
        meDoc.GetProperty("email").GetString().Should().Be(uniqueEmail);
        meDoc.GetProperty("ssoProvider").GetString().Should().Be("google");
    }
}
