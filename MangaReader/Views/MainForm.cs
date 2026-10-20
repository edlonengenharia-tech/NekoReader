using MangaReader.Models;
using MangaReader.Services;

namespace MangaReader.Views;

/// <summary>
/// Janela principal: painel lateral (biblioteca) + painel central (páginas) +
/// barra inferior de controles + atalhos de teclado.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ReaderEngine _engine = new();
    private readonly LibraryService _library;
    private readonly ReadingStateService _states = new();

    private readonly LibraryPanel _sidebar;
    private readonly PagePanel _pagePanel;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblPage = new("Nenhuma página aberta");
    private readonly Splitter _splitter = new();

    // Estado de tela cheia
    private bool _isFullscreen;
    private FormWindowState _lastWindowState;
    private bool _sidebarWasVisible = true;

    public MainForm()
    {
        Text = "MangaReader";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(640, 480);
        BackColor = Color.FromArgb(24, 24, 28);
        KeyPreview = true;

        _library = new LibraryService(_states);

        // ---- Painéis ----
        _pagePanel = new PagePanel(_engine) { Dock = DockStyle.Fill };
        _sidebar = new LibraryPanel(_engine, _library) { Dock = DockStyle.Left, Visible = false };
        _sidebar.BookChosen += (_, path) => OpenBook(path);

        _splitter.Dock = DockStyle.Left;
        _splitter.Width = 4;
        _splitter.BackColor = Color.FromArgb(48, 48, 56);

        Controls.Add(_pagePanel);
        Controls.Add(_splitter);
        Controls.Add(_sidebar);
        BuildBottomBar();

        // ---- Atualização da barra/status ----
        _engine.StateChanged += (_, _) => UpdateHud();

        // Biblioteca inicial: pasta padrão + última usada
        var defaultLib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Comics");
        Directory.CreateDirectory(defaultLib);
        _sidebar.ConfigureFolders([defaultLib]);
        _sidebar.Visible = true;

        // Abre arquivo passado pela linha de comando (associação do Explorer)
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && BookFactory.IsSupported(args[1]))
            OpenBook(args[1]);
    }

    /// <summary>Chamado pelo Program quando o Explorer pede para abrir um arquivo.</summary>
    public void OpenFromCommandLine(string path) => OpenBook(path);

    // ---------- Barra inferior de controles ----------

    private void BuildBottomBar()
    {
        var strip = new ToolStrip
        {
            Dock = DockStyle.Bottom,
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = Color.FromArgb(32, 32, 38),
            ForeColor = Color.Gainsboro,
            Padding = new Padding(6, 2, 6, 2),
        };

        ToolStripButton Btn(string text, string tip, Action act)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += (_, _) => act();
            return b;
        }

        strip.Items.Add(Btn("⏮", "Primeira página (Home)", _engine.First));
        strip.Items.Add(Btn("◀", "Anterior (← / PageUp)", () => _pagePanel.Navigate(-1)));
        strip.Items.Add(Btn("▶", "Próxima (→ / PageDown / Espaço)", () => _pagePanel.Navigate(+1)));
        strip.Items.Add(Btn("⏭", "Última página (End)", _engine.Last));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(Btn("↔ Mangá", "Inverte o sentido de leitura (M)", _engine.ToggleRightToLeft));
        strip.Items.Add(Btn("⬛ Dupla", "Página dupla (D)",
            () => _engine.SetLayout(_engine.Layout == PageLayout.DoublePage ? PageLayout.SinglePage : PageLayout.DoublePage)));
        strip.Items.Add(Btn("🔍 Ajuste", "Alterna ajuste (W): largura → altura → página → 100%",
            () => { _pagePanel.CycleFit(); _pagePanel.Invalidate(); }));
        strip.Items.Add(Btn("－", "Zoom − (−)", () => _engine.SetZoom(_engine.Zoom / 1.25)));
        strip.Items.Add(Btn("＋", "Zoom ＋ (+)", () => _engine.SetZoom(_engine.Zoom * 1.25)));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(Btn("F11 Tela cheia", "Tela cheia (F / F11 — Esc sai)", ToggleFullscreen));
        strip.Items.Add(Btn("📚 Biblioteca", "Mostra/oculta a biblioteca (Tab)",
            () => { _sidebar.Visible = !_sidebar.Visible; _splitter.Visible = _sidebar.Visible; }));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(Btn("Abrir…", "Abrir arquivo (Ctrl+O)", OpenFileDialog));

        Controls.Add(strip);

        _status.Items.Add(_lblPage);
        _status.SizingGrip = false;
        _status.BackColor = Color.FromArgb(32, 32, 38);
        _status.ForeColor = Color.Gainsboro;
        Controls.Add(_status);
    }

    private void OpenFileDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Quadrinhos|Manga|PDF|" +
                     "*.cbz;*.cbr;*.cbt;*.cb7;*.zip;*.rar;*.tar;*.7z;*.pdf|Todos os arquivos|*.*",
            Title = "Abrir quadrinho",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenBook(dlg.FileName);
    }

    private void OpenBook(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _engine.Open(path);
            Text = $"MangaReader — {_engine.Book!.Metadata.DisplayTitle}";
            _pagePanel.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir:\n{path}\n\n{ex.Message}",
                "Erro ao abrir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Cursor = Cursors.Default; }
    }

    // ---------- HUD ----------

    private void UpdateHud()
    {
        if (_engine.Book is null) { _lblPage.Text = "Nenhuma página aberta"; return; }
        var spread = _engine.CurrentSpread();
        int shown = spread.Count > 0 ? spread.Max(p => p) + 1 : 0;
        var dir = _engine.RightToLeft ? "→← mangá" : "←→";
        _lblPage.Text =
            $"Página {shown} / {_engine.Book.PageCount}   •   folha {_engine.DisplayIndex + 1}/{_engine.SpreadCount}   •   {dir}" +
            (_engine.Layout == PageLayout.DoublePage ? "   •   dupla" : "") +
            (_engine.Zoom != 1.0 ? $"   •   zoom {_engine.Zoom:P0}" : "");
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
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _lastWindowState;
            TopMost = false;
            _sidebar.Visible = _sidebarWasVisible;
            _splitter.Visible = _sidebarWasVisible;
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
                _sidebar.Visible = !_sidebar.Visible;
                _splitter.Visible = _sidebar.Visible;
                return true;
            case Keys.Oemplus or Keys.Add: _engine.SetZoom(_engine.Zoom * 1.25); return true;
            case Keys.OemMinus or Keys.Subtract: _engine.SetZoom(_engine.Zoom / 1.25); return true;
            case Keys.Control | Keys.O: OpenFileDialog(); return true;
            case Keys.Control | Keys.L: ConfigureLibraryFolders(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ConfigureLibraryFolders()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pasta da biblioteca" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _sidebar.ConfigureFolders([dlg.SelectedPath]);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.Dispose();
        base.OnFormClosed(e);
    }
}
