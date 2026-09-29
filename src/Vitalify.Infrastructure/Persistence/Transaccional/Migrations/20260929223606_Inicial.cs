using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vitalify.Infrastructure.Persistence.Transaccional.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Crea el esquema del contexto (CREATE SCHEMA IF NOT EXISTS).
            migrationBuilder.EnsureSchema(name: "vitalify");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // El esquema no se elimina: también contiene la tabla __EFMigrationsHistory.
        }
    }
}
