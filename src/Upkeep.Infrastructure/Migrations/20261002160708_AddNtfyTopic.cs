using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Upkeep.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNtfyTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NtfyTopic",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NtfyTopic",
                table: "users");
        }
    }
}
