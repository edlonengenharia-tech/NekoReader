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
    private readonly Services.AppSettings? _settings;

    // Retangulos calculados das paginas desenhadas (para pan/zoom e clique)
    private RectangleF[] _pageRects = [];
    private PointF _panOffset = PointF.Empty;
    private bool _dragging;
    private Point _dragStart;

    /// <summary>Animacao de virada de pagina: 0..1 (null = sem animacao).</summary>
    private float? _flipProgress;
    private int _flipDirection; // +1 avanca, -1 volta
    private System.Windows.Forms.Timer? _flipTimer;

    /// <summary>Modo de ajuste da pagina (lido no LayoutPages; CycleFit o alterna).</summary>
    public FitMode Fit { get; set; } = FitMode.FitWidth;
    public bool FlipAnimationEnabled => _settings?.FlipAnimation ?? true;

    /// <summary>Verdadeiro quando o ponteiro esta sobre uma zona de clique ativa.</summary>
    public bool HoverAdvanceZone { get; private set; }

    public PagePanel(Services.ReaderEngine engine, Services.AppSettings? settings = null)
    {
        _engine = engine;
        _settings = settings;
        Fit = settings?.Fit ?? FitMode.FitWidth;
        DoubleBuffered = true;
        BackColor = ThemeColors.Window;   // fundo "cinema" do tema corrente
        SetStyle(ControlStyles.ResizeRedraw, true);
        TabStop = true;                            // recebe teclado
        Cursor = Cursors.Hand;

        _engine.StateChanged += (_, _) => { _panOffset = PointF.Empty; Invalidate(); };
        ThemeColors.Changed += (_, _) => { BackColor = ThemeColors.Window; Invalidate(); };
    }

    /// <summary>Arrastar-e-soltar: aceita arquivos suportados ou pastas.</summary>
    public void EnableDropSupport()
    {
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                FileDropped?.Invoke(this, files[0]);
        };
    }

    public event EventHandler<string>? FileDropped;

    // ---------- Entrada do usuario ----------

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ModifierKeys.HasFlag(Keys.Control))
            _engine.SetZoom(_engine.Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
        else if (e.Delta < 0) Navigate(+1);          // roda p/ baixo = proxima
        else Navigate(-1);
    }

    /// <summary>Zonas de clique configuraveis (esq./dir.), espelhadas em modo manga.</summary>
    private (ClickAction left, ClickAction right) ClickZones()
    {
        var l = _settings?.LeftClickZone ?? ClickAction.None;
        var r = _settings?.RightClickZone ?? ClickAction.NextPage;
        return _engine.RightToLeft ? (r, l) : (l, r);
    }

    private bool ZoneAction(float xNorm, out ClickAction action)
    {
        var (left, right) = ClickZones();
        action = xNorm switch
        {
            < 0.25f => left,
            > 0.75f => right,
            _ => ClickAction.None,
        };
        return action != ClickAction.None;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _engine.Book is not null && _engine.Zoom <= 1.0)
        {
            // Zonas de clique estilo CDisplayEx (~25% / ~75%), conforme as Opcoes.
            float x = e.X / Math.Max(1f, Width);
            if (ZoneAction(x, out var action))
            {
                if (action == ClickAction.NextPage) Navigate(+1);
                else Navigate(-1);
                return;
            }
            _dragging = true; _dragStart = e.Location;
        }
        else if (e.Button == MouseButtons.Left)
        {
            _dragging = true; _dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_engine.Book is not null && _engine.Zoom <= 1.0)
        {
            bool hover = ZoneAction(e.X / Math.Max(1f, Width), out _);
            if (hover != HoverAdvanceZone) { HoverAdvanceZone = hover; Invalidate(); }
        }
        if (_dragging && _engine.Zoom > 1.0)
        {
            // PointF nao sobrecarrega operator+ — soma componente a componente.
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
        Invalidate();
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
        if (_engine.Book is null) { DrawEmptyState(g); return; }

        var spread = _engine.CurrentSpread();
        if (spread.Count == 0) return;

        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        // Páginas que falharam ao decodificar viram placeholder — nada de crash no paint.
        var bitmaps = new System.Drawing.Bitmap?[spread.Count];
        for (int i = 0; i < spread.Count; i++)
        {
            try { bitmaps[i] = _engine.GetPageBitmap(spread[i]); }
            catch (Exception) { bitmaps[i] = null; }
        }
        var valid = bitmaps.Where(b => b is not null).Cast<System.Drawing.Bitmap>().ToArray();
        if (valid.Length == 0) { DrawErrorState(g, spread[0]); return; }

        _pageRects = LayoutPages(valid, out var totalSize);

        // Centraliza o bloco de páginas + aplica pan
        float ox = (Width - totalSize.Width) / 2f + _panOffset.X;
        float oy = (Height - totalSize.Height) / 2f + _panOffset.Y;

        int vi = 0;
        for (int i = 0; i < bitmaps.Length; i++)
        {
            if (bitmaps[i] is null) continue;   // página corrompida: pula
            var rect = _pageRects[Math.Min(vi, _pageRects.Length - 1)];
            vi++;
            var dest = RectangleF.FromLTRB(rect.Left + ox, rect.Top + oy, rect.Right + ox, rect.Bottom + oy);
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillRectangle(shadow, dest.X + 4, dest.Y + 6, dest.Width, dest.Height); // sombra "livro"
            g.DrawImage(bitmaps[i]!, dest);
            // NÃO dá Dispose aqui: os bitmaps pertencem ao PageCache.
        }

        // Sombra/efeito de virada por cima da cena estática
        if (_flipProgress is { } p && p < 1f)
            DrawFlipEffect(g, p, ox, oy, totalSize);

        DrawHud(g);
    }

    /// <summary>Tela inicial: moldura de capa + instruções de arrastar/soltar.</summary>
    private void DrawEmptyState(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float cx = Width / 2f, cy = Height / 2f;

        float cw = Math.Min(240f, Width * 0.35f), ch = cw * 1.45f;
        var frame = new RectangleF(cx - cw / 2, Math.Max(40, cy - ch / 2 - 40), cw, ch);
        using (var path = UiDrawing.Rounded(frame, 14))
        {
            using var fill = new SolidBrush(ThemeColors.Card);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(120, ThemeColors.Border), 1.5f);
            g.DrawPath(pen, path);
        }
        if (CoverHint is { } cover)
        {
            g.SetClip(UiDrawing.Rounded(frame, 14));
            float s = Math.Max(frame.Width / cover.Width, frame.Height / cover.Height);
            int w = (int)(cover.Width * s), h = (int)(cover.Height * s);
            g.DrawImage(cover, frame.X + (frame.Width - w) / 2f, frame.Y + (frame.Height - h) / 2f, w, h);
            g.ResetClip();
        }
        else
        {
            using var f = new Font("Segoe UI", 40f);
            TextRenderer.DrawText(g, "📚", f, Rectangle.Truncate(frame), ThemeColors.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var titleRect = new Rectangle(0, (int)frame.Bottom + 26, Width, 34);
        TextRenderer.DrawText(g, "MangaReader", new Font("Segoe UI", 17f, FontStyle.Bold),
            titleRect, ThemeColors.Text, TextFormatFlags.HorizontalCenter);
        var subRect = new Rectangle(0, (int)frame.Bottom + 62, Width, 60);
        TextRenderer.DrawText(g,
            "Arraste um arquivo .cbz .cbr .cbt .cb7 .zip .rar .7z .pdf para cá\n" +
            "Ctrl+O abre um arquivo  •  Tab mostra a biblioteca  •  F1 ajuda",
            new Font("Segoe UI", 9.5f), subRect, ThemeColors.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }

    private void DrawErrorState(Graphics g, int physicalIndex)
    {
        var r = new Rectangle(0, Height / 2 - 40, Width, 80);
        TextRenderer.DrawText(g,
            $"Não foi possível exibir a página {physicalIndex + 1}.\n" +
            "O arquivo está corrompido ou protegido por senha — tente abri-lo com 7-Zip.",
            new Font("Segoe UI", 11f), r, Color.FromArgb(240, 120, 120),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }

    /// <summary>HUD flutuante: número da página (topo dir.) e barra de progresso (base).</summary>
    private void DrawHud(Graphics g)
    {
        var book = _engine.Book!;
        int shown = _engine.CurrentSpread().Count > 0 ? _engine.CurrentSpread().Max() + 1 : 0;

        string label = $"{shown} / {book.PageCount}";
        using (var font = new Font("Segoe UI", 9.5f, FontStyle.Bold))
        {
            var sz = TextRenderer.MeasureText(label, font);
            var chip = new RectangleF(Width - sz.Width - 26, 10, sz.Width + 18, sz.Height + 8);
            using var path = UiDrawing.Rounded(chip, 9);
            using var bg = new SolidBrush(Color.FromArgb(170, ThemeColors.Panel));
            g.FillPath(bg, path);
            TextRenderer.DrawText(g, label, font, Rectangle.Truncate(chip), ThemeColors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        const float barH = 4f;
        var barR = new RectangleF(0, Height - barH, Width, barH);
        using (var bgBar = new SolidBrush(Color.FromArgb(60, ThemeColors.Border)))
            g.FillRectangle(bgBar, barR);
        float frac = book.PageCount > 0 ? shown / (float)book.PageCount : 0;
        using var acc = new SolidBrush(Color.FromArgb(220, ThemeColors.Accent));
        bool rtl = _engine.RightToLeft;
        g.FillRectangle(acc, rtl ? Width - barR.Width * frac : 0, barR.Y, barR.Width * frac, barH);
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
