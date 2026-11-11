using FluentAssertions;
using NexusDesk.Modules.Tenants.Domain;
using Xunit;

namespace NexusDesk.UnitTests.MultiTenancy;

public class SubdomainValidationTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("beta")]
    [InlineData("globex")]
    [InlineData("tenant-123")]
    [InlineData("my-cool-company")]
    [InlineData("ab1")]
    public void IsValid_ShouldAccept_ValidSubdomains(string subdomain)
    {
        var isValid = SubdomainValidator.IsValid(subdomain, out var error);

        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_ShouldReject_EmptySubdomain(string? subdomain)
    {
        var isValid = SubdomainValidator.IsValid(subdomain, out var error);

        isValid.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public void IsValid_ShouldReject_TooShortSubdomain(string subdomain)
    {
        var isValid = SubdomainValidator.IsValid(subdomain, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("between 3 and 63");
    }

    [Fact]
    public void IsValid_ShouldReject_TooLongSubdomain()
    {
        var longSubdomain = new string('a', 64);
        var isValid = SubdomainValidator.IsValid(longSubdomain, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("between 3 and 63");
    }

    [Theory]
    [InlineData("acme_corp")]
    [InlineData("acme.corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme!")]
    public void IsValid_ShouldReject_InvalidCharactersOrFormatting(string subdomain)
    {
        var isValid = SubdomainValidator.IsValid(subdomain, out var error);

        isValid.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("system")]
    [InlineData("app")]
    [InlineData("auth")]
    [InlineData("status")]
    [InlineData("www")]
    [InlineData("mail")]
    [InlineData("support")]
    [InlineData("billing")]
    [InlineData("docs")]
    [InlineData("cdn")]
    [InlineData("ADMIN")]
    [InlineData("ApI")]
    public void IsValid_ShouldReject_ReservedKeywords(string subdomain)
    {
        var isValid = SubdomainValidator.IsValid(subdomain, out var error);

        isValid.Should().BeFalse();
        error.Should().Contain("reserved");
    }
}
