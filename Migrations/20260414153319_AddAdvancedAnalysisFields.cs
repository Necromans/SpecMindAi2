using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpecMind.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedAnalysisFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedDeadlinesJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedExpectedResultsJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedKpisJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedRequirementsJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionClassificationsJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedDeadlinesJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "ExtractedExpectedResultsJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "ExtractedKpisJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "ExtractedRequirementsJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "SectionClassificationsJson",
                table: "AnalysisHistoryItems");
        }
    }
}
