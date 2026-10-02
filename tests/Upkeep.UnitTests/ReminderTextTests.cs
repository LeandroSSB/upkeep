using Upkeep;
using Upkeep.Api.Notifications;
using Xunit;

namespace Upkeep.UnitTests;

/// <summary>
/// ntfy v1.1: formatação pura dos lembretes (title + body) a partir dos DueItems
/// vencidos/a vencer — mesmos labels do StatusService ("vencido"/"vence_em_breve").
/// </summary>
public class ReminderTextTests
{
    [Fact]
    public void So_overdue_titulo_conta_vencidos_e_linhas_com_label_vencido()
    {
        var items = new List<DueItem>
        {
            new("Gol", "Troca de óleo", DueStatus.Overdue, KmRemaining: null, DateDue: new DateOnly(2026, 9, 1)),
            new("Casa", "Pintura", DueStatus.Overdue, KmRemaining: null, DateDue: null)
        };

        var (title, body) = ReminderText.Format(items);

        Assert.Equal("upkeep: 2 vencido(s)", title);
        Assert.Equal(
            "- [vencido] Gol: Troca de óleo · venceu 2026-09-01\n" +
            "- [vencido] Casa: Pintura",
            body);
    }

    [Fact]
    public void So_duesoon_titulo_vence_em_breve_e_sufixos_km_e_data()
    {
        var items = new List<DueItem>
        {
            new("Gol", "Troca de óleo", DueStatus.DueSoon, KmRemaining: 1_500, DateDue: new DateOnly(2026, 11, 1))
        };

        var (title, body) = ReminderText.Format(items);

        Assert.Equal("upkeep: 1 vence(m) em breve", title);
        Assert.Equal("- [vence_em_breve] Gol: Troca de óleo · faltam 1500 km · vence 2026-11-01", body);
    }

    [Fact]
    public void Misto_titulo_com_as_duas_partes_e_overdue_antes_de_duesoon()
    {
        var items = new List<DueItem>
        {
            new("Gol", "Troca de óleo", DueStatus.DueSoon, KmRemaining: 1_500, DateDue: new DateOnly(2026, 11, 1)),
            new("Casa", "Pintura", DueStatus.Overdue, KmRemaining: null, DateDue: new DateOnly(2026, 8, 1)),
            new("Bike", "Revisão", DueStatus.DueSoon, KmRemaining: 900, DateDue: null)
        };

        var (title, body) = ReminderText.Format(items);

        Assert.Equal("upkeep: 1 vencido(s), 2 vence(m) em breve", title);
        var lines = body.Split("\n");
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("- [vencido] Casa: Pintura", lines[0]); // overdue primeiro
        Assert.StartsWith("- [vence_em_breve] Gol: Troca de óleo", lines[1]);
        Assert.StartsWith("- [vence_em_breve] Bike: Revisão", lines[2]);
    }

    [Fact]
    public void Sufixos_venceu_para_overdue_com_data_e_faltam_km_quando_positivo()
    {
        var (t1, b1) = ReminderText.Format(
            [new DueItem("Gol", "Revisão", DueStatus.Overdue, KmRemaining: null, DateDue: new DateOnly(2026, 9, 1))]);
        Assert.Equal("upkeep: 1 vencido(s)", t1);
        Assert.Equal("- [vencido] Gol: Revisão · venceu 2026-09-01", b1);

        var (_, b2) = ReminderText.Format(
            [new DueItem("Gol", "Revisão", DueStatus.DueSoon, KmRemaining: 1_500, DateDue: null)]);
        Assert.Equal("- [vence_em_breve] Gol: Revisão · faltam 1500 km", b2);
    }
}
