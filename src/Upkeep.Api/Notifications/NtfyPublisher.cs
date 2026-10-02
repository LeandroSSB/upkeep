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
    /// <summary>true = ntfy aceitou (2xx); false = rede fora ou resposta não-2xx (já logado aqui).</summary>
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

            logger.LogWarning("ntfy respondeu {Status} ao push do tópico {Topic}",
                (int)resp.StatusCode, topic);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false; // shutdown/cancelamento — não é falha de entrega
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar push ntfy para o tópico {Topic}", topic);
            return false;
        }
    }
}
