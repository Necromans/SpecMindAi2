using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpecMind.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryAndReferenceMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Recommendation",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Recommendation",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "AnalysisHistoryItems");
        }
    }
}
