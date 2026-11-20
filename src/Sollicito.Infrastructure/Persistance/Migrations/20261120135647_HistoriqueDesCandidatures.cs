using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <inheritdoc />
    public partial class HistoriqueDesCandidatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvenementsDeCandidature",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SurvenuLe = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CandidatureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sorte = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Ancien = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Nouveau = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CorrigeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvenementsDeCandidature", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvenementsDeCandidature_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvenementsDeCandidature_EvenementsDeCandidature_CorrigeId",
                        column: x => x.CorrigeId,
                        principalTable: "EvenementsDeCandidature",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvenementsDeCandidature_CandidatureId",
                table: "EvenementsDeCandidature",
                column: "CandidatureId");

            migrationBuilder.CreateIndex(
                name: "IX_EvenementsDeCandidature_CorrigeId",
                table: "EvenementsDeCandidature",
                column: "CorrigeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvenementsDeCandidature");
        }
    }
}
