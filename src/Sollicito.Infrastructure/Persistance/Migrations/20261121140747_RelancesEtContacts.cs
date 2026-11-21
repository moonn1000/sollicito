using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <summary>
    /// Les relances et les contacts (#14, absorbe #30) : deux tables, et une colonne des événements qui désigne la relance
    /// qu'une clôture ferme, unique, avec sa clé étrangère.
    /// <list type="bullet">
    /// <item>Une seule relance à faire par candidature : un index unique filtré sur l'état.</item>
    /// <item>Supprimer une candidature emporte ses relances ; supprimer un contact le retire de ses relances
    /// (<c>ON DELETE SET NULL</c>).</item>
    /// </list>
    /// <para>
    /// Comme pour #25, la colonne de la clôture porte sa clé dès son ajout, en une instruction : Entity Framework Core
    /// ajouterait sinon la clé en reconstruisant la table des événements, en plusieurs transactions. La migration reste
    /// une seule transaction, et les événements déjà enregistrés ne bougent pas.
    /// </para>
    /// </summary>
    public partial class RelancesEtContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntrepriseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contacts_Entreprises_EntrepriseId",
                        column: x => x.EntrepriseId,
                        principalTable: "Entreprises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Relances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Echeance = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Etat = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DestinataireId = table.Column<int>(type: "INTEGER", nullable: true),
                    CandidatureId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relances_Candidatures_CandidatureId",
                        column: x => x.CandidatureId,
                        principalTable: "Candidatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Relances_Contacts_DestinataireId",
                        column: x => x.DestinataireId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(
                """
                ALTER TABLE "EvenementsDeCandidature" ADD "RelanceId" INTEGER NULL
                    CONSTRAINT "FK_EvenementsDeCandidature_Relances_RelanceId"
                    REFERENCES "Relances" ("Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EvenementsDeCandidature_RelanceId",
                table: "EvenementsDeCandidature",
                column: "RelanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_EntrepriseId",
                table: "Contacts",
                column: "EntrepriseId");

            migrationBuilder.CreateIndex(
                name: "IX_Relances_DestinataireId",
                table: "Relances",
                column: "DestinataireId");

            migrationBuilder.CreateIndex(
                name: "IX_Relances_Etat_Echeance",
                table: "Relances",
                columns: new[] { "Etat", "Echeance" });

            migrationBuilder.CreateIndex(
                name: "IX_Relances_UneAFaireParCandidature",
                table: "Relances",
                column: "CandidatureId",
                unique: true,
                filter: "\"Etat\" = 'AFaire'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EvenementsDeCandidature_Relances_RelanceId",
                table: "EvenementsDeCandidature");

            migrationBuilder.DropTable(
                name: "Relances");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_EvenementsDeCandidature_RelanceId",
                table: "EvenementsDeCandidature");

            migrationBuilder.DropColumn(
                name: "RelanceId",
                table: "EvenementsDeCandidature");
        }
    }
}
