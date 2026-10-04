using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpsosPagoHonorarios.Web.Data.Migraciones
{
    /// <inheritdoc />
    public partial class IndicesCarga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LineasPago_PlanillaId",
                table: "LineasPago");

            migrationBuilder.DropIndex(
                name: "IX_Boletas_PlanillaId",
                table: "Boletas");

            migrationBuilder.CreateIndex(
                name: "IX_LineasPago_PlanillaId_Estado",
                table: "LineasPago",
                columns: new[] { "PlanillaId", "Estado" })
                .Annotation("SqlServer:Include", new[] { "PrestadorId", "ResultadoCuenta", "AlertaCuentaResuelta", "ValorTotalBruto" });

            migrationBuilder.CreateIndex(
                name: "IX_Boletas_PlanillaId_PrestadorId",
                table: "Boletas",
                columns: new[] { "PlanillaId", "PrestadorId" })
                .Annotation("SqlServer:Include", new[] { "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LineasPago_PlanillaId_Estado",
                table: "LineasPago");

            migrationBuilder.DropIndex(
                name: "IX_Boletas_PlanillaId_PrestadorId",
                table: "Boletas");

            migrationBuilder.CreateIndex(
                name: "IX_LineasPago_PlanillaId",
                table: "LineasPago",
                column: "PlanillaId");

            migrationBuilder.CreateIndex(
                name: "IX_Boletas_PlanillaId",
                table: "Boletas",
                column: "PlanillaId");
        }
    }
}
