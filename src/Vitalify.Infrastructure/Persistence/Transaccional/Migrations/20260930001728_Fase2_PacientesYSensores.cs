using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase2_PacientesYSensores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cama",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    servicio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cama", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dispositivo",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispositivo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paciente",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_completo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    fecha_nacimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_nacimiento_estimada = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hospitalizacion",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cama_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnostico_ingreso = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ingreso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    egreso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    motivo_egreso = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    observacion_egreso = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    egreso_registrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hospitalizacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_hospitalizacion_cama_cama_id",
                        column: x => x.cama_id,
                        principalSchema: "vitalify",
                        principalTable: "cama",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hospitalizacion_paciente_paciente_id",
                        column: x => x.paciente_id,
                        principalSchema: "vitalify",
                        principalTable: "paciente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hospitalizacion_usuario_egreso_registrado_por",
                        column: x => x.egreso_registrado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hospitalizacion_usuario_registrado_por",
                        column: x => x.registrado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asignacion_dispositivo",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    asignado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    liberado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    liberado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_liberacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_dispositivo", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignacion_dispositivo_dispositivo_dispositivo_id",
                        column: x => x.dispositivo_id,
                        principalSchema: "vitalify",
                        principalTable: "dispositivo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignacion_dispositivo_hospitalizacion_hospitalizacion_id",
                        column: x => x.hospitalizacion_id,
                        principalSchema: "vitalify",
                        principalTable: "hospitalizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignacion_dispositivo_usuario_asignado_por",
                        column: x => x.asignado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_asignacion_dispositivo_usuario_liberado_por",
                        column: x => x.liberado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_dispositivo_asignado_por",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "asignado_por");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_dispositivo_dispositivo_id",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "dispositivo_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_dispositivo_hospitalizacion_id",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "hospitalizacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_dispositivo_liberado_por",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "liberado_por");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_dispositivo_vigente",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "dispositivo_id",
                unique: true,
                filter: "liberado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_asignacion_hospitalizacion_vigente",
                schema: "vitalify",
                table: "asignacion_dispositivo",
                column: "hospitalizacion_id",
                unique: true,
                filter: "liberado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_cama_codigo",
                schema: "vitalify",
                table: "cama",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dispositivo_estado",
                schema: "vitalify",
                table: "dispositivo",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ux_dispositivo_codigo",
                schema: "vitalify",
                table: "dispositivo",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_cama_id",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "cama_id");

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_egreso_registrado_por",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "egreso_registrado_por");

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_estado",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_paciente_id",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_registrado_por",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "registrado_por");

            migrationBuilder.CreateIndex(
                name: "ux_hospitalizacion_cama_activa",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "cama_id",
                unique: true,
                filter: "estado = 'Activa'");

            migrationBuilder.CreateIndex(
                name: "ux_hospitalizacion_paciente_activa",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "paciente_id",
                unique: true,
                filter: "estado = 'Activa'");

            migrationBuilder.CreateIndex(
                name: "ux_paciente_documento",
                schema: "vitalify",
                table: "paciente",
                columns: new[] { "tipo_documento", "numero_documento" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignacion_dispositivo",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "dispositivo",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "hospitalizacion",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "cama",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "paciente",
                schema: "vitalify");
        }
    }
}
