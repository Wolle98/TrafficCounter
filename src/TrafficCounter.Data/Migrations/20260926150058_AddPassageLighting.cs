using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficCounter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPassageLighting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Lighting",
                table: "VehiclePassage",
                type: "tinyint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lighting",
                table: "VehiclePassage");
        }
    }
}
