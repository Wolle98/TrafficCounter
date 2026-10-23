using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Camera",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DirectionAName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DirectionBName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camera", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleClass",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleClass", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperatingPeriod",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConfigHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingPeriod", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatingPeriod_Camera_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Camera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VehiclePassage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CameraId = table.Column<int>(type: "int", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "int", nullable: false),
                    PassedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    Direction = table.Column<byte>(type: "tinyint", nullable: false),
                    VehicleClass = table.Column<byte>(type: "tinyint", nullable: true),
                    ClassConfidence = table.Column<float>(type: "real", nullable: true),
                    SpeedKmh = table.Column<float>(type: "real", nullable: true),
                    TrackId = table.Column<int>(type: "int", nullable: false),
                    TrackLengthFrames = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehiclePassage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehiclePassage_Camera_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Camera",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehiclePassage_OperatingPeriod_OperatingPeriodId",
                        column: x => x.OperatingPeriodId,
                        principalTable: "OperatingPeriod",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehiclePassage_VehicleClass_VehicleClass",
                        column: x => x.VehicleClass,
                        principalTable: "VehicleClass",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "VehicleClass",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { (byte)0, "Unbekannt" },
                    { (byte)1, "PKW" },
                    { (byte)2, "Transporter" },
                    { (byte)3, "LKW" },
                    { (byte)4, "Bus" },
                    { (byte)5, "Motorrad/Motorroller" },
                    { (byte)6, "Fahrrad/E-Scooter" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Camera_Name",
                table: "Camera",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingPeriod_CameraId_StartedAt",
                table: "OperatingPeriod",
                columns: new[] { "CameraId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_CameraId_PassedAt",
                table: "VehiclePassage",
                columns: new[] { "CameraId", "PassedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_OperatingPeriodId",
                table: "VehiclePassage",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_VehicleClass",
                table: "VehiclePassage",
                column: "VehicleClass");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehiclePassage");

            migrationBuilder.DropTable(
                name: "OperatingPeriod");

            migrationBuilder.DropTable(
                name: "VehicleClass");

            migrationBuilder.DropTable(
                name: "Camera");
        }
    }
}
