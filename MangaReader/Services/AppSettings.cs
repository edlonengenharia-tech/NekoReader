using Newtonsoft.Json;

namespace MangaReader.Services;

/// <summary>Modos de clique no painel central (estilo CDisplayEx).</summary>
public enum ClickAction { None, PreviousPage, NextPage }

/// <summary>
/// Configurações persistentes do app em %APPDATA%\MangaReader\settings.json.
/// Carregadas no start-up e salvas ao fechar/aplicar as Opções.
/// </summary>
public sealed class AppSettings
{
    // ---------- Leitura ----------
    public bool RightToLeftByDefault { get; set; }            // abre tudo em modo mangá
    public bool DoublePageByDefault { get; set; }
    public string FitMode { get; set; } = "FitWidth";         // FitWidth|FitHeight|FitPage|ActualSize
    public bool FlipAnimation { get; set; } = true;
    public int PrefetchPages { get; set; } = 2;               // 0..5
    public int CacheEntries { get; set; } = 12;               // páginas em memória
    public ClickAction LeftClickZone { get; set; } = ClickAction.None;   // zona esquerda (~25%)
    public ClickAction RightClickZone { get; set; } = ClickAction.NextPage; // zona direita (~75%)

    // ---------- Interface ----------
    public string Theme { get; set; } = "Dark";               // Dark|Midnight|Light
    public bool ShowSidebarOnStart { get; set; } = true;
    public int SidebarWidth { get; set; } = 300;              // 220..480

    // ---------- Biblioteca ----------
    public List<string> LibraryFolders { get; set; } = [];

    // ---------- Integração com o Windows ----------
    public bool ShellAssociateOnSave { get; set; }            // marcar/desmarcar no diálogo de Opções

    [JsonIgnore]
    public Models.FitMode Fit => Enum.TryParse<Models.FitMode>(FitMode, out var f) ? f : Models.FitMode.FitWidth;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MangaReader", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* settings corrompido: volta ao padrão */ }

        var s = new AppSettings();
        s.LibraryFolders.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Comics"));
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch { /* falha silenciosa — configurações são conveniência */ }
    }
}
