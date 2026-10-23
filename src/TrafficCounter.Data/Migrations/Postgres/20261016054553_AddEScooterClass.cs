using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddEScooterClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "VehicleClass",
                columns: new[] { "Id", "Name" },
                values: new object[] { (byte)8, "E-Scooter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "VehicleClass",
                keyColumn: "Id",
                keyValue: (byte)8);
        }
    }
}
