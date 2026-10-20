using System.Drawing.Drawing2D;   // LinearGradientBrush, InterpolationMode
using MangaReader.Models;         // FitMode, PageLayout

namespace MangaReader.Views;

/// <summary>
/// Painel central de exibição. Desenha as páginas diretamente com GDI+
/// (DoubleBuffered) — mais rápido e flexível que PictureBox para zoom/pan,
/// página dupla e o efeito de virada de página.
/// </summary>
public sealed class PagePanel : Control
{
    private readonly Services.ReaderEngine _engine;

    // Retângulos calculados das páginas desenhadas (para pan/zoom e clique)
    private RectangleF[] _pageRects = [];
    private PointF _panOffset = PointF.Empty;
    private bool _dragging;
    private Point _dragStart;

    /// <summary>Animação de virada de página: 0..1 (null = sem animação).</summary>
    private float? _flipProgress;
    private int _flipDirection; // +1 avança, -1 volta
    private System.Windows.Forms.Timer? _flipTimer;

    /// <summary>Modo de ajuste da página (lido no LayoutPages; CycleFit o alterna).</summary>
    public FitMode Fit { get; set; } = FitMode.FitWidth;
    public bool FlipAnimationEnabled { get; set; } = true;

    public PagePanel(Services.ReaderEngine engine)
    {
        _engine = engine;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 24, 28);   // fundo escuro "cinema"
        SetStyle(ControlStyles.ResizeRedraw, true);
        TabStop = true;                            // recebe teclado
        Cursor = Cursors.Hand;

