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
    // Clamp defensivo 0-23: config fora do intervalo (ex.: CheckHourUtc=25) não derruba
    // o boot — ajusta para o equivalente dentro do dia e avisa no log.
    private readonly int _checkHourUtc = ClampHour(ntfy.Value.CheckHourUtc, logger);

    private static int ClampHour(int configured, ILogger<NtfyReminderWorker> log)
    {
        var hour = ((configured % 24) + 24) % 24;
        if (hour != configured)
            log.LogWarning(
                "Ntfy:CheckHourUtc={Configurado} fora de 0-23 — usando {Ajustado} (UTC)",
                configured, hour);
        return hour;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddHours(_checkHourUtc);
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
