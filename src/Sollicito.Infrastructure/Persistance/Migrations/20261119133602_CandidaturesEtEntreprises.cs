using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class CandidaturesEtEntreprises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Entreprises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Cle = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entreprises", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Candidatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntrepriseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Poste = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EnvoyeeLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidatures_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_EntrepriseId",
                table: "Candidatures",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_EnvoyeeLe",
                table: "Candidatures",
                column: "EnvoyeeLe");

            migrationBuilder.CreateIndex(
                name: "IX_Entreprises_Cle",
                table: "Entreprises",
                column: "Cle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Candidatures");

            migrationBuilder.DropTable(
                name: "Entreprises");
        }
    }
}
