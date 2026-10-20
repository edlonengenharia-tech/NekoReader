using Docnet.Core;
using Docnet.Core.Models;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Backend de PDF baseado em Docnet.Core (wrapper do MuPDF). Renderiza cada página
/// como PNG sob demanda — sem exigir Adobe nem poppler instalados.
/// </summary>
public sealed class PdfBook : IBook
{
    private readonly IDocLib _docLib;

    public string FilePath { get; }
    public BookMetadata Metadata { get; }

    // Dimensões da última página medida (cache simples para GetPageWidth/Height)
    private readonly Dictionary<int, (int W, int H)> _sizes = [];
    private readonly int _dpi;

    public PdfBook(string path, int dpi = 150)
    {
        FilePath = path;
        _dpi = dpi;
        _docLib = DocLib.Instance;

        using var doc = _docLib.GetDoc(path);
        PageCount = doc.PageCount;

        var info = doc.GetMetadata();
        Metadata = new BookMetadata
        {
            EbookPath = path,
            Title = info?.Title ?? Path.GetFileNameWithoutExtension(path),
            Author = info?.Author,
            PageCount = PageCount,
        };
    }

    public int PageCount { get; }

    public byte[] ReadPageBytes(int physicalIndex)
    {
        using var doc = _docLib.GetDoc(FilePath);
        var page = doc.GetPage(physicalIndex);

        // Width/Height são calculados na primeira medição e reutilizados.
        if (!_sizes.TryGetValue(physicalIndex, out var size))
        {
            using var tmp = page.GetModulatedImage(DocumentFormat.Png, _dpi, true, 0);
            size = MeasurePng(tmp);
            _sizes[physicalIndex] = size;
        }

        return page.GetModulatedImage(compress: true, documentFormat: DocumentFormat.Png, quality: _dpi, 0);
    }

    private static (int W, int H) MeasurePng(byte[] png)
    {
        // Header PNG: largura nos bytes 16-19, altura 20-23 (big-endian).
        if (png.Length > 24 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G')
        {
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (w, h);
        }
        return (800, 1200);
    }

    public int GetPageWidth(int i) => GetOrMeasure(i).W;
    public int GetPageHeight(int i) => GetOrMeasure(i).H;

    private (int W, int H) GetOrMeasure(int i)
    {
        if (!_sizes.TryGetValue(i, out var s))
        {
            var png = ReadPageBytes(i);
            s = MeasurePng(png);
            _sizes[i] = s;
        }
        return s;
    }

    public void Dispose() { /* IDocLib é singleton; nada a liberar */ }
}
