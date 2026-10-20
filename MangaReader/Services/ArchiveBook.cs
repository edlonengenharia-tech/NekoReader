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
    /// <summary>Extrator externo (7z.exe/WinRAR) usado como fallback p/ RAR corrompido. Pode ser null.</summary>
    private readonly ExternalExtractor? _external;

    public string FilePath { get; }
    public IReadOnlyList<IArchiveEntry> Pages => _pageEntries;
    public int PageCount => _pageEntries.Count;

    private ArchiveBook(string path, IArchive archive, ExternalExtractor? external)
    {
        FilePath = path;
        _archive = archive;
        _external = external;
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
        IArchive archive;
        try
        {
            archive = ArchiveFactory.Open(path);
        }
        catch (Exception ex)
        {
            // Arquivo que o SharpCompress nem consegue ler o catálogo (ex.: RAR raro):
            // tenta extrator externo antes de desistir.
            var ext = ExternalExtractor.FindFor(path);
            if (ext is null) throw new InvalidDataException(
                $"Não foi possível ler '{Path.GetFileName(path)}': {ex.Message}", ex);
            archive = new CatalogArchive(path, ext.ListImageEntries());
        }

        var book = new ArchiveBook(path, archive, null);
        // Prova de fogo: se a primeira página falhar no descompactador embutido
        // (NullReferenceException clássica do UnpackV2017 em RAR5 problemático),
        // troca silenciosamente para 7z.exe/WinRAR se estiver instalado.
        if (book.PageCount > 0)
        {
            try { _ = book.ReadPageBytesRaw(0); }
            catch (InvalidDataException) when (ExternalExtractor.FindFor(path) is { } helper)
            {
                book.Dispose();
                book = new ArchiveBook(path, new CatalogArchive(path, helper.ListImageEntries()), helper);
            }
        }
        return book;
    }

    /// <summary>Leitura crua sem o wrapper de mensagem (usada na prova de fogo).</summary>
    private byte[] ReadPageBytesRaw(int index)
    {
        var entry = _pageEntries[index];
        using var ms = new MemoryStream((int)(entry.Size > 0 ? Math.Min(entry.Size, 32L * 1024 * 1024) : 64 * 1024));
        entry.WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Extrai uma página para memória (sem tocar no disco).</summary>
    public byte[] ReadPageBytes(int index)
    {
        var entry = _pageEntries[index];
        if (_external is not null)
            return _external.ExtractFile(FilePath, entry.Key ?? "");

        using var ms = new MemoryStream((int)(entry.Size > 0 ? Math.Min(entry.Size, 32L * 1024 * 1024) : 64 * 1024));
        try
        {
            entry.WriteTo(ms);
        }
        catch (Exception ex)
        {
            // RAR: o descompactador V2017 do SharpCompress lança NullReferenceException em
            // cabeçalhos de arquivos RAR5/7z solid ou levemente corrompidos. Converte em
            // mensagem amigável — e ArchiveBook.Open tenta o fallback por 7z.exe depois.
            throw new InvalidDataException(
                $"Falha ao descomprimir a página '{entry.Key}' de '{Path.GetFileName(FilePath)}'. " +
                "O arquivo pode estar corrompido, protegido por senha ou usar um formato RAR " +
                "não suportado pelo descompactador embutido. " +
                "Dica: converta para .cbz (ZIP) com 7-Zip, WinRAR ou Bandizip.", ex);
        }
        return ms.ToArray();
    }

    /// <summary>Lê o ComicInfo.xml, se existir no interior do arquivo.</summary>
    public string? TryReadComicInfoXml()
    {
        var info = _archive.Entries.FirstOrDefault(
            e => string.Equals(Path.GetFileName(e.Key), "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (info is null) return null;
        try
        {
            using var reader = new StreamReader(info.OpenEntryStream());
            return reader.ReadToEnd();
        }
        catch when (_external is not null)
        {
            return _external.ExtractText(FilePath, info.Key ?? "");
        }
        catch { return null; }
    }

    public void Dispose() => _archive.Dispose();
}

/// <summary>
/// "Arquivo" somente-catálogo: lista as entradas (nome/tamanho) lidas por um extrator
/// externo, para quando o SharpCompress não consegue abrir o contêiner. As páginas são
/// sempre extraídas via ExternalExtractor (OpenEntryStream não é suportado aqui).
/// </summary>
internal sealed class CatalogArchive : IArchive
{
    private readonly List<CatalogEntry> _entries;

    public CatalogArchive(string path, IEnumerable<(string Name, long Size)> catalog)
        => _entries = catalog.Select(n => new CatalogEntry(this, n.Name, n.Size)).ToList();

    public bool CanDisposeResources => true;
    public ARCHIVE_TYPE Type => ARCHIVE_TYPE.OTHER;
    public bool IsSolid => false;
    public Stream? Stream => null;
    public DateTime? ArchiveCreated => null;
    public IEnumerable<IEntry> Entries => _entries;
    public IEnumerable<string> Filenames => _entries.Select(e => e.Key!);
    public long TotalSize => _entries.Sum(e => e.Size);
    public long CompressedSize => 0;

    public void Dispose() { }
    public void CompleteArchiving() { }

    internal sealed class CatalogEntry(IArchive parent, string key, long size) : IEntry
    {
        public string? Key { get; } = key;
        public string? LinkTarget => null;
        public bool IsDirectory => false;
        public bool IsEncrypted => false;
        public DateTime? LastModifiedTimeUtc => null;
        public long Size { get; } = size;
        public long CompressedSize => size;
        public uint? Crc32 => null;
        public string? PartingName => null;
        public int PartIndex => 0;
        public ARCHIVE_ENTRY_TYPE EntryType => ARCHIVE_ENTRY_TYPE.File;
        public int DataStartPosition => 0;
        public IEnumerable<ExtendedHeader> ExtendedHeaders => [];
        IArchive IEntry.Archive => parent;

        public Stream OpenEntryStream() =>
            throw new NotSupportedException("Use o extrator externo (ArchiveBook._external).");
    }
}

/// <summary>
/// Fallback de descompressão usando 7-Zip ou WinRAR instalados no PC.
/// Localiza o executável no PATH e nos diretórios padrão de instalação.
/// </summary>
internal sealed class ExternalExtractor
{
    private enum Kind { SevenZip, WinRar }
    private readonly Kind _kind;
    private readonly string _exe;

    private ExternalExtractor(Kind kind, string exe) { _kind = kind; _exe = exe; }

    public static ExternalExtractor? FindFor(string archivePath)
    {
        var seven = Find("7z.exe",
            @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe");
        if (seven is not null) return new ExternalExtractor(Kind.SevenZip, seven);

        // WinRAR só para RAR/CBR
        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext is ".rar" or ".cbr")
        {
            var rar = Find("UnRAR.exe",
                @"C:\Program Files\WinRAR\UnRAR.exe", @"C:\Program Files (x86)\WinRAR\UnRAR.exe");
            if (rar is not null) return new ExternalExtractor(Kind.WinRar, rar);
        }
        return null;
    }

    private static string? Find(string onPath, params string[] standardDirs)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), onPath);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* PATH com caractere inválido */ }
        }
        return standardDirs.FirstOrDefault(File.Exists);
    }

    /// <summary>Lista nomes+tamanhos das entradas (para montar o catálogo).</summary>
    public List<(string Name, long Size)> ListImageEntries(string archive)
    {
        var output = Run(_kind switch
        {
            Kind.SevenZip => $"l -slt \"{archive}\"",
            _ => $"l -ab \"{archive}\"",
        });
        var result = new List<(string, long)>();
        string? pendingName = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (_kind == Kind.SevenZip)
            {
                if (line.StartsWith("Path = ", StringComparison.Ordinal))
                    pendingName = line["Path = ".Length..];
                else if (line.StartsWith("Size = ") && pendingName is not null &&
                         long.TryParse(line["Size = ".Length..], out var sz))
                {
                    if (ArchiveBook.IsImage(pendingName)) result.Add((pendingName, sz));
                    pendingName = null;
                }
            }
            else // UnRAR -ab: linhas tab-separadas "...\tsize\t... name"
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && long.TryParse(parts[^2], out var s2))
                {
                    var name = parts[^1];
                    if (ArchiveBook.IsImage(name)) result.Add((name, s2));
                }
            }
        }
        if (result.Count == 0)
            throw new InvalidDataException($"Nenhuma imagem encontrada em '{Path.GetFileName(archive)}' (via extrator externo).");
        return result;
    }

    public byte[] ExtractFile(string archive, string entryName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "MangaReader", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var args = _kind switch
            {
                Kind.SevenZip => $"e -y -o\"{tmp}\" \"{archive}\" -- \"{entryName}\"",
                _ => `x -y "{archive}" "{tmp}\" "{entryName}"`,
            };
            var run = Process.Start(new ProcessStartInfo
            {
                FileName = _exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("Falha ao iniciar o extrator externo.");
            run.StandardOutput.ReadToEndAsync(); // drena para não bloquear
            if (!run.WaitForExit(30_000)) { try { run.Kill(true); } catch { } }
            var outFile = Path.Combine(tmp, Path.GetFileName(entryName));
            if (run.ExitCode != 0 || !File.Exists(outFile))
                throw new InvalidDataException(
                    $"O extrator externo ({Path.GetFileName(_exe)}) não conseguiu extrair '{entryName}'. " +
                    "O arquivo provavelmente está corrompido ou protegido por senha.");
            return File.ReadAllBytes(outFile);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    public string ExtractText(string archive, string entryName)
        => System.Text.Encoding.UTF8.GetString(ExtractFile(archive, entryName));

    private string Run(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exe,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar o extrator.");
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15_000);
        return text;
    }
}
