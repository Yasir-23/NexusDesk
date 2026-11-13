using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Chat.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialChatSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "chat");

        migrationBuilder.CreateTable(
            name: "chat_sessions",
            schema: "chat",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                assigned_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                converted_ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_sessions", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "chat_messages",
            schema: "chat",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                chat_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_chat_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_chat_messages_chat_sessions_chat_session_id",
                    column: x => x.chat_session_id,
                    principalSchema: "chat",
                    principalTable: "chat_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_chat_sessions_tenant_id",
            schema: "chat",
            table: "chat_sessions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_chat_sessions_tenant_customer",
            schema: "chat",
            table: "chat_sessions",
            columns: new[] { "tenant_id", "customer_id" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_sessions_tenant_status",
            schema: "chat",
            table: "chat_sessions",
            columns: new[] { "tenant_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_tenant_id",
            schema: "chat",
            table: "chat_messages",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "ix_chat_messages_tenant_session",
            schema: "chat",
            table: "chat_messages",
            columns: new[] { "tenant_id", "chat_session_id" });

        migrationBuilder.Sql("""
            ALTER TABLE chat.chat_sessions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE chat.chat_sessions FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON chat.chat_sessions;
            CREATE POLICY tenant_isolation_policy ON chat.chat_sessions
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

            ALTER TABLE chat.chat_messages ENABLE ROW LEVEL SECURITY;
            ALTER TABLE chat.chat_messages FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation_policy ON chat.chat_messages;
            CREATE POLICY tenant_isolation_policy ON chat.chat_messages
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "chat_messages",
            schema: "chat");

        migrationBuilder.DropTable(
            name: "chat_sessions",
            schema: "chat");
    }
}
