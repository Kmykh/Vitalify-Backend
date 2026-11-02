using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase6_Alertas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerta_clinica",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    prioridad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nivel_riesgo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    news2 = table.Column<int>(type: "integer", nullable: true),
                    mews = table.Column<int>(type: "integer", nullable: true),
                    detalle = table.Column<string>(type: "jsonb", nullable: true),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tomada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    tomada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    atendida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    atendida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    nota_atencion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    escalada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notificada_a = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_clinica", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerta_clinica_hospitalizacion_hospitalizacion_id",
                        column: x => x.hospitalizacion_id,
                        principalSchema: "vitalify",
                        principalTable: "hospitalizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alerta_clinica_paciente_paciente_id",
                        column: x => x.paciente_id,
                        principalSchema: "vitalify",
                        principalTable: "paciente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alerta_clinica_servicio_servicio_id",
                        column: x => x.servicio_id,
                        principalSchema: "vitalify",
                        principalTable: "servicio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alerta_clinica_usuario_atendida_por",
                        column: x => x.atendida_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_alerta_clinica_usuario_tomada_por",
                        column: x => x.tomada_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispositivo_movil",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    plataforma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    registrado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispositivo_movil", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispositivo_movil_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_atendida_por",
                schema: "vitalify",
                table: "alerta_clinica",
                column: "atendida_por");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_creada_en",
                schema: "vitalify",
                table: "alerta_clinica",
                column: "creada_en");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_paciente_id",
                schema: "vitalify",
                table: "alerta_clinica",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_servicio_id_estado",
                schema: "vitalify",
                table: "alerta_clinica",
                columns: new[] { "servicio_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_tomada_por",
                schema: "vitalify",
                table: "alerta_clinica",
                column: "tomada_por");

            migrationBuilder.CreateIndex(
                name: "ux_alerta_abierta",
                schema: "vitalify",
                table: "alerta_clinica",
                columns: new[] { "hospitalizacion_id", "tipo" },
                unique: true,
                filter: "estado IN ('Pendiente', 'EnAtencion')");

            migrationBuilder.CreateIndex(
                name: "ix_dispositivo_movil_usuario_id",
                schema: "vitalify",
                table: "dispositivo_movil",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_dispositivo_movil_token",
                schema: "vitalify",
                table: "dispositivo_movil",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerta_clinica",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "dispositivo_movil",
                schema: "vitalify");
        }
    }
}
