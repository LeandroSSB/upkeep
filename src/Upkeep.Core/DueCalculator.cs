namespace Upkeep;

public enum DueStatus
{
    Ok,
    DueSoon,
    Overdue
}

public sealed record DueBaseline(int? Odometro, DateOnly Data);

public sealed record DueResult(DueStatus Status, int? KmRemaining, DateOnly? DateDue);

public static class DueCalculator
{
    /// <summary>20% do intervalo (regra "vence em breve" da spec).</summary>
    public const double DueSoonFraction = 0.2;

    private const double DiasPorMes = 30.4375;
    private const int DueSoonDiasFixos = 30;

    public static DueResult Evaluate(MaintenanceTemplate template, DueBaseline baseline,
        int? currentOdometer, DateOnly today)
    {
        var overdue = false;
        var dueSoon = false;
        int? kmRemaining = null;
        DateOnly? dateDue = null;

        // Critério km ativo ⟺ IntervaloKm e odômetros (baseline e atual) presentes.
        if (template.IntervaloKm is int intervaloKm
            && baseline.Odometro is int baseOdo
            && currentOdometer is int odoAtual)
        {
            kmRemaining = baseOdo + intervaloKm - odoAtual;
            if (kmRemaining <= 0)
                overdue = true;
            else if (kmRemaining <= (int)Math.Ceiling(DueSoonFraction * intervaloKm))
                dueSoon = true;
        }

        // Critério tempo ativo ⟺ IntervaloMeses presente.
        if (template.IntervaloMeses is int intervaloMeses)
        {
            dateDue = baseline.Data.AddMonths(intervaloMeses);
            var diasRestantes = dateDue.Value.DayNumber - today.DayNumber;
            if (diasRestantes <= 0)
                overdue = true;
            else if (diasRestantes <= DueSoonDiasFixos
                     || diasRestantes <= DueSoonFraction * intervaloMeses * DiasPorMes)
                dueSoon = true;
        }

        var status = overdue ? DueStatus.Overdue : dueSoon ? DueStatus.DueSoon : DueStatus.Ok;
        return new DueResult(status, kmRemaining, dateDue);
    }
}
