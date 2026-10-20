using MangaReader.Services;

namespace MangaReader.Views;

/// <summary>
/// Diálogo "Opções" com abas Leitura / Interface / Biblioteca & Windows.
/// Edita uma cópia de AppSettings; só grava ao clicar em OK (e aplica a associação
/// do Explorer quando marcada).
/// </summary>
public sealed class OptionsForm : Form
{
    private readonly AppSettings _settings;

    // Leitura
    private readonly CheckBox _chkRtl = new() { Text = "Abrir tudo em modo mangá (direita → esquerda)" };
    private readonly CheckBox _chkDouble = new() { Text = "Começar em página dupla" };
    private readonly ComboBox _cmbFit = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkFlip = new() { Text = "Animação de virada de página" };
    private readonly NumericUpDown _numPrefetch = new() { Minimum = 0, Maximum = 5 };
    private readonly NumericUpDown _numCache = new() { Minimum = 4, Maximum = 64, Increment = 2 };
    private readonly ComboBox _cmbClickLeft = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbClickRight = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    // Interface
    private readonly ComboBox _cmbTheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkSidebarStart = new() { Text = "Mostrar a biblioteca ao abrir o programa" };
    private readonly TrackBar _trSidebar = new() { Minimum = 220, Maximum = 480, TickFrequency = 20, SmallChange = 10, LargeChange = 40 };
    private readonly Label _lblSidebarVal = new();

    // Biblioteca & Windows
    private readonly ListBox _lstFolders = new() { Height = 140, IntegralHeight = false };
    private readonly CheckBox _chkAssociate = new() { Text = "Associar .cbz .cbr .cbt .cb7 .zip .rar .tar .7z .pdf com o MangaReader" };
    private readonly Label _lblAssocState = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    public OptionsForm(AppSettings settings)
    {
        _settings = settings;

        Title = "Opções — MangaReader";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 470);
        Font = new Font("Segoe UI", 9f);
        BackColor = ThemeColors.Panel;
        ForeColor = ThemeColors.Text;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        var tabRead = NewTab("Leitura");
        var tabUi = NewTab("Interface");
        var tabLib = NewTab("Biblioteca & Windows");
        tabs.TabPages.AddRange([tabRead, tabUi, tabLib]);

        // ---------- aba Leitura ----------
        _cmbFit.Items.AddRange(["Ajustar à largura", "Ajustar à altura", "Página inteira", "100% (real)"]);
        _cmbFit.SelectedIndex = FitIndex(_settings.FitMode);
        FillClickCombo(_cmbClickLeft, _settings.LeftClickZone);
        FillClickCombo(_cmbClickRight, _settings.RightClickZone);

        Stack(tabRead,
            _chkRtl, _chkDouble, _chkFlip,
            Row("Ajuste padrão:", _cmbFit, 130),
            Row("Páginas pré-carregadas:", _numPrefetch, 70),
            Row("Cache de páginas:", _numCache, 70),
            Row("Clique zona esq.:", _cmbClickLeft, 160),
            Row("Clique zona dir.:", _cmbClickRight, 160));

        // ---------- aba Interface ----------
        foreach (var (_, label) in Themes.Options) _cmbTheme.Items.Add(label);
        _cmbTheme.SelectedIndex = Math.Max(0, Array.FindIndex(Themes.Options, o => o.Id == _settings.Theme));
        _trSidebar.Value = Math.Clamp(_settings.SidebarWidth, _trSidebar.Minimum, _trSidebar.Maximum);
        _lblSidebarVal.Text = $"{_trSidebar.Value} px";
        _trSidebar.ValueChanged += (_, _) => _lblSidebarVal.Text = $"{_trSidebar.Value} px";

        Stack(tabUi,
            Row("Tema:", _cmbTheme, 160),
            _chkSidebarStart,
            Row("Largura da biblioteca:", _trSidebar, 240),
            _lblSidebarVal);

        // ---------- aba Biblioteca & Windows ----------
        foreach (var f in _settings.LibraryFolders) _lstFolders.Items.Add(f);
        _chkAssociate.Checked = ShellIntegration.IsAssociated();
        _lblAssocState.Text = ShellIntegration.IsAssociated()
            ? "Status: associado neste usuário (HKCU)."
            : "Status: não associado.";

