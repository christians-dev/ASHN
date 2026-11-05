using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Ordering.Data.Migrations
{
    /// <inheritdoc />
    public partial class Coupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "ordering",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "coupon_code",
                schema: "ordering",
                table: "carts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "coupon_code",
                schema: "ordering",
                table: "carts");
        }
    }
}
