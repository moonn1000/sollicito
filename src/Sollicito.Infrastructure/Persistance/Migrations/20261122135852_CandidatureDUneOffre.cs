using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sollicito.Infrastructure.Persistance.Migrations
{
    /// <summary>
    /// Le lien entre une offre et sa candidature (#38, ADR-0014) : une colonne des candidatures qui désigne l'offre d'où
    /// elle vient, nulle, avec sa clé étrangère, et un index unique.
    /// <list type="bullet">
    /// <item>Une offre fait l'objet d'au plus une candidature : l'index unique (SQLite y admet plusieurs <c>NULL</c>).</item>
    /// <item>Une candidature supprimée emporte sa ligne, donc le lien : l'offre reste. Une offre supprimée, ce que la V1
    /// ne fait jamais, laisserait sa candidature, sans offre (<c>ON DELETE SET NULL</c>).</item>
    /// </list>
    /// <para>
    /// Comme pour #25 et #14, la colonne porte sa clé dès son ajout, en une instruction : Entity Framework Core ajouterait
    /// sinon la clé en reconstruisant la table des candidatures, en plusieurs transactions. Aucune table n'est reconstruite,
    /// aucune ligne réécrite : la migration est une seule transaction (<c>MigrationsEnUneTransactionTests</c>).
    /// </para>
    /// <para>
    /// L'instruction tient sur une ligne : SQLite recopie la définition de la colonne dans le texte de la table, que la
    /// restauration (#28, ADR-0012) compare à celui de l'exe. Écrite sur plusieurs lignes, elle y mettrait les fins de ligne
    /// du fichier source, CRLF ou LF selon la machine qui construit l'exe.
    /// </para>
    /// </summary>
    public partial class CandidatureDUneOffre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Candidatures\" ADD \"OffreId\" INTEGER NULL CONSTRAINT \"FK_Candidatures_Offres_OffreId\" REFERENCES \"Offres\" (\"Id\") ON DELETE SET NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Candidatures_OffreId",
                table: "Candidatures",
                column: "OffreId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Candidatures_Offres_OffreId",
                table: "Candidatures");

            migrationBuilder.DropIndex(
                name: "IX_Candidatures_OffreId",
                table: "Candidatures");

            migrationBuilder.DropColumn(
                name: "OffreId",
                table: "Candidatures");
        }
    }
}