        var btnAddFolder = PillButton("+ Adicionar pasta…");
        btnAddFolder.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = "Pasta para adicionar à biblioteca" };
            if (dlg.ShowDialog(this) == DialogResult.OK && !_lstFolders.Items.Contains(dlg.SelectedPath))
                _lstFolders.Items.Add(dlg.SelectedPath);
        };
        var btnRemoveFolder = PillButton("− Remover selecionada");
        btnRemoveFolder.Click += (_, _) =>
        {
            if (_lstFolders.SelectedIndex >= 0) _lstFolders.Items.RemoveAt(_lstFolders.SelectedIndex);
        };

        var libPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 6, 0, 6),
        };
        libPanel.Controls.Add(btnAddFolder);
        libPanel.Controls.Add(btnRemoveFolder);

        Stack(tabLib, _lstFolders, libPanel, _chkAssociate, _lblAssocState);

        // ---------- OK / Cancelar ----------
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(8, 10, 12, 8), BackColor = ThemeColors.Panel,
        };
        var btnCancel = PillButton("Cancelar");
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = PillButton("OK", accent: true);
        btnOk.Click += (_, _) => { Apply(); DialogResult = DialogResult.OK; Close(); };
        bottom.Controls.Add(btnOk);
        bottom.Controls.Add(btnCancel);

        Controls.Add(tabs);
        Controls.Add(bottom);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static TabPage NewTab(string title)
    {
        var page = new TabPage(title) { BackColor = ThemeColors.Panel, UseVisualStyleBackColor = false };
        return page;
    }

    private static void Stack(TabPage page, params Control[] controls)
    {
        int y = 14;
        foreach (var c in controls)
        {
            c.Location = new Point(16, y);
            c.Width = Math.Max(c.Width, page.DisplayRectangle.Width - 32);
            if (c is CheckBox or Label) c.AutoSize = true;
            page.Controls.Add(c);
            y += c.Height + 10;
        }
    }

    private static Control Row(string caption, Control input, int inputWidth)
    {
        var panel = new Panel { Height = 30 };
        var lbl = new Label { Text = caption, AutoSize = true, Location = new Point(0, 6) };
        input.Location = new Point(190, 2);
        input.Width = inputWidth;
        panel.Controls.Add(lbl);
        panel.Controls.Add(input);
        return panel;
    }

    private static Button PillButton(string text, bool accent = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat,
            Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 0),
            BackColor = accent ? ThemeColors.Accent : ThemeColors.Card,
            ForeColor = accent ? Color.White : ThemeColors.Text,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private static int FitIndex(string fit) => fit switch
    {
        "FitHeight" => 1, "FitPage" => 2, "ActualSize" => 3, _ => 0,
    };

    private static string FitName(int i) => i switch
    {
        1 => "FitHeight", 2 => "FitPage", 3 => "ActualSize", _ => "FitWidth",
    };

    private static void FillClickCombo(ComboBox cmb, ClickAction selected)
    {
        cmb.Items.AddRange(["Nada", "Página anterior", "Próxima página"]);
        cmb.SelectedIndex = (int)selected;
    }

    private void Apply()
    {
        _settings.RightToLeftByDefault = _chkRtl.Checked;
        _settings.DoublePageByDefault = _chkDouble.Checked;
        _settings.FlipAnimation = _chkFlip.Checked;
        _settings.FitMode = FitName(_cmbFit.SelectedIndex);
        _settings.PrefetchPages = (int)_numPrefetch.Value;
        _settings.CacheEntries = (int)_numCache.Value;
        _settings.LeftClickZone = (ClickAction)Math.Max(0, _cmbClickLeft.SelectedIndex);
        _settings.RightClickZone = (ClickAction)Math.Max(0, _cmbClickRight.SelectedIndex);

        _settings.Theme = Themes.Options[Math.Max(0, _cmbTheme.SelectedIndex)].Id;
        _settings.ShowSidebarOnStart = _chkSidebarStart.Checked;
        _settings.SidebarWidth = _trSidebar.Value;

        _settings.LibraryFolders = _lstFolders.Items.Cast<string>().ToList();

        bool wasAssociated = ShellIntegration.IsAssociated();
        if (_chkAssociate.Checked && !wasAssociated)
        {
            var iconDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MangaReader");
            Directory.CreateDirectory(iconDir);
            var icon = Path.Combine(iconDir, "app.ico");
            ShellIntegration.Associate(File.Exists(icon) ? icon : "");
        }
        else if (!_chkAssociate.Checked && wasAssociated)
        {
            ShellIntegration.RemoveAssociation();
        }

        _settings.Save();
    }
}
