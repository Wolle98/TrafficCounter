using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Source",
                table: "VehiclePassage",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "SourceVideo",
                table: "VehiclePassage",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VideoOffsetSeconds",
                table: "VehiclePassage",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Source",
                table: "OperatingPeriod",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "SourceVideo",
                table: "OperatingPeriod",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehiclePassage_SourceVideo",
                table: "VehiclePassage",
                column: "SourceVideo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehiclePassage_SourceVideo",
                table: "VehiclePassage");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "VehiclePassage");

            migrationBuilder.DropColumn(
                name: "SourceVideo",
                table: "VehiclePassage");

            migrationBuilder.DropColumn(
                name: "VideoOffsetSeconds",
                table: "VehiclePassage");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "OperatingPeriod");

            migrationBuilder.DropColumn(
                name: "SourceVideo",
                table: "OperatingPeriod");
        }
    }
}
