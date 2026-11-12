using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Automation.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAutomationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automation");

            migrationBuilder.CreateTable(
                name: "sla_policies",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_response_target_minutes = table.Column<int>(type: "integer", nullable: false),
                    resolution_target_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sla_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "automation_rules",
                schema: "automation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    trigger_event = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    condition_json = table.Column<string>(type: "jsonb", nullable: false),
                    action_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    action_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_id",
                schema: "automation",
                table: "sla_policies",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sla_policies_tenant_priority",
                schema: "automation",
                table: "sla_policies",
                columns: new[] { "tenant_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_tenant_id",
                schema: "automation",
                table: "automation_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_automation_rules_tenant_trigger_enabled",
                schema: "automation",
                table: "automation_rules",
                columns: new[] { "tenant_id", "trigger_event", "is_enabled" });

            migrationBuilder.Sql("""
                ALTER TABLE automation.sla_policies ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.sla_policies FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON automation.sla_policies;
                CREATE POLICY tenant_isolation_policy ON automation.sla_policies
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE automation.automation_rules ENABLE ROW LEVEL SECURITY;
                ALTER TABLE automation.automation_rules FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON automation.automation_rules;
                CREATE POLICY tenant_isolation_policy ON automation.automation_rules
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_rules",
                schema: "automation");

            migrationBuilder.DropTable(
                name: "sla_policies",
                schema: "automation");
        }
    }
}
