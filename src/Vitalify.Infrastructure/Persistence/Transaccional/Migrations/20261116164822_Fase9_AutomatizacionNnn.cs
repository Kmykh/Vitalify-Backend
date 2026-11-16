using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase9_AutomatizacionNnn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ultima_evaluacion_id",
                schema: "vitalify",
                table: "plan_sugerencia",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ultima_vez_propuesta_en",
                schema: "vitalify",
                table: "plan_sugerencia",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origen",
                schema: "vitalify",
                table: "plan_intervencion_ejecucion",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual"); // las ejecuciones de la fase 8 las registró la enfermera a mano

            migrationBuilder.AddColumn<bool>(
                name: "confirmado_sin_cambios",
                schema: "vitalify",
                table: "plan_diagnostico",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version_protocolo",
                schema: "vitalify",
                table: "plan_diagnostico",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardia",
                schema: "vitalify",
                table: "nota_clinica",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_nota_turno",
                schema: "vitalify",
                table: "nota_clinica",
                columns: new[] { "hospitalizacion_id", "guardia" },
                unique: true,
                filter: "tipo = 'ProcesoEnfermeria' AND guardia IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_nota_turno",
                schema: "vitalify",
                table: "nota_clinica");

            migrationBuilder.DropColumn(
                name: "ultima_evaluacion_id",
                schema: "vitalify",
                table: "plan_sugerencia");

            migrationBuilder.DropColumn(
                name: "ultima_vez_propuesta_en",
                schema: "vitalify",
                table: "plan_sugerencia");

            migrationBuilder.DropColumn(
                name: "origen",
                schema: "vitalify",
                table: "plan_intervencion_ejecucion");

            migrationBuilder.DropColumn(
                name: "confirmado_sin_cambios",
                schema: "vitalify",
                table: "plan_diagnostico");

            migrationBuilder.DropColumn(
                name: "version_protocolo",
                schema: "vitalify",
                table: "plan_diagnostico");

            migrationBuilder.DropColumn(
                name: "guardia",
                schema: "vitalify",
                table: "nota_clinica");
        }
    }
}
