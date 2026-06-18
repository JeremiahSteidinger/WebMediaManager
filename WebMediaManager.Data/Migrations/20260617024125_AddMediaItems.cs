using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebMediaManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Plot = table.Column<string>(type: "TEXT", nullable: true),
                    Tagline = table.Column<string>(type: "TEXT", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    MatchState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PrimaryProvider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TmdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TvdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    Votes = table.Column<int>(type: "INTEGER", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    Studios = table.Column<string>(type: "TEXT", nullable: false),
                    DateAdded = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DateScanned = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    VideoFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    Runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaItems_Libraries_LibraryId",
                        column: x => x.LibraryId,
                        principalTable: "Libraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TvShowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Seasons_MediaItems_TvShowId",
                        column: x => x.TvShowId,
                        principalTable: "MediaItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Plot = table.Column<string>(type: "TEXT", nullable: true),
                    AirDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    VideoFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TmdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TvdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Seasons_SeasonId",
                        column: x => x.SeasonId,
                        principalTable: "Seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeasonId",
                table: "Episodes",
                column: "SeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_LibraryId",
                table: "MediaItems",
                column: "LibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaItems_LibraryId_MatchState",
                table: "MediaItems",
                columns: new[] { "LibraryId", "MatchState" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_TvShowId_SeasonNumber",
                table: "Seasons",
                columns: new[] { "TvShowId", "SeasonNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "Seasons");

            migrationBuilder.DropTable(
                name: "MediaItems");
        }
    }
}
