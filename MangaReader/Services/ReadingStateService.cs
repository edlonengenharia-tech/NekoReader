using Newtonsoft.Json;
using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// Persistência da posição de leitura. Um arquivo JSON por livro em
/// %APPDATA%\MangaReader\positions\ — robusto (não corrompe a biblioteca inteira
/// se um arquivo falhar) e simples de sincronizar.
/// </summary>
public sealed class ReadingStateService
{
    private readonly string _dir;

    /// <summary>Construtor padrão: %APPDATA%\MangaReader\positions.</summary>
    public ReadingStateService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MangaReader", "positions"))
    { }

    /// <summary>Permite isolar o diretório de estado (usado nos testes).</summary>
    public ReadingStateService(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(ReadingState s)
        => System.IO.Path.Combine(_dir, ReadingState.SanitizeKey(s.CacheKey) + ".json");

    public void Save(ReadingState state)
    {
        try
        {
            state.LastOpenedUtc = DateTime.UtcNow;
            File.WriteAllText(PathFor(state), JsonConvert.SerializeObject(state, Formatting.Indented));
        }
        catch { /* falha silenciosa: estado é conveniência, não dado crítico */ }
    }

    public ReadingState? Load(string filePath)
    {
        try
        {
            long len = new FileInfo(filePath).Length;
            var key = ReadingState.SanitizeKey($"{filePath}|{len}") + ".json";
            var file = System.IO.Path.Combine(_dir, key);
            if (!File.Exists(file)) return null;
            return JsonConvert.DeserializeObject<ReadingState>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    /// <summary>Todos os estados conhecidos (para a biblioteca marcar "lido/continuando").</summary>
    public IReadOnlyDictionary<string, ReadingState> LoadAll()
    {
        var result = new Dictionary<string, ReadingState>();
        if (!Directory.Exists(_dir)) return result;
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                var s = JsonConvert.DeserializeObject<ReadingState>(File.ReadAllText(f));
                if (s is not null && !string.IsNullOrEmpty(s.FilePath))
                    result[s.FilePath] = s;
            }
            catch { /* ignora arquivos corrompidos */ }
        }
        return result;
    }
}
