using System.Xml.Linq;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Extração de metadados. Padrão da comunidade: ComicInfo.xml dentro do arquivo
/// (compatível com CDisplayEx, YACReader, Komga...). Fallback: nome do arquivo.
/// </summary>
public static class MetadataService
{
    public static BookMetadata ReadFromArchive(ArchiveBook archive)
    {
        var meta = new BookMetadata { EbookPath = archive.FilePath };

        var xml = archive.TryReadComicInfoXml();
        if (!string.IsNullOrWhiteSpace(xml))
        {
            try
            {
                var root = XElement.Parse(xml);
                string? Get(string name) => root.Element(name)?.Value.Trim();

                meta.Title    = Get("Title") ?? "";
                meta.Series   = Get("Series");
                meta.Number   = Get("Number");
                meta.Volume   = Get("Volume");
                meta.Writer   = Get("Writer");
                meta.Publisher = Get("Publisher");
                meta.IsManga  = string.Equals(Get("Manga"), "Yes", StringComparison.OrdinalIgnoreCase);
                if (int.TryParse(Get("Year"), out var y))  meta.Year  = y;
                if (int.TryParse(Get("Month"), out var m)) meta.Month = m;
            }
            catch { /* XML malformado: segue com fallback */ }
        }

        // Fallback: tenta extrair série/número do nome do arquivo ("One Piece - 104.cbz")
        if (string.IsNullOrWhiteSpace(meta.Title))
        {
            var name = Path.GetFileNameWithoutExtension(archive.FilePath);
            var dash = name.LastIndexOf('-');
            if (dash > 2 && int.TryParse(name[(dash + 1)..].Trim().TrimStart('0'), out _))
            {
                meta.Series = name[..dash].Trim();
                meta.Number = name[(dash + 1)..].Trim();
            }
            meta.Title = name;
        }

        return meta;
    }
}
