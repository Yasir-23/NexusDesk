using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.KnowledgeBase.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialKnowledgeBaseSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "kb");

        migrationBuilder.CreateTable(
            name: "kb_categories",
            schema: "kb",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kb_categories", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "kb_articles",
            schema: "kb",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                category_id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                body_markdown = table.Column<string>(type: "text", nullable: false),
                tags = table.Column<string[]>(type: "text[]", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                helpful_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                not_helpful_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kb_articles", x => x.id);
                table.ForeignKey(
                    name: "FK_kb_articles_kb_categories_category_id",
                    column: x => x.category_id,
                    principalSchema: "kb",
                    principalTable: "kb_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_kb_categories_tenant_id",
            schema: "kb",
            table: "kb_categories",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_kb_categories_tenant_slug",
            schema: "kb",
            table: "kb_categories",
            columns: new[] { "tenant_id", "slug" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_kb_categories_tenant_order_name",
            schema: "kb",
            table: "kb_categories",
            columns: new[] { "tenant_id", "display_order", "name" });

        migrationBuilder.CreateIndex(
            name: "ix_kb_articles_tenant_id",
            schema: "kb",
            table: "kb_articles",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_kb_articles_tenant_slug",
            schema: "kb",
            table: "kb_articles",
            columns: new[] { "tenant_id", "slug" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_kb_articles_tenant_category",
            schema: "kb",
            table: "kb_articles",
            columns: new[] { "tenant_id", "category_id" });

        migrationBuilder.CreateIndex(
            name: "ix_kb_articles_tenant_status",
            schema: "kb",
            table: "kb_articles",
            columns: new[] { "tenant_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_kb_articles_tenant_status_published",
            schema: "kb",
            table: "kb_articles",
            columns: new[] { "tenant_id", "status", "published_at_utc" });

        migrationBuilder.Sql("""
            ALTER TABLE kb.kb_categories ENABLE ROW LEVEL SECURITY;
            ALTER TABLE kb.kb_categories FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON kb.kb_categories;
            CREATE POLICY tenant_isolation_policy ON kb.kb_categories
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

            ALTER TABLE kb.kb_articles ENABLE ROW LEVEL SECURITY;
            ALTER TABLE kb.kb_articles FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON kb.kb_articles;
            CREATE POLICY tenant_isolation_policy ON kb.kb_articles
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

            CREATE INDEX IF NOT EXISTS ix_kb_articles_fts ON kb.kb_articles
                USING gin (to_tsvector('english', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(body_markdown, '')));
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "kb_articles",
            schema: "kb");

        migrationBuilder.DropTable(
            name: "kb_categories",
            schema: "kb");
    }
}
