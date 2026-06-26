using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebMediaManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodeGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EpisodeGroupId",
                table: "MediaItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpisodeGroupName",
                table: "MediaItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpisodeGroupId",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "EpisodeGroupName",
                table: "MediaItems");
        }
    }
}
