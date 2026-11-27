using System.Runtime.CompilerServices;
using Sollicito.Application.Export;

namespace Sollicito.Application.Tests;

/// <summary>Un instantané factice : des listes, ou une lecture paresseuse des offres.</summary>
internal sealed class InstantaneFactice : IInstantaneDeLaBase
{
    public EntrepriseExportee[] Entreprises { get; init; } = [];

    public ContactExporte[] Contacts { get; init; } = [];

    public OffreExportee[] Offres { get; init; } = [];

    public Func<IAsyncEnumerable<OffreExportee>>? LectureDesOffres { get; init; }

    public CandidatureExportee[] Candidatures { get; init; } = [];

    public RelanceExportee[] Relances { get; init; } = [];

    public EvenementExporte[] Evenements { get; init; } = [];

    IAsyncEnumerable<EntrepriseExportee> IInstantaneDeLaBase.Entreprises(CancellationToken annulation) => Lire(Entreprises, annulation);

    IAsyncEnumerable<ContactExporte> IInstantaneDeLaBase.Contacts(CancellationToken annulation) => Lire(Contacts, annulation);

    IAsyncEnumerable<OffreExportee> IInstantaneDeLaBase.Offres(CancellationToken annulation) => LectureDesOffres?.Invoke() ?? Lire(Offres, annulation);

    IAsyncEnumerable<CandidatureExportee> IInstantaneDeLaBase.Candidatures(CancellationToken annulation) => Lire(Candidatures, annulation);

    IAsyncEnumerable<RelanceExportee> IInstantaneDeLaBase.Relances(CancellationToken annulation) => Lire(Relances, annulation);

    IAsyncEnumerable<EvenementExporte> IInstantaneDeLaBase.Evenements(CancellationToken annulation) => Lire(Evenements, annulation);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async IAsyncEnumerable<T> Lire<T>(T[] lignes, [EnumeratorCancellation] CancellationToken annulation)
    {
        foreach (var ligne in lignes)
        {
            annulation.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return ligne;
        }
    }
}

/// <summary>Un flux qui ne garde rien, et note, pour chaque écriture, sa taille et le nombre de lignes déjà lues.</summary>
internal sealed class FluxQuiNoteSesEcritures(Func<int> lignesLues) : Stream
{
    public List<(int Octets, int LignesLues)> Ecritures { get; } = [];

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Ecritures.Add((count, lignesLues()));

    public override void Write(ReadOnlySpan<byte> buffer) => Ecritures.Add((buffer.Length, lignesLues()));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Ecritures.Add((buffer.Length, lignesLues()));
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Ecritures.Add((count, lignesLues()));
        return Task.CompletedTask;
    }
}
