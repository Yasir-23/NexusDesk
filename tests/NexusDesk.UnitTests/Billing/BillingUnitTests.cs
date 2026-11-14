using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NexusDesk.Modules.Billing.Domain;
using NexusDesk.Modules.Billing.Services;
using Xunit;

namespace NexusDesk.UnitTests.Billing;

public class BillingUnitTests
{
    private static readonly SubscriptionPlan FreePlan = new(
        Guid.NewGuid(), "Free", "Free Plan", 0, 2, 100, 50_000L, false, false, false, true);

    private static readonly SubscriptionPlan StarterPlan = new(
        Guid.NewGuid(), "Starter", "Starter Plan", 2900, 5, 500, 250_000L, false, true, false, true);

    private static readonly SubscriptionPlan ProPlan = new(
        Guid.NewGuid(), "Pro", "Pro Plan", 9900, 15, 2500, 1_000_000L, true, true, true, true);

    private static readonly SubscriptionPlan EnterprisePlan = new(
        Guid.NewGuid(), "Enterprise", "Enterprise Plan", 49900, 1000, 50_000, 50_000_000L, true, true, true, true);

    [Fact]
    public void MetricLimits_ShouldMatchPlanSpecifications()
    {
        BillingQuotaService.GetMetricLimit(FreePlan, "tickets_created").Should().Be(100);
        BillingQuotaService.GetMetricLimit(FreePlan, "active_agents").Should().Be(2);
        BillingQuotaService.GetMetricLimit(FreePlan, "ai_tokens_used").Should().Be(50_000L);
        BillingQuotaService.GetMetricLimit(FreePlan, "webhook_deliveries").Should().Be(long.MaxValue);

        BillingQuotaService.GetMetricLimit(StarterPlan, "tickets_created").Should().Be(500);
        BillingQuotaService.GetMetricLimit(StarterPlan, "active_agents").Should().Be(5);
        BillingQuotaService.GetMetricLimit(StarterPlan, "ai_tokens_used").Should().Be(250_000L);

        BillingQuotaService.GetMetricLimit(ProPlan, "tickets_created").Should().Be(2500);
        BillingQuotaService.GetMetricLimit(ProPlan, "active_agents").Should().Be(15);
        BillingQuotaService.GetMetricLimit(ProPlan, "ai_tokens_used").Should().Be(1_000_000L);

        BillingQuotaService.GetMetricLimit(EnterprisePlan, "tickets_created").Should().Be(50_000);
        BillingQuotaService.GetMetricLimit(EnterprisePlan, "active_agents").Should().Be(1000);
        BillingQuotaService.GetMetricLimit(EnterprisePlan, "ai_tokens_used").Should().Be(50_000_000L);
    }

    [Fact]
    public void DowngradeValidation_ShouldBlockWhenCurrentUsageExceedsTargetPlan()
    {
        var tenantId = Guid.NewGuid();
        var usage = new List<UsageMeterRecord>
        {
            new(Guid.NewGuid(), tenantId, "active_agents", "2026-11", quantity: 8),
            new(Guid.NewGuid(), tenantId, "tickets_created", "2026-11", quantity: 150)
        };

        // Downgrading to Free (limit 2 agents, 100 tickets) must fail
        var error = BillingQuotaService.ValidateDowngrade(FreePlan, usage);
        error.Should().NotBeNull();
        error.Should().Contain("active_agents");

        // Downgrading to Starter (limit 5 agents) must also fail on agents (8 > 5)
        var starterError = BillingQuotaService.ValidateDowngrade(StarterPlan, usage);
        starterError.Should().NotBeNull();
        starterError.Should().Contain("active_agents");

        // Downgrading to Pro (limit 15 agents, 2500 tickets) must succeed (8 <= 15, 150 <= 2500)
        var proError = BillingQuotaService.ValidateDowngrade(ProPlan, usage);
        proError.Should().BeNull();
    }

    [Fact]
    public void StripeSignatureValidator_ValidSignature_ShouldReturnTrue()
    {
        var validator = new StripeSignatureValidator();
        var secret = "whsec_test_secret_123456";
        var payload = "{\"id\":\"evt_test123\",\"type\":\"invoice.paid\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var signature = Convert.ToHexStringLower(hash);

        var header = $"t={timestamp},v1={signature}";
        var isValid = validator.ValidateSignature(header, payload, secret);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void StripeSignatureValidator_TamperedPayload_ShouldReturnFalse()
    {
        var validator = new StripeSignatureValidator();
        var secret = "whsec_test_secret_123456";
        var originalPayload = "{\"id\":\"evt_test123\",\"type\":\"invoice.paid\"}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{originalPayload}"));
        var signature = Convert.ToHexStringLower(hash);

        var header = $"t={timestamp},v1={signature}";
        var isValid = validator.ValidateSignature(header, originalPayload + "tampered", secret);

        isValid.Should().BeFalse();
    }

    [Fact]
    public void StripeSignatureValidator_InvalidSecret_ShouldReturnFalse()
    {
        var validator = new StripeSignatureValidator();
        var correctSecret = "whsec_correct_secret";
        var wrongSecret = "whsec_wrong_secret";
        var payload = "{\"id\":\"evt_test123\"}";
        var timestamp = "1672531199";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(correctSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var signature = Convert.ToHexStringLower(hash);

        var header = $"t={timestamp},v1={signature}";
        var isValid = validator.ValidateSignature(header, payload, wrongSecret);

        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_header")]
    [InlineData("t=123456")]
    [InlineData("v1=abcdef")]
    public void StripeSignatureValidator_MalformedHeaders_ShouldReturnFalse(string? malformedHeader)
    {
        var validator = new StripeSignatureValidator();
        var isValid = validator.ValidateSignature(malformedHeader, "{}", "whsec_secret");
        isValid.Should().BeFalse();
    }

    [Fact]
    public void StripeSignatureValidator_MultipleV1Signatures_ShouldReturnTrueIfOneMatches()
    {
        var validator = new StripeSignatureValidator();
        var secret = "whsec_multi_test";
        var payload = "{\"data\":\"test\"}";
        var timestamp = "1672531200";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        var validSignature = Convert.ToHexStringLower(hash);

        var header = $"t={timestamp},v1=0000000000000000000000000000000000000000000000000000000000000000,v1={validSignature}";
        var isValid = validator.ValidateSignature(header, payload, secret);

        isValid.Should().BeTrue();
    }
}
