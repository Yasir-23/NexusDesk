using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Billing.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialBillingSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "billing");

        migrationBuilder.CreateTable(
            name: "subscription_plans",
            schema: "billing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                monthly_price_cents = table.Column<int>(type: "integer", nullable: false),
                max_agents = table.Column<int>(type: "integer", nullable: false),
                max_monthly_tickets = table.Column<int>(type: "integer", nullable: false),
                max_monthly_ai_tokens = table.Column<long>(type: "bigint", nullable: false),
                custom_branding_enabled = table.Column<bool>(type: "boolean", nullable: false),
                webhooks_enabled = table.Column<bool>(type: "boolean", nullable: false),
                sla_enabled = table.Column<bool>(type: "boolean", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscription_plans", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "tenant_subscriptions",
            schema: "billing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                billing_cycle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                stripe_customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                stripe_subscription_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                current_period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                current_period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_subscriptions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "usage_meter_records",
            schema: "billing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                metric_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                period_key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                quantity = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_usage_meter_records", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "billing_invoices",
            schema: "billing",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                stripe_invoice_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                plan_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                amount_cents = table.Column<int>(type: "integer", nullable: false),
                currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "usd"),
                status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                paid_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                hosted_invoice_url = table.Column<string>(type: "text", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_invoices", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "processed_stripe_events",
            schema: "billing",
            columns: table => new
            {
                event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_processed_stripe_events", x => x.event_id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_subscription_plans_code",
            schema: "billing",
            table: "subscription_plans",
            column: "code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tenant_subscriptions_tenant_id",
            schema: "billing",
            table: "tenant_subscriptions",
            column: "tenant_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_tenant_subscriptions_stripe_customer_id",
            schema: "billing",
            table: "tenant_subscriptions",
            column: "stripe_customer_id");

        migrationBuilder.CreateIndex(
            name: "ix_tenant_subscriptions_stripe_sub_id",
            schema: "billing",
            table: "tenant_subscriptions",
            column: "stripe_subscription_id");

        migrationBuilder.CreateIndex(
            name: "ix_usage_meter_records_tenant_id",
            schema: "billing",
            table: "usage_meter_records",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_usage_meter_records_tenant_metric_period",
            schema: "billing",
            table: "usage_meter_records",
            columns: new[] { "tenant_id", "metric_name", "period_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_billing_invoices_tenant_id",
            schema: "billing",
            table: "billing_invoices",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_billing_invoices_invoice_number",
            schema: "billing",
            table: "billing_invoices",
            column: "invoice_number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_billing_invoices_stripe_invoice_id",
            schema: "billing",
            table: "billing_invoices",
            column: "stripe_invoice_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "processed_stripe_events",
            schema: "billing");

        migrationBuilder.DropTable(
            name: "billing_invoices",
            schema: "billing");

        migrationBuilder.DropTable(
            name: "usage_meter_records",
            schema: "billing");

        migrationBuilder.DropTable(
            name: "tenant_subscriptions",
            schema: "billing");

        migrationBuilder.DropTable(
            name: "subscription_plans",
            schema: "billing");
    }
}
