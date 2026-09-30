using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Fase1_UsuariosYSesiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vitalify");

            migrationBuilder.CreateTable(
                name: "usuario",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    correo = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    hash_contrasena = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "auditoria",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditoria_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sesion_refresco",
                schema: "vitalify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash_token = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    creada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ultimo_uso_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revocada_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reemplazada_por_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sesion_refresco", x => x.id);
                    table.ForeignKey(
                        name: "fk_sesion_refresco_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "token_revocado",
                schema: "vitalify",
                columns: table => new
                {
                    jti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expira_en = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_token_revocado", x => x.jti);
                    table.ForeignKey(
                        name: "fk_token_revocado_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalSchema: "vitalify",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_fecha",
                schema: "vitalify",
                table: "auditoria",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_usuario_id",
                schema: "vitalify",
                table: "auditoria",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_refresco_hash_token",
                schema: "vitalify",
                table: "sesion_refresco",
                column: "hash_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesion_refresco_usuario_id",
                schema: "vitalify",
                table: "sesion_refresco",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_token_revocado_expira_en",
                schema: "vitalify",
                table: "token_revocado",
                column: "expira_en");

            migrationBuilder.CreateIndex(
                name: "ix_token_revocado_usuario_id",
                schema: "vitalify",
                table: "token_revocado",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_rol",
                schema: "vitalify",
                table: "usuario",
                column: "rol");

            migrationBuilder.CreateIndex(
                name: "ux_usuario_correo",
                schema: "vitalify",
                table: "usuario",
                column: "correo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "sesion_refresco",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "token_revocado",
                schema: "vitalify");

            migrationBuilder.DropTable(
                name: "usuario",
                schema: "vitalify");
        }
    }
}
