using MangaReader.Models;
using MangaReader.Services;

namespace MangaReader.Views;

/// <summary>
/// Janela principal: sidebar (biblioteca) + painel central (páginas) + barra superior
/// com controles + barra inferior de navegação + status. Toda a paleta vem de
/// ThemeColors (troca em Opções → Interface).
/// </summary>
public sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ReaderEngine _engine;
    private readonly LibraryService _library;

    private readonly PagePanel _pagePanel;
    private readonly LibraryPanel _sidebar;
    private readonly Splitter _splitter = new() { Dock = DockStyle.Left, Width = 4 };
    private readonly ToolStrip _topBar = new() { Dock = DockStyle.Top };
    private readonly ToolStrip _bottomBar = new() { Dock = DockStyle.Bottom };
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblPage = new("Nenhuma página aberta");

    // botões "toggle" da barra inferior
    private readonly IconButton _btnManga = new("↔ Mangá");
    private readonly IconButton _btnDouble = new("⬛ Dupla");
    private readonly IconButton _btnFit = new("🔍 Ajuste");
    private readonly ToolStripLabel _lblZoom = new("");

    // estado de tela cheia
    private bool _isFullscreen;
    private FormWindowState _lastWindowState;
    private bool _sidebarWasVisible = true;

    public MainForm()
    {
        ThemeColors.Apply(Themes.FromName(_settings.Theme));

        Text = "MangaReader";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(640, 480);
        KeyPreview = true;
        BackColor = ThemeColors.Window;

        _engine = new ReaderEngine { Settings = _settings };
        _library = new LibraryService(new ReadingStateService());

        // ---- Painéis ----
        _pagePanel = new PagePanel(_engine, _settings) { Dock = DockStyle.Fill };
        _pagePanel.EnableDropSupport();
        _pagePanel.FileDropped += (_, path) => OpenPath(path);

        _sidebar = new LibraryPanel(_library, _settings) { Dock = DockStyle.Left };
        _sidebar.BookChosen += (_, path) => OpenBook(path);

        _splitter.BackColor = ThemeColors.Border;
        ThemeColors.Changed += (_, _) =>
        {
            BackColor = ThemeColors.Window;
            _splitter.BackColor = ThemeColors.Border;
            RestyleBars();
        };

        Controls.Add(_pagePanel);
        Controls.Add(_splitter);
        Controls.Add(_sidebar);
        BuildTopBar();
        BuildBottomBar();

        _engine.StateChanged += (_, _) => UpdateHud();

        // Biblioteca inicial (pastas das Opções; garante a pasta padrão existindo)
        var defaultLib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Comics");
        Directory.CreateDirectory(defaultLib);
        if (_settings.LibraryFolders.Count == 0) _settings.LibraryFolders.Add(defaultLib);
        _sidebar.Width = Math.Clamp(_settings.SidebarWidth, 220, 480);
        _sidebar.Visible = _settings.ShowSidebarOnStart;
        _splitter.Visible = _sidebar.Visible;
        _sidebar.Scan();

        // Abre arquivo passado pela linha de comando / "Abrir com…" do Explorer
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && BookFactory.IsSupported(args[1]))
            Shown += (_, _) => OpenBook(args[1]);
    }

    /// <summary>Chamado pelo Program quando o Explorer pede para abrir um arquivo.</summary>
    public void OpenFromCommandLine(string path) => OpenPath(path);

    /// <summary>Arquivo ou pasta (drag&drop / associação).</summary>
    public void OpenPath(string path)
    {
        if (Directory.Exists(path))
        {
            if (!_settings.LibraryFolders.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.LibraryFolders.Add(path);
                _settings.Save();
            }
            _sidebar.Visible = true;
            _splitter.Visible = true;
            _sidebar.Scan();
            return;
        }
        if (BookFactory.IsSupported(path)) { OpenBook(path); return; }
        MessageBox.Show(this, $"Formato não suportado:\n{path}\n\nUse .cbz .cbr .cbt .cb7 .zip .rar .tar .7z .pdf.",
            "MangaReader", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------- Barra superior ----------

    private void BuildTopBar()
    {
        _topBar.GripStyle = ToolStripGripStyle.Hidden;
        _topBar.Padding = new Padding(8, 4, 8, 4);
        _topBar.ImageScalingSize = new Size(20, 20);

        var btnOpen = new ToolStripButton("📂 Abrir…") { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Abrir arquivo (Ctrl+O)" };
        btnOpen.Click += (_, _) => OpenFileDialog();
        var btnLibrary = new ToolStripButton("📚") { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Mostrar/ocultar biblioteca (Tab)" };
        btnLibrary.Click += (_, _) => ToggleSidebar();
        var btnOptions = new ToolStripButton("⚙ Opções") { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Configurações (Ctrl+,)" };
        btnOptions.Click += (_, _) => ShowOptions();
        var btnHelp = new ToolStripButton("?") { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = "Atalhos (F1)" };
        btnHelp.Click += (_, _) => ShowHelp();

        _topBar.Items.AddRange([btnOpen, btnLibrary, new ToolStripSeparator(), btnOptions, btnHelp]);
        Controls.Add(_topBar);
        RestyleBars();
    }

    // ---------- Barra inferior ----------

    private void BuildBottomBar()
    {
        _bottomBar.GripStyle = ToolStripGripStyle.Hidden;
        _bottomBar.AutoSize = false;
        _bottomBar.Height = 44;
        _bottomBar.Padding = new Padding(10, 6, 10, 6);

        ToolStripControlHost Host(Control c) => new(c) { AutoSize = false, Margin = new Padding(2, 0, 2, 0) };

        IconButton Btn(string glyph, string tip, Action act)
        {
            var b = new IconButton(glyph) { ToolTipText = tip, Font = new Font("Segoe UI", 9f) };
            b.Click += (_, _) => act();
            return b;
        }

        _bottomBar.Items.Add(Host(Btn("⏮", "Primeira página (Home)", () => _engine.First())));
        _bottomBar.Items.Add(Host(Btn("◀", "Anterior (← / PageUp)", () => _pagePanel.Navigate(-1))));
        _bottomBar.Items.Add(Host(Btn("▶", "Próxima (→ / PageDown / Espaço)", () => _pagePanel.Navigate(+1))));
        _bottomBar.Items.Add(Host(Btn("⏭", "Última página (End)", () => _engine.Last())));
        _bottomBar.Items.Add(new ToolStripSeparator());

        _btnManga.ToolTipText = "Inverte o sentido de leitura (M)";
        _btnManga.Font = new Font("Segoe UI", 9f);
        _btnManga.Click += (_, _) => _engine.ToggleRightToLeft();
        _bottomBar.Items.Add(Host(_btnManga));

        _btnDouble.ToolTipText = "Página dupla (D)";
        _btnDouble.Font = new Font("Segoe UI", 9f);
        _btnDouble.Click += (_, _) => _engine.SetLayout(
            _engine.Layout == PageLayout.DoublePage ? PageLayout.SinglePage : PageLayout.DoublePage);
        _bottomBar.Items.Add(Host(_btnDouble));

        _btnFit.ToolTipText = "Alterna ajuste (W): largura → altura → página → 100%";
        _btnFit.Font = new Font("Segoe UI", 9f);
        _btnFit.Click += (_, _) => { _pagePanel.CycleFit(); _pagePanel.Invalidate(); };
        _bottomBar.Items.Add(Host(_btnFit));

        _bottomBar.Items.Add(new ToolStripSeparator());
        _bottomBar.Items.Add(Host(Btn("－", "Zoom − (−)", () => _engine.SetZoom(_engine.Zoom / 1.25))));
        _lblZoom.Font = new Font("Segoe UI", 9f);
        _lblZoom.AutoToolTip = false;
        _bottomBar.Items.Add(_lblZoom);
        _bottomBar.Items.Add(Host(Btn("＋", "Zoom ＋ (+)", () => _engine.SetZoom(_engine.Zoom * 1.25))));
        _bottomBar.Items.Add(new ToolStripSeparator());

        _bottomBar.Items.Add(Host(Btn("🖵 Tela cheia", "Tela cheia (F / F11 — Esc sai)", ToggleFullscreen)));

        var btnClose = Btn("✕ Fechar livro", "Fecha o livro atual e volta para a biblioteca (Ctrl+W)", () => CloseBook());
        _bottomBar.Items.Add(Host(btnClose));

        Controls.Add(_bottomBar);

        _status.Items.Add(_lblPage);
        _status.SizingGrip = false;
        Controls.Add(_status);
    }

    private void RestyleBars()
    {
        foreach (var bar in new[] { _topBar, _bottomBar })
        {
            bar.BackColor = ThemeColors.Panel;
            bar.ForeColor = ThemeColors.Text;
            bar.Renderer = new FlatRenderer(ThemeColors.Panel, ThemeColors.CardHover, ThemeColors.Accent, ThemeColors.Text);
        }
        _status.BackColor = ThemeColors.Panel;
        _status.ForeColor = ThemeColors.TextDim;
        _bottomBar.Refresh();
    }

    /// <summary>ToolStrip "flat" com hover/selection nas cores do tema.</summary>
    private sealed class FlatRenderer(Color back, Color hover, Color accent, Color text) : ToolStripProfessionalRenderer(new FlatColors(back, hover, accent, text))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = text;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(80, text));
            int y = e.Item.Bounds.Height / 2;
            e.Graphics.DrawLine(pen, 0, y, e.Item.Bounds.Width - 6, y);
        }

        private sealed class FlatColors(Color back, Color hover, Color accent, Color text) : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => back;
            public override Color ToolStripGradientMiddle => back;
            public override Color ToolStripGradientEnd => back;
            public override Color MenuItemSelected => hover;
            public override Color MenuItemSelectedGradientBegin => hover;
            public override Color MenuItemSelectedGradientEnd => hover;
            public override Color MenuItemPressedGradientBegin => accent;
            public override Color MenuItemPressedGradientEnd => accent;
            public override Color ImageMarginGradientBegin => back;
            public override Color ImageMarginGradientMiddle => back;
            public override Color ImageMarginGradientEnd => back;
            public override Color SeparatorDark => Color.FromArgb(70, text);
            public override Color SeparatorLight => Color.FromArgb(30, text);
        }
    }

    private void OpenFileDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Quadrinhos|Manga|PDF|*.cbz;*.cbr;*.cbt;*.cb7;*.zip;*.rar;*.tar;*.7z;*.pdf|Todos os arquivos|*.*",
            Title = "Abrir quadrinho",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenBook(dlg.FileName);
    }

    private void ShowOptions()
    {
        using var dlg = new OptionsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // aplica em cascata
        ThemeColors.Apply(Themes.FromName(_settings.Theme));
        _engine.Settings = _settings;
        _engine.ApplyCacheSettings();
        _pagePanel.Fit = _settings.Fit;
        _sidebar.ApplySettings(_settings);
        _splitter.Visible = _sidebar.Visible;
        if (_engine.Book is not null)
        {
            _engine.SetLayout(_settings.DoublePageByDefault ? PageLayout.DoublePage : PageLayout.SinglePage);
            _engine.Raise();
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(this,
            "Leitura\n" +
            "  ← → / PageUp/PageDown / Espaço   navegar     Home/End  início/fim\n" +
            "  Roda do mouse                     virar       Ctrl+Roda  zoom\n" +
            "  Clique nas bordas esq./dir.       configurável nas Opções (padrão: direita avança)\n" +
            "  Arrastar                          pan (com zoom > 100%)\n\n" +
            "Modos\n" +
            "  M mangá (direita→esquerda)   D página dupla   W alterna ajuste\n" +
            "  + / − zoom                   F ou F11 tela cheia (Esc sai)\n\n" +
            "Janela\n" +
            "  Tab biblioteca   Ctrl+O abrir   Ctrl+, opções   Ctrl+W fechar livro",
            "Atalhos do MangaReader", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void CloseBook()
    {
        _engine.CloseCurrent();
        Text = "MangaReader";
        _sidebar.Visible = true;
        _splitter.Visible = true;
        _sidebar.Scan();
        _pagePanel.Invalidate();
    }

    private void OpenBook(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _engine.Open(path);
            Text = $"MangaReader — {_engine.Book!.Metadata.DisplayTitle}";
            _pagePanel.Focus();
            _sidebar.NoteOpenedFile(path);

            // capa para a tela-vazia + ícone do app no Explorer (associação)
            TryUpdateCoverArtifacts(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir:\n{path}\n\n{ex.Message}",
                "Erro ao abrir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void TryUpdateCoverArtifacts(string path)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MangaReader");
            Directory.CreateDirectory(dir);
            var cover = Path.Combine(dir, "cover.png");
            using (var bmp = _engine.GetPageBitmap(0))
                bmp.Save(cover, System.Drawing.Imaging.ImageFormat.Png);

            ShellIntegration.BuildAppIconFromCover(cover, Path.Combine(dir, "app.ico"));
        }
        catch { /* melhor esforço */ }
    }

    private void ToggleSidebar()
    {
        _sidebar.Visible = !_sidebar.Visible;
        _splitter.Visible = _sidebar.Visible;
    }

    // ---------- HUD ----------

    private void UpdateHud()
    {
        if (_engine.Book is null)
        {
            _lblPage.Text = "Nenhuma página aberta";
            _lblZoom.Text = "";
            _btnManga.Active = _settings.RightToLeftByDefault;
            _btnDouble.Active = _settings.DoublePageByDefault;
            _bottomBar.Refresh();
            _pagePanel.Invalidate();
            return;
        }
        var spread = _engine.CurrentSpread();
        int shown = spread.Count > 0 ? spread.Max(p => p) + 1 : 0;
        var dir = _engine.RightToLeft ? "→← mangá" : "←→";
        _lblPage.Text =
            $"Página {shown} / {_engine.Book.PageCount}   •   folha {_engine.DisplayIndex + 1}/{_engine.SpreadCount}   •   {dir}" +
            (_engine.Layout == PageLayout.DoublePage ? "   •   dupla" : "") +
            (_engine.Zoom != 1.0 ? $"   •   zoom {_engine.Zoom:P0}" : "");
        _lblZoom.Text = $"{_engine.Zoom:P0}";

        _btnManga.Active = _engine.RightToLeft;
        _btnDouble.Active = _engine.Layout == PageLayout.DoublePage;
        _btnFit.Active = _pagePanel.Fit != FitMode.ActualSize;
        _bottomBar.Refresh();
        _pagePanel.Invalidate();
    }

    // ---------- Tela cheia ----------

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        if (_isFullscreen)
        {
            _lastWindowState = WindowState;
            _sidebarWasVisible = _sidebar.Visible;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            _sidebar.Visible = false;
            _splitter.Visible = false;
            _topBar.Visible = false;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _lastWindowState;
            TopMost = false;
            _sidebar.Visible = _sidebarWasVisible;
            _splitter.Visible = _sidebarWasVisible;
            _topBar.Visible = true;
        }
    }

    // ---------- Atalhos de teclado ----------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Right or Keys.PageDown or Keys.Space:
                if (_engine.Book is not null) _pagePanel.Navigate(+1); return true;
            case Keys.Left or Keys.PageUp or Keys.Back:
                if (_engine.Book is not null) _pagePanel.Navigate(-1); return true;
            case Keys.Home: _engine.First(); return true;
            case Keys.End: _engine.Last(); return true;
            case Keys.F or Keys.F11: ToggleFullscreen(); return true;
            case Keys.Escape when _isFullscreen: ToggleFullscreen(); return true;
            case Keys.M: _engine.ToggleRightToLeft(); return true;
            case Keys.D:
                _engine.SetLayout(_engine.Layout == PageLayout.DoublePage ? PageLayout.SinglePage : PageLayout.DoublePage);
                return true;
            case Keys.W: _pagePanel.CycleFit(); _pagePanel.Invalidate(); return true;
            case Keys.Tab:
                ToggleSidebar();
                return true;
            case Keys.Oemplus or Keys.Add: _engine.SetZoom(_engine.Zoom * 1.25); return true;
            case Keys.OemMinus or Keys.Subtract: _engine.SetZoom(_engine.Zoom / 1.25); return true;
            case Keys.Control | Keys.O: OpenFileDialog(); return true;
            case Keys.Control | Keys.OemPeriod: ShowOptions(); return true;   // Ctrl+,
            case Keys.Control | Keys.W: CloseBook(); return true;
            case Keys.F1: ShowHelp(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _settings.Save();
        _engine.Dispose();
        base.OnFormClosed(e);
    }
}
