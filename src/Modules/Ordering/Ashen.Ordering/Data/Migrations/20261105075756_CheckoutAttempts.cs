using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Ordering.Data.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checkout_attempts",
                schema: "ordering",
                columns: table => new
                {
                    checkout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_attempts", x => x.checkout_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkout_attempts",
                schema: "ordering");
        }
    }
}
