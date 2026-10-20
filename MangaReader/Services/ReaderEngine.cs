using MangaReader.Models;

namespace MangaReader.Services;

/// <summary>
/// "Cérebro" do leitor: mantém o livro aberto, traduz índices de exibição ⇄ físicos
/// conforme o sentido de leitura (RTL = mangá) e o layout (1 ou 2 páginas), serve
/// bitmaps via cache LRU e persiste a posição automaticamente.
/// </summary>
public sealed class ReaderEngine : IDisposable
{
    private readonly PageCache _cache = new(maxEntries: 12);
    private readonly ReadingStateService _stateService = new();
    private CancellationTokenSource? _prefetchCts;

    public IBook? Book { get; private set; }
    public bool RightToLeft { get; private set; }          // modo mangá
    public PageLayout Layout { get; private set; } = PageLayout.SinglePage;
    public int DisplayIndex { get; private set; }           // "folha" atual (0-based na ordem de leitura)
    public double Zoom { get; private set; } = 1.0;         // multiplicador sobre o modo de ajuste

    /// <summary>Total de "folhas" no modo atual (páginas ÷ 2 em dupla-página).</summary>
    public int SpreadCount => Book is null ? 0
        : Layout == PageLayout.DoublePage ? (int)Math.Ceiling(Book.PageCount / 2.0)
        : Book.PageCount;

    public event EventHandler? StateChanged;

    public void Open(string path)
    {
        CloseCurrent();
        Book = BookFactory.Open(path);

        // Auto-detecção de mangá: ComicInfo.xml diz, ou usuário já salvou RTL antes.
        var saved = _stateService.Load(path);
        RightToLeft = saved?.RightToLeft ?? Book.Metadata.IsManga;

        DisplayIndex = saved is not null && saved.TotalPages == Book.PageCount
            ? Math.Clamp(saved.GetDisplayIndex(RightToLeft), 0, SpreadCount - 1)
            : 0;

        Raise();
        PrefetchAsync();
    }

    public void CloseCurrent()
    {
        _prefetchCts?.Cancel();
        Book?.Dispose();
        Book = null;
        _cache.Clear();
        DisplayIndex = 0;
    }

    // ---------- Navegação ----------

    /// <summary>Avança uma "folha". Em RTL, avançar = virar para a esquerda.</summary>
    public void Next() => SetIndex(DisplayIndex + 1);
    public void Previous() => SetIndex(DisplayIndex - 1);
    public void First() => SetIndex(0);
    public void Last() => SetIndex(SpreadCount - 1);

    private void SetIndex(int newIndex)
    {
        if (Book is null) return;
        var clamped = Math.Clamp(newIndex, 0, SpreadCount - 1);
        if (clamped == DisplayIndex) return;
        DisplayIndex = clamped;
        PersistState();
        Raise();
        PrefetchAsync();
    }

    public void ToggleRightToLeft()
    {
        // Mantém a mesma página física visível ao inverter o sentido.
        var physical = PhysicalIndexOfSpread(DisplayIndex).LastOrDefault(p => p >= 0);
        RightToLeft = !RightToLeft;
        if (Book is not null && physical >= 0)
            DisplayIndex = Math.Clamp(SpreadIndexForPhysical(physical), 0, Math.Max(0, SpreadCount - 1));
        PersistState();
        Raise();
    }

    public void SetLayout(PageLayout layout)
    {
        if (layout == Layout) return;
        // Preserva a página física central da folha atual
        var keep = PhysicalIndexOfSpread(DisplayIndex).FirstOrDefault(p => p >= 0);
        Layout = layout;
        if (keep >= 0) DisplayIndex = Math.Clamp(SpreadIndexForPhysical(keep), 0, Math.Max(0, SpreadCount - 1));
        Raise();
    }

    public void SetZoom(double factor)
    {
        Zoom = Math.Clamp(factor, 0.25, 8.0);
        Raise();
    }

    // ---------- Índices ----------

    /// <summary>Índices físicos (ordem do arquivo) das páginas da "folha" atual.</summary>
    public IReadOnlyList<int> CurrentSpread() => PhysicalIndexOfSpread(DisplayIndex);

    private List<int> PhysicalIndexOfSpread(int spread)
    {
        if (Book is null) return [];
        int total = Book.PageCount;
        var result = new List<int>(2);

        if (Layout == PageLayout.SinglePage)
        {
            int phys = RightToLeft ? total - 1 - spread : spread;
            if (phys >= 0 && phys < total) result.Add(phys);
        }
        else
        {
            // Em dupla página, cada "spread" contém duas páginas físicas.
            // Em RTL, a página da ESQUERDA da tela é a de MAIOR índice físico.
            int first = spread * 2;
            int left = RightToLeft ? total - 1 - first : first;
            int right = RightToLeft ? left - 1 : left + 1;
            if (left >= 0 && left < total) result.Add(left);
            if (right >= 0 && right < total) result.Add(right);
            if (result.Count == 0) result.Add(Math.Clamp(first, 0, total - 1));
        }
        return result;
    }

    private int SpreadIndexForPhysical(int physical)
    {
        if (Book is null) return 0;
        int displayPos = RightToLeft ? Book.PageCount - 1 - physical : physical;
        return Layout == PageLayout.DoublePage ? displayPos / 2 : displayPos;
    }

    // ---------- Entrega de imagens ----------

    public System.Drawing.Bitmap GetPageBitmap(int physicalIndex)
        => _cache.GetOrAdd(Book!.FilePath, physicalIndex, () => Book.ReadPageBytes(physicalIndex));

    private void PrefetchAsync()
    {
        if (Book is null) return;
        _prefetchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _prefetchCts = cts;
        var book = Book;
        var neighbors = new[] { DisplayIndex + 1, DisplayIndex + 2, DisplayIndex - 1 }
            .Where(i => i >= 0 && i < SpreadCount)
            .SelectMany(PhysicalIndexOfSpread)
            .Distinct()
            .ToList();

        _ = Task.Run(() =>
        {
            foreach (var p in neighbors)
            {
                if (cts.IsCancellationRequested) return;
                try { _cache.GetOrAdd(book.FilePath, p, () => book.ReadPageBytes(p)); }
                catch { /* prefetch é melhor esforço */ }
            }
        }, cts.Token);
    }

    private void PersistState()
    {
        if (Book is null) return;
        var lastPhys = PhysicalIndexOfSpread(DisplayIndex).LastOrDefault(p => p >= 0);
        _stateService.Save(new ReadingState
        {
            FilePath = Book.FilePath,
            LastPageIndex = Math.Max(0, lastPhys),
            TotalPages = Book.PageCount,
            RightToLeft = RightToLeft,
        });
    }

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _prefetchCts?.Cancel();
        CloseCurrent();
        _cache.Dispose();
    }
}
