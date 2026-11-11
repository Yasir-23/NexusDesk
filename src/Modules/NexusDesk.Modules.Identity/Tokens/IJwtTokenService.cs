using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NexusDesk.Modules.Identity.Domain;
using NexusDesk.Modules.Identity.Security;

namespace NexusDesk.Modules.Identity.Tokens;

public record TokenResponse(
    string access_token,
    string token_type,
    int expires_in,
    string refresh_token,
    string scope,
    string? id_token = null);

public interface IJwtTokenService
{
    TokenResponse GenerateTokenResponse(
        User user,
        string subdomain,
        string planTier,
        string? scope = null,
        string? existingRefreshToken = null);

    ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true);

    string CreateRefreshTokenString();
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly byte[] _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenLifetimeMinutes;
    private readonly int _refreshTokenLifetimeDays;

    public const string DefaultSecretKey = "NexusDesk_Milestone3_Secret_Signing_Key_2026_ProdGrade!";

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        var keyString = configuration["Jwt:SigningKey"] ?? DefaultSecretKey;
        _signingKey = Encoding.UTF8.GetBytes(keyString);
        _issuer = configuration["Jwt:Issuer"] ?? "https://nexusdesk.local";
        _audience = configuration["Jwt:Audience"] ?? "nexusdesk-api";
        _accessTokenLifetimeMinutes = int.TryParse(configuration["Jwt:AccessTokenLifetimeMinutes"], out var atm) ? atm : 60;
        _refreshTokenLifetimeDays = int.TryParse(configuration["Jwt:RefreshTokenLifetimeDays"], out var rtd) ? rtd : 30;
    }

    public TokenResponse GenerateTokenResponse(
        User user,
        string subdomain,
        string planTier,
        string? scope = null,
        string? existingRefreshToken = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_accessTokenLifetimeMinutes);
        var jti = Guid.NewGuid().ToString("D");

        var isSuperAdmin = string.Equals(user.Role, IdentityRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);
        var tenantIdClaim = isSuperAdmin && user.TenantId == Guid.Empty
            ? "platform"
            : user.TenantId.ToString("D");

        var effectiveSubdomain = isSuperAdmin && user.TenantId == Guid.Empty
            ? "platform"
            : (string.IsNullOrWhiteSpace(subdomain) ? "platform" : subdomain);

        var permissions = IdentityPermissions.GetPermissionsForRole(user.Role);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new("tenant_id", tenantIdClaim),
            new("subdomain", effectiveSubdomain),
            new(ClaimTypes.Role, user.Role),
            new("role", user.Role),
            new("plan_tier", planTier),
            new(JwtRegisteredClaimNames.Jti, jti),
            new("permissions", JsonSerializer.Serialize(permissions))
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var securityKey = new SymmetricSecurityKey(_signingKey);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        var accessTokenString = handler.WriteToken(token);

        var refreshTokenString = existingRefreshToken ?? CreateRefreshTokenString();

        var effectiveScope = string.IsNullOrWhiteSpace(scope) ? "openid profile email" : scope;

        return new TokenResponse(
            access_token: accessTokenString,
            token_type: "Bearer",
            expires_in: (int)(expiresAt - now).TotalSeconds,
            refresh_token: refreshTokenString,
            scope: effectiveScope);
    }

    public ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(_signingKey),
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.FromSeconds(5)
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    public string CreateRefreshTokenString()
    {
        var randomBytes = new byte[48];
        RandomNumberGenerator.Fill(randomBytes);
        return PkceHelper.Base64UrlEncode(randomBytes);
    }
}
