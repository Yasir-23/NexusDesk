using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NexusDesk.Analyzers;
using NexusDesk.SharedKernel.MultiTenancy;
using NexusDesk.SharedKernel.Primitives;
using Xunit;

namespace NexusDesk.UnitTests.Analyzers;

public class RoslynAnalyzerDiagnosticTests
{
    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ITenantScoped).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(AllowCrossTenantQueryAttribute).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new IgnoreQueryFiltersTenantAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task IgnoreQueryFilters_OnTenantScopedEntity_WithoutAttribute_ShouldEmitND0001()
    {
        var code = """
            using System;
            using System.Linq;
            using NexusDesk.SharedKernel.Primitives;

            public class TenantEntity : ITenantScoped
            {
                public Guid TenantId { get; set; }
            }

            public static class QueryExtensions
            {
                public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
            }

            public class VulnerableService
            {
                public void LeakData(IQueryable<TenantEntity> query)
                {
                    var result = query.IgnoreQueryFilters().ToList();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(code);

        diagnostics.Should().ContainSingle(d => d.Id == "ND0001");
        var diag = diagnostics.Single(d => d.Id == "ND0001");
        diag.Severity.Should().Be(DiagnosticSeverity.Error);
        diag.GetMessage().Should().Contain("TenantEntity");
    }

    [Fact]
    public async Task IgnoreQueryFilters_OnTenantScopedEntity_WithAttribute_ShouldNotEmitDiagnostic()
    {
        var code = """
            using System;
            using System.Linq;
            using NexusDesk.SharedKernel.Primitives;
            using NexusDesk.SharedKernel.MultiTenancy;

            public class TenantEntity : ITenantScoped
            {
                public Guid TenantId { get; set; }
            }

            public static class QueryExtensions
            {
                public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
            }

            [AllowCrossTenantQuery(Reason = "Super admin platform wide billing aggregation")]
            public class PlatformAdminService
            {
                public void RunAggregation(IQueryable<TenantEntity> query)
                {
                    var result = query.IgnoreQueryFilters().ToList();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(code);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task IgnoreQueryFilters_OnNonTenantScopedEntity_ShouldNotEmitDiagnostic()
    {
        var code = """
            using System;
            using System.Linq;

            public class GlobalLookupEntity
            {
                public string Code { get; set; }
            }

            public static class QueryExtensions
            {
                public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
            }

            public class PublicService
            {
                public void LoadGlobalLookups(IQueryable<GlobalLookupEntity> query)
                {
                    var result = query.IgnoreQueryFilters().ToList();
                }
            }
            """;

        var diagnostics = await RunAnalyzerAsync(code);

        diagnostics.Should().BeEmpty();
    }
}
