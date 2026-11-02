using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase5_VinculacionQr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "confusion_basal",
                schema: "vitalify",
                table: "paciente",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "escala_spo2",
                schema: "vitalify",
                table: "hospitalizacion",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Las hospitalizaciones que ya existen siguen con la escala 1 de SpO2, la de siempre.
                defaultValue: "Escala1");

            migrationBuilder.AddColumn<DateTime>(
                name: "escala_spo2_cambiada_en",
                schema: "vitalify",
                table: "hospitalizacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_escala_spo2",
                schema: "vitalify",
                table: "hospitalizacion",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "codigo_vinculacion",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_codigo = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invalidado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    intentos_fallidos = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    token_expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigo_vinculacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_codigo_vinculacion_dispositivo_dispositivo_id",
                        column: x => x.dispositivo_id,
                        principalSchema: "vitalify",
                        principalTable: "dispositivo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hospitalizacion_escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "escala_spo2_cambiada_por");

            migrationBuilder.CreateIndex(
                name: "ix_codigo_vinculacion_dispositivo_id",
                schema: "vitalify",
                table: "codigo_vinculacion",
                column: "dispositivo_id");

            migrationBuilder.CreateIndex(
                name: "ux_codigo_vinculacion_activo",
                schema: "vitalify",
                table: "codigo_vinculacion",
                column: "dispositivo_id",
                unique: true,
                filter: "usado_en IS NULL AND invalidado_en IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_codigo_vinculacion_token",
                schema: "vitalify",
                table: "codigo_vinculacion",
                column: "token_hash",
                unique: true,
                filter: "token_hash IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_hospitalizacion_usuario_escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion",
                column: "escala_spo2_cambiada_por",
                principalSchema: "vitalify",
                principalTable: "usuario",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hospitalizacion_usuario_escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion");

            migrationBuilder.DropTable(
                name: "codigo_vinculacion",
                schema: "vitalify");

            migrationBuilder.DropIndex(
                name: "ix_hospitalizacion_escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion");

            migrationBuilder.DropColumn(
                name: "confusion_basal",
                schema: "vitalify",
                table: "paciente");

            migrationBuilder.DropColumn(
                name: "escala_spo2",
                schema: "vitalify",
                table: "hospitalizacion");

            migrationBuilder.DropColumn(
                name: "escala_spo2_cambiada_en",
                schema: "vitalify",
                table: "hospitalizacion");

            migrationBuilder.DropColumn(
                name: "escala_spo2_cambiada_por",
                schema: "vitalify",
                table: "hospitalizacion");

            migrationBuilder.DropColumn(
                name: "motivo_escala_spo2",
                schema: "vitalify",
                table: "hospitalizacion");
        }
    }
}
