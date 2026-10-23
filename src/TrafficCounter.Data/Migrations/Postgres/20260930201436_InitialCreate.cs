using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrafficCounter.Data.Migrations.Postgres
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
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DirectionAName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DirectionBName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Camera", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleClass",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleClass", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperatingPeriod",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: true),
                    EndReason = table.Column<byte>(type: "smallint", nullable: true),
                    NominalFps = table.Column<float>(type: "real", nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfigHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Source = table.Column<byte>(type: "smallint", nullable: false),
                    SourceVideo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false)
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
                name: "DriftMeasurement",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    Status = table.Column<byte>(type: "smallint", nullable: false),
                    DxPixels = table.Column<float>(type: "real", nullable: false),
                    DyPixels = table.Column<float>(type: "real", nullable: false),
                    RotationDegrees = table.Column<float>(type: "real", nullable: false),
                    Scale = table.Column<float>(type: "real", nullable: false),
                    Matches = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "MinuteStatistic",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    MinuteStart = table.Column<DateTimeOffset>(type: "timestamp(0) with time zone", precision: 0, nullable: false),
                    FramesCaptured = table.Column<int>(type: "integer", nullable: false),
                    FramesProcessed = table.Column<int>(type: "integer", nullable: false),
                    FramesDropped = table.Column<int>(type: "integer", nullable: false),
                    Detections = table.Column<int>(type: "integer", nullable: false),
                    Passages = table.Column<int>(type: "integer", nullable: false),
                    SourceState = table.Column<byte>(type: "smallint", nullable: false),
                    Quality = table.Column<byte>(type: "smallint", nullable: false),
                    Brightness = table.Column<float>(type: "real", nullable: true),
                    Contrast = table.Column<float>(type: "real", nullable: true),
                    Sharpness = table.Column<float>(type: "real", nullable: true),
                    Lighting = table.Column<byte>(type: "smallint", nullable: true)
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
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    Kind = table.Column<byte>(type: "smallint", nullable: false),
                    Severity = table.Column<byte>(type: "smallint", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "VehiclePassage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    OperatingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    PassedAt = table.Column<DateTimeOffset>(type: "timestamp(3) with time zone", precision: 3, nullable: false),
                    Direction = table.Column<byte>(type: "smallint", nullable: false),
                    VehicleClass = table.Column<byte>(type: "smallint", nullable: true),
                    ClassConfidence = table.Column<float>(type: "real", nullable: true),
                    SpeedKmh = table.Column<float>(type: "real", nullable: true),
                    LinePosition = table.Column<float>(type: "real", nullable: true),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    TrackLengthFrames = table.Column<int>(type: "integer", nullable: false),
                    TrailerCount = table.Column<byte>(type: "smallint", nullable: false),
                    Source = table.Column<byte>(type: "smallint", nullable: false),
                    SourceVideo = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    VideoOffsetSeconds = table.Column<double>(type: "double precision", nullable: true),
                    Lighting = table.Column<byte>(type: "smallint", nullable: true)
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
                    { (byte)6, "Fahrrad/E-Scooter" },
                    { (byte)7, "Landwirtschaft" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Camera_Name",
                table: "Camera",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_CameraId_MeasuredAt",
                table: "DriftMeasurement",
                columns: new[] { "CameraId", "MeasuredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_OperatingPeriodId",
                table: "DriftMeasurement",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_SyncId",
                table: "DriftMeasurement",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_CameraId_MinuteStart",
                table: "MinuteStatistic",
                columns: new[] { "CameraId", "MinuteStart" });

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_OperatingPeriodId",
                table: "MinuteStatistic",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_SyncId",
                table: "MinuteStatistic",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingPeriod_CameraId_StartedAt",
                table: "OperatingPeriod",
                columns: new[] { "CameraId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingPeriod_SyncId",
                table: "OperatingPeriod",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_CameraId_OccurredAt",
                table: "SystemEvent",
                columns: new[] { "CameraId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_OperatingPeriodId",
                table: "SystemEvent",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_SyncId",
                table: "SystemEvent",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_CameraId_PassedAt",
                table: "VehiclePassage",
                columns: new[] { "CameraId", "PassedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_OperatingPeriodId",
                table: "VehiclePassage",
                column: "OperatingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_SourceVideo",
                table: "VehiclePassage",
                column: "SourceVideo");

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_SyncId",
                table: "VehiclePassage",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_VehicleClass",
                table: "VehiclePassage",
                column: "VehicleClass");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DriftMeasurement");

            migrationBuilder.DropTable(
                name: "MinuteStatistic");

            migrationBuilder.DropTable(
                name: "SystemEvent");

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
