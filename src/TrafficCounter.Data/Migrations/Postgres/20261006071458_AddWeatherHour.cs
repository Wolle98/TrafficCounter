using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TrafficCounter.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWeatherHour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeatherHour",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StationId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Hour = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    PrecipitationMm = table.Column<float>(type: "real", nullable: true),
                    TemperatureC = table.Column<float>(type: "real", nullable: true),
                    SunshineMinutes = table.Column<float>(type: "real", nullable: true),
                    WindSpeedKmh = table.Column<float>(type: "real", nullable: true),
                    CloudCoverPercent = table.Column<float>(type: "real", nullable: true),
                    VisibilityMeters = table.Column<float>(type: "real", nullable: true),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherHour", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherHour_StationId_Hour",
                table: "WeatherHour",
                columns: new[] { "StationId", "Hour" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeatherHour");
        }
    }
}
