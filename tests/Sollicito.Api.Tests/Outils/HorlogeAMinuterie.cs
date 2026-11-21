namespace Sollicito.Api.Tests.Outils;

/// <summary>
/// Une horloge de test dont les minuteries ne partent que quand le test la fait avancer : la vérification de chaque heure
/// des copies du jour (#26) se prouve sans attendre une heure. Le fuseau est celui de Windows en Belgique, comme pour
/// <see cref="HorlogeDeTest"/>.
/// </summary>
public sealed class HorlogeAMinuterie(DateTimeOffset maintenant) : TimeProvider
{
    private readonly Lock _verrou = new();
    private readonly List<Minuterie> _minuteries = [];
    private DateTimeOffset _maintenant = maintenant;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_verrou)
        {
            return _maintenant;
        }
    }

    public override TimeZoneInfo LocalTimeZone => HorlogeDeTest.Bruxelles;

    /// <summary>Le nombre de minuteries actives, celles qui attendent une échéance.</summary>
    public int MinuteriesActives
    {
        get
        {
            lock (_verrou)
            {
                return _minuteries.Count(minuterie => minuterie.Echeance is not null);
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var minuterie = new Minuterie(this, callback, state);
        lock (_verrou)
        {
            _minuteries.Add(minuterie);
            minuterie.Regler(_maintenant, dueTime, period);
        }

        return minuterie;
    }

    /// <summary>Fait avancer l'horloge, et part chaque minuterie dont l'échéance est atteinte, dans l'ordre.</summary>
    public void Avancer(TimeSpan duree)
    {
        DateTimeOffset fin;
        lock (_verrou)
        {
            fin = _maintenant + duree;
        }

        while (true)
        {
            Minuterie? prochaine;
            lock (_verrou)
            {
                prochaine = _minuteries
                    .Where(minuterie => minuterie.Echeance is { } echeance && echeance <= fin)
                    .MinBy(minuterie => minuterie.Echeance);
                if (prochaine is null)
                {
                    _maintenant = fin;
                    return;
                }

                _maintenant = prochaine.Echeance!.Value;
                prochaine.Echoir();
            }

            prochaine.Partir();
        }
    }

    private sealed class Minuterie(HorlogeAMinuterie horloge, TimerCallback rappel, object? etat) : ITimer
    {
        private TimeSpan _periode = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Echeance { get; private set; }

        // Sous le verrou de l'horloge.
        public void Regler(DateTimeOffset maintenant, TimeSpan delai, TimeSpan periode)
        {
            _periode = periode;
            Echeance = delai == Timeout.InfiniteTimeSpan ? null : maintenant + delai;
        }

        // Sous le verrou de l'horloge : la prochaine échéance, ou aucune.
        public void Echoir() => Echeance = _periode == Timeout.InfiniteTimeSpan || _periode == TimeSpan.Zero ? null : Echeance + _periode;

        public void Partir() => rappel(etat);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (horloge._verrou)
            {
                Regler(horloge._maintenant, dueTime, period);
            }

            return true;
        }

        public void Dispose()
        {
            lock (horloge._verrou)
            {
                Echeance = null;
                horloge._minuteries.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
