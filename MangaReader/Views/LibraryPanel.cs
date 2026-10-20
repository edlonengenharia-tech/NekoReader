using MangaReader.Models;
using MangaReader.Services;

namespace MangaReader.Views;

/// <summary>
/// Painel lateral "Biblioteca" no estilo do protótipo: busca com chips de filtro,
/// seção "Continuar lendo" (cartões horizontais com barra de progresso) e a grade
/// completa em lista. As pastas vêm das Opções; o scan roda em background.
/// </summary>
public sealed class LibraryPanel : UserControl
{
    private readonly LibraryService _library;
    private readonly ReadingStateService _states = new();
    private AppSettings _settings;

    private List<LibraryService.LibraryItem> _items = [];
    private CancellationTokenSource? _scanCts;
    private string _filter = "";
    private bool _onlyUnread, _onlyManga;

    private readonly Label _lblTitle = new()
    {
        Text = "  📚 Biblioteca",
        Dock = DockStyle.Top,
        Height = 42,
        Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft,
    };
    private readonly Panel _searchWrap = new() { Dock = DockStyle.Top, Height = 40 };
    private readonly TextBox _txtSearch = new()
    {
        PlaceholderText = "Buscar por título, série ou autor…",
        BorderStyle = BorderStyle.None,
        Font = new Font("Segoe UI", 9.5f),
    };
    private readonly FlowLayoutPanel _chips = new()
    {
        Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false, Padding = new Padding(10, 5, 6, 2),
    };
    private readonly FlowLayoutPanel _flow = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, AutoScroll = true, Padding = new Padding(8, 2, 8, 14),
    };

    /// <summary>Disparado quando o usuário clica num item.</summary>
    public event EventHandler<string>? BookChosen;

    public LibraryPanel(LibraryService library, AppSettings settings)
    {
        _library = library;
        _settings = settings;
        Width = Math.Clamp(settings.SidebarWidth, 220, 480);

        // caixa de busca dentro de um "card" arredondado
        _searchWrap.Padding = new Padding(10, 8, 10, 8);
        _txtSearch.Width = _searchWrap.Width - 24;
        _txtSearch.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _txtSearch.TextChanged += (_, _) => { _filter = _txtSearch.Text.Trim(); RebuildGrid(); };
        _searchWrap.Controls.Add(_txtSearch);

        BuildChips();

        Controls.Add(_flow);
        Controls.Add(_chips);
        Controls.Add(_searchWrap);
        Controls.Add(_lblTitle);

        ThemeColors.Changed += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    private void BuildChips()
    {
        _chips.Controls.Clear();
        _chips.Controls.Add(new ToggleChip("Não lidos")
        {
            Active = _onlyUnread,
        }.OnToggle(v => { _onlyUnread = v; RebuildGrid(); }));
        _chips.Controls.Add(new ToggleChip("Mangá")
        {
            Active = _onlyManga,
        }.OnToggle(v => { _onlyManga = v; RebuildGrid(); }));
        _chips.Controls.Add(new ToggleChip("↻")
        {
            Active = false,
        }.OnToggle(_ => Scan()));
    }

    /// <summary>Aplica novas configurações (pastas/tema/largura) vindas das Opções.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        Width = Math.Clamp(settings.SidebarWidth, 220, 480);
        Scan();
    }

    /// <summary>Varre as pastas configuradas em background (UI não congela).</summary>
    public void Scan()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        _flow.SuspendLayout();
        ShowBusy(true);

        var folders = _settings.LibraryFolders.Where(Directory.Exists).ToList();
        Task.Run(() =>
        {
            var found = _library.Scan(folders).ToList();
            // capas ainda ausentes são geradas durante o scan (melhor esforço)
            foreach (var it in found.Where(i => !File.Exists(i.CoverCachePath)))
            {
                if (cts.IsCancellationRequested) return;
                try { _library.EnsureCover(it); } catch { }
            }
            return found;
        }, cts.Token).ContinueWith(t =>
        {
            if (cts.IsCancellationRequested || t.IsFaulted) return;
            _items = t.Result;
            ShowBusy(false);
            RebuildGrid();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Garante que a pasta de um arquivo aberto entre na biblioteca.</summary>
    public void NoteOpenedFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        if (!_settings.LibraryFolders.Any(f => string.Equals(f, dir, StringComparison.OrdinalIgnoreCase)) &&
            !_settings.LibraryFolders.Any(f => dir.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.LibraryFolders.Add(dir);
            _settings.Save();
        }
    }

    private void ShowBusy(bool busy)
    {
        _lblTitle.Text = busy ? "  📚 Biblioteca — escaneando…" : "  📚 Biblioteca";
        _lblTitle.Invalidate();
    }

    // ---------- grade ----------

    private void RebuildGrid()
    {
        _flow.SuspendLayout();
        foreach (Control c in _flow.Controls.Values.OfType<Control>()) c.Dispose();
        _flow.Controls.Clear();

        var filtered = _items
            .Where(i => string.IsNullOrWhiteSpace(_filter) ||
                        i.Metadata.DisplayTitle.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                        (i.Metadata.Series ?? "").Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                        (i.Metadata.Author ?? "").Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                        (i.Metadata.Writer ?? "").Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.State?.LastOpenedUtc ?? DateTime.MinValue)
            .ThenBy(i => i.Metadata.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_onlyUnread)
            filtered = filtered.Where(i => i.State is null || i.State.Progress <= 0.01).ToList();
        if (_onlyManga)
            filtered = filtered.Where(i => i.Metadata.IsManga).ToList();

        // ---- Continuar lendo ----
        if (string.IsNullOrWhiteSpace(_filter) && !_onlyUnread && !_onlyManga)
        {
            var resume = _items
                .Where(i => i.State is { Progress: > 0.01 and < 0.995 })
                .OrderByDescending(i => i.State!.LastOpenedUtc)
                .Take(5)
                .ToList();
            if (resume.Count > 0)
            {
                _flow.Controls.Add(SectionHeader("▶  Continuar lendo"));
                foreach (var item in resume)
                    _flow.Controls.Add(BuildCard(item, progress: true));
            }
        }

        _flow.Controls.Add(SectionHeader($"Todos os itens ({filtered.Count})"));
        if (filtered.Count == 0 && _items.Count == 0)
        {
            _flow.Controls.Add(new Label
            {
                Text = "Nada por aqui ainda.\n\nArraste um arquivo para a janela,\nou adicione pastas em\nOpções → Biblioteca & Windows.",
                ForeColor = ThemeColors.TextDim,
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Margin = new Padding(8, 14, 0, 0),
            });
        }
        foreach (var item in filtered)
            _flow.Controls.Add(BuildCard(item, progress: false));

        _flow.ResumeLayout();
    }

    private static Control SectionHeader(string text) => new Label
    {
        Text = text.ToUpperInvariant(),
        Width = 250, Height = 32, AutoSize = false,
        ForeColor = ThemeColors.TextDim,
        Font = new Font("Segoe UI", 8.2f, FontStyle.Bold),
        TextAlign = ContentAlignment.BottomLeft,
        Margin = new Padding(4, 12, 0, 4),
    };

    private BookCard BuildCard(LibraryService.LibraryItem item, bool progress)
    {
        var card = new BookCard(item, progress) { Margin = new Padding(2, 3, 2, 3) };
        card.Click += (_, _) => BookChosen?.Invoke(this, item.FilePath);
        return card;
    }

    private void ApplyTheme()
    {
        BackColor = ThemeColors.Sidebar;
        _lblTitle.BackColor = ThemeColors.Sidebar;
        _lblTitle.ForeColor = ThemeColors.Text;
        _searchWrap.BackColor = ThemeColors.Sidebar;
        _chips.BackColor = ThemeColors.Sidebar;
        _flow.BackColor = ThemeColors.Sidebar;
        _txtSearch.BackColor = ThemeColors.Card;
        _txtSearch.ForeColor = ThemeColors.Text;
        BuildChips();
        RebuildGrid();
    }
}

internal static class ChipExtensions
{
    public static ToggleChip OnToggle(this ToggleChip chip, Action<bool> act)
    {
        chip.Toggled += act;
        return chip;
    }
}

/// <summary>Chip filtrável (pill) usado acima da grade da biblioteca.</summary>
internal sealed class ToggleChip : Control
{
    private bool _hovered;
    public bool Active { get; set; }
    public event Action<bool>? Toggled;

    public ToggleChip(string text)
    {
        Text = text;
        Size = new Size(text.Length * 8 + 26, 26);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Active = !Active;
        Toggled?.Invoke(Active);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? ThemeColors.Sidebar);
        var r = new RectangleF(1, 2, Width - 3, Height - 5);
        using var path = UiDrawing.Rounded(r, r.Height / 2);
        Color back = Active ? ThemeColors.Accent : _hovered ? ThemeColors.CardHover : ThemeColors.Card;
        using (var b = new SolidBrush(back)) g.FillPath(b, path);
        using var pen = new Pen(Color.FromArgb(90, ThemeColors.Border));
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, Rectangle.Truncate(r),
            Active ? Color.White : ThemeColors.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Cartão horizontal: mini-capa + título + subtítulo (+ barra de progresso).</summary>
internal sealed class BookCard : Control
{
    private readonly LibraryService.LibraryItem _item;
    private readonly bool _showProgress;
    private Image? _cover;
    private bool _hovered;

    public BookCard(LibraryService.LibraryItem item, bool showProgress)
    {
        _item = item;
        _showProgress = showProgress;
        Size = new Size(252, 64);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        LoadCover();
    }

    private void LoadCover()
    {
        try
        {
            if (!File.Exists(_item.CoverCachePath)) return;
            using var fs = File.OpenRead(_item.CoverCachePath);
            if (fs.Length == 0) return;
            using var tmp = Image.FromStream(fs);
            _cover = new Bitmap(tmp);
        }
        catch { _cover = null; }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? ThemeColors.Sidebar);

        var meta = _item.Metadata;
        var full = new RectangleF(0, 1, Width - 4, Height - 6);
        using (var path = UiDrawing.Rounded(full, 10))
        {
            using var b = new SolidBrush(_hovered ? ThemeColors.CardHover : ThemeColors.Card);
            g.FillPath(b, path);
        }

        var coverRect = new RectangleF(8, 7, 42, 50);
        using (var clip = UiDrawing.Rounded(coverRect, 5))
        {
            g.SetClip(clip);
            if (_cover is { } img)
            {
                float s = Math.Max(coverRect.Width / img.Width, coverRect.Height / img.Height);
                int w = (int)(img.Width * s), h = (int)(img.Height * s);
                g.DrawImage(img, coverRect.X + (coverRect.Width - w) / 2f,
                            coverRect.Y + (coverRect.Height - h) / 2f, w, h);
            }
            else
            {
                using var ph = new SolidBrush(ThemeColors.Panel);
                g.FillRectangle(ph, coverRect.X, coverRect.Y, coverRect.Width, coverRect.Height);
                using var f = new Font("Segoe UI", 16f);
                TextRenderer.DrawText(g, "📖", f, Rectangle.Truncate(coverRect), ThemeColors.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            g.ResetClip();
        }

        var textRect = new Rectangle((int)coverRect.Right + 10, 8,
            (int)full.Width - (int)coverRect.Right - 20, 30);
        TextRenderer.DrawText(g, meta.DisplayTitle, new Font("Segoe UI", 9.3f, FontStyle.Bold),
            textRect, ThemeColors.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.Top);

        string sub = string.Join(" · ", new[]
        {
            meta.Series, string.IsNullOrWhiteSpace(meta.Number) ? null : $"#{meta.Number}", $"{meta.PageCount} pág."
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var subRect = new Rectangle(textRect.X, textRect.Bottom + 1, textRect.Width, 15);
        TextRenderer.DrawText(g, sub, new Font("Segoe UI", 8.3f), subRect, ThemeColors.TextDim,
            TextFormatFlags.EndEllipsis | TextFormatFlags.Left);

        if (_showProgress && _item.State is { } st)
        {
            var bar = new RectangleF(textRect.X, Height - 15, textRect.Width, 4);
            using var bg = new SolidBrush(ThemeColors.Panel);
            g.FillRectangle(bg, bar);
            using var acc = new SolidBrush(ThemeColors.Accent);
            g.FillRectangle(acc, new RectangleF(bar.X, bar.Y, bar.Width * (float)st.Progress, bar.Height));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cover?.Dispose();
        base.Dispose(disposing);
    }
}
