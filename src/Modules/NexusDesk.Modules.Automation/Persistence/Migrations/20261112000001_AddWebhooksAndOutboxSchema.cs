using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Automation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhooksAndOutboxSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automation");

            migrationBuilder.CreateTable(
                name: "webhook_subscriptions",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    encrypted_secret = table.Column<string>(type: "text", nullable: false),
                    event_types = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_retry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dead_letter_webhooks",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_http_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: false),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_webhooks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_subscriptions_tenant_id",
                schema: "automation",
                table: "webhook_subscriptions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_subscriptions_tenant_active",
                schema: "automation",
                table: "webhook_subscriptions",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_id",
                schema: "automation",
                table: "outbox_messages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_tenant_status_retry",
                schema: "automation",
                table: "outbox_messages",
                columns: new[] { "tenant_id", "status", "next_retry_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_retry",
                schema: "automation",
                table: "outbox_messages",
                columns: new[] { "status", "next_retry_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_webhooks_tenant_id",
                schema: "automation",
                table: "dead_letter_webhooks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_webhooks_outbox_message_id",
                schema: "automation",
                table: "dead_letter_webhooks",
                column: "outbox_message_id");

            migrationBuilder.Sql("""
                ALTER TABLE automation.webhook_subscriptions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.webhook_subscriptions FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON automation.webhook_subscriptions;
                CREATE POLICY tenant_isolation_policy ON automation.webhook_subscriptions
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE automation.outbox_messages ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.outbox_messages FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON automation.outbox_messages;
                CREATE POLICY tenant_isolation_policy ON automation.outbox_messages
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE automation.dead_letter_webhooks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.dead_letter_webhooks FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON automation.dead_letter_webhooks;
                CREATE POLICY tenant_isolation_policy ON automation.dead_letter_webhooks
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_webhooks",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "webhook_subscriptions",
                schema: "automation");
        }
    }
}
