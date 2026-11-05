using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase7_NotasClinicas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nota_clinica",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hospitalizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    editada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nota_clinica", x => x.id);
                    table.ForeignKey(
                        name: "fk_nota_clinica_hospitalizacion_hospitalizacion_id",
                        column: x => x.hospitalizacion_id,
                        principalSchema: "vitalify",
                        principalTable: "hospitalizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nota_clinica_paciente_paciente_id",
                        column: x => x.paciente_id,
                        principalSchema: "vitalify",
                        principalTable: "paciente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_nota_clinica_usuario_autor_id",
                        column: x => x.autor_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nota_clinica_version",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    vigente_desde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reemplazada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nota_clinica_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_nota_clinica_version_nota_clinica_nota_id",
                        column: x => x.nota_id,
                        principalSchema: "vitalify",
                        principalTable: "nota_clinica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_accion_fecha",
                schema: "vitalify",
                table: "auditoria",
                columns: new[] { "accion", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clinica_hospitalizacion_id_tipo_atendida_en",
                schema: "vitalify",
                table: "alerta_clinica",
                columns: new[] { "hospitalizacion_id", "tipo", "atendida_en" });

            migrationBuilder.CreateIndex(
                name: "ix_nota_clinica_autor_id",
                schema: "vitalify",
                table: "nota_clinica",
                column: "autor_id");

            migrationBuilder.CreateIndex(
                name: "ix_nota_clinica_hospitalizacion_id_creada_en",
                schema: "vitalify",
                table: "nota_clinica",
                columns: new[] { "hospitalizacion_id", "creada_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_nota_clinica_paciente_id",
                schema: "vitalify",
                table: "nota_clinica",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ux_nota_clinica_version",
                schema: "vitalify",
                table: "nota_clinica_version",
                columns: new[] { "nota_id", "numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nota_clinica_version",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "nota_clinica",
                schema: "vitalify");

            migrationBuilder.DropIndex(
                name: "ix_auditoria_accion_fecha",
                schema: "vitalify",
                table: "auditoria");

            migrationBuilder.DropIndex(
                name: "ix_alerta_clinica_hospitalizacion_id_tipo_atendida_en",
                schema: "vitalify",
                table: "alerta_clinica");
        }
    }
}