        _engine.StateChanged += (_, _) => { _panOffset = PointF.Empty; Invalidate(); };
    }

    // ---------- Entrada do usuário ----------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ModifierKeys.HasFlag(Keys.Control))
            _engine.SetZoom(_engine.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        else if (e.Delta < 0) Navigate(+1);          // roda p/ baixo = próxima
        else Navigate(-1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _engine.Zoom <= 1.0)
        {
            // Zonas de clique estilo CDisplayEx: ~25% esquerda/volta, ~75% direita/avança
            // (invertidas em modo mangá).
            var rtl = _engine.RightToLeft;
            float x = e.X / Math.Max(1f, Width);
            bool advance = rtl ? x < 0.25f : x > 0.75f;
            bool back    = rtl ? x > 0.75f : x < 0.25f;
            if (advance) Navigate(+1);
            else if (back) Navigate(-1);
            else { _dragging = true; _dragStart = e.Location; }
        }
        else if (e.Button == MouseButtons.Left)
        {
            _dragging = true; _dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && _engine.Zoom > 1.0)
        {
            // PointF não sobrecarrega operator+ — soma componente a componente.
            _panOffset = new PointF(_panOffset.X + (e.X - _dragStart.X),
                                    _panOffset.Y + (e.Y - _dragStart.Y));
            _dragStart = e.Location;
            Invalidate();
        }
        else if (_dragging)
        {
            _dragStart = e.Location;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    public void Navigate(int delta)
    {
        if (delta > 0) _engine.Next(); else _engine.Previous();
        if (FlipAnimationEnabled) StartFlip(delta);
    }

    // ---------- Animação de virada de página ----------

    private void StartFlip(int direction)
    {
        _flipDirection = direction;
        _flipProgress = 0f;
        _flipTimer?.Dispose();
        _flipTimer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60fps
        _flipTimer.Tick += (_, _) =>
        {
            if (_flipProgress is null) return;
            _flipProgress += 0.08f;
            if (_flipProgress >= 1f) { _flipProgress = null; _flipTimer?.Stop(); _flipTimer?.Dispose(); _flipTimer = null; }
            Invalidate();
        };
        _flipTimer.Start();
    }

    // ---------- Renderização ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_engine.Book is null) return;

        var spread = _engine.CurrentSpread();
        if (spread.Count == 0) return;

        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        var bitmaps = spread.Select(_engine.GetPageBitmap).ToArray();
        _pageRects = LayoutPages(bitmaps, out var totalSize);

        // Centraliza o bloco de páginas + aplica pan
        float ox = (Width - totalSize.Width) / 2f + _panOffset.X;
        float oy = (Height - totalSize.Height) / 2f + _panOffset.Y;

        for (int i = 0; i < bitmaps.Length; i++)
        {
            var dest = RectangleF.FromLTRB(
                _pageRects[i].Left + ox, _pageRects[i].Top + oy,
                _pageRects[i].Right + ox, _pageRects[i].Bottom + oy);
            g.DrawImage(bitmaps[i], dest);
            // NÃO dá Dispose aqui: os bitmaps pertencem ao PageCache.
        }

        // Sombra/efeito de virada por cima da cena estática
        if (_flipProgress is { } p && p < 1f)
            DrawFlipEffect(g, p, ox, oy, totalSize);
    }

    /// <summary>Calcula retângulos das páginas dentro do painel conforme FitMode/Zoom.</summary>
    private RectangleF[] LayoutPages(System.Drawing.Bitmap[] bitmaps, out SizeF totalSize)
    {
        float availW = Width, availH = Height;
        float scale = (float)_engine.Zoom;   // Zoom é double → conversão explícita p/ float

        // Escala base pelo modo de ajuste (usa a propriedade Fit, não o tipo FitMode)
        float baseScale = _engine.Layout == PageLayout.DoublePage && bitmaps.Length == 2
            ? Fit switch
            {
                FitMode.FitWidth  => availW / Math.Max(1f, bitmaps.Sum(b => (float)b.Width)),
                FitMode.FitHeight => availH / Math.Max(1f, bitmaps.Max(b => (float)b.Height)),
                FitMode.FitPage   => Math.Min(availW / Math.Max(1f, bitmaps.Sum(b => (float)b.Width)),
                                              availH / Math.Max(1f, bitmaps.Max(b => (float)b.Height))),
                _                 => 1f,
            }
            : Fit switch
            {
                FitMode.FitWidth  => availW / Math.Max(1f, bitmaps[0].Width),
                FitMode.FitHeight => availH / Math.Max(1f, bitmaps[0].Height),
                FitMode.FitPage   => Math.Min(availW / Math.Max(1f, bitmaps[0].Width),
                                              availH / Math.Max(1f, bitmaps[0].Height)),
                _                 => 1f,
            };

        float s = Math.Clamp(baseScale * scale, 0.05f, 16f);
        float gap = 6f * s; // respiro entre páginas da dobra

        float totalW = bitmaps.Sum(b => b.Width * s) + (bitmaps.Length - 1) * gap;
        float maxH = bitmaps.Max(b => b.Height * s);
        totalSize = new SizeF(totalW, maxH);

        var rects = new RectangleF[bitmaps.Length];
        float x = 0;
        for (int i = 0; i < bitmaps.Length; i++)
        {
            float w = bitmaps[i].Width * s, h = bitmaps[i].Height * s;
            rects[i] = new RectangleF(x, (maxH - h) / 2f, w, h);
            x += w + gap;
        }
        return rects;
    }

    /// <summary>
    /// Efeito simples de "página virando": um gradiente deslizante sobre a cena,
    /// simulando a folha em movimento. Para algo realista (perspectiva/curl),
    /// migre o painel para Direct2D/SharpDX ou WPF com PageTurnTemplate.
    /// </summary>
    private void DrawFlipEffect(Graphics g, float progress, float ox, float oy, SizeF totalSize)
    {
        float dirX = _flipDirection > 0 ^ _engine.RightToLeft ? 1 : -1; // sentido visual
        float pageX = ox + (dirX > 0 ? totalSize.Width * progress : totalSize.Width * (1 - progress));

        using var brush = new LinearGradientBrush(
            new RectangleF(pageX - 30, oy, 60, totalSize.Height),
            Color.FromArgb(120, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 0f);
        g.FillRectangle(brush, pageX - 30, oy, 60, totalSize.Height);
        g.DrawLine(Pens.Gray, pageX, oy, pageX, oy + totalSize.Height);
    }

    /// <summary>Modo tela cheia gerenciado pelo MainForm; o painel só se ajusta.</summary>
    public void CycleFit() =>
        Fit = Fit switch
        {
            FitMode.FitWidth  => FitMode.FitHeight,
            FitMode.FitHeight => FitMode.FitPage,
            FitMode.FitPage   => FitMode.ActualSize,
            _                 => FitMode.FitWidth,
        };
}
