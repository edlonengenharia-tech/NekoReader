using System.Globalization;
using System.Text;   // StringBuilder

namespace MangaReader.Models;

/// <summary>Estado persistido por livro: posição de leitura, modo mangá, etc.</summary>
public sealed class ReadingState
{
    public string FilePath { get; set; } = "";
    /// <summary>Índice da última página "aberta" (na ordem esquerda→direita do arquivo).</summary>
    public int LastPageIndex { get; set; }
    public int TotalPages { get; set; }
    public bool RightToLeft { get; set; }
    public double Progress => TotalPages == 0 ? 0 : Math.Clamp((double)(LastPageIndex + 1) / TotalPages, 0, 1);
    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Converte o índice salvo para o índice de exibição no sentido atual de leitura.</summary>
    public int GetDisplayIndex(bool rightToLeft)
        => rightToLeft ? Math.Max(0, TotalPages - 1 - LastPageIndex) : LastPageIndex;

    /// <summary>Converte um índice de exibição de volta para o índice físico do arquivo.</summary>
    public int ToPhysicalIndex(int displayIndex, bool rightToLeft)
        => rightToLeft ? Math.Max(0, TotalPages - 1 - displayIndex) : displayIndex;

    /// <summary>Chave estável (caminho + tamanho) usada no cache de estado.</summary>
    public string CacheKey
    {
        get
        {
            long len = 0;
            try { if (File.Exists(FilePath)) len = new FileInfo(FilePath).Length; } catch { }
            return $"{FilePath}|{len}";
        }
    }

    public static string SanitizeKey(string key)
    {
        var sb = new StringBuilder(key.Length + 8);
        foreach (var c in key)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        return sb.ToString();
    }
}
