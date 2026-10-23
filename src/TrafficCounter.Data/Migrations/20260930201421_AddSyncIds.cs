using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "VehiclePassage",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // existing rows get their own ids

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "SystemEvent",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // existing rows get their own ids

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "OperatingPeriod",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // existing rows get their own ids

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "OperatingPeriod",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("UPDATE OperatingPeriod SET UpdatedAt = COALESCE(EndedAt, LastHeartbeatAt);");

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "MinuteStatistic",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // existing rows get their own ids

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "DriftMeasurement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()"); // existing rows get their own ids

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_SyncId",
                table: "VehiclePassage",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvent_SyncId",
                table: "SystemEvent",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingPeriod_SyncId",
                table: "OperatingPeriod",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MinuteStatistic_SyncId",
                table: "MinuteStatistic",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriftMeasurement_SyncId",
                table: "DriftMeasurement",
                column: "SyncId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehiclePassage_SyncId",
                table: "VehiclePassage");

            migrationBuilder.DropIndex(
                name: "IX_SystemEvent_SyncId",
                table: "SystemEvent");

            migrationBuilder.DropIndex(
                name: "IX_OperatingPeriod_SyncId",
                table: "OperatingPeriod");

            migrationBuilder.DropIndex(
                name: "IX_MinuteStatistic_SyncId",
                table: "MinuteStatistic");

            migrationBuilder.DropIndex(
                name: "IX_DriftMeasurement_SyncId",
                table: "DriftMeasurement");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "VehiclePassage");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "SystemEvent");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "OperatingPeriod");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "OperatingPeriod");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "MinuteStatistic");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "DriftMeasurement");
        }
    }
}
