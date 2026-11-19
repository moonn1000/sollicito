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
        });
    }

    /// <summary>Un instant écrit tel quel, en UTC, et relu de genre <see cref="DateTimeKind.Utc"/>.</summary>
    private sealed class InstantEnUtc() : ValueConverter<DateTime, DateTime>(
        instant => instant,
        lu => DateTime.SpecifyKind(lu, DateTimeKind.Utc));
}
