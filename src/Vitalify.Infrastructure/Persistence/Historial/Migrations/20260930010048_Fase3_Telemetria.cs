using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase3_Telemetria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vitalify_historial");

            migrationBuilder.CreateTable(
                name: "estado_signos_actual",
                schema: "vitalify_historial",
                columns: table => new
                {
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_dispositivo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    fc_valor = table.Column<int>(type: "integer", nullable: true),
                    fc_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fr_valor = table.Column<int>(type: "integer", nullable: true),
                    fr_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    spo2_valor = table.Column<int>(type: "integer", nullable: true),
                    spo2_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    temp_valor = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    temp_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pas_valor = table.Column<int>(type: "integer", nullable: true),
                    pas_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pad_valor = table.Column<int>(type: "integer", nullable: true),
                    pad_medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultima_lectura_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ultima_caida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    bateria = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_estado_signos_actual", x => x.hospitalizacion_id);
                });

            migrationBuilder.CreateTable(
                name: "incidencia_telemetria",
                schema: "vitalify_historial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ocurrida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    codigo_dispositivo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    variable = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    valor_recibido = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requiere_nueva_lectura = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_incidencia_telemetria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lectura_signos",
                schema: "vitalify_historial",
                columns: table => new
                {
                    codigo_dispositivo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    medido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recibido_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fc = table.Column<int>(type: "integer", nullable: true),
                    fr = table.Column<int>(type: "integer", nullable: true),
                    spo2 = table.Column<int>(type: "integer", nullable: true),
                    temperatura = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    pas = table.Column<int>(type: "integer", nullable: true),
                    pad = table.Column<int>(type: "integer", nullable: true),
                    caida = table.Column<bool>(type: "boolean", nullable: false),
                    bateria = table.Column<int>(type: "integer", nullable: true),
                    seq = table.Column<long>(type: "bigint", nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lectura_signos", x => new { x.codigo_dispositivo, x.medido_en });
                });

            migrationBuilder.CreateIndex(
                name: "ix_incidencia_telemetria_ocurrida_en",
                schema: "vitalify_historial",
                table: "incidencia_telemetria",
                column: "ocurrida_en",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_lectura_signos_hospitalizacion_id_medido_en",
                schema: "vitalify_historial",
                table: "lectura_signos",
                columns: new[] { "hospitalizacion_id", "medido_en" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estado_signos_actual",
                schema: "vitalify_historial");

            migrationBuilder.DropTable(
                name: "incidencia_telemetria",
                schema: "vitalify_historial");

            migrationBuilder.DropTable(
                name: "lectura_signos",
                schema: "vitalify_historial");
        }
    }
}
