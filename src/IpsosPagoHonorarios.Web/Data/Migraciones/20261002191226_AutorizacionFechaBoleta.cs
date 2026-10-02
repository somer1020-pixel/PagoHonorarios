using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpsosPagoHonorarios.Web.Data.Migraciones
{
    /// <inheritdoc />
    public partial class AutorizacionFechaBoleta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaAutorizadaEn",
                table: "Boletas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FechaAutorizadaMotivo",
                table: "Boletas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FechaAutorizadaPor",
                table: "Boletas",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FueraDePlazo",
                table: "Boletas",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaAutorizadaEn",
                table: "Boletas");

            migrationBuilder.DropColumn(
                name: "FechaAutorizadaMotivo",
                table: "Boletas");

            migrationBuilder.DropColumn(
                name: "FechaAutorizadaPor",
                table: "Boletas");

            migrationBuilder.DropColumn(
                name: "FueraDePlazo",
                table: "Boletas");
        }
    }
}
