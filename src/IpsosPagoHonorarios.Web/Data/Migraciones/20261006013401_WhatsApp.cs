using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpsosPagoHonorarios.Web.Data.Migraciones
{
    /// <inheritdoc />
    public partial class WhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppAutorizado",
                table: "Prestadores",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppAutorizadoEn",
                table: "Prestadores",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsApp",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrestadorId = table.Column<int>(type: "int", nullable: true),
                    Para = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Plantilla = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Parametros = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EnviadoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    UltimoIntentoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProveedorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsApp", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsApp_EnviadoEn_Intentos",
                table: "WhatsApp",
                columns: new[] { "EnviadoEn", "Intentos" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsApp_Para_Plantilla_CreadoEn",
                table: "WhatsApp",
                columns: new[] { "Para", "Plantilla", "CreadoEn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsApp");

            migrationBuilder.DropColumn(
                name: "WhatsAppAutorizado",
                table: "Prestadores");

            migrationBuilder.DropColumn(
                name: "WhatsAppAutorizadoEn",
                table: "Prestadores");
        }
    }
}
