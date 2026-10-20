using System.IO.Compression;
using MangaReader.Models;
using MangaReader.Services;
using Xunit;

namespace MangaReader.Tests;

/// <summary>
/// Gera um CBZ em memória com N páginas PNG sólidas (sem dependência de GDI,
/// usando um chunk PNG fixo + CRC32 calculado à mão) e um ComicInfo.xml opcional.
/// </summary>
internal static class TestBookFactory
{
    private static readonly byte[] PngPayload = BuildPng();

    public static string CreateCbz(string dir, string name, int pages, string? comicInfoXml = null)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        if (comicInfoXml is not null)
        {
            var meta = zip.CreateEntry("ComicInfo.xml");
            using var writer = new StreamWriter(meta.Open());
            writer.Write(comicInfoXml);
        }

        for (var i = 0; i < pages; i++)
        {
            // Nomes fora de ordem lexicográfica pura para exercitar a ordenação natural:
            // "page_2" deve vir antes de "page_10".
            var entry = zip.CreateEntry($"images/page_{(i + 1):D2}.png");
            using var s = entry.Open();
            s.Write(PngPayload);
        }
        return path;
    }

    /// <summary>PNG 1x1 pixel vermelho válido (assinatura + IHDR + IDAT + IEND).</summary>
    private static byte[] BuildPng()
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length);
            Array.Reverse(len); // big-endian
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            var crc = Crc32(typeBytes.Concat(data).ToArray());
            var crcBytes = BitConverter.GetBytes(crc);
            Array.Reverse(crcBytes);
            return [.. len, .. typeBytes, .. data, .. crcBytes];
        }

        var ihdr = new List<byte>();
        void BE(int v) { var b = BitConverter.GetBytes(v); Array.Reverse(b); ihdr.AddRange(b); }
        BE(1); BE(1); ihdr.Add(8); ihdr.Add(2 /*RGB*/); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);

        // IDAT: zlib-stream de uma linha filtro-0 + RGB(255,0,0)
        var raw = new byte[] { 0, 255, 0, 0 };
        var deflated = DeflateStore(raw);
        var zlib = new List<byte> { 0x78, 0x01 };
        zlib.AddRange(deflated);
        uint adler = Adler32(raw);
        var ab = BitConverter.GetBytes(adler); Array.Reverse(ab); zlib.AddRange(ab);

        var parts = new List<byte>
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };
        parts.AddRange(Chunk("IHDR", ihdr.ToArray()));
        parts.AddRange(Chunk("IDAT", zlib.ToArray()));
        parts.AddRange(Chunk("IEND", []));
        return parts.ToArray();
    }

    private static byte[] DeflateStore(byte[] data)
    {
        // Bloco "stored" (sem compressão): header 0x01 + LEN/NLEN + bytes
        var result = new List<byte> { 0x01, (byte)data.Length, 0x00, (byte)(~data.Length & 0xFF), 0x00 };
        result.AddRange(data);
        return result.ToArray();
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)(-(crc & 1)));
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Adler32(byte[] bytes)
    {
        uint a = 1, b = 0;
        foreach (var x in bytes) { a = (a + x) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
}

public class ArchiveBookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mangareader-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void Opens_Cbz_And_Counts_Pages()
    {
        var path = TestBookFactory.CreateCbz(_dir, "volume01.cbz", pages: 12);
        using var book = BookFactory.Open(path);
        Assert.Equal(12, book.PageCount);
        Assert.True(BookFactory.IsSupported(path));
    }

    [Fact]
    public void Reads_Page_Bytes_As_Valid_Png()
    {
        var path = TestBookFactory.CreateCbz(_dir, "volume02.cbz", pages: 3);
        using var book = BookFactory.Open(path);
        var bytes = book.ReadPageBytes(0);
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);           // assinatura PNG
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal(1, book.GetPageWidth(0));  // imagem 1x1
        Assert.Equal(1, book.GetPageHeight(0));
    }

    [Fact]
    public void Natural_Ordering_Keeps_Page_2_Before_Page_10()
    {
        var path = TestBookFactory.CreateCbz(_dir, "volume03.cbz", pages: 12);
        using var archive = ArchiveBook.Open(path);
        var names = archive.Pages.Select(p => p.Key).ToList();
        var i2 = names.FindIndex(n => n.EndsWith("page_02.png"));
        var i10 = names.FindIndex(n => n.EndsWith("page_10.png"));
        Assert.True(i2 >= 0 && i10 >= 0);
        Assert.True(i2 < i10, "ordenação natural esperada: 02 antes de 10");
    }

    [Fact]
    public void Parses_ComicInfo_Xml()
    {
        const string xml = """
            <?xml version="1.0"?>
            <ComicInfo>
              <Title>Teste de Título</Title>
              <Number>7</Number>
              <Manga>Yes</Manga>
              <Writer>Autor X</Writer>
            </ComicInfo>
            """;
        var path = TestBookFactory.CreateCbz(_dir, "volume04.cbz", pages: 2, comicInfoXml: xml);
        using var archive = ArchiveBook.Open(path);
        var meta = MetadataService.ReadFromArchive(archive);
        Assert.Equal("Teste de Título", meta.Title);
        Assert.Equal("7", meta.Number);
        Assert.True(meta.IsManga);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* melhor esforço */ }
    }
}

