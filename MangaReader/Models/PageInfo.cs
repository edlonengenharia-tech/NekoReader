namespace MangaReader.Models;

/// <summary>Modos de ajuste de exibição da página.</summary>
public enum FitMode
{
    FitWidth,     // Ajustar à largura (padrão para mangá)
    FitHeight,    // Ajustar à altura
    FitPage,      // Página inteira visível
    ActualSize    // 100% (zoom manual)
}

/// <summary>Layout de exibição.</summary>
public enum PageLayout
{
    SinglePage,
    DoublePage
}

/// <summary>Representa uma página dentro de um livro (imagem extraída do arquivo ou página de PDF).</summary>
public sealed class PageInfo
{
    public int Index { get; init; }              // índice zero-based na ordem natural do arquivo
    public string EntryName { get; init; } = ""; // nome interno no arquivo compactado
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Índice quando as páginas são ordenadas para leitura ocidental (esquerda→direita).</summary>
    public int LeftToRightIndex { get; set; }

    /// <summary>Índice quando ordenadas para leitura mangá (direita→esquerda).</summary>
    public int RightToLeftIndex { get; set; }
}
