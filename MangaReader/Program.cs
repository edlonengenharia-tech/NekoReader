using MangaReader.Views;

namespace MangaReader;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // DPI awareness por manifest + configuração moderna do .NET
        ApplicationConfiguration.Configure(cfg =>
        {
            cfg.DefaultFont = new Font("Segoe UI", 9f);
            cfg.HighDpiMode = HighDpiMode.PerMonitorV2;
            cfg.UseCompatibleTextRendering = false;
        });

        var form = new MainForm();
        // Abre arquivo passado via "Abrir com..." do Explorer (args[1])
        if (args.Length > 0 && Services.BookFactory.IsSupported(args[0]))
            form.Shown += (_, _) => form.OpenFromCommandLine(args[0]);

        Application.Run(form);
    }
}
