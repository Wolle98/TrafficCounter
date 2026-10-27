using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TrafficCounter.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddHolidaysAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarEntry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EnglishName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Subdivision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DayNote",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayNote", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntry_ExternalId",
                table: "CalendarEntry",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEntry_Subdivision_StartDate",
                table: "CalendarEntry",
                columns: new[] { "Subdivision", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DayNote_Start",
                table: "DayNote",
                column: "Start");

            migrationBuilder.CreateIndex(
                name: "IX_DayNote_SyncId",
                table: "DayNote",
                column: "SyncId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarEntry");

            migrationBuilder.DropTable(
                name: "DayNote");
        }
    }
}
