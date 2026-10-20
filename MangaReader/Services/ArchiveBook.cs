using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Abre arquivos compactados de quadrinhos (CBZ/CBT/CBR/CB7) usando SharpCompress,
/// que trata ZIP, TAR, RAR e 7z com uma única API. As páginas são lidas sob demanda
/// direto do stream — nada é extraído para disco.
/// </summary>
public sealed class ArchiveBook : IDisposable
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".avif"];

    private readonly IArchive _archive;
    private readonly List<IArchiveEntry> _pageEntries = [];

    public string FilePath { get; }
    public IReadOnlyList<IArchiveEntry> Pages => _pageEntries;
    public int PageCount => _pageEntries.Count;

    private ArchiveBook(string path, IArchive archive)
    {
        FilePath = path;
        _archive = archive;
        _pageEntries = archive.Entries
            .Where(e => !e.IsDirectory && IsImage(e.Key ?? ""))
            .OrderBy(e => NaturalFileNameComparer.NaturalCompare(e.Key ?? "", "", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static bool IsImage(string entryName)
        => ImageExtensions.Contains(Path.GetExtension(entryName).ToLowerInvariant());

    public static ArchiveBook Open(string path)
    {
        // SharpCompress detecta o formato real pelo header, não pela extensão:
        // .cbz → Zip, .cbr → Rar, .cbt → Tar, .cb7 → SevenZip.
        var archive = ArchiveFactory.Open(path);
        return new ArchiveBook(path, archive);
    }

    /// <summary>Extrai uma página para memória (sem tocar no disco).</summary>
    public byte[] ReadPageBytes(int index)
    {
        var entry = _pageEntries[index];
        using var ms = new MemoryStream((int)(entry.CompressedSize > 0 ? entry.CompressedSize : 64 * 1024));
        entry.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Lê o ComicInfo.xml, se existir no interior do arquivo.</summary>
    public string? TryReadComicInfoXml()
    {
        var info = _archive.Entries.FirstOrDefault(
            e => string.Equals(Path.GetFileName(e.Key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (info is null) return null;
        using var reader = new StreamReader(info.OpenEntryStream());
        return reader.ReadToEnd();
    }

    public void Dispose() => _archive.Dispose();
}
