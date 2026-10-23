using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "EndReason",
                table: "OperatingPeriod",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "NominalFps",
                table: "OperatingPeriod",
                type: "real",
                nullable: false,
                defaultValue: 0f);

            migrationBuilder.CreateTable(
                name: "MinuteStatistic",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "int", nullable: false),
                    MinuteStart = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", precision: 0, nullable: false),
                    FramesCaptured = table.Column<int>(type: "int", nullable: false),
                    FramesProcessed = table.Column<int>(type: "int", nullable: false),
                    FramesDropped = table.Column<int>(type: "int", nullable: false),
                    Detections = table.Column<int>(type: "int", nullable: false),
                    Passages = table.Column<int>(type: "int", nullable: false),
                    SourceState = table.Column<byte>(type: "tinyint", nullable: false),
                    Quality = table.Column<byte>(type: "tinyint", nullable: false),
                    Brightness = table.Column<float>(type: "real", nullable: true),
                    Contrast = table.Column<float>(type: "real", nullable: true),
                    Sharpness = table.Column<float>(type: "real", nullable: true),
                    Lighting = table.Column<byte>(type: "tinyint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinuteStatistic", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MinuteStatistic_Camera_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Camera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MinuteStatistic_OperatingPeriod_OperatingPeriodId",
                        column: x => x.OperatingPeriodId,
                        principalTable: "OperatingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemEvent_Camera_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Camera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SystemEvent_OperatingPeriod_OperatingPeriodId",
                        column: x => x.OperatingPeriodId,
                        principalTable: "OperatingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_CameraId_MinuteStart",
                table: "MinuteStatistic",
                columns: new[] { "CameraId", "MinuteStart" });

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_OperatingPeriodId",
                table: "MinuteStatistic",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_CameraId_OccurredAt",
                table: "SystemEvent",
                columns: new[] { "CameraId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_OperatingPeriodId",
                table: "SystemEvent",
                column: "OperatingPeriodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinuteStatistic");

            migrationBuilder.DropTable(
                name: "SystemEvent");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "OperatingPeriod");

            migrationBuilder.DropColumn(
                name: "NominalFps",
                table: "OperatingPeriod");
        }
    }
}
