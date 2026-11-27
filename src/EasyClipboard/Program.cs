namespace EasyClipboard;

internal static class Program
{
    private const string ActivationEvent = "Local\\EasyClipboard.Activate";
    internal const string AppearanceFile = "appearance.txt";
    internal static string Appearance { get; private set; } = "Dark";
    internal static bool LightTheme { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();
        ApplicationConfiguration.Initialize();
        var previewMode = args is ["--preview"] or ["--preview-light"];
        var demoRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyClipboard.Demo");
        Appearance = previewMode ? args[0] == "--preview-light" ? "Light" : "Dark"
            : ReadAppearance(args is ["--demo"] or ["--demo-tour"] ? demoRoot : LocalFiles.DefaultRoot);
        LightTheme = Appearance == "Light" || Appearance == "System" &&
            Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is not 0;
        if (!SystemInformation.HighContrast)
            Application.SetColorMode(LightTheme ? SystemColorMode.Classic : SystemColorMode.Dark);
        // --demo-tour: the demo, but the first-time tour opens on every launch.
        if (args is ["--demo"] or ["--demo-tour"])
        {
            using var demo = new MainWindow(demoRoot, demo: true, tour: args[0] == "--demo-tour");
            Application.Run(demo);
            // Applying a theme restarts the window; stay in the same demo mode with its own theme file.
            if (demo.RestartRequested) System.Diagnostics.Process.Start(Application.ExecutablePath, args[0]);
            return;
        }
        if (previewMode)
        {
            var folder = Path.Combine(Environment.CurrentDirectory, ".tools", "preview");
            Directory.CreateDirectory(folder);
            var preview = new MainWindow(Path.Combine(folder, "data"));
            preview.Shown += (_, _) => preview.BeginInvoke((Action)(() =>
            {
                using var bitmap = new Bitmap(preview.Width, preview.Height);
                preview.DrawToBitmap(bitmap, preview.ClientRectangle with { Width = preview.Width, Height = preview.Height });
                bitmap.Save(Path.Combine(folder, LightTheme ? "window-light.png" : "window.png"), System.Drawing.Imaging.ImageFormat.Png);
                preview.FinishPreview();
            }));
            Application.Run(preview);
            return;
        }
        var startReceiving = args.Length == 1 && args[0] == "--start-receiving";
        if (args.Length != 0 && !startReceiving) return;
        using var mutex = new Mutex(true, "Local\\EasyClipboard.Receiver", out var first);
        if (!first)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (EventWaitHandle.TryOpenExisting(ActivationEvent, out var running))
                {
                    using (running) running.Set();
                    return;
                }
                Thread.Sleep(50);
            }
            MessageBox.Show("An older EasyClipboard instance may still be running. Open it from the system tray.", "EasyClipboard");
            return;
        }
        var restart = false;
        var rebuild = false;
        var resumeReceiving = false;
        try
        {
            using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEvent);
            using var window = new MainWindow(startReceiving: startReceiving);
            _ = window.Handle;
            var listener = ThreadPool.RegisterWaitForSingleObject(activate,
                (_, _) => window.RequestActivation(), null, Timeout.Infinite, executeOnlyOnce: false);
            try
            {
                Application.Run(window);
                restart = window.RestartRequested;
                rebuild = window.RebuildRequested;
                resumeReceiving = window.ResumeReceivingAfterRestart;
            }
            finally { listener.Unregister(null); }
        }
        catch (Exception error)
        {
            MessageBox.Show($"EasyClipboard could not start ({error.GetType().Name}). Check access to %LOCALAPPDATA%\\EasyClipboard. Your existing data has not been reset.", "EasyClipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        if (restart)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            RestartApp(rebuild, resumeReceiving);
        }
    }

    private static string ReadAppearance(string root)
    {
        var fallback = File.Exists(Path.Combine(root, "light-theme")) ? "Light" : "Dark";
        try
        {
            return File.ReadAllText(Path.Combine(root, AppearanceFile)).Trim() switch
            {
                "Dark" => "Dark",
                "Light" => "Light",
                "System" => "System",
                _ => fallback
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    private static void RestartApp(bool rebuild, bool resumeReceiving)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("EASYCLIPBOARD_SOURCE_ROOT");
        var executable = Application.ExecutablePath;
        var launcher = sourceRoot is null ? null : Path.Combine(sourceRoot, "scripts", "run.ps1");
        if (rebuild && File.Exists(launcher) && string.Equals(executable,
            Path.Combine(sourceRoot!, "src", "EasyClipboard", "bin", "Release", "net10.0-windows10.0.22621.0", "EasyClipboard.exe"),
            StringComparison.OrdinalIgnoreCase))
        {
            var build = new System.Diagnostics.ProcessStartInfo("powershell.exe") { WorkingDirectory = sourceRoot!, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", launcher! })
                build.ArgumentList.Add(arg);
            if (resumeReceiving) build.ArgumentList.Add("-StartReceiving");
            System.Diagnostics.Process.Start(build);
            return;
        }
        System.Diagnostics.Process.Start(executable, resumeReceiving ? "--start-receiving" : "");
    }
}
