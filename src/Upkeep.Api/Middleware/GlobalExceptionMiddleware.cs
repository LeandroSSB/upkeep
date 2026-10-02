using System.Text.Json;

namespace Upkeep.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // cliente foi embora (fechou a conexão/timeout) — não há resposta a escrever;
            // não é defeito do servidor, não vira 500 nem Error no log
            logger.LogDebug("Request cancelado pelo cliente {TraceId}", context.TraceIdentifier);
            return;
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
                throw; // exceção mid-response: resposta já começou a ser enviada, escrever
                       // um 500 corromperia o corpo — rethrow para o host logar/abortar

            logger.LogError(ex, "Unhandled exception {TraceId}", context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "An error occurred while processing your request.",
                status = 500,
                traceId = context.TraceIdentifier
            }));
        }
    }
}
