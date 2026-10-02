using Microsoft.Extensions.Options;

namespace Upkeep.Api.Notifications;

/// <summary>
/// Varredura diária: dorme até a próxima ocorrência de Ntfy:CheckHourUtc (UTC) e chama
/// SendDueRemindersAsync. Nada roda no startup — só no horário agendado. Só é registrado
/// quando Ntfy:Enabled=true. Falha da varredura é logada e o loop segue pro próximo dia.
/// </summary>
public sealed class NtfyReminderWorker(
    IServiceProvider services,
    ILogger<NtfyReminderWorker> logger,
    IOptions<NtfyOptions> ntfy) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddHours(ntfy.Value.CheckHourUtc);
            if (next <= now)
                next = next.AddDays(1);

            try
            {
                await Task.Delay(next - now, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // shutdown do host
            }

            // BackgroundService é singleton; DbContext é scoped → scope por execução.
            using var scope = services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
            try
            {
                var n = await svc.SendDueRemindersAsync(stoppingToken);
                logger.LogInformation("ntfy: {N} lembrete(s) enviados", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ntfy: falha na varredura diária");
            }
        }
    }
}
