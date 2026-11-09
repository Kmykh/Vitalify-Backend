using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase8_ProcesoEnfermeria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nnn_diagnostico",
                schema: "vitalify",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    dominio = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    clase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    edicion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verificado = table.Column<bool>(type: "boolean", nullable: false),
                    verificado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    verificado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nnn_diagnostico", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_nnn_diagnostico_usuario_verificado_por",
                        column: x => x.verificado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nnn_intervencion",
                schema: "vitalify",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    campo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    etiqueta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    edicion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verificado = table.Column<bool>(type: "boolean", nullable: false),
                    verificado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    verificado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nnn_intervencion", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_nnn_intervencion_usuario_verificado_por",
                        column: x => x.verificado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nnn_resultado",
                schema: "vitalify",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    escala = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    edicion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verificado = table.Column<bool>(type: "boolean", nullable: false),
                    verificado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    verificado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nnn_resultado", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_nnn_resultado_usuario_verificado_por",
                        column: x => x.verificado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nnn_vinculo",
                schema: "vitalify",
                columns: table => new
                {
                    diagnostico_codigo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nnn_vinculo", x => new { x.diagnostico_codigo, x.tipo, x.codigo });
                    table.ForeignKey(
                        name: "fk_nnn_vinculo_nnn_diagnostico_diagnostico_codigo",
                        column: x => x.diagnostico_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_diagnostico",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_diagnostico",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnostico_codigo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    regla_origen = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    alerta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo_cierre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    confirmado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cerrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    cerrado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version_reglas = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_diagnostico", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_alerta_clinica_alerta_id",
                        column: x => x.alerta_id,
                        principalSchema: "vitalify",
                        principalTable: "alerta_clinica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_hospitalizacion_hospitalizacion_id",
                        column: x => x.hospitalizacion_id,
                        principalSchema: "vitalify",
                        principalTable: "hospitalizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_nnn_diagnostico_diagnostico_codigo",
                        column: x => x.diagnostico_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_diagnostico",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_paciente_paciente_id",
                        column: x => x.paciente_id,
                        principalSchema: "vitalify",
                        principalTable: "paciente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_usuario_cerrado_por",
                        column: x => x.cerrado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_diagnostico_usuario_confirmado_por",
                        column: x => x.confirmado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nnn_indicador",
                schema: "vitalify",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    resultado_codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    parametro_vital = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    etiqueta = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    edicion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verificado = table.Column<bool>(type: "boolean", nullable: false),
                    verificado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    verificado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nnn_indicador", x => x.codigo);
                    table.ForeignKey(
                        name: "fk_nnn_indicador_nnn_resultado_resultado_codigo",
                        column: x => x.resultado_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_resultado",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nnn_indicador_usuario_verificado_por",
                        column: x => x.verificado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_intervencion",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    intervencion_codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    frecuencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    agregada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    agregada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_intervencion", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_nnn_intervencion_intervencion_codigo",
                        column: x => x.intervencion_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_intervencion",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_plan_diagnostico_plan_diagnostico_id",
                        column: x => x.plan_diagnostico_id,
                        principalSchema: "vitalify",
                        principalTable: "plan_diagnostico",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_usuario_agregada_por",
                        column: x => x.agregada_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_resultado",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_diagnostico_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado_codigo = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    puntuacion_basal = table.Column<int>(type: "integer", nullable: false),
                    puntuacion_meta = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_resultado", x => x.id);
                    table.CheckConstraint("ck_plan_resultado_puntuaciones", "puntuacion_basal BETWEEN 1 AND 5 AND puntuacion_meta BETWEEN 1 AND 5 AND puntuacion_meta >= puntuacion_basal");
                    table.ForeignKey(
                        name: "fk_plan_resultado_nnn_resultado_resultado_codigo",
                        column: x => x.resultado_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_resultado",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_resultado_plan_diagnostico_plan_diagnostico_id",
                        column: x => x.plan_diagnostico_id,
                        principalSchema: "vitalify",
                        principalTable: "plan_diagnostico",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_sugerencia",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    diagnostico_codigo = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    regla = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version_reglas = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prioridad = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    evaluacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alerta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    emitida_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resuelta_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    plan_diagnostico_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_sugerencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_sugerencia_alerta_clinica_alerta_id",
                        column: x => x.alerta_id,
                        principalSchema: "vitalify",
                        principalTable: "alerta_clinica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_sugerencia_hospitalizacion_hospitalizacion_id",
                        column: x => x.hospitalizacion_id,
                        principalSchema: "vitalify",
                        principalTable: "hospitalizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_sugerencia_nnn_diagnostico_diagnostico_codigo",
                        column: x => x.diagnostico_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_diagnostico",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_sugerencia_paciente_paciente_id",
                        column: x => x.paciente_id,
                        principalSchema: "vitalify",
                        principalTable: "paciente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_sugerencia_plan_diagnostico_plan_diagnostico_id",
                        column: x => x.plan_diagnostico_id,
                        principalSchema: "vitalify",
                        principalTable: "plan_diagnostico",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_intervencion_ejecucion",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_intervencion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ejecutada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    ejecutada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    alerta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_intervencion_ejecucion", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_ejecucion_alerta_clinica_alerta_id",
                        column: x => x.alerta_id,
                        principalSchema: "vitalify",
                        principalTable: "alerta_clinica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_ejecucion_plan_intervencion_plan_interven~",
                        column: x => x.plan_intervencion_id,
                        principalSchema: "vitalify",
                        principalTable: "plan_intervencion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_intervencion_ejecucion_usuario_ejecutada_por",
                        column: x => x.ejecutada_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_resultado_valoracion",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_resultado_id = table.Column<Guid>(type: "uuid", nullable: false),
                    indicador_codigo = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                    puntuacion = table.Column<int>(type: "integer", nullable: false),
                    puntuacion_sugerida = table.Column<int>(type: "integer", nullable: true),
                    valorada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    valorada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    turno = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_resultado_valoracion", x => x.id);
                    table.CheckConstraint("ck_plan_resultado_valoracion_puntuacion", "puntuacion BETWEEN 1 AND 5 AND (puntuacion_sugerida IS NULL OR puntuacion_sugerida BETWEEN 1 AND 5)");
                    table.ForeignKey(
                        name: "fk_plan_resultado_valoracion_nnn_indicador_indicador_codigo",
                        column: x => x.indicador_codigo,
                        principalSchema: "vitalify",
                        principalTable: "nnn_indicador",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_resultado_valoracion_plan_resultado_plan_resultado_id",
                        column: x => x.plan_resultado_id,
                        principalSchema: "vitalify",
                        principalTable: "plan_resultado",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_resultado_valoracion_usuario_valorada_por",
                        column: x => x.valorada_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_nnn_diagnostico_verificado_por",
                schema: "vitalify",
                table: "nnn_diagnostico",
                column: "verificado_por");

            migrationBuilder.CreateIndex(
                name: "ix_nnn_indicador_resultado_codigo",
                schema: "vitalify",
                table: "nnn_indicador",
                column: "resultado_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_nnn_indicador_verificado_por",
                schema: "vitalify",
                table: "nnn_indicador",
                column: "verificado_por");

            migrationBuilder.CreateIndex(
                name: "ix_nnn_intervencion_verificado_por",
                schema: "vitalify",
                table: "nnn_intervencion",
                column: "verificado_por");

            migrationBuilder.CreateIndex(
                name: "ix_nnn_resultado_verificado_por",
                schema: "vitalify",
                table: "nnn_resultado",
                column: "verificado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_alerta_id",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "alerta_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_cerrado_por",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "cerrado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_confirmado_en",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "confirmado_en");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_confirmado_por",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "confirmado_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_diagnostico_codigo",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "diagnostico_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_hospitalizacion_id_estado",
                schema: "vitalify",
                table: "plan_diagnostico",
                columns: new[] { "hospitalizacion_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_diagnostico_paciente_id",
                schema: "vitalify",
                table: "plan_diagnostico",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ux_plan_diagnostico_activo",
                schema: "vitalify",
                table: "plan_diagnostico",
                columns: new[] { "hospitalizacion_id", "diagnostico_codigo" },
                unique: true,
                filter: "estado = 'Activo'");

            migrationBuilder.CreateIndex(
                name: "ix_plan_intervencion_agregada_por",
                schema: "vitalify",
                table: "plan_intervencion",
                column: "agregada_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_intervencion_intervencion_codigo",
                schema: "vitalify",
                table: "plan_intervencion",
                column: "intervencion_codigo");

            migrationBuilder.CreateIndex(
                name: "ux_plan_intervencion_activa",
                schema: "vitalify",
                table: "plan_intervencion",
                columns: new[] { "plan_diagnostico_id", "intervencion_codigo" },
                unique: true,
                filter: "activa");

            migrationBuilder.CreateIndex(
                name: "ix_plan_intervencion_ejecucion_alerta_id",
                schema: "vitalify",
                table: "plan_intervencion_ejecucion",
                column: "alerta_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_intervencion_ejecucion_ejecutada_por",
                schema: "vitalify",
                table: "plan_intervencion_ejecucion",
                column: "ejecutada_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_intervencion_ejecucion_plan_intervencion_id_ejecutada_~",
                schema: "vitalify",
                table: "plan_intervencion_ejecucion",
                columns: new[] { "plan_intervencion_id", "ejecutada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_resultado_resultado_codigo",
                schema: "vitalify",
                table: "plan_resultado",
                column: "resultado_codigo");

            migrationBuilder.CreateIndex(
                name: "ux_plan_resultado",
                schema: "vitalify",
                table: "plan_resultado",
                columns: new[] { "plan_diagnostico_id", "resultado_codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_resultado_valoracion_indicador_codigo",
                schema: "vitalify",
                table: "plan_resultado_valoracion",
                column: "indicador_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_plan_resultado_valoracion_plan_resultado_id_valorada_en",
                schema: "vitalify",
                table: "plan_resultado_valoracion",
                columns: new[] { "plan_resultado_id", "valorada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_resultado_valoracion_valorada_por",
                schema: "vitalify",
                table: "plan_resultado_valoracion",
                column: "valorada_por");

            migrationBuilder.CreateIndex(
                name: "ix_plan_sugerencia_alerta_id",
                schema: "vitalify",
                table: "plan_sugerencia",
                column: "alerta_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_sugerencia_diagnostico_codigo",
                schema: "vitalify",
                table: "plan_sugerencia",
                column: "diagnostico_codigo");

            migrationBuilder.CreateIndex(
                name: "ix_plan_sugerencia_emitida_en",
                schema: "vitalify",
                table: "plan_sugerencia",
                column: "emitida_en");

            migrationBuilder.CreateIndex(
                name: "ix_plan_sugerencia_paciente_id",
                schema: "vitalify",
                table: "plan_sugerencia",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_sugerencia_plan_diagnostico_id",
                schema: "vitalify",
                table: "plan_sugerencia",
                column: "plan_diagnostico_id");

            migrationBuilder.CreateIndex(
                name: "ux_plan_sugerencia_pendiente",
                schema: "vitalify",
                table: "plan_sugerencia",
                columns: new[] { "hospitalizacion_id", "diagnostico_codigo" },
                unique: true,
                filter: "estado = 'Pendiente'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nnn_vinculo",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_intervencion_ejecucion",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_resultado_valoracion",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_sugerencia",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_intervencion",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "nnn_indicador",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_resultado",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "nnn_intervencion",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "nnn_resultado",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "plan_diagnostico",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "nnn_diagnostico",
                schema: "vitalify");
        }
    }
}
