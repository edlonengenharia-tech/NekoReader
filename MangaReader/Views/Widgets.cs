using System.Drawing.Drawing2D;
using MangaReader.Models;

namespace MangaReader.Views;

/// <summary>
/// Botão "pílula" desenhado à mão (GDI+): cantos arredondados, hover e estado
/// pressionado — a base visual da barra de ferramentas do leitor.
/// </summary>
public sealed class IconButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public string Glyph { get; }
    public bool Active { get; set; }   // botão "ligado" (mangá/dupla/tela cheia)
    public string? BadgeText { get; set; }

    public IconButton(string glyph)
    {
        Glyph = glyph;
        Size = new Size(40, 30);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Color.FromArgb(36, 37, 44));

        var r = new RectangleF(1, 2, Width - 3, Height - 5);
        using var path = UiDrawing.Rounded(r, 8f);

        Color back, fore;
        if (Active)
        {
            back = ThemeColors.Accent;
            fore = Color.White;
        }
        else if (_pressed)
        {
            back = ThemeColors.CardHover;
            fore = ThemeColors.Text;
        }
        else if (_hovered)
        {
            back = ThemeColors.Card;
            fore = ThemeColors.Text;
        }
        else
        {
            back = Color.Transparent;
            fore = ThemeColors.TextDim;
        }

        if (back != Color.Transparent)
            using (var b = new SolidBrush(back)) g.FillPath(b, path);
        if (_hovered || Active)
            using (var p = new Pen(Color.FromArgb(60, ThemeColors.Border))) g.DrawPath(p, path);

        TextRenderer.DrawText(g, Glyph, Font, Rectangle.Truncate(r), fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Cores correntes da UI — um único ponto mutável para trocar temas em cascata.</summary>
public static class ThemeColors
{
    public static Services.Theme Current { get; private set; } = Services.Themes.Dark;
    public static Color Window => Current.Window;
    public static Color Panel => Current.Panel;
    public static Color Sidebar => Current.Sidebar;
    public static Color Card => Current.Card;
    public static Color CardHover => Current.CardHover;
    public static Color Accent => Current.Accent;
    public static Color Text => Current.Text;
    public static Color TextDim => Current.TextDim;
    public static Color Border => Current.Border;

    public static event EventHandler? Changed;

    public static void Apply(Services.Theme theme)
    {
        Current = theme;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

internal static class UiDrawing
{
    /// <summary>Retorno de um retângulo com cantos arredondados (GDI+ GraphicsPath).</summary>
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0 || d > Math.Min(r.Width, r.Height)) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
