using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDriftMeasurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriftMeasurement",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "int", nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    DxPixels = table.Column<float>(type: "real", nullable: false),
                    DyPixels = table.Column<float>(type: "real", nullable: false),
                    RotationDegrees = table.Column<float>(type: "real", nullable: false),
                    Scale = table.Column<float>(type: "real", nullable: false),
                    Matches = table.Column<int>(type: "int", nullable: false),
                    InlierRatio = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftMeasurement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriftMeasurement_Camera_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Camera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DriftMeasurement_OperatingPeriod_OperatingPeriodId",
                        column: x => x.OperatingPeriodId,
                        principalTable: "OperatingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_CameraId_MeasuredAt",
                table: "DriftMeasurement",
                columns: new[] { "CameraId", "MeasuredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_OperatingPeriodId",
                table: "DriftMeasurement",
                column: "OperatingPeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriftMeasurement");
        }
    }
}