public class ReaderEngineRtlTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mangareader-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void Rtl_Inverts_Display_To_Physical_Mapping()
    {
        var path = TestBookFactory.CreateCbz(_dir, "rtl.cbz", pages: 10);
        using var engine = new ReaderEngine();
        engine.Open(path);
        engine.ToggleRightToLeft(); // modo mangá ON (novo livro começa em LTR)

        engine.First();
        Assert.Equal(9, engine.CurrentSpread()[0]); // primeira "folha" mangá = última física

        engine.Next();
        Assert.Equal(8, engine.CurrentSpread()[0]);

        engine.Last();
        Assert.Equal(0, engine.CurrentSpread()[0]);
    }

    [Fact]
    public void Ltr_Navigates_Forward()
    {
        var path = TestBookFactory.CreateCbz(_dir, "ltr.cbz", pages: 10);
        using var engine = new ReaderEngine();
        engine.Open(path);
        engine.First();
        Assert.Equal(0, engine.CurrentSpread()[0]);
        engine.Next();
        Assert.Equal(1, engine.CurrentSpread()[0]);
    }

    [Fact]
    public void Double_Page_Spread_Has_Two_Entries_And_Rtl_Puts_Higher_Left()
    {
        var path = TestBookFactory.CreateCbz(_dir, "spread.cbz", pages: 10);
        using var engine = new ReaderEngine();
        engine.Open(path);
        engine.SetLayout(PageLayout.DoublePage);
        engine.ToggleRightToLeft();
        engine.First();
        var idx = engine.CurrentSpread();
        Assert.Equal(2, idx.Count);
        Assert.True(idx[0] > idx[1], "em RTL a página de maior índice vai à esquerda");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

public class ReadingStateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mangareader-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void Save_And_Load_Roundtrip_With_Isolated_Dir()
    {
        var svc = new ReadingStateService(_dir);
        var state = new ReadingState
        {
            FilePath = @"C:\quadrinhos\volum e #1.cbz",
            LastPageIndex = 42,
            TotalPages = 100,
            RightToLeft = true,
        };
        svc.Save(state);

        var loaded = svc.Load(state.FilePath);
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded!.LastPageIndex);
        Assert.True(loaded.RightToLeft);
        Assert.Equal(0.43, loaded.Progress, 2);
    }

    [Fact]
    public void LoadAll_Returns_Saved_Books()
    {
        var svc = new ReadingStateService(_dir);
        svc.Save(new ReadingState { FilePath = "a.cbz", LastPageIndex = 1, TotalPages = 5 });
        svc.Save(new ReadingState { FilePath = "b.cbz", LastPageIndex = 2, TotalPages = 5 });
        var all = svc.LoadAll();
        Assert.Equal(2, all.Count);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
