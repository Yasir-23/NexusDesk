using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using NexusDesk.Modules.Identity.Authorization;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Security;
using NexusDesk.Modules.Identity.Tokens;
using NexusDesk.SharedKernel.MultiTenancy;
using Xunit;

namespace NexusDesk.UnitTests.Identity;

public class IdentityUnitTests
{
    [Fact]
    public void Pkce_GenerateCodeVerifierAndChallenge_ShouldValidateSuccessfully()
    {
        // Arrange
        var verifier = PkceHelper.GenerateCodeVerifier(32);
        verifier.Should().NotBeNullOrWhiteSpace();
        verifier.Length.Should().BeGreaterOrEqualTo(43);

        // Act
        var challenge = PkceHelper.GenerateCodeChallenge(verifier);
        challenge.Should().NotBeNullOrWhiteSpace();

        // Assert
        var isValid = PkceHelper.ValidateCodeVerifier(verifier, challenge, "S256");
        isValid.Should().BeTrue("Valid code verifier and S256 challenge must succeed.");

        var isInvalid = PkceHelper.ValidateCodeVerifier("invalid-verifier-string-1234567890", challenge, "S256");
        isInvalid.Should().BeFalse("Invalid code verifier must fail validation.");
    }

    [Fact]
    public void Pkce_Rfc7636AppendixBTestVector_AndBase64UrlEncoding_ShouldVerify()
    {
        // RFC 7636 Appendix B official test vector
        const string rfc7636Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedRfc7636Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var computedChallenge = PkceHelper.GenerateCodeChallenge(rfc7636Verifier);
        computedChallenge.Should().Be(expectedRfc7636Challenge);
        PkceHelper.VerifyCodeChallenge(rfc7636Verifier, expectedRfc7636Challenge, "S256").Should().BeTrue();
        PkceHelper.ValidateCodeVerifier(rfc7636Verifier, expectedRfc7636Challenge, "S256").Should().BeTrue();

        // Standard Base64 contains '+', '/', '=' and must NOT match or be emitted
        var raw = new byte[] { 0xFB, 0xFF, 0xBF, 0xFE }; // Standard Base64: "+/+//g=="
        var encoded = PkceHelper.Base64UrlEncode(raw);
        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
        encoded.Should().Be("-_-__g");
    }

    [Fact]
    public void PasswordHasher_HashAndVerify_ShouldBeSecureAndAccurate()
    {
        // Arrange
        var hasher = new PasswordHasherService();
        var user = new User(Guid.NewGuid(), Guid.NewGuid(), "tester@example.com", "Test User", IdentityRoles.Agent);
        const string rawPassword = "P@ssword123!Secure";

        // Act
        var hash = hasher.HashPassword(user, rawPassword);

        // Assert
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().NotBe(rawPassword);

        var verifySuccess = hasher.VerifyPassword(user, hash, rawPassword);
        verifySuccess.Should().BeTrue("Matching password must verify successfully.");

        var verifyFailure = hasher.VerifyPassword(user, hash, "WrongPassword123!");
        verifyFailure.Should().BeFalse("Mismatched password must fail verification.");
    }

    [Fact]
    public void PiiEncryption_RoundTrip_ShouldEncryptAndDecryptAccurately()
    {
        // Arrange
        var dpProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "NexusDeskDataProtectionKeys")));
        var piiService = new PiiEncryptionService(dpProvider);

        const string originalPhone = "+1-800-555-0199";

        // Act
        var ciphertext = piiService.Encrypt(originalPhone);
        ciphertext.Should().NotBeNullOrWhiteSpace();
        ciphertext.Should().NotBe(originalPhone, "Encrypted phone number must not equal plaintext on disk.");

        var decryptedPhone = piiService.Decrypt(ciphertext);

        // Assert
        decryptedPhone.Should().Be(originalPhone, "Decrypted phone number must exactly match original plaintext.");
    }

    [Fact]
    public void JwtTokenService_GenerateTokenResponse_ShouldIncludeMandatoryClaims()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = JwtTokenService.DefaultSecretKey,
            ["Jwt:Issuer"] = "https://nexusdesk.local",
            ["Jwt:Audience"] = "nexusdesk-api",
            ["Jwt:AccessTokenLifetimeMinutes"] = "60"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var tokenService = new JwtTokenService(config);

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var user = new User(userId, tenantId, "agent@acme.com", "Acme Agent", IdentityRoles.Agent, "hash");

        // Act
        var response = tokenService.GenerateTokenResponse(user, "acme", "Pro");

        // Assert
        response.Should().NotBeNull();
        response.access_token.Should().NotBeNullOrWhiteSpace();
        response.refresh_token.Should().NotBeNullOrWhiteSpace();
        response.token_type.Should().Be("Bearer");
        response.expires_in.Should().BeGreaterThan(0);

        var principal = tokenService.ValidateToken(response.access_token);
        principal.Should().NotBeNull();

        principal!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value.Should().Be(userId.ToString("D"));
        principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value.Should().Be("agent@acme.com");
        principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value.Should().Be("Acme Agent");
        principal.FindFirst("tenant_id")?.Value.Should().Be(tenantId.ToString("D"));
        principal.FindFirst("subdomain")?.Value.Should().Be("acme");
        principal.FindFirst("role")?.Value.Should().Be(IdentityRoles.Agent);
        principal.FindFirst("plan_tier")?.Value.Should().Be("Pro");
        principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value.Should().NotBeNullOrWhiteSpace();
        principal.FindAll("permission").Should().NotBeEmpty();
    }

    [Fact]
    public async Task TenantBoundaryAuthorizationHandler_CrossTenantAccess_ShouldFailRequirement()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var accessor = new TenantContextAccessor();
        accessor.TenantContext = new TenantContext
        {
            TenantId = tenantB,
            Subdomain = "beta",
            PlanTier = TenantPlanTier.Free,
            IsolationStrategy = IsolationStrategy.SharedDatabase,
            Status = TenantStatus.Active
        };

        var handler = new TenantBoundaryAuthorizationHandler(accessor);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim("tenant_id", tenantA.ToString("D")),
            new Claim(ClaimTypes.Role, IdentityRoles.TenantAdmin)
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var requirement = new TenantBoundaryRequirement(IdentityRoles.TenantAdmin);
        var authContext = new AuthorizationHandlerContext([requirement], user, null);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        authContext.HasSucceeded.Should().BeFalse("TenantAdmin of TenantA must not pass authorization for TenantB.");
    }

    [Fact]
    public async Task TenantBoundaryAuthorizationHandler_SuperAdmin_ShouldSucceedRegardlessOfTenant()
    {
        // Arrange
        var tenantB = Guid.NewGuid();
        var accessor = new TenantContextAccessor();
        accessor.TenantContext = new TenantContext
        {
            TenantId = tenantB,
            Subdomain = "beta",
            PlanTier = TenantPlanTier.Free,
            IsolationStrategy = IsolationStrategy.SharedDatabase,
            Status = TenantStatus.Active
        };

        var handler = new TenantBoundaryAuthorizationHandler(accessor);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim("tenant_id", "platform"),
            new Claim(ClaimTypes.Role, IdentityRoles.SuperAdmin)
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var requirement = new TenantBoundaryRequirement(IdentityRoles.TenantAdmin);
        var authContext = new AuthorizationHandlerContext([requirement], user, null);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        authContext.HasSucceeded.Should().BeTrue("Platform SuperAdmin must succeed regardless of tenant context.");
    }
}
