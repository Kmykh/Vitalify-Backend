using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase7_Retencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lectura_signos_resumen",
                schema: "vitalify_historial",
                columns: table => new
                {
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lecturas = table.Column<int>(type: "integer", nullable: false),
                    caida = table.Column<bool>(type: "boolean", nullable: false),
                    fc_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    fc_minimo = table.Column<int>(type: "integer", nullable: true),
                    fc_maximo = table.Column<int>(type: "integer", nullable: true),
                    fc_mediciones = table.Column<int>(type: "integer", nullable: false),
                    fr_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    fr_minimo = table.Column<int>(type: "integer", nullable: true),
                    fr_maximo = table.Column<int>(type: "integer", nullable: true),
                    fr_mediciones = table.Column<int>(type: "integer", nullable: false),
                    spo2_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    spo2_minimo = table.Column<int>(type: "integer", nullable: true),
                    spo2_maximo = table.Column<int>(type: "integer", nullable: true),
                    spo2_mediciones = table.Column<int>(type: "integer", nullable: false),
                    temperatura_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    temperatura_minimo = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    temperatura_maximo = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    temperatura_mediciones = table.Column<int>(type: "integer", nullable: false),
                    pas_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    pas_minimo = table.Column<int>(type: "integer", nullable: true),
                    pas_maximo = table.Column<int>(type: "integer", nullable: true),
                    pas_mediciones = table.Column<int>(type: "integer", nullable: false),
                    pad_promedio = table.Column<decimal>(type: "numeric", nullable: true),
                    pad_minimo = table.Column<int>(type: "integer", nullable: true),
                    pad_maximo = table.Column<int>(type: "integer", nullable: true),
                    pad_mediciones = table.Column<int>(type: "integer", nullable: false),
                    resumido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lectura_signos_resumen", x => new { x.hospitalizacion_id, x.inicio });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lectura_signos_resumen",
                schema: "vitalify_historial");
        }
    }
}
