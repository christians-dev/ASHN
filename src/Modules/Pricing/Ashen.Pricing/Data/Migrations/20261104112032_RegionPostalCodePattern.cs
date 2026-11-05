using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashen.Pricing.Data.Migrations
{
    /// <inheritdoc />
    public partial class RegionPostalCodePattern : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "postal_code_pattern",
                schema: "pricing",
                table: "region_settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Regions that existed before this column: the only one is the United States.
            migrationBuilder.Sql(@"UPDATE pricing.region_settings SET postal_code_pattern = '^\d{5}(-\d{4})?$' WHERE country_code = 'US';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "postal_code_pattern",
                schema: "pricing",
                table: "region_settings");
        }
    }
}
