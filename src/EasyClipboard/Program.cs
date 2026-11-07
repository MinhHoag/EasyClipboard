namespace EasyClipboard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == "--preview")
        {
            var folder = Path.Combine(Environment.CurrentDirectory, ".tools", "preview");
            Directory.CreateDirectory(folder);
            var preview = new MainWindow(Path.Combine(folder, "data"));
            preview.Shown += (_, _) => preview.BeginInvoke((Action)(() =>
            {
                using var bitmap = new Bitmap(preview.Width, preview.Height);
                preview.DrawToBitmap(bitmap, preview.ClientRectangle with { Width = preview.Width, Height = preview.Height });
                bitmap.Save(Path.Combine(folder, "window.png"), System.Drawing.Imaging.ImageFormat.Png);
                preview.FinishPreview();
            }));
            Application.Run(preview);
            return;
        }
        if (args.Length != 0) return;
        using var mutex = new Mutex(true, "Local\\EasyClipboard.Receiver", out var first);
        if (!first) { MessageBox.Show("EasyClipboard is already running. Open it from the system tray.", "EasyClipboard"); return; }
        try { Application.Run(new MainWindow()); }
        catch (Exception error)
        {
            MessageBox.Show($"EasyClipboard could not start ({error.GetType().Name}). Check access to {LocalFiles.DefaultRoot}. Your existing data has not been reset.", "EasyClipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
