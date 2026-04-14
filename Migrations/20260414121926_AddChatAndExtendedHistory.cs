using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpecMind.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAndExtendedHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClarityPercent",
                table: "AnalysisHistoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CompletenessPercent",
                table: "AnalysisHistoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CriteriaJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImprovementsJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProblemsJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TemplateComplianceJson",
                table: "AnalysisHistoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AnalysisHistoryItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ContextText = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalText = table.Column<string>(type: "TEXT", nullable: false),
                    DraftPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversationMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChatConversationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConversationMessages_ChatConversations_ChatConversationId",
                        column: x => x.ChatConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversationMessages_ChatConversationId",
                table: "ChatConversationMessages",
                column: "ChatConversationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatConversationMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ClarityPercent",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "CompletenessPercent",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "CriteriaJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "ImprovementsJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "ProblemsJson",
                table: "AnalysisHistoryItems");

            migrationBuilder.DropColumn(
                name: "TemplateComplianceJson",
                table: "AnalysisHistoryItems");
        }
    }
}
