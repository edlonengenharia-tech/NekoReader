using MangaReader.Views;

namespace MangaReader;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // DPI awareness + fonte padrão via API (equivalente ao ApplicationConfiguration
        // gerado pelo designer — funciona mesmo com <ApplicationHighDpiMode> no csproj).
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new MainForm();
        // Abre arquivo passado via "Abrir com..." do Explorer (args[1])
        if (args.Length > 0 && Services.BookFactory.IsSupported(args[0]))
            form.Shown += (_, _) => form.OpenFromCommandLine(args[0]);

        Application.Run(form);
    }
}
