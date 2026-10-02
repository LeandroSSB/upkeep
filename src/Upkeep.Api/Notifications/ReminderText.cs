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

    /// <summary>
    /// Teto do body em CHARS. ntfy limita o request inteiro a 4096 bytes UTF-8; o
    /// envelope JSON (topic ≤64 + title ~45 + tags/priority/chaves ~90) sobra ~200
    /// bytes, daí 3800 (e não 4000). Aproximação por chars≈bytes: linha de lembrete
    /// típica é quase toda ASCII (nomes/labels); a folga absorve multibyte pontual.
    /// </summary>
    public const int MaxBodyChars = 3800;

    /// <summary>Espaço reservado para a linha final "… (+N itens)" (16 chars no pior caso).</summary>
    private const int FinalLineReserve = 20;

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
        return (title, TruncateBody(string.Join("\n", lines)));
    }

    /// <summary>
    /// Trunca o body no limite de chars SEM cortar linha no meio: mantém linhas
    /// inteiras (as mais urgentes, que vêm primeiro) e fecha com "… (+N itens)"
    /// informando quantas ficaram de fora. Linha única maior que o teto (não ocorre
    /// na prática: nome/título ≤200 chars) recebe corte duro na própria linha.
    /// </summary>
    internal static string TruncateBody(string body)
    {
        if (body.Length <= MaxBodyChars) return body;

        var lines = body.Split('\n');
        var kept = new List<string>();
        var used = 0;
        foreach (var line in lines)
        {
            var cost = line.Length + (kept.Count > 0 ? 1 : 0); // +1 do '\n' separador
            if (used + cost > MaxBodyChars - FinalLineReserve) break;
            kept.Add(line);
            used += cost;
        }
        if (kept.Count == 0)
            kept.Add(lines[0][..(MaxBodyChars - FinalLineReserve)]);

        return string.Join("\n", kept) + $"\n… (+{lines.Length - kept.Count} itens)";
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
