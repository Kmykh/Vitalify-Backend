using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase4_MotorClinico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluacion_riesgo",
                schema: "vitalify_historial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fc = table.Column<int>(type: "integer", nullable: true),
                    spo2 = table.Column<int>(type: "integer", nullable: true),
                    temperatura = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    fr = table.Column<int>(type: "integer", nullable: true),
                    pas = table.Column<int>(type: "integer", nullable: true),
                    conciencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    oxigeno_suplementario = table.Column<bool>(type: "boolean", nullable: true),
                    escala_spo2 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    news2_total = table.Column<int>(type: "integer", nullable: false),
                    news2_nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    news2_completo = table.Column<bool>(type: "boolean", nullable: false),
                    mews_total = table.Column<int>(type: "integer", nullable: false),
                    mews_nivel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mews_completo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluacion_riesgo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "observacion_enfermeria",
                schema: "vitalify_historial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    registrada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    registrada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    fr = table.Column<int>(type: "integer", nullable: true),
                    pas = table.Column<int>(type: "integer", nullable: true),
                    pad = table.Column<int>(type: "integer", nullable: true),
                    temperatura = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    conciencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    oxigeno_suplementario = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observacion_enfermeria", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evaluacion_riesgo_hospitalizacion_id_evaluada_en",
                schema: "vitalify_historial",
                table: "evaluacion_riesgo",
                columns: new[] { "hospitalizacion_id", "evaluada_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_observacion_enfermeria_hospitalizacion_id_observada_en",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                columns: new[] { "hospitalizacion_id", "observada_en" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluacion_riesgo",
                schema: "vitalify_historial");

            migrationBuilder.DropTable(
                name: "observacion_enfermeria",
                schema: "vitalify_historial");
        }
    }
}
