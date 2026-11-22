using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <summary>
    /// Les offres collées (#36, ADR-0013) : une table neuve, sa clé vers les entreprises et deux index.
    /// <list type="bullet">
    /// <item>Règle 2 : une seule offre par source et par empreinte, un index unique.</item>
    /// <item>Règle 3 : les doublons possibles se cherchent par entreprise et poste normalisé.</item>
    /// <item>Une offre peut taire son entreprise ; une entreprise qu'une offre désigne ne se supprime pas (<c>RESTRICT</c>).</item>
    /// </list>
    /// <para>
    /// Aucune table existante n'est touchée ni reconstruite : la migration est une seule transaction (vérifié sur son
    /// script SQL), et les candidatures, leurs événements, les contacts et les relances ne bougent pas. Le lien entre une
    /// offre et sa candidature attend #38 ; l'identifiant externe d'une source qui en fournit un attend le Forem (V2).
    /// </para>
    /// </summary>
    public partial class Offres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Offres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Empreinte = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntrepriseId = table.Column<int>(type: "INTEGER", nullable: true),
                    Poste = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PosteNormalise = table.Column<string>(type: "TEXT", nullable: false),
                    Lieu = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PublieeLe = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Adresse = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Texte = table.Column<string>(type: "TEXT", maxLength: 50000, nullable: true),
                    AjouteeLe = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Offres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Offres_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Offres_EntrepriseId_PosteNormalise",
                table: "Offres",
                columns: new[] { "EntrepriseId", "PosteNormalise" });

            migrationBuilder.CreateIndex(
                name: "IX_Offres_Source_Empreinte",
                table: "Offres",
                columns: new[] { "Source", "Empreinte" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Offres");
        }
    }
}
