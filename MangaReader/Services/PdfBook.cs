using System.Runtime.InteropServices;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Backend de PDF baseado diretamente no Pdfium (a engine de renderização do Google Chrome),
/// via P/Invoke — sem Adobe, sem poppler e sem pacotes NuGet "exóticos".
/// A nativa pdfium.dll vem do pacote PdfiumViewer.Native.x86_64.v8-xfa (copiada no build).
/// Cada página é rasterizada em BGRA32 e codificada como PNG.
/// </summary>
public sealed class PdfBook : IBook
{
    private const int FPDFBitmap_BGRA = 4; // formato com alpha ignorado => buffer BGRA32
    private readonly byte[] _pdfBytes;
    private readonly Dictionary<int, (int W, int H)> _sizes = [];
    private readonly int _dpi;

    public string FilePath { get; }
    public BookMetadata Metadata { get; }
    public int PageCount { get; }

    /// <summary>Indica se a engine de PDF está disponível nesta instalação.</summary>
    public static bool EngineAvailable => PdfiumLoader.IsAvailable;

    public PdfBook(string path, int dpi = 150)
    {
        PdfiumLoader.EnsureAvailable();

        FilePath = path;
        _dpi = Math.Clamp(dpi, 72, 600);
        _pdfBytes = File.ReadAllBytes(path);

        var doc = LoadDoc();
        try
        {
            PageCount = PdfiumNative.FPDF_GetPageCount(doc);
            if (PageCount <= 0)
                throw new InvalidDataException($"'{Path.GetFileName(path)}' não contém páginas legíveis (PDF corrompido ou protegido?).");
        }
        finally
        {
            PdfiumNative.FPDF_CloseDocument(doc);
        }

        Metadata = new BookMetadata
        {
            EbookPath = path,
            Title = Path.GetFileNameWithoutExtension(path),
            PageCount = PageCount,
        };
    }

    private IntPtr LoadDoc()
    {
        // O Pdfium mantém apenas referências ao buffer durante o uso do documento;
        // mantemos o array vivo em campo (_pdfBytes) por toda a vida do PdfBook.
        var doc = PdfiumNative.FPDF_LoadMemDocument64(_pdfBytes, _pdfBytes.LongLength, IntPtr.Zero, 0);
        if (doc == IntPtr.Zero)
            throw new InvalidDataException(
                $"Não foi possível abrir '{Path.GetFileName(FilePath)}'. " +
                "O arquivo pode estar corrompido, criptografado ou exigir senha.");
        GC.KeepAlive(_pdfBytes);
        return doc;
    }

    public byte[] ReadPageBytes(int physicalIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(physicalIndex, PageCount);

        var doc = LoadDoc();
        try
        {
            var page = PdfiumNative.FPDF_LoadPage(doc, physicalIndex);
            if (page == IntPtr.Zero)
                throw new InvalidDataException($"Falha ao carregar a página {physicalIndex + 1} do PDF.");
            try
            {
                if (PdfiumNative.FPDF_GetPageSizeByIndex(doc, physicalIndex, out var widthPt, out var heightPt) != 0)
                    throw new InvalidDataException($"Não foi possível medir a página {physicalIndex + 1}.");

                float scale = _dpi / 72f;
                int w = Math.Max(1, (int)Math.Round(widthPt * scale));
                int h = Math.Max(1, (int)Math.Round(heightPt * scale));

                var bitmap = PdfiumNative.FPDFBitmap_CreateEx(w, h, FPDFBitmap_BGRA, IntPtr.Zero, w * 4);
                if (bitmap == IntPtr.Zero)
                    throw new OutOfMemoryException("Sem memória para rasterizar a página do PDF.");
                try
                {
                    PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, w, h, 0xFFFFFFFF); // fundo branco
                    PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, w, h, 0,
                        PdfiumNative.FPDF_PAGEFLAG_ANNOT_PRINT | PdfiumNative.FPDF_RENDER_SKIPFORMS);

                    var buffer = PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
                    var bgra = new byte[checked(w * h * 4)];
                    Marshal.Copy(buffer, bgra, 0, bgra.Length);

                    _sizes[physicalIndex] = (w, h);
                    return EncodePng(bgra, w, h);
                }
                finally
                {
                    PdfiumNative.FPDFBitmap_Destroy(bitmap);
                }
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
        finally
        {
            PdfiumNative.FPDF_CloseDocument(doc);
        }
    }

    public int GetPageWidth(int i)  => GetOrMeasure(i).W;
    public int GetPageHeight(int i) => GetOrMeasure(i).H;

    private (int W, int H) GetOrMeasure(int i)
    {
        if (_sizes.TryGetValue(i, out var s)) return s;
        var doc = LoadDoc();
        try
        {
            if (PdfiumNative.FPDF_GetPageSizeByIndex(doc, i, out var wPt, out var hPt) != 0)
                return (800, 1200);
            float scale = _dpi / 72f;
            s = ((int)Math.Round(wPt * scale), (int)Math.Round(hPt * scale));
            _sizes[i] = s;
            return s;
        }
        finally { PdfiumNative.FPDF_CloseDocument(doc); }
    }

    /// <summary>Codifica o buffer BGRA32 como PNG usando GDI+ (Format32bppArgb já é BGRA).</summary>
    private static byte[] EncodePng(byte[] bgra, int w, int h)
    {
        using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(bgra, 0, data.Scan0, Math.Min(bgra.Length, data.Stride * h));
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _sizes.Clear();
        Array.Clear(_pdfBytes);
    }
}
