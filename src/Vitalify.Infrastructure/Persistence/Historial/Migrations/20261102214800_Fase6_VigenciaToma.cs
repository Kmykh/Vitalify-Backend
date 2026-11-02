using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase6_VigenciaToma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "vigente_hasta",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "proxima_toma_manual_en",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vigente_hasta",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "proxima_toma_manual_en",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo");
        }
    }
}
