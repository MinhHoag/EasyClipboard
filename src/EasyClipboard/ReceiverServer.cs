using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyClipboard.HotspotProbe;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EasyClipboard;

public sealed record ReceiverActivity(DateTimeOffset At, string Operation, bool Finished, int? StatusCode,
    int ActiveRequests, long RequestCount);

public sealed class ReceiverServer : IAsyncDisposable
{
    public const int SetupPort = 47831;
    public const int TransferPort = 47832;
    private readonly WebApplication _app;
    private readonly X509Certificate2? _certificate;
    private int _activePeerRequests;
    private long _peerRequestCount;
    public event Action<ReceiverActivity>? PeerActivity;
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };

    public ReceiverServer(HotspotBinding binding, Identity identity, TransferService transfers,
        int setupPort = SetupPort, int transferPort = TransferPort, Func<bool>? present = null, bool allowHomeHttp = false,
        ClipboardExport? clipboardExport = null)
    {
        present ??= binding.IsPresent;
        if (binding.IsTailscale && !HotspotBinding.IsTailscaleV4(binding.Address))
            throw new ArgumentException("Tailscale mode requires a Tailscale interface address.", nameof(binding));
        // HTTP transfers are a deliberate user-selected home-network prototype mode.
        // HTTPS remains the default for callers and for the private-hotspot mode.
        if (!allowHomeHttp) _certificate = identity.IssueServer(binding.Address);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = TransferValidation.MaxBodyBytes;
            options.Limits.MaxConcurrentConnections = 4;
            options.Limits.MaxRequestHeadersTotalSize = 8192;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(10);
            options.Listen(binding.Address, setupPort);
            if (!allowHomeHttp)
                options.Listen(binding.Address, transferPort, endpoint => endpoint.UseHttps(https =>
                {
                    https.ServerCertificate = _certificate;
                    https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                }));
        });
        _app = builder.Build();
        // Ephemeral diagnostics only: no addresses, URLs, headers, keys, or clipboard contents.
        // A local health check must never be presented as evidence that the phone reached us.
        _app.Use(async (context, next) =>
        {
            if (context.Connection.RemoteIpAddress is not { } remote || remote.Equals(binding.Address))
            {
                await next(context);
                return;
            }
            var operation = context.Request.Method == "POST" && context.Request.Path == "/v1/transfers"
                ? "Transfer" : context.Request.Method == "GET" && context.Request.Path == "/v1/clipboard" ? "PC clipboard fetch"
                : context.Request.Method == "GET" && context.Request.Path == "/" ? "Page check" : "Other request";
            var count = Interlocked.Increment(ref _peerRequestCount);
            var active = Interlocked.Increment(ref _activePeerRequests);
            ReportActivity(new(DateTimeOffset.Now, operation, false, null, active, count));
            var completed = false;
            try { await next(context); completed = !context.RequestAborted.IsCancellationRequested; }
            finally
            {
                active = Interlocked.Decrement(ref _activePeerRequests);
                ReportActivity(new(DateTimeOffset.Now, operation, true, completed ? context.Response.StatusCode : null,
                    active, Interlocked.Read(ref _peerRequestCount)));
            }
        });
        _app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (!present() || context.Connection.RemoteIpAddress is not { } remote || !binding.Contains(remote))
            {
                await Error(context, 503, "network_unavailable", "The selected local network is unavailable.");
                return;
            }
            try { await next(context); }
            catch (TransferError error) { await Error(context, error.Status, error.Code, error.Message); }
            catch (BadHttpRequestException) { await Error(context, 413, "request_rejected", "The request was invalid or exceeds 18 MB."); }
            catch (JsonException) { await Error(context, 400, "invalid_json", "Provide a version 1 transfer JSON object."); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { /* Incomplete upload; no clipboard operation began. */ }
            catch (Exception) { await Error(context, 500, "receiver_error", context.Request.Path == "/v1/clipboard"
                ? "The PC clipboard could not be read. Try copying the item again."
                : "The receiver could not complete the transfer. Check local history and retry the same transferId."); }
        });

        if (!allowHomeHttp)
        {
            _app.MapGet("/root.cer", () => Results.File(identity.Authority.RawData, "application/pkix-cert", "EasyClipboard-root.cer"));
            _app.MapGet("/install.mobileconfig", () => Results.File(identity.Profile(), "application/x-apple-aspen-config", "EasyClipboard.mobileconfig"));
        }
        _app.MapGet("/", () => Results.Text(binding.IsTailscale && allowHomeHttp
            ? "EasyClipboard Tailscale receiver is reachable. Transfers require the pairing key. HTTP travels through the encrypted Tailscale connection; no iPhone certificate profile is required."
            : allowHomeHttp
            ? "EasyClipboard home-network prototype is reachable. Transfers require the pairing key. HTTP content and the key are unencrypted. No iPhone certificate profile is needed."
            : "EasyClipboard HTTPS prototype. Verify the local root certificate before trusting it. See the Shortcut setup guide. This page contains no pairing credentials."));
        _app.MapGet("/v1/clipboard", async (HttpContext context) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method))
                throw new TransferError(405, "get_required", "Use GET to fetch the PC clipboard.");
            if (!context.Request.IsHttps && !allowHomeHttp)
                throw new TransferError(426, "https_required", "Use the HTTPS endpoint shown in the Windows app.");
            if (identity.AuthenticationFailure(context.Request.Headers.Authorization.ToString()) is { } authError)
                throw authError;
            if (clipboardExport is null)
                throw new TransferError(403, "clipboard_export_disabled", "PC clipboard fetching is not enabled.");
            var content = await clipboardExport.ReadAsync(context.RequestAborted);
            context.Response.ContentType = content.ContentType;
            context.Response.ContentLength = content.Bytes.Length;
            if (content.ContentType == "image/png")
                context.Response.Headers.ContentDisposition = "inline; filename=\"clipboard.png\"";
            await context.Response.Body.WriteAsync(content.Bytes, context.RequestAborted);
        });
        _app.MapPost("/v1/transfers", async (HttpContext context) =>
        {
            if (!context.Request.IsHttps && !allowHomeHttp)
            {
                await Error(context, 426, "https_required", "Use the HTTPS endpoint shown in the Windows app. No transfer was processed.");
                return;
            }
            if (identity.AuthenticationFailure(context.Request.Headers.Authorization.ToString()) is { } authError)
                throw authError;
            if (!context.Request.HasJsonContentType())
            {
                await Error(context, 415, "json_required", "Use application/json.");
                return;
            }
            var request = await JsonSerializer.DeserializeAsync<TransferRequest>(context.Request.Body, RequestJson, context.RequestAborted)
                ?? throw new TransferError(400, "invalid_json", "Request cannot be null.");
            context.RequestAborted.ThrowIfCancellationRequested();
            // After a complete request is accepted, finishing the save/clipboard transaction is independent
            // of response delivery. A retry uses the same ID and returns the durable receipt.
            var duplicate = await transfers.ReceiveAsync(request);
            await context.Response.WriteAsJsonAsync(new { version = 1, ok = true, transferId = request.TransferId,
                saved = true, clipboardUpdated = true, duplicate });
        });
    }

    private void ReportActivity(ReceiverActivity activity)
    {
        if (PeerActivity is not { } observers) return;
        foreach (Action<ReceiverActivity> observer in observers.GetInvocationList())
            try { observer(activity); }
            catch { /* A diagnostic display cannot interrupt a transfer. */ }
    }

    private static async Task Error(HttpContext context, int status, string code, string message)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = status;
        // Close after a rejected body so it cannot be interpreted as another request on this connection.
        context.Response.Headers.Connection = "close";
        await context.Response.WriteAsJsonAsync(new { version = 1, ok = false, error = new { code, message } });
    }
    public Task StartAsync(CancellationToken token = default) => _app.StartAsync(token);
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _app.StopAsync(timeout.Token); }
        finally { await _app.DisposeAsync(); _certificate?.Dispose(); }
    }
}
