using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebMediaManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Artwork = table.Column<string>(type: "TEXT", nullable: false),
                    Nfo = table.Column<string>(type: "TEXT", nullable: false),
                    Providers = table.Column<string>(type: "TEXT", nullable: false),
                    RenamePatterns = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
