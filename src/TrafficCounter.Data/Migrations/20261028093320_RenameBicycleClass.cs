using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameBicycleClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "VehicleClass",
                keyColumn: "Id",
                keyValue: (byte)6,
                column: "Name",
                value: "Fahrrad");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "VehicleClass",
                keyColumn: "Id",
                keyValue: (byte)6,
                column: "Name",
                value: "Fahrrad/E-Scooter");
        }
    }
}
