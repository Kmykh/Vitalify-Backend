using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase6_ServiciosYTurnos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Catálogo de servicios.
            migrationBuilder.CreateTable(
                name: "servicio",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicio", x => x.id);
                });

            // 2. Los servicios que ya estaban escritos en las camas pasan a la tabla, con el código derivado del nombre
            //    (la misma regla que Servicio.CodigoDesdeNombre: sin tildes, mayúsculas y guiones; "Medicina B" → MEDICINA-B).
            migrationBuilder.AddColumn<Guid>(
                name: "servicio_id",
                schema: "vitalify",
                table: "cama",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO vitalify.servicio (id, codigo, nombre, activo)
                SELECT gen_random_uuid(),
                       trim(both '-' from left(trim(both '-' from regexp_replace(
                           upper(translate(s.servicio, 'áéíóúüñÁÉÍÓÚÜÑ', 'aeiouunAEIOUUN')), '[^A-Z0-9]+', '-', 'g')), 30)),
                       s.servicio,
                       true
                FROM (SELECT DISTINCT servicio FROM vitalify.cama) AS s;

                UPDATE vitalify.cama AS c
                SET servicio_id = s.id
                FROM vitalify.servicio AS s
                WHERE s.nombre = c.servicio;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "servicio_id",
                schema: "vitalify",
                table: "cama",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "servicio",
                schema: "vitalify",
                table: "cama");

            // 3. Servicio de cada enfermera y médico (opcional para las cuentas que ya existen).
            migrationBuilder.AddColumn<Guid>(
                name: "servicio_id",
                schema: "vitalify",
                table: "usuario",
                type: "uuid",
                nullable: true);

            // 4. Programación mensual de turnos.
            migrationBuilder.CreateTable(
                name: "programacion_turno",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo_turno = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    letra = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    cargado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    cargado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programacion_turno", x => x.id);
                    table.ForeignKey(
                        name: "fk_programacion_turno_servicio_servicio_id",
                        column: x => x.servicio_id,
                        principalSchema: "vitalify",
                        principalTable: "servicio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_programacion_turno_usuario_cargado_por",
                        column: x => x.cargado_por,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_programacion_turno_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuario_servicio_id",
                schema: "vitalify",
                table: "usuario",
                column: "servicio_id");

            migrationBuilder.CreateIndex(
                name: "ix_cama_servicio_id",
                schema: "vitalify",
                table: "cama",
                column: "servicio_id");

            migrationBuilder.CreateIndex(
                name: "ix_programacion_turno_cargado_por",
                schema: "vitalify",
                table: "programacion_turno",
                column: "cargado_por");

            migrationBuilder.CreateIndex(
                name: "ix_programacion_turno_usuario_id_fecha",
                schema: "vitalify",
                table: "programacion_turno",
                columns: new[] { "usuario_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ux_programacion_turno",
                schema: "vitalify",
                table: "programacion_turno",
                columns: new[] { "servicio_id", "fecha", "tipo_turno", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_servicio_codigo",
                schema: "vitalify",
                table: "servicio",
                column: "codigo",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_cama_servicio_servicio_id",
                schema: "vitalify",
                table: "cama",
                column: "servicio_id",
                principalSchema: "vitalify",
                principalTable: "servicio",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_usuario_servicio_servicio_id",
                schema: "vitalify",
                table: "usuario",
                column: "servicio_id",
                principalSchema: "vitalify",
                principalTable: "servicio",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cama_servicio_servicio_id",
                schema: "vitalify",
                table: "cama");

            migrationBuilder.DropForeignKey(
                name: "fk_usuario_servicio_servicio_id",
                schema: "vitalify",
                table: "usuario");

            migrationBuilder.DropTable(
                name: "programacion_turno",
                schema: "vitalify");

            // La cama vuelve a guardar el nombre del servicio como texto.
            migrationBuilder.AddColumn<string>(
                name: "servicio",
                schema: "vitalify",
                table: "cama",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE vitalify.cama AS c
                SET servicio = s.nombre
                FROM vitalify.servicio AS s
                WHERE s.id = c.servicio_id;
                """);

            migrationBuilder.DropIndex(
                name: "ix_usuario_servicio_id",
                schema: "vitalify",
                table: "usuario");

            migrationBuilder.DropIndex(
                name: "ix_cama_servicio_id",
                schema: "vitalify",
                table: "cama");

            migrationBuilder.DropColumn(
                name: "servicio_id",
                schema: "vitalify",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "servicio_id",
                schema: "vitalify",
                table: "cama");

            migrationBuilder.DropTable(
                name: "servicio",
                schema: "vitalify");
        }
    }
}
