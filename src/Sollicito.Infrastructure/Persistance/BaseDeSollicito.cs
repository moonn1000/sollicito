using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sollicito.Domain.Candidatures;
using Sollicito.Domain.Entreprises;
using Sollicito.Domain.Textes;

namespace Sollicito.Infrastructure.Persistance;

/// <summary>
/// La base SQLite de Sollicito (ADR-0001, D3), dont le schéma vient des migrations de <c>Persistance/Migrations</c>.
/// Les instants s'y écrivent en texte, en UTC, et se relisent en UTC (modèle de domaine, « Dates et heures »).
/// </summary>
public sealed class BaseDeSollicito(DbContextOptions<BaseDeSollicito> options) : DbContext(options)
{
    /// <summary>Les entreprises, reconnues par leur clé.</summary>
    public DbSet<Entreprise> Entreprises => Set<Entreprise>();

    /// <summary>Les candidatures.</summary>
    public DbSet<Candidature> Candidatures => Set<Candidature>();

    /// <summary>
    /// Les événements des candidatures, de toutes sortes, dans une seule table (modèle de domaine, « Événement de
    /// candidature : une entité, trois sortes ») : une colonne <c>Sorte</c> dit laquelle.
    /// </summary>
    public DbSet<EvenementDeCandidature> EvenementsDeCandidature => Set<EvenementDeCandidature>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQLite ne connaît pas les fuseaux : relu, un instant reprend son genre UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<InstantEnUtc>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Entreprise>(entreprise =>
        {
            entreprise.ToTable("Entreprises");
            entreprise.HasKey(e => e.Id);
            entreprise.Property(e => e.Nom).HasMaxLength(TexteDUneLigne.LongueurMaximale);
            entreprise.Property(e => e.Cle);
            // Même clé, même entreprise : la base refuse une seconde entreprise de même clé.
            entreprise.HasIndex(e => e.Cle).IsUnique();
        });

        modelBuilder.Entity<Candidature>(candidature =>
        {
            candidature.ToTable("Candidatures");
            candidature.HasKey(c => c.Id);
            candidature.Property(c => c.Poste).HasMaxLength(TexteDUneLigne.LongueurMaximale);
            candidature.Property(c => c.Statut).HasConversion<string>().HasMaxLength(20);
            // L'entreprise vit à part : supprimer une candidature la laisse (#25), et une entreprise qui a des
            // candidatures ne se supprime pas.
            candidature.HasOne<Entreprise>().WithMany().HasForeignKey(c => c.EntrepriseId).OnDelete(DeleteBehavior.Restrict);
            candidature.HasIndex(c => c.EnvoyeeLe);
            // Ses événements n'existent que par elle : supprimer la candidature les supprime aussi (#25). La
            // candidature les tient dans un champ, qu'elle seule complète.
            candidature.HasMany(c => c.Evenements).WithOne().HasForeignKey(ColonneDeLaCandidature).IsRequired().OnDelete(DeleteBehavior.Cascade);
            candidature.Navigation(c => c.Evenements).UsePropertyAccessMode(PropertyAccessMode.Field);
            // Calculés par le domaine à partir des événements et du statut : rien de plus en base.
            candidature.Ignore(c => c.Historique);
            candidature.Ignore(c => c.PassagesPermis);
            candidature.Ignore(c => c.ChangementACorriger);
        });

        modelBuilder.Entity<EvenementDeCandidature>(evenement =>
        {
            evenement.ToTable("EvenementsDeCandidature");
            evenement.HasKey(e => e.Id);
            evenement.HasDiscriminator<string>("Sorte")
                .HasValue<ChangementDeStatut>(nameof(ChangementDeStatut))
                .HasValue<Etape>(nameof(Etape));
            evenement.Property<string>("Sorte").HasMaxLength(30);
        });

        modelBuilder.Entity<ChangementDeStatut>(changement =>
        {
            changement.Property(c => c.Ancien).HasConversion<string>().HasMaxLength(20);
            changement.Property(c => c.Nouveau).HasConversion<string>().HasMaxLength(20);
            // Une correction désigne le changement qu'elle corrige, qui n'est corrigé qu'une fois : l'index est unique.
            // NO ACTION, et non RESTRICT : SQLite ne vérifie la clé qu'en fin d'instruction, quand la suppression d'une
            // candidature a emporté tous ses événements, correction et changement corrigé ensemble.
            changement.HasOne(c => c.Corrige).WithOne().HasForeignKey<ChangementDeStatut>("CorrigeId").OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Etape>(etape =>
        {
            etape.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            etape.Property(e => e.Note).HasMaxLength(Etape.LongueurMaximaleDeLaNote);
        });
    }

    // La clé de la candidature d'un événement : une colonne de la table, que le domaine ne montre pas.
    private const string ColonneDeLaCandidature = "CandidatureId";

    /// <summary>Un instant écrit tel quel, en UTC, et relu de genre <see cref="DateTimeKind.Utc"/>.</summary>
    private sealed class InstantEnUtc() : ValueConverter<DateTime, DateTime>(
        instant => instant,
        lu => DateTime.SpecifyKind(lu, DateTimeKind.Utc));
}
