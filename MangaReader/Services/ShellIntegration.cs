using Microsoft.Win32;

namespace MangaReader.Services;

/// <summary>
/// Associação do MangaReader com .cbz/.cbr/.cbt/.cb7/.zip/.rar/.pdf no Explorer
/// (HKCU\Software\Classes — não exige administrador). Aproveita para criar um ícone
/// de aplicativo em cache extraído da primeira página do último livro aberto, de modo
/// que os arquivos associados apareçam com a "capa" nos resultados de busca do Win+R.
/// </summary>
public static class ShellIntegration
{
    public static readonly string[] Extensions =
        [".cbz", ".cbr", ".cbt", ".cb7", ".zip", ".rar", ".tar", ".7z", ".pdf"];

    /// <summary>Caminho do executável como o Explorer o verá (full-path no publish single-file).</summary>
    public static string ExePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "MangaReader.exe";

    /// <summary>Verifica se pelo menos uma extensão já está apontando para este exe.</summary>
    public static bool IsAssociated()
    {
        try
        {
            using var cbz = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.cbz\shell\open\command");
            var cmd = cbz?.GetValue(null) as string;
            return cmd is not null && cmd.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void Associate(string iconPath)
    {
        foreach (var ext in Extensions)
        {
            // ProgID próprio por tipo genérico; .zip/.rar etc. ganham subkey sem sobrescrever
            // o ProgID original (só adicionamos o comando "Abrir com MangaReader").
            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\MangaReader.File");
            root.SetValue("", "Arquivo de quadrinho MangaReader");
            using (var cmd = root.CreateSubKey(@"shell\open\command"))
                cmd.SetValue($"\", \"{ExePath}\" \"%1\"");

            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            // Não substitui o default value de .zip/.pdf (mantém Excel/Edge como padrão);
            // MangaReader aparece no menu "Abrir com…" e fica duplo-clique para os formatos CB*.
            if (ext.StartsWith(".cb") || ext is ".rar" or ".7z" or ".tar")
                extKey.SetValue("", "MangaReader.File");

            using var shellKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\shell\MangaReader");
            shellKey.SetValue("", "Abrir com MangaReader");
            using (var cmd = shellKey.CreateSubKey("command"))
                cmd.SetValue($"\", \"{ExePath}\" \"%1\"");

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                extKey.CreateSubKey("DefaultIcon").SetValue("", $"\"{iconPath}\",0");
        }
        NotifyShell();
    }

    public static void RemoveAssociation()
    {
        foreach (var ext in Extensions)
        {
            try
            {
                using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
                if ((extKey?.GetValue("") as string) == "MangaReader.File")
                    extKey.SetValue("", (string?)null);
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ext}\shell\MangaReader", throwOnMissingSubKey: false);
            }
            catch { /* extensão pode ter sumido: ignora */ }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\MangaReader.File", throwOnMissingSubKey: false); }
        catch { }
        NotifyShell();
    }

    /// <summary>Gera um .ico 256x256 a partir de uma imagem de capa (primeira página).</summary>
    public static string? BuildAppIconFromCover(string sourceImage, string targetIco)
    {
        try
        {
            using var img = System.Drawing.Image.FromFile(sourceImage);
            using var bmp = new System.Drawing.Bitmap(256, 256);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float s = Math.Min(256f / img.Width, 256f / img.Height);
                int w = (int)(img.Width * s), h = (int)(img.Height * s);
                g.DrawImage(img, (256 - w) / 2, (256 - h) / 2, w, h);
            }
            SaveIco(bmp, targetIco);
            return targetIco;
        }
        catch { return null; }
    }

    /// <summary>ICO formato "PNG-in-ICO" suportado pelo Vista+ (um único diretório de entrada).</summary>
    private static void SaveIco(System.Drawing.Bitmap bmp, string path)
    {
        using var png = new MemoryStream();
        bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);        // reserved
        bw.Write((ushort)1);        // type: icon
        bw.Write((ushort)1);        // count
        bw.Write((byte)0);          // width  (0 = 256)
        bw.Write((byte)0);          // height (0 = 256)
        bw.Write((byte)0);          // colors
        bw.Write((byte)0);          // reserved
        bw.Write((ushort)1);        // planes
        bw.Write((ushort)32);       // bpp
        bw.Write((uint)pngBytes.Length); // bytes in resource
        bw.Write((uint)22);         // image offset (header 6 + entry 16)
        bw.Write(pngBytes);
    }

    /// <summary>Avisa o Explorer que as associações mudaram (atualiza ícones sem logout).</summary>
    private static void NotifyShell()
    {
        try
        {
            const uint SHCNE_ASSOCCHANGED = 0x08000000;
            const uint SHCNF_IDLIST = 0x0000;
            const uint SHCNF_FLUSH = 0x1000;
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* shell32 sempre existe no Windows; proteção para testes fora do SO */ }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
