namespace MangaReader.Services;

/// <summary>Tema de cores da interface (aplicado em cascata pelo MainForm).</summary>
public sealed record Theme(
    string Name,
    Color Window,       // fundo do painel central ("cinema")
    Color Panel,        // barra superior/inferior e status
    Color Sidebar,      // painel da biblioteca
    Color Card,         // cartões/fundo de itens
    Color CardHover,
    Color Accent,       // cor primária (botões ativos, seleção, slider)
    Color Text,
    Color TextDim,
    Color Border);

public static class Themes
{
    public static readonly Theme Dark = new(
        "Escuro",
        Color.FromArgb(24, 24, 28),
        Color.FromArgb(36, 37, 44),
        Color.FromArgb(30, 31, 37),
        Color.FromArgb(48, 50, 58),
        Color.FromArgb(62, 65, 76),
        Color.FromArgb(96, 165, 250),   // azul suave
        Color.FromArgb(228, 229, 232),
        Color.FromArgb(150, 153, 162),
        Color.FromArgb(60, 62, 72));

    public static readonly Theme Midnight = new(
        "Meia-noite",
        Color.FromArgb(12, 14, 24),
        Color.FromArgb(20, 24, 40),
        Color.FromArgb(16, 19, 32),
        Color.FromArgb(28, 33, 54),
        Color.FromArgb(38, 45, 72),
        Color.FromArgb(167, 139, 250),  // roxo
        Color.FromArgb(222, 226, 240),
        Color.FromArgb(138, 145, 170),
        Color.FromArgb(40, 46, 70));

    public static readonly Theme Light = new(
        "Claro",
        Color.FromArgb(243, 244, 246),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(248, 249, 251),
        Color.FromArgb(235, 237, 240),
        Color.FromArgb(222, 226, 232),
        Color.FromArgb(37, 99, 235),
        Color.FromArgb(30, 32, 38),
        Color.FromArgb(110, 115, 125),
        Color.FromArgb(210, 213, 220));

    public static Theme FromName(string? name) => name switch
    {
        "Midnight" => Midnight,
        "Light" => Light,
        _ => Dark,
    };

    public static readonly (string Id, string Label)[] Options =
    [
        ("Dark", "Escuro"),
        ("Midnight", "Meia-noite"),
        ("Light", "Claro"),
    ];
}
