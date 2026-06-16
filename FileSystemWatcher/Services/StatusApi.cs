using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WinAgent.FileSystemWatcher.Configuration;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class StatusApi : IDisposable
{
    private readonly WebApplication _app;
    private readonly TaskCompletionSource _startedTcs = new();

    public StatusApi(
        WatcherOptions options,
        EventRepository repository,
        EventBroadcaster broadcaster,
        DateTimeOffset startedAt)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Loopback, options.Port);
        });

        builder.Services.Configure<JsonOptions>(jsonOptions =>
        {
            jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        _app = builder.Build();

        _app.MapGet("/health", () => Results.Ok(new { status = "OK" }));

        _app.MapGet("/status", () =>
        {
            var counts = repository.GetCounts();
            return Results.Ok(new
            {
                watchedPaths = options.WatchedPaths,
                uptimeSeconds = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
                eventCounts = counts,
                port = options.Port
            });
        });

        _app.MapGet("/events", (int? count) =>
        {
            var events = repository.GetRecentEvents(count ?? 50);
            return Results.Ok(events);
        });

        _app.MapGet("/events/stream", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";

            try
            {
                await foreach (var fileEvent in broadcaster.ReadAllAsync(cancellationToken).WithCancellation(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(fileEvent, JsonEventSerializer.Options);
                    await context.Response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on client disconnect or shutdown.
            }
        });

        _app.Lifetime.ApplicationStarted.Register(() => _startedTcs.TrySetResult());
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _ = _app.RunAsync();
        return _startedTcs.Task;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _app.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        _app.DisposeAsync().AsTask().Wait();
    }
}
