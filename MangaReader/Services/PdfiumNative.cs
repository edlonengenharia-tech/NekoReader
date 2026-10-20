using System.Reflection;
using System.Runtime.InteropServices;

namespace MangaReader.Services;

/// <summary>
/// P/Invoke mínimo para o Pdfium (a engine de renderização do Google Chrome).
/// A DLL nativa é distribuída pelo pacote NuGet <c>PdfiumViewer.Native.x86_64.v8-xfa</c>
/// e copiada para a pasta da aplicação em tempo de build.
/// API documentada em: pdfium/fpdfview.h (https://pdfium.googlesource.com/pdfium/)
/// </summary>
internal static class PdfiumNative
{
    private const string Dll = "pdfium"; // resolve "pdfium.dll" ao lado do executável

    public const int FPDF_PAGEFLAG_ANNOT_PRINT = 1;
    public const int FPDF_RENDER_REVERSE = 0x02;   // RGBA em ordem BGRA no buffer
    public const int FPDF_RENDER_SKIPFORMS = 0x10; // ignora widgets de formulário

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_CreateNewDocument();

    /// <param name="data">buffer com o PDF; pode ser IntPtr.Zero quando readBlock é fornecido.</param>
    /// <param name="size">tamanho do buffer em bytes.</param>
    /// <param name="readBlock">callback opcional de leitura em streaming (usamos null).</param>
    /// <param name="hints">flags de preservação de estrutura (0 = padrão).</param>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_LoadMemDocument64(byte[] data, long size, IntPtr readBlock, int hints);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_ClosePage(IntPtr page);

    /// <summary>Dimensões da página em pontos (1/72 de polegada).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern double FPDF_GetPageSizeByIndex(IntPtr document, int pageIndex, out double width, out double height);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY,
        int sizeX, int sizeY, int rotate, int flags);
}

/// <summary>
/// Carrega a pdfium.dll distribuída junto com o app. Se ela não estiver presente
/// (ex.: publish sem RID), lança uma mensagem clara em vez de DllNotFoundException crua.
/// </summary>
internal static class PdfiumLoader
{
    public static readonly string NativeFileName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pdfium.dll" : "libpdfium.so";

    private static bool _tried;
    private static bool _available;

    public static bool IsAvailable
    {
        get
        {
            if (!_tried)
            {
                _tried = true;
                var path = Path.Combine(AppContext.BaseDirectory, NativeFileName);
                if (!File.Exists(path)) return false;
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(PdfiumNative).Assembly, Resolver);
                    _ = PdfiumNative.FPDF_CreateNewDocument(); // teste de carga
                    _available = true;
                }
                catch
                {
                    _available = false;
                }
            }
            return _available;
        }
    }

    private static IntPtr Resolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "pdfium") return IntPtr.Zero;
        var path = Path.Combine(AppContext.BaseDirectory, NativeFileName);
        return NativeLibrary.TryLoad(path, assembly, searchPath, out var handle) ? handle : IntPtr.Zero;
    }

    public static void EnsureAvailable()
    {
        if (IsAvailable) return;
        throw new FileNotFoundException(
            $"A engine de PDF ({NativeFileName}) não foi encontrada na pasta do aplicativo.\n\n" +
            "Ela vem do pacote NuGet 'PdfiumViewer.Native.x86_64.v8-xfa' e é copiada automaticamente\n" +
            "no build — se você criou um publish 'trimado', rode novamente RECOMPILAR.cmd.\n" +
            "O restante do leitor (CBZ/CBR/CBT/CB7) continua funcionando normalmente.");
    }
}
