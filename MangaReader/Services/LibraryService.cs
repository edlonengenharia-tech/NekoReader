using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Varre pastas da biblioteca em busca de arquivos suportados e monta itens com
/// metadados + cache de capa em disco (para o painel lateral abrir rápido).
/// </summary>
public sealed class LibraryService
{
    private readonly ReadingStateService _states;
    private readonly string _coverCacheDir;

    public LibraryService(ReadingStateService states)
    {
        _states = states;
        _coverCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MangaReader", "covers");
        Directory.CreateDirectory(_coverCacheDir);
    }

    /// <summary>Item da biblioteca: caminho, metadados e último estado de leitura.</summary>
    public sealed record LibraryItem(string FilePath, BookMetadata Metadata, ReadingState? State, string CoverCachePath);

    private static string CoverFileName(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash[..8]) + ".jpg";
    }

    /// <summary>Varrida recursiva das pastas configuradas.</summary>
    public IEnumerable<LibraryItem> Scan(IEnumerable<string> rootFolders)
    {
        foreach (var root in rootFolders.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                                 .Where(BookFactory.IsSupported);
            }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var f in files)
            {
                BookMetadata meta;
                try
                {
                    meta = PeekMetadata(f);
                }
                catch { continue; } // arquivo corrompido/trancado: ignora

                yield return new LibraryItem(f, meta, _states.Load(f),
                    Path.Combine(_coverCacheDir, CoverFileName(f)));
            }
        }
    }

    /// <summary>Metadados rápidos sem descomprimir páginas inteiras.</summary>
    public static BookMetadata PeekMetadata(string path)
    {
        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new BookMetadata
            {
                EbookPath = path,
                Title = Path.GetFileNameWithoutExtension(path),
            };
        }

        using var archive = ArchiveBook.Open(path);
        var meta = MetadataService.ReadFromArchive(archive);
        meta.PageCount = archive.PageCount;
        return meta;
    }

    /// <summary>Gera (ou reutiliza) a miniatura de capa em disco. Chame em background.</summary>
    public void EnsureCover(LibraryItem item)
    {
        if (File.Exists(item.CoverCachePath)) return;

        try
        {
            using var book = BookFactory.Open(item.FilePath);
            var bytes = book.ReadPageBytes(0); // primeira página = capa
            using var ms = new MemoryStream(bytes);
            using var img = System.Drawing.Image.FromStream(ms);

            const int w = 160; // largura da miniatura, proporção mantida
            int h = Math.Max(1, img.Height * w / Math.Max(1, img.Width));
            using var thumb = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, w, h);
            }
            thumb.Save(item.CoverCachePath, System.Drawing.Imaging.ImageFormat.Jpeg);
            item.Metadata.CoverImagePath = item.CoverCachePath;
        }
        catch { /* sem capa: a UI mostra placeholder */ }
    }
}
