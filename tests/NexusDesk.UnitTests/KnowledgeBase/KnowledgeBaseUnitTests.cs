using FluentAssertions;
using NexusDesk.Modules.KnowledgeBase.Domain;
using Xunit;

namespace NexusDesk.UnitTests.KnowledgeBase;

public class KnowledgeBaseUnitTests
{
    [Theory]
    [InlineData("How to Reset Your Password?", "how-to-reset-your-password")]
    [InlineData("  Billing & Invoices (2026)  ", "billing-invoices-2026")]
    [InlineData("Quick---Start---Guide!", "quick-start-guide")]
    [InlineData("multi   space   slug", "multi-space-slug")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void SlugHelper_ShouldNormalizeAndGenerateValidSlugs(string input, string? expectedPrefix)
    {
        var slug = SlugHelper.Generate(input);

        slug.Should().NotBeNullOrWhiteSpace();
        slug.Should().NotContain(" ");
        slug.Should().NotContain("--");
        slug.Should().Be(slug.ToLowerInvariant());

        if (expectedPrefix != null)
        {
            slug.Should().Be(expectedPrefix);
        }
        else
        {
            slug.Should().StartWith("article-");
        }
    }

    [Fact]
    public void KbCategory_CreateAndUpdate_ShouldEnforceInvariantsAndNormalizeSlug()
    {
        var tenantId = Guid.NewGuid();
        var category = new KbCategory(
            Guid.NewGuid(),
            tenantId,
            "Billing Support",
            "Billing & Invoices!",
            "Articles on invoices and plans",
            10,
            DateTimeOffset.UtcNow);

        category.TenantId.Should().Be(tenantId);
        category.Name.Should().Be("Billing Support");
        category.Slug.Should().Be("billing-invoices");
        category.Description.Should().Be("Articles on invoices and plans");
        category.DisplayOrder.Should().Be(10);

        category.Update("Invoicing", "invoicing-support", "New description", 5);
        category.Name.Should().Be("Invoicing");
        category.Slug.Should().Be("invoicing-support");
        category.DisplayOrder.Should().Be(5);

        // Validation failures
        var actNullName = () => new KbCategory(Guid.NewGuid(), tenantId, "", "slug", null, 0, DateTimeOffset.UtcNow);
        actNullName.Should().Throw<ArgumentException>();

        var actEmptyTenant = () => new KbCategory(Guid.NewGuid(), Guid.Empty, "Name", "slug", null, 0, DateTimeOffset.UtcNow);
        actEmptyTenant.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void KbArticle_LifecycleTransitions_DraftToPublishedToArchived()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var authorUserId = Guid.NewGuid();

        // 1. Create defaults to Draft
        var article = KbArticle.Create(
            tenantId,
            categoryId,
            authorUserId,
            "Configuring Webhooks",
            null, // auto slug
            "Summary of webhook configuration",
            "# Webhook Setup\nFollow these steps...",
            ["webhooks", "api", "integration"],
            publishImmediately: false);

        article.Status.Should().Be(KbArticleStatus.Draft);
        article.PublishedAtUtc.Should().BeNull();
        article.Slug.Should().Be("configuring-webhooks");
        article.Tags.Should().Contain(["webhooks", "api", "integration"]);
        article.ViewCount.Should().Be(0);
        article.HelpfulCount.Should().Be(0);
        article.NotHelpfulCount.Should().Be(0);

        // 2. Publish
        article.Publish();
        article.Status.Should().Be(KbArticleStatus.Published);
        article.PublishedAtUtc.Should().NotBeNull();
        article.PublishedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));

        // 3. Increment views and votes
        article.IncrementViewCount();
        article.IncrementViewCount();
        article.ViewCount.Should().Be(2);

        article.RecordFeedback(isHelpful: true);
        article.RecordFeedback(isHelpful: true);
        article.RecordFeedback(isHelpful: false);
        article.HelpfulCount.Should().Be(2);
        article.NotHelpfulCount.Should().Be(1);

