using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Ordering.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrderCompletionPending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completion_pending",
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
                name: "completion_pending",
                schema: "ordering",
                table: "orders");
        }
    }
}
