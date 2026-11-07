using System.Text.Json;

namespace EasyClipboard.HotspotProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var singleInstance = new Mutex(true, "Local\\EasyClipboard.HotspotProbe", out var first);
        if (!first) return 2;
        if (args.Length == 2 && args[0] == "--smoke-result")
            return Task.Run(() => SmokeAsync(args[1])).GetAwaiter().GetResult();
        if (args.Length != 0) return 2;
        ApplicationConfiguration.Initialize();
        Application.Run(new ProbeWindow());
        return 0;
    }

    private static async Task<int> SmokeAsync(string resultPath)
    {
        object result;
        var exit = 0;
        await using var host = new HotspotHost();
        try
        {
            await host.StartAsync();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var json = await client.GetStringAsync($"http://{host.Binding!.Address}:{ProbeServer.DefaultPort}/v1/probe");
            using var response = JsonDocument.Parse(json);
            if (response.RootElement.GetProperty("probeId").GetString() != host.ProbeId)
                throw new InvalidOperationException("Unexpected probe response.");
            result = new { started = true, localHttpCheck = true, address = host.Binding.Address.ToString(),
                interfaceId = host.Binding.InterfaceId, stopped = true, iphoneTested = false };
        }
        catch (Exception error)
        {
            result = new { started = false, errorType = error.GetType().Name, errorCode = $"0x{error.HResult:X8}",
                error = error.Message, iphoneTested = false };
            exit = 1;
        }
        // Write the outcome only after cleanup, so a successful result means the AP was stopped.
        await host.StopAsync();
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return exit;
    }
}
