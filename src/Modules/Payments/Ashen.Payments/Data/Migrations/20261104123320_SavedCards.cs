using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Payments.Data.Migrations
{
    /// <inheritdoc />
    public partial class SavedCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_cards",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway_token = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    brand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    exp_month = table.Column<int>(type: "integer", nullable: false),
                    exp_year = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_cards", x => x.id);
                    table.CheckConstraint("ck_saved_cards_exp_month", "exp_month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_cards_customer_id",
                schema: "payments",
                table: "saved_cards",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ux_saved_cards_one_default",
                schema: "payments",
                table: "saved_cards",
                column: "customer_id",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_cards",
                schema: "payments");
        }
    }
}
