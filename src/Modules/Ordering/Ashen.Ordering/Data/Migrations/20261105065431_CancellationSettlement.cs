using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Ordering.Data.Migrations
{
    /// <inheritdoc />
    public partial class CancellationSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "refund_pending",
                schema: "ordering",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "restock_pending",
                schema: "ordering",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "refund_pending",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "restock_pending",
                schema: "ordering",
                table: "orders");
        }
    }
}
