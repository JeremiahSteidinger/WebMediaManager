using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebMediaManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHasNfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasNfo",
                table: "MediaItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasNfo",
                table: "MediaItems");
        }
    }
}
