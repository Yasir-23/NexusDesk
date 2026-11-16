using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Ticketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS ticketing.ticket_attachments (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    ticket_id uuid NOT NULL,
                    uploaded_by_user_id uuid NOT NULL,
                    file_name character varying(255) NOT NULL,
                    content_type character varying(100) NOT NULL,
                    size_bytes bigint NOT NULL,
                    storage_path character varying(500) NOT NULL,
                    created_at_utc timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_ticket_attachments" PRIMARY KEY (id),
                    CONSTRAINT "FK_ticket_attachments_tickets_ticket_id" FOREIGN KEY (ticket_id) REFERENCES ticketing.tickets (id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_ticket_attachments_tenant_ticket ON ticketing.ticket_attachments (tenant_id, ticket_id);
                CREATE INDEX IF NOT EXISTS ix_ticket_attachments_ticket_id ON ticketing.ticket_attachments (ticket_id);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_attachments",
                schema: "ticketing");
        }
    }
}