        // 4. Archive
        article.Archive();
        article.Status.Should().Be(KbArticleStatus.Archived);
    }

    [Fact]
    public void KbArticle_PublishImmediately_SetsPublishedStateDirectly()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var authorUserId = Guid.NewGuid();

        var article = KbArticle.Create(
            tenantId,
            categoryId,
            authorUserId,
            "Immediate Publish Article",
            "custom-slug",
            "Summary",
            "Body markdown",
            ["faq"],
            publishImmediately: true);

        article.Status.Should().Be(KbArticleStatus.Published);
        article.PublishedAtUtc.Should().NotBeNull();
        article.Slug.Should().Be("custom-slug");
    }

    [Fact]
    public void KbArticle_Update_ModifiesFieldsAndNormalizesTags()
    {
        var tenantId = Guid.NewGuid();
        var categoryId1 = Guid.NewGuid();
        var categoryId2 = Guid.NewGuid();
        var authorUserId = Guid.NewGuid();

        var article = KbArticle.Create(
            tenantId,
            categoryId1,
            authorUserId,
            "Initial Title",
            "initial-slug",
            "Initial Summary",
            "Initial Body",
            ["Tag1", "TAG2"],
            publishImmediately: false);

        article.Tags.Should().Contain(["tag1", "tag2"]);

        article.Update(
            "Updated Title",
            "updated-slug",
            "Updated Summary",
            "Updated Body Markdown",
            categoryId2,
            ["NEWTAG", "tag1", "newtag"]);

        article.Title.Should().Be("Updated Title");
        article.Slug.Should().Be("updated-slug");
        article.Summary.Should().Be("Updated Summary");
        article.BodyMarkdown.Should().Be("Updated Body Markdown");
        article.CategoryId.Should().Be(categoryId2);
        article.Tags.Should().HaveCount(2); // distinct lowercased
        article.Tags.Should().Contain(["newtag", "tag1"]);
    }

    [Fact]
    public void DeflectionSearchRanking_ScoringSimulation_RanksExactTitleAndHelpfulArticlesHigher()
    {
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var article1 = KbArticle.Create(tenantId, categoryId, authorId, "Resetting Your Password", null, "How to reset password", "Password body", ["password", "auth"], publishImmediately: true);
        var article2 = KbArticle.Create(tenantId, categoryId, authorId, "Account Security Overview", null, "Security guide", "Mentions password recovery options", ["security"], publishImmediately: true);
        var draftArticle = KbArticle.Create(tenantId, categoryId, authorId, "Password Secrets Draft", null, "Secret", "Password body", ["password"], publishImmediately: false);

        // Simulated relevance ranking scoring rules matching the PostgreSQL query:
        // Title ILIKE match: +5.0
        // Body ILIKE match: +1.0
        // Tag match: +4.0
        // Helpful count boost: helpful * 0.05
        article1.RecordFeedback(true);
        article1.RecordFeedback(true); // +0.10

        double ScoreArticle(KbArticle a, string query)
        {
            if (a.Status != KbArticleStatus.Published) return -1; // Drafts/Archived excluded

            double score = 0;
            if (a.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 5.0;
            if (a.BodyMarkdown.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 1.0;
            if (a.Tags.Any(t => t.Equals(query, StringComparison.OrdinalIgnoreCase))) score += 4.0;
            score += Math.Min(a.HelpfulCount, 100) * 0.05;
            return score;
        }

        var score1 = ScoreArticle(article1, "password");
        var score2 = ScoreArticle(article2, "password");
        var scoreDraft = ScoreArticle(draftArticle, "password");

        scoreDraft.Should().Be(-1, "Draft articles must never qualify for deflection search");
        score1.Should().BeGreaterThan(score2, "Article with title and tag match plus helpful feedback must outrank body-only match");
    }
}
