namespace MangaReader.Services;

/// <summary>
/// Cache LRU em memória de páginas decodificadas (Bitmap). Evita descomprimir a
/// mesma página repetidamente durante navegação; as vizinhas são pré-carregadas
/// pelo ReaderEngine.
/// </summary>
public sealed class PageCache : IDisposable
{
    private sealed class Entry(string key, System.Drawing.Bitmap bitmap) : IDisposable
    {
        public string Key { get; } = key;
        public System.Drawing.Bitmap Bitmap { get; } = bitmap;
        public void Dispose() => Bitmap.Dispose();
    }

    private readonly int _maxEntries;
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = [];
    private readonly object _gate = new();

    public PageCache(int maxEntries = 12) => _maxEntries = Math.Max(4, maxEntries);

    /// <summary>Retorna o bitmap da página, carregando via <paramref name="loader"/> se necessário.</summary>
    public System.Drawing.Bitmap GetOrAdd(string filePath, int physicalIndex, Func<byte[]> loader)
    {
        var key = $"{filePath}|{physicalIndex}";
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);          // hit: move para o topo (MRU)
                _lru.AddFirst(node);
                return node.Value.Bitmap;
            }
        }

        // Descomprime fora do lock (pode ser lento em RAR/7z grandes)
        var bmp = Load(loader);

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                bmp.Dispose();              // outra thread já carregou esta página
                return existing.Value.Bitmap;
            }

            var n = _lru.AddFirst(new Entry(key, bmp));
            _map[key] = n;

            while (_map.Count > _maxEntries && _lru.Last is { } tail)
            {
                _lru.RemoveLast();
                _map.Remove(tail.Value.Key);
                tail.Value.Dispose();
            }
            return bmp;
        }
    }

    private static System.Drawing.Bitmap Load(Func<byte[]> loader)
    {
        var bytes = loader();
        using var ms = new MemoryStream(bytes);
        using var img = System.Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
        return new System.Drawing.Bitmap(img); // cópia própria — não depende do stream
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var e in _lru) e.Dispose();
            _lru.Clear();
            _map.Clear();
        }
    }

    public void Dispose() => Clear();
}
