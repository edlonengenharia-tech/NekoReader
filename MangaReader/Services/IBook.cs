using System.Xml.Linq;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Abstração de "livro aberto": esconde se a origem é um arquivo compactado ou um PDF.
/// Expõe contagem de páginas, leitura de bytes por página e metadados.
/// </summary>
public interface IBook : IDisposable
{
    string FilePath { get; }
    int PageCount { get; }
    BookMetadata Metadata { get; }
    byte[] ReadPageBytes(int physicalIndex);
    int GetPageWidth(int physicalIndex);
    int GetPageHeight(int physicalIndex);
}

/// <summary>Fábrica: escolhe o backend certo pela extensão do arquivo.</summary>
public static class BookFactory
{
    public static readonly string[] SupportedExtensions =
        [".cbz", ".cbr", ".cbt", ".cb7", ".zip", ".rar", ".tar", ".7z", ".pdf"];

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static IBook Open(string path)
    {
        IBook book = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => new PdfBook(path),
            _      => new ArchiveBookAdapter(path),
        };
        book.Metadata.EbookPath = path;
        book.Metadata.PageCount = book.PageCount;
        return book;
    }
}

/// <summary>Adaptador que transforma ArchiveBook em IBook e extrai os metadados.</summary>
public sealed class ArchiveBookAdapter : IBook
{
    private readonly ArchiveBook _archive;

    public string FilePath => _archive.FilePath;
    public int PageCount => _archive.PageCount;
    public BookMetadata Metadata { get; }

    public ArchiveBookAdapter(string path)
    {
        _archive = ArchiveBook.Open(path);
        Metadata = MetadataService.ReadFromArchive(_archive);
    }

    public byte[] ReadPageBytes(int physicalIndex) => _archive.ReadPageBytes(physicalIndex);

    // Dimensões reais exigem decodificar o header da imagem; usamos GDI+ de forma leve.
    public int GetPageWidth(int i)  => Measure(i, w => w);
    public int GetPageHeight(int i) => Measure(i, h => h);

    private int Measure(int index, Func<int, int> pick)
    {
        try
        {
            using var ms = new MemoryStream(ReadPageBytes(index));
            using var img = System.Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
            return pick(img.Width);
        }
        catch { return 800; } // fallback razoável para ordenação dupla-página
    }

    public void Dispose() => _archive.Dispose();
}
