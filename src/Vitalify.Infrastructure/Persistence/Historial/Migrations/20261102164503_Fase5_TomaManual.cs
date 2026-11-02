using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Historial.Migrations
{
    /// <inheritdoc />
    public partial class Fase5_TomaManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brazo_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checklist_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contexto",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dispositivo_oxigeno",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fio2",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "flujo_oxigeno_lpm",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lecturas_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metodo_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "objetivo_spo2_88a92",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "posicion_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "preocupacion_clinica",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version_guia",
                schema: "vitalify_historial",
                table: "observacion_enfermeria",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brazo_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "checklist_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "contexto",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "dispositivo_oxigeno",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "fio2",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "flujo_oxigeno_lpm",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "lecturas_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "metodo_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "objetivo_spo2_88a92",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "posicion_pa",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "preocupacion_clinica",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");

            migrationBuilder.DropColumn(
                name: "version_guia",
                schema: "vitalify_historial",
                table: "observacion_enfermeria");
        }
    }
}
