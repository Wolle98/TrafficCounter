using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameAgriculturalClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "VehicleClass",
                keyColumn: "Id",
                keyValue: (byte)7,
                column: "Name",
                value: "Landwirtschaft & Baufahrzeuge");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "VehicleClass",
                keyColumn: "Id",
                keyValue: (byte)7,
                column: "Name",
                value: "Landwirtschaft");
        }
    }
}
