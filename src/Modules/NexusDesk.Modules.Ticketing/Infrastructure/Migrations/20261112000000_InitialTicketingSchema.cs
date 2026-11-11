using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Ticketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialTicketingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticketing");

            migrationBuilder.CreateTable(
                name: "tickets",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "text", nullable: true),
                    first_response_due_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_due_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_responded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_activity_logs",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    old_value = table.Column<string>(type: "text", nullable: true),
                    new_value = table.Column<string>(type: "text", nullable: true),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_activity_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_activity_logs_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "ticketing",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_customer = table.Column<bool>(type: "boolean", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_comments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "ticketing",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_tags",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_tags", x => x.id);
                    table.ForeignKey(
                        name: "FK_ticket_tags_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "ticketing",
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_activity_logs_tenant_ticket",
                schema: "ticketing",
                table: "ticket_activity_logs",
                columns: new[] { "tenant_id", "ticket_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_activity_logs_ticket_id",
                schema: "ticketing",
                table: "ticket_activity_logs",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_tenant_ticket",
                schema: "ticketing",
                table: "ticket_comments",
                columns: new[] { "tenant_id", "ticket_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_ticket_id",
                schema: "ticketing",
                table: "ticket_comments",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_tags_tenant_ticket",
                schema: "ticketing",
                table: "ticket_tags",
                columns: new[] { "tenant_id", "ticket_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_tags_ticket_name",
                schema: "ticketing",
                table: "ticket_tags",
                columns: new[] { "ticket_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_assigned_agent",
                schema: "ticketing",
                table: "tickets",
                columns: new[] { "tenant_id", "assigned_agent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_created_at",
                schema: "ticketing",
                table: "tickets",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_customer",
                schema: "ticketing",
                table: "tickets",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_status",
                schema: "ticketing",
                table: "tickets",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_tenant_ticket_number",
                schema: "ticketing",
                table: "tickets",
                columns: new[] { "tenant_id", "ticket_number" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS ticketing.ticket_sequences (
                    tenant_id uuid PRIMARY KEY,
                    last_number bigint NOT NULL DEFAULT 1000
                );

                ALTER TABLE ticketing.tickets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ticketing.tickets FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON ticketing.tickets;
                CREATE POLICY tenant_isolation_policy ON ticketing.tickets
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE ticketing.ticket_comments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ticketing.ticket_comments FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON ticketing.ticket_comments;
                CREATE POLICY tenant_isolation_policy ON ticketing.ticket_comments
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE ticketing.ticket_activity_logs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ticketing.ticket_activity_logs FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON ticketing.ticket_activity_logs;
                CREATE POLICY tenant_isolation_policy ON ticketing.ticket_activity_logs
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE ticketing.ticket_tags ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ticketing.ticket_tags FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON ticketing.ticket_tags;
                CREATE POLICY tenant_isolation_policy ON ticketing.ticket_tags
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                ALTER TABLE ticketing.ticket_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ticketing.ticket_sequences FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation_policy ON ticketing.ticket_sequences;
                CREATE POLICY tenant_isolation_policy ON ticketing.ticket_sequences
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS ticketing.ticket_sequences;");

            migrationBuilder.DropTable(
                name: "ticket_activity_logs",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "ticket_comments",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "ticket_tags",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "tickets",
                schema: "ticketing");
        }
    }
}
