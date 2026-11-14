using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NexusDesk.Modules.Identity.Endpoints;
using NexusDesk.Modules.KnowledgeBase.Contracts;
using NexusDesk.Modules.Tenants.Endpoints;
using Xunit;

namespace NexusDesk.IntegrationTests.KnowledgeBase;

[Collection("DatabaseIntegrationCollection")]
public class KnowledgeBaseIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string PostgresConnectionString = "Host=127.0.0.1;Port=56132;Database=nexusdesk_shared;Username=postgres;Password=postgres;SSL Mode=Disable";
    private const string RedisConnectionString = "127.0.0.1:56179,abortConnect=false";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public KnowledgeBaseIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:PostgreSql", PostgresConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
            builder.UseSetting("SignalR:UseRedisBackplane", "false");
        });
    }

    private async Task EnsureTenantsActiveAsync()
    {
        var rootClient = _factory.CreateClient();
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Acme Corporation", "acme", "admin@acme.com", "Acme Admin", "Pro"));
        await rootClient.PostAsJsonAsync("/api/v1/tenants/provision",
            new ProvisionTenantRequest("Beta Innovations", "beta", "admin@beta.com", "Beta Admin", "Free"));

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
                        new UpdateTenantStatusRequest("Active", "Ensure active for KB integration tests"));
                }
            }
        }
    }

    private async Task<(HttpClient Client, Guid UserId, string Token)> LoginAsAsync(string subdomain, string email, string password = "Password123!")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Login for '{email}' in '{subdomain}' must succeed.");

        var doc = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = doc.GetProperty("accessToken").GetString()!;
        var userId = Guid.Parse(doc.GetProperty("user").GetProperty("id").GetString()!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId, token);
    }

    private async Task<(HttpClient Client, Guid UserId, string Token)> RegisterAndLoginCustomerAsync(string subdomain)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        var email = $"kb-cust-{Guid.NewGuid():N}@test.local";
        var regRes = await client.PostAsJsonAsync("/api/v1/users/customers/register",
            new RegisterCustomerRequest(email, "KB Customer", "Password123!"));
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);

        return await LoginAsAsync(subdomain, email);
    }

    [Fact]
    public async Task MultiTenantIsolation_AcmeAndBeta_MustNeverLeakCategoriesOrArticles()
    {
        await EnsureTenantsActiveAsync();

        var (acmeAdmin, _, _) = await LoginAsAsync("acme", "admin@acme.com");
        var (betaAdmin, _, _) = await LoginAsAsync("beta", "admin@beta.com");

        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 1. Create category in Acme
        var acmeCatSlug = $"acme-category-{suffix}";
        var acmeCatRes = await acmeAdmin.PostAsJsonAsync("/api/v1/kb/categories",
            new CreateCategoryRequest($"Acme Cat {suffix}", acmeCatSlug, "Acme Description", 1));
        acmeCatRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var acmeCategory = await acmeCatRes.Content.ReadFromJsonAsync<KbCategoryDto>(JsonOptions);
        acmeCategory.Should().NotBeNull();

        // 2. Create category in Beta
        var betaCatSlug = $"beta-category-{suffix}";
        var betaCatRes = await betaAdmin.PostAsJsonAsync("/api/v1/kb/categories",
            new CreateCategoryRequest($"Beta Cat {suffix}", betaCatSlug, "Beta Description", 1));
        betaCatRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var betaCategory = await betaCatRes.Content.ReadFromJsonAsync<KbCategoryDto>(JsonOptions);
        betaCategory.Should().NotBeNull();

        // 3. Create published article in Acme
        var acmeArtRes = await acmeAdmin.PostAsJsonAsync("/api/v1/kb/articles",
            new CreateArticleRequest(
                acmeCategory!.Id,
                $"Acme Secret Guide {suffix}",
                $"acme-guide-{suffix}",
                "Acme only guide",
                "Detailed instructions for Acme confidential workflows.",
                ["acme", "confidential"],
                PublishImmediately: true));
        acmeArtRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var acmeArticle = await acmeArtRes.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        acmeArticle.Should().NotBeNull();

        // 4. Create published article in Beta
        var betaArtRes = await betaAdmin.PostAsJsonAsync("/api/v1/kb/articles",
            new CreateArticleRequest(
                betaCategory!.Id,
                $"Beta Secret Guide {suffix}",
                $"beta-guide-{suffix}",
                "Beta only guide",
                "Detailed instructions for Beta confidential workflows.",
                ["beta", "confidential"],
                PublishImmediately: true));
        betaArtRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var betaArticle = await betaArtRes.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        betaArticle.Should().NotBeNull();

        // 5. Verify Categories Isolation
        var acmeCatsRes = await acmeAdmin.GetAsync("/api/v1/kb/categories");
        acmeCatsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeCats = await acmeCatsRes.Content.ReadFromJsonAsync<List<KbCategoryDto>>(JsonOptions);
        acmeCats.Should().Contain(c => c.Id == acmeCategory.Id);
        acmeCats.Should().NotContain(c => c.Id == betaCategory.Id, "Beta category must not leak to Acme");

        var betaCatsRes = await betaAdmin.GetAsync("/api/v1/kb/categories");
        betaCatsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaCats = await betaCatsRes.Content.ReadFromJsonAsync<List<KbCategoryDto>>(JsonOptions);
        betaCats.Should().Contain(c => c.Id == betaCategory.Id);
        betaCats.Should().NotContain(c => c.Id == acmeCategory.Id, "Acme category must not leak to Beta");

        // 6. Verify Articles List Isolation
        var acmeArticlesRes = await acmeAdmin.GetAsync("/api/v1/kb/articles");
        acmeArticlesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeArticles = await acmeArticlesRes.Content.ReadFromJsonAsync<PaginatedArticlesResponse>(JsonOptions);
        acmeArticles!.Items.Should().Contain(a => a.Id == acmeArticle!.Id);
        acmeArticles.Items.Should().NotContain(a => a.Id == betaArticle!.Id, "Beta article must not leak to Acme list");

        var betaArticlesRes = await betaAdmin.GetAsync("/api/v1/kb/articles");
        betaArticlesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var betaArticles = await betaArticlesRes.Content.ReadFromJsonAsync<PaginatedArticlesResponse>(JsonOptions);
        betaArticles!.Items.Should().Contain(a => a.Id == betaArticle!.Id);
        betaArticles.Items.Should().NotContain(a => a.Id == acmeArticle!.Id, "Acme article must not leak to Beta list");

        // 7. Verify Cross-Tenant Direct Article Lookup Returns 404
        var crossLookupRes = await acmeAdmin.GetAsync($"/api/v1/kb/articles/{betaArticle!.Id}");
        crossLookupRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Cross-tenant direct lookup must return 404");

        // 8. Verify Search Isolation
        var acmeSearchRes = await acmeAdmin.GetAsync($"/api/v1/kb/search?q={suffix}");
        acmeSearchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var acmeSearchResults = await acmeSearchRes.Content.ReadFromJsonAsync<List<DeflectionSearchResultDto>>(JsonOptions);
        acmeSearchResults.Should().Contain(r => r.Id == acmeArticle!.Id);
        acmeSearchResults.Should().NotContain(r => r.Id == betaArticle!.Id, "Search results must not leak cross-tenant articles");
    }

    [Fact]
    public async Task ArticleVisibilityAndDeflectionLifecycle_CustomerCannotSeeDrafts_PublishEnablesSearchAndFeedback()
    {
        await EnsureTenantsActiveAsync();

        var (adminClient, _, _) = await LoginAsAsync("acme", "admin@acme.com");
        var (customerClient, _, _) = await RegisterAndLoginCustomerAsync("acme");

        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 1. Create Category
        var catRes = await adminClient.PostAsJsonAsync("/api/v1/kb/categories",
            new CreateCategoryRequest($"Deflection Testing {suffix}", $"deflection-{suffix}", "Testing deflection flow", 1));
        catRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = await catRes.Content.ReadFromJsonAsync<KbCategoryDto>(JsonOptions);

        // 2. Admin creates article as Draft
        var articleSlug = $"reset-password-{suffix}";
        var artRes = await adminClient.PostAsJsonAsync("/api/v1/kb/articles",
            new CreateArticleRequest(
                category!.Id,
                $"How to Reset Your Account Password {suffix}",
                articleSlug,
                "Step by step guide on self-service password reset.",
                "Navigate to security settings and click reset your password with your verified email address.",
                ["password", "security", "login"],
                PublishImmediately: false));

        artRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var article = await artRes.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        article.Should().NotBeNull();
        article!.Status.Should().Be("Draft");
        article.ViewCount.Should().Be(0);

        // 3. Admin CAN see draft by direct ID and in list
        var adminGetRes = await adminClient.GetAsync($"/api/v1/kb/articles/{article.Id}");
        adminGetRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var adminListRes = await adminClient.GetAsync($"/api/v1/kb/articles?status=Draft");
        adminListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminArticles = await adminListRes.Content.ReadFromJsonAsync<PaginatedArticlesResponse>(JsonOptions);
        adminArticles!.Items.Should().Contain(a => a.Id == article.Id);

        // 4. SECURITY CHECK: Customer MUST NOT see Draft article via list, direct ID, slug, or deflection search
        var custListRes = await customerClient.GetAsync($"/api/v1/kb/articles");
        custListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var custArticles = await custListRes.Content.ReadFromJsonAsync<PaginatedArticlesResponse>(JsonOptions);
        custArticles!.Items.Should().NotContain(a => a.Id == article.Id, "Customer must never see Draft articles in list");

        var custByIdRes = await customerClient.GetAsync($"/api/v1/kb/articles/{article.Id}");
        custByIdRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Draft article direct ID lookup by customer must return 404");

        var custBySlugRes = await customerClient.GetAsync($"/api/v1/kb/articles/{articleSlug}");
        custBySlugRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Draft article slug lookup by customer must return 404");

        var custSearchRes = await customerClient.GetAsync($"/api/v1/kb/search?q=password+{suffix}");
        custSearchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var custSearchResults = await custSearchRes.Content.ReadFromJsonAsync<List<DeflectionSearchResultDto>>(JsonOptions);
        custSearchResults.Should().NotContain(r => r.Id == article.Id, "Draft article must never appear in deflection search");

        // 5. Customer cannot submit feedback to non-published article
        var draftFeedbackRes = await customerClient.PostAsJsonAsync($"/api/v1/kb/articles/{article.Id}/feedback",
            new ArticleFeedbackRequest(IsHelpful: true));
        draftFeedbackRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 6. Admin Publishes the article
        var publishRes = await adminClient.PostAsync($"/api/v1/kb/articles/{article.Id}/publish", null);
        publishRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var publishedArticle = await publishRes.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        publishedArticle!.Status.Should().Be("Published");
        publishedArticle.PublishedAtUtc.Should().NotBeNull();

        // 7. Now Customer CAN find article via Deflection Search
        var searchPublishedRes = await customerClient.GetAsync($"/api/v1/kb/search?q=password+{suffix}");
        searchPublishedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchPublishedResults = await searchPublishedRes.Content.ReadFromJsonAsync<List<DeflectionSearchResultDto>>(JsonOptions);
        searchPublishedResults.Should().Contain(r => r.Id == article.Id, "Published article must appear in deflection search");
        var match = searchPublishedResults!.First(r => r.Id == article.Id);
        match.Title.Should().Contain("Password");
        match.CategoryName.Should().Be(category.Name);
        match.RelevanceScore.Should().BeGreaterThan(0);

        // 8. Customer reads article via Slug -> ViewCount increments
        var readRes1 = await customerClient.GetAsync($"/api/v1/kb/articles/{articleSlug}");
        readRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var readArticle1 = await readRes1.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        readArticle1!.ViewCount.Should().Be(1, "Customer read must increment ViewCount to 1");

        var readRes2 = await customerClient.GetAsync($"/api/v1/kb/articles/{article.Id}");
        readRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var readArticle2 = await readRes2.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        readArticle2!.ViewCount.Should().Be(2, "Second customer read must increment ViewCount to 2");

        // 9. Helpfulness Voting: Helpful & NotHelpful feedback
        var feedbackHelpfulRes = await customerClient.PostAsJsonAsync($"/api/v1/kb/articles/{article.Id}/feedback",
            new ArticleFeedbackRequest(IsHelpful: true));
        feedbackHelpfulRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fb1 = await feedbackHelpfulRes.Content.ReadFromJsonAsync<ArticleFeedbackResponse>(JsonOptions);
        fb1!.HelpfulCount.Should().Be(1);
        fb1.NotHelpfulCount.Should().Be(0);

        var feedbackNotHelpfulRes = await customerClient.PostAsJsonAsync($"/api/v1/kb/articles/{article.Id}/feedback",
            new ArticleFeedbackRequest(IsHelpful: false));
        feedbackNotHelpfulRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fb2 = await feedbackNotHelpfulRes.Content.ReadFromJsonAsync<ArticleFeedbackResponse>(JsonOptions);
        fb2!.HelpfulCount.Should().Be(1);
        fb2.NotHelpfulCount.Should().Be(1);

        // 10. Admin Archives the Article -> Immediately invisible to Customer
        var archiveRes = await adminClient.PostAsync($"/api/v1/kb/articles/{article.Id}/archive", null);
        archiveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var archivedArticle = await archiveRes.Content.ReadFromJsonAsync<KbArticleDto>(JsonOptions);
        archivedArticle!.Status.Should().Be("Archived");

        var custPostArchiveRes = await customerClient.GetAsync($"/api/v1/kb/articles/{article.Id}");
        custPostArchiveRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Archived article must return 404 to customer");

        var custPostArchiveSearch = await customerClient.GetAsync($"/api/v1/kb/search?q=password+{suffix}");
        custPostArchiveSearch.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchPostArchive = await custPostArchiveSearch.Content.ReadFromJsonAsync<List<DeflectionSearchResultDto>>(JsonOptions);
        searchPostArchive.Should().NotContain(r => r.Id == article.Id, "Archived article must never appear in search");
    }
}
