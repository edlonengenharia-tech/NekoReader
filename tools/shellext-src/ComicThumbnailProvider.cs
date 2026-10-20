using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

// NOTA: este arquivo é um ESQUELETO didático. Ele demonstra a estrutura de uma
// IThumbnailProvider em C#. Para compilar de verdade:
//   1. Registre com: regasm /codebase MangaReader.ShellExt.dll
//      (ou use [ComRegisterFunction] como abaixo).
//   2. O Explorer roda em-proc: mantenha a DLL leve e à prova de vazamentos.
//   3. Numa implementação completa, o caminho do arquivo chega via
//      IInitializeCommand::Initialize — aqui é simplificado com SetPath().

namespace MangaReader.ShellExt
{
    /// <summary>
    /// Miniatura de capa de CBZ/CBR no Windows Explorer.
    /// Implementa IThumbnailProvider, a interface que o Explorer consulta para
    /// gerar previews (painel de detalhes e modo "ícones extras grandes").
    /// </summary>
    [ComVisible(true)]
    [Guid("D0B1C6A4-7E5F-4F11-9B2D-3A4E5F607182")]          // gere o SEU guid!
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ComicThumbnailProvider : IThumbnailProvider
    {
        // GUIDs oficiais da shell API (não mude)
        private const string GuidIUnknown = "00000000-0000-0000-C000-000000000046";

        [DllImport("windowscodecs.dll", EntryPoint = "CreateStreamOnHGlobal")]
        private static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool fDeleteOnRelease, out IntPtr ppstm);

        /// <summary>Chamado pelo Explorer. cxMaxHeight/cxMaxWidth pedem o tamanho desejado.</summary>
        public void GetThumbnail(int cx, int cy, THUMBNAILFLAGS flags, out IntPtr phbmp, out WICBitmapUseAlpha useAlpha)
        {
            useAlpha = WICBitmapUseAlpha.WICBITMAP_USEASIS;
            phbmp = IntPtr.Zero;

            // 1. O Explorer nos passou o caminho via IInitializeCommand antes; aqui assumimos
            //    que _filePath foi preenchido por InitializeFromItem (ver ReadFirstImage).
            var cover = CoverExtractor.ExtractFirstImage(_filePath, 256);
            if (cover is null)
                throw new COMException("Sem página de capa legível", unchecked((int)0x80070057));

            // 2. Converte Bitmap → HBITMAP (o Explorer espera um HBITMAP 32bpp).
            IntPtr hBmp = cover.GetHbitmap();
            phbmp = hBmp;
        }

        private string _filePath = "";

        /// <summary>Usado pelo handler de associação para informar o arquivo alvo.</summary>
        internal void SetPath(string path) => _filePath = path;
    }

    // ---- Interop mínimo (o SDK completo está no Windows API Code Pack / CsWin32) ----

    [ComImport, Guid("e358fcc3-686a-4cbf-b181-41ae273e6d8a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IThumbnailProvider
    {
        void GetThumbnail(int cx, int cy, THUMBNAILFLAGS flags, out IntPtr phbmp, out WICBitmapUseAlpha pdwFlags);
    }

    [Flags]
    public enum THUMBNAILFLAGS { NONE = 0, ALLOWTRANSPARENCY = 4 }
    public enum WICBitmapUseAlpha { WICBITMAP_USEASIS = 0, WICBITMAP_USEALPHA = 1, WICBITMAP_DONTUSE = 2 }

    /// <summary>
    /// Lê a primeira imagem de um .cbz/.zip sem dependências externas além de
    /// System.IO.Compression (disponível no net48). Para .cbr/.cb7, chame o
    /// MangaReader.exe via IPC ou inclua SharpCompress aqui também.
    /// </summary>
    internal static class CoverExtractor
    {
        public static Bitmap? ExtractFirstImage(string path, int maxDim)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries)
                {
                    var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (ext is not (".jpg" or ".jpeg" or ".png")) continue;
                    using var s = entry.Open();
                    using var img = Image.FromStream(s);
                    return new Bitmap(img, new Size(maxDim, maxDim * img.Height / Math.Max(1, img.Width)));
                }
            }
            catch { /* arquivos protegidos/quebrados → sem miniatura */ }
            return null;
        }
    }

    /// <summary>Registro/desregistro automático ao usar regasm.</summary>
    [ComRegisterFunction]
    internal static class Register
    {
        private const string Clsid = "D0B1C6A4-7E5F-4F11-9B2D-3A4E5F607182";

        public static void RegisterType(Type t)
        {
            using var keyClsid = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey($@"CLSID\{{{Clsid}}}");
            keyClsid!.SetValue("", "MangaReader Comic Thumbnail Provider");
            using var keyInproc = keyClsid.CreateSubKey("InprocServer32");
            keyInproc!.SetValue("", "");
            keyInproc.SetValue("Assembly", t.Assembly.FullName);
            keyInproc.SetValue("Class", t.FullName);
            keyInproc.SetValue("ThreadingModel", "Both");

            // Associa o provider às extensões de quadrinho:
            foreach (var ext in new[] { ".cbz", ".cbr", ".cbt", ".cb7" })
            {
                using var k = Microsoft.Win32.Registry.ClassesRoot
                    .CreateSubKey($@"{ext}\ShellEx\{{e358fcc3-686a-4cbf-b181-41ae273e6d8a}}"); // IID_IThumbnailProvider
                k!.SetValue("", Clsid);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterType(Type t)
        {
            foreach (var ext in new[] { ".cbz", ".cbr", ".cbt", ".cb7" })
                Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree(
                    $@"{ext}\ShellEx\{{e358fcc3-686a-4cbf-b181-41ae273e6d8a}}", false);
            Microsoft.Win32.Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{{{Clsid}}}", false);
        }
    }
}
