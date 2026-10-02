using Upkeep;
using Xunit;

namespace Upkeep.UnitTests;

public class DueCalculatorTests
{
    private static readonly DateOnly Hoje = new(2026, 10, 2);

    private static MaintenanceTemplate Template(int? km = null, int? meses = null,
        int? baselineOdo = null, DateOnly? baselineData = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Titulo = "T",
            Categoria = null,
            IntervaloKm = km,
            IntervaloMeses = meses,
            CustoEstimado = null,
            BaselineOdometro = baselineOdo,
            BaselineData = baselineData ?? new DateOnly(2026, 1, 1)
        };

    [Fact]
    public void So_tempo_ok_quando_longe_do_vencimento()
    {
        var t = Template(meses: 12, baselineData: new DateOnly(2026, 1, 1));
        var r = DueCalculator.Evaluate(t, new DueBaseline(null, new DateOnly(2026, 1, 1)), null, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status);
        Assert.Null(r.KmRemaining);
        Assert.Equal(new DateOnly(2027, 1, 1), r.DateDue);
    }

    [Fact]
    public void So_tempo_vencido()
    {
        var t = Template(meses: 3, baselineData: new DateOnly(2026, 1, 1));
        var r = DueCalculator.Evaluate(t, new DueBaseline(null, new DateOnly(2026, 1, 1)), null, Hoje);
        Assert.Equal(DueStatus.Overdue, r.Status);
    }

    [Fact]
    public void So_tempo_vence_em_breve_por_30_dias()
    {
        // vence 2026-10-20 → faltam 18 dias ≤ 30
        var t = Template(meses: 12, baselineData: new DateOnly(2025, 10, 20));
        var r = DueCalculator.Evaluate(t, new DueBaseline(null, new DateOnly(2025, 10, 20)), null, Hoje);
        Assert.Equal(DueStatus.DueSoon, r.Status);
    }

    [Fact]
    public void So_tempo_vence_em_breve_por_20_porcento_do_intervalo()
    {
        // intervalo 60 meses: 20% = 12 meses; vence 2027-06-01, faltam ~242 dias < 365
        var t = Template(meses: 60, baselineData: new DateOnly(2022, 6, 1));
        var r = DueCalculator.Evaluate(t, new DueBaseline(null, new DateOnly(2022, 6, 1)), null, Hoje);
        Assert.Equal(DueStatus.DueSoon, r.Status);
    }

    [Fact]
    public void So_km_vencido()
    {
        var t = Template(km: 10_000, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), 61_000, Hoje);
        Assert.Equal(DueStatus.Overdue, r.Status);
        Assert.Equal(-1_000, r.KmRemaining);
        Assert.Null(r.DateDue);
    }

    [Fact]
    public void So_km_ok()
    {
        var t = Template(km: 10_000, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), 55_000, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status);
        Assert.Equal(5_000, r.KmRemaining);
    }

    [Fact]
    public void So_km_vence_em_breve()
    {
        // falta 1.500 de 10.000 (≤ 20%)
        var t = Template(km: 10_000, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), 58_500, Hoje);
        Assert.Equal(DueStatus.DueSoon, r.Status);
    }

    [Fact]
    public void Sem_odometro_atual_criterio_km_inativo()
    {
        var t = Template(km: 10_000, meses: 12, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), null, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status); // tempo: 12m desde jan/26 → ok
        Assert.Null(r.KmRemaining);
    }

    [Fact]
    public void Sem_baseline_odometro_criterio_km_inativo()
    {
        var t = Template(km: 10_000, baselineOdo: null);
        var r = DueCalculator.Evaluate(t, new DueBaseline(null, new DateOnly(2026, 1, 1)), 60_000, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status);
    }

    [Fact]
    public void Ambos_km_vence_primeiro()
    {
        var t = Template(km: 5_000, meses: 24, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), 55_500, Hoje);
        Assert.Equal(DueStatus.Overdue, r.Status);
    }

    [Fact]
    public void Ambos_ok()
    {
        var t = Template(km: 10_000, meses: 12, baselineOdo: 50_000);
        var r = DueCalculator.Evaluate(t, new DueBaseline(50_000, new DateOnly(2026, 1, 1)), 52_000, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status);
        Assert.Equal(8_000, r.KmRemaining);
        Assert.Equal(new DateOnly(2027, 1, 1), r.DateDue);
    }

    [Fact]
    public void Baseline_do_ultimo_servico_reseta_o_ciclo()
    {
        var t = Template(km: 10_000, baselineOdo: 50_000);
        // último serviço: km 60.000 → vence em 70.000; atual 65.000 → ok
        var r = DueCalculator.Evaluate(t, new DueBaseline(60_000, new DateOnly(2026, 6, 1)), 65_000, Hoje);
        Assert.Equal(DueStatus.Ok, r.Status);
        Assert.Equal(5_000, r.KmRemaining);
    }
}
