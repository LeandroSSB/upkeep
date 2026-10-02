using System.Text;
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
    /// Teto do body em BYTES UTF-8. ntfy limita o request inteiro a 4096 bytes; o
    /// envelope JSON (topic ≤64 + title ~45 + tags/priority/chaves ~90) sobra ~200
    /// bytes, daí 3800 (e não 4000). Conta BYTES (não chars): nomes em pt-BR carregam
    /// acentos multibyte (ã/ç = 2 bytes cada) e um corpo "pequeno em chars" estoura
    /// o limite real do ntfy.
    /// </summary>
    public const int MaxBodyBytes = 3800;

    /// <summary>Espaço reservado para a linha final "… (+N itens)" ("…" = 3 bytes; ~17 bytes no pior caso).</summary>
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
    /// Trunca o body no limite de BYTES UTF-8 SEM cortar linha no meio: mantém
    /// linhas inteiras (as mais urgentes, que vêm primeiro) e fecha com "… (+N
    /// itens)" informando quantas ficaram de fora. Linha única maior que o teto
    /// (não ocorre na prática: nome/título ≤200 chars) recebe corte duro na
    /// própria linha — também por bytes, sempre em fronteira de char.
    /// </summary>
    internal static string TruncateBody(string body)
    {
        if (Encoding.UTF8.GetByteCount(body) <= MaxBodyBytes) return body;

        var lines = body.Split('\n');
        var kept = new List<string>();
        var used = 0;
        foreach (var line in lines)
        {
            var cost = Encoding.UTF8.GetByteCount(line) + (kept.Count > 0 ? 1 : 0); // +1 do '\n' (1 byte)
            if (used + cost > MaxBodyBytes - FinalLineReserve) break;
            kept.Add(line);
            used += cost;
        }
        if (kept.Count == 0)
            kept.Add(PrefixFittingBytes(lines[0], MaxBodyBytes - FinalLineReserve));

        return string.Join("\n", kept) + $"\n… (+{lines.Length - kept.Count} itens)";
    }

    /// <summary>Maior prefixo que cabe em maxBytes UTF-8 (busca binária: byteCount é monótono no prefixo).</summary>
    private static string PrefixFittingBytes(string s, int maxBytes)
    {
        var lo = 0;
        var hi = s.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Encoding.UTF8.GetByteCount(s[..mid]) <= maxBytes) lo = mid;
            else hi = mid - 1;
        }
        return s[..lo];
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
