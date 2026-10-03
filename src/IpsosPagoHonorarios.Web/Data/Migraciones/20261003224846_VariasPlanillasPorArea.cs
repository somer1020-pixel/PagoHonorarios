using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpsosPagoHonorarios.Web.Data.Migraciones
{
    /// <inheritdoc />
    public partial class VariasPlanillasPorArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Planillas_CicloId_AreaId",
                table: "Planillas");

            migrationBuilder.AddColumn<string>(
                name: "Nombre",
                table: "Planillas",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Numero",
                table: "Planillas",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Planillas_CicloId_AreaId_Numero",
                table: "Planillas",
                columns: new[] { "CicloId", "AreaId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Planillas_CicloId_AreaId_Numero",
                table: "Planillas");

            migrationBuilder.DropColumn(
                name: "Nombre",
                table: "Planillas");

            migrationBuilder.DropColumn(
                name: "Numero",
                table: "Planillas");

            migrationBuilder.CreateIndex(
                name: "IX_Planillas_CicloId_AreaId",
                table: "Planillas",
                columns: new[] { "CicloId", "AreaId" },
                unique: true);
        }
    }
}
