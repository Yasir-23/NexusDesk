using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDesk.Modules.Ticketing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketSlaBreachTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_sla_breached",
                schema: "ticketing",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_breached_at_utc",
                schema: "ticketing",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_sla_breached",
                schema: "ticketing",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_breached_at_utc",
                schema: "ticketing",
                table: "tickets");
        }
    }
}
