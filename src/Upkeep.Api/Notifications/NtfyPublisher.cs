using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Upkeep.Api.Notifications;

/// <summary>
/// Envio de 1 push ntfy — o ÚNICO lugar com a forma do POST (topic/title/message/
/// tags/priority no Server do NtfyOptions, via client "ntfy"). Compartilhado entre
/// a varredura diária (DueReminderService) e o teste instantâneo de /me/ntfy-topic/test.
/// </summary>
public interface INtfyPublisher
{
    /// <summary>
    /// true = ntfy aceitou (2xx); false = rede fora ou resposta não-2xx (já logado aqui).
    /// Cancelamento real do ct PROPAGA (OperationCanceledException) — não vira falha.
    /// </summary>
    Task<bool> PublishAsync(
        string topic, string title, string message, string[] tags, int priority, CancellationToken ct);
}

public sealed class NtfyPublisher(
    IHttpClientFactory httpFactory,
    ILogger<NtfyPublisher> logger,
    IOptions<NtfyOptions> options) : INtfyPublisher
{
    public async Task<bool> PublishAsync(
        string topic, string title, string message, string[] tags, int priority, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("ntfy");
            var resp = await http.PostAsJsonAsync(options.Value.Server, new
            {
                topic, title, message, tags, priority
            }, ct);
            if (resp.IsSuccessStatusCode) return true;

            // Corpo do erro (limitado, quebra de linha achatada) no log: distingue
            // 429 "too many requests" de 502 outage sem precisar dig no servidor.
            var corpo = (await resp.Content.ReadAsStringAsync(CancellationToken.None))
                .Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (corpo.Length > 200) corpo = corpo[..200];
            logger.LogWarning("ntfy respondeu {Status} ao push do tópico {Topic}: {Corpo}",
                (int)resp.StatusCode, topic, corpo);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelamento REAL (shutdown/término): propaga em vez de devolver false —
            // senão a varredura trataria o shutdown como "falha por usuário" e seguiria
            // tentando os demais usuários com o token já cancelado. Timeout do HttpClient
            // (TaskCanceledException SEM ct cancelado) não cai aqui: vai pro catch geral.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar push ntfy para o tópico {Topic}", topic);
            return false;
        }
    }
}
