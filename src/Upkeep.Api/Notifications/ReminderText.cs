using Upkeep;

namespace Upkeep.Api.Notifications;

/// <summary>Manutenção pendente, já avaliada — insumo da formatação de lembretes.</summary>
public sealed record DueItem(
    string AssetNome,
    string TemplateTitulo,
    DueStatus Status,
    int? KmRemaining,
    DateOnly? DateDue);

/// <summary>
/// Formatação pura do lembrete ntfy (title + body) a partir dos itens vencidos/a vencer.
/// Mesmos labels do StatusService ("vencido"/"vence_em_breve"). Contrato: nunca recebe
/// lista vazia nem itens Ok — o serviço só chama quando há pendências.
/// </summary>
public static class ReminderText
{
    private const string LabelOverdue = "vencido";
    private const string LabelDueSoon = "vence_em_breve";

    public static (string Title, string Body) Format(IReadOnlyList<DueItem> items)
    {
        // separa preservando a ordem original dentro de cada grupo
        var overdue = new List<DueItem>();
        var dueSoon = new List<DueItem>();
        foreach (var item in items)
        {
            if (item.Status == DueStatus.Overdue) overdue.Add(item);
            else if (item.Status == DueStatus.DueSoon) dueSoon.Add(item);
        }

        var title = (overdue.Count, dueSoon.Count) switch
        {
            (> 0, > 0) => $"upkeep: {overdue.Count} vencido(s), {dueSoon.Count} vence(m) em breve",
            (> 0, _) => $"upkeep: {overdue.Count} vencido(s)",
            _ => $"upkeep: {dueSoon.Count} vence(m) em breve"
        };

        // vencidos primeiro — o mais urgente no topo do push
        var lines = overdue.Concat(dueSoon).Select(FormatLine);
        return (title, string.Join("\n", lines));
    }

    private static string FormatLine(DueItem item)
    {
        var line = $"- [{Label(item)}] {item.AssetNome}: {item.TemplateTitulo}";
        if (item.Status == DueStatus.Overdue && item.DateDue is { } venceuEm)
            line += $" · venceu {venceuEm:yyyy-MM-dd}";
        if (item.KmRemaining is int km and > 0)
            line += $" · faltam {km} km";
        if (item.Status == DueStatus.DueSoon && item.DateDue is { } venceEm)
            line += $" · vence {venceEm:yyyy-MM-dd}";
        return line;
    }

    private static string Label(DueItem item) =>
        item.Status == DueStatus.Overdue ? LabelOverdue : LabelDueSoon;
}
