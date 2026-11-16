using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase9_FuentesEvaluacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fuente_fr",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuente_pas",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuente_temperatura",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fuente_fr",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo");

            migrationBuilder.DropColumn(
                name: "fuente_pas",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo");

            migrationBuilder.DropColumn(
                name: "fuente_temperatura",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo");
        }
    }
}
