using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <summary>
    /// La correction d'une étape (#25) : une colonne qui désigne l'étape corrigée, unique, et sa clé étrangère.
    /// <para>
    /// Sous SQLite, Entity Framework Core n'ajoute une clé étrangère qu'en reconstruisant la table, en plusieurs
    /// transactions, les clés étrangères coupées entre deux : un arrêt au milieu laisserait une table temporaire, et le
    /// lancement suivant échouerait. Une colonne qui porte sa clé s'ajoute, elle, en une instruction : la migration reste
    /// une seule transaction, et les événements déjà enregistrés ne bougent pas.
    /// </para>
    /// </summary>
    public partial class CorrectionsDesEtapes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "EvenementsDeCandidature" ADD "EtapeCorrigeeId" INTEGER NULL
                    CONSTRAINT "FK_EvenementsDeCandidature_EvenementsDeCandidature_EtapeCorrigeeId"
                    REFERENCES "EvenementsDeCandidature" ("Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EvenementsDeCandidature_EtapeCorrigeeId",
                table: "EvenementsDeCandidature",
                column: "EtapeCorrigeeId",
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Sollicito ne lance jamais <c>Down()</c> : revenir à une version précédente passe par la copie d'avant la mise à
        /// jour (ADR-0008). Seul <c>dotnet ef database update</c> vers une migration antérieure le jouerait, et il n'est pas
        /// pris en charge (#85, point 1) : sous SQLite, Entity Framework Core reconstruit alors la table en trois
        /// transactions, un arrêt au milieu laisse une table temporaire, et chaque correction d'étape perd son lien : elle
        /// redevient une étape en vigueur, à côté de celle qu'elle corrigeait.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EvenementsDeCandidature_EvenementsDeCandidature_EtapeCorrigeeId",
                table: "EvenementsDeCandidature");

            migrationBuilder.DropIndex(
                name: "IX_EvenementsDeCandidature_EtapeCorrigeeId",
                table: "EvenementsDeCandidature");

            migrationBuilder.DropColumn(
                name: "EtapeCorrigeeId",
                table: "EvenementsDeCandidature");
        }
    }
}
