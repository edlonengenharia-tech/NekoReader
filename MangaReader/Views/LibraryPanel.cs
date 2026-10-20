using MangaReader.Services;

namespace MangaReader.Views;

/// <summary>
/// Painel lateral (esquerdo) com a biblioteca em grade de capas + árvore rápida
/// de navegação por páginas. Pode ser ocultado com Tab ou botão.
/// </summary>
public sealed class LibraryPanel : UserControl
{
    private readonly LibraryService _library;
    private readonly ReaderEngine _engine;
    private readonly ToolStrip _toolbar = new();
    private readonly FlowLayoutPanel _grid = new();
    private readonly ContextMenuStrip _menu = new();

    private List<string> _rootFolders = [];

    public event EventHandler<string>? BookChosen;

    public LibraryPanel(ReaderEngine engine, LibraryService library)
    {
        _engine = engine;
        _library = library;
        Width = 260;
        BackColor = Color.FromArgb(32, 32, 38);

        // ---- Toolbar ----
        var btnAdd = new ToolStripButton("📂 Adicionar pasta");
        btnAdd.Click += (_, _) => AddFolderDialog();
        var btnRefresh = new ToolStripButton("🔄 Atualizar");
        btnRefresh.Click += (_, _) => _ = ReloadAsync();
        _toolbar.Items.AddRange([btnAdd, btnRefresh]);
        _toolbar.Dock = DockStyle.Top;
        _toolbar.GripStyle = ToolStripGripStyle.Hidden;

        // ---- Grade de capas ----
        _grid.Dock = DockStyle.Fill;
        _grid.AutoScroll = true;
        _grid.WrapContents = true;
        _grid.FlowDirection = FlowDirection.LeftToRight;
        _grid.Padding = new Padding(8);

        Controls.Add(_grid);
        Controls.Add(_toolbar);
    }

    public void ConfigureFolders(IEnumerable<string> folders)
    {
        _rootFolders = folders.ToList();
        _ = ReloadAsync();
    }

    private void AddFolderDialog()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pasta da biblioteca de quadrinhos" };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _rootFolders.Add(dlg.SelectedPath);
            _ = ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        _grid.Controls.Clear();
        var items = await Task.Run(() => _library.Scan(_rootFolders).ToList());

        foreach (var item in items)
        {
            var card = CreateCard(item);
            _grid.Controls.Add(card);
            // Miniaturas geradas em background para não travar a UI
            _ = Task.Run(() =>
            {
                _library.EnsureCover(item);
                if (card.IsDisposed) return;
                card.BeginInvoke(() => ApplyCover(card, item));
            });
        }
    }

    private Control CreateCard(LibraryService.LibraryItem item)
    {
        var panel = new DoubleBufferedPanel
        {
            Size = new Size(176, 240),
            Margin = new Padding(6),
            Tag = item,
            Cursor = Cursors.Hand,
        };

        var title = new Label
        {
            Text = item.Metadata.DisplayTitle +
                   (item.State is { } s && s.Progress > 0 ? $"\n{s.Progress:P0} lido" : ""),
            Dock = DockStyle.Bottom,
            Height = 44,
            ForeColor = Color.Gainsboro,
            Font = new Font("Segoe UI", 8.5f),
            TextAlign = ContentAlignment.TopLeft,
        };
        panel.Controls.Add(title);

        panel.Click += (_, _) => BookChosen?.Invoke(this, item.FilePath);
        panel.Paint += (_, e) =>
        {
            var g = e.Graphics!;
            var r = panel.ClientRectangle;
            r.Height -= 44;
            using var bg = new SolidBrush(Color.FromArgb(48, 48, 56));
            g.FillRectangle(bg, r); // placeholder até a capa carregar
            if (panel.Tag is LibraryService.LibraryItem li && File.Exists(li.CoverCachePath))
            {
                try
                {
                    using var img = System.Drawing.Image.FromFile(li.CoverCachePath);
                    DrawCoverFit(g, img, r);
                }
                catch { /* arquivo de cache corrompido: mantém placeholder */ }
            }
            else
            {
                using var f = new Font("Segoe UI", 26f);
                g.DrawString("📖", f, Brushes.DimGray,
                    r.X + r.Width / 2f - 20, r.Y + r.Height / 2f - 24);
            }
        };
        return panel;
    }

    private static void DrawCoverFit(Graphics g, System.Drawing.Image img, Rectangle r)
    {
        float s = Math.Min(r.Width / (float)img.Width, r.Height / (float)img.Height);
        int w = (int)(img.Width * s), h = (int)(img.Height * s);
        g.DrawImage(img, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
    }

    private void ApplyCover(Control card, LibraryService.LibraryItem item)
    {
        if (!File.Exists(item.CoverCachePath)) return;
        card.Invalidate(); // o handler de Paint acima passa a desenhar a capa
    }

    /// <summary>Sub-classe só para habilitar double buffering no painel custom.</summary>
    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() => DoubleBuffered = true;
    }
}
