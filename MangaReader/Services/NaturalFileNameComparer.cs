namespace MangaReader.Services;

/// <summary>
/// Comparação "natural": ordena 001, 002 ... 010, 100 corretamente e trata nomes
/// como "page_9.jpg" vs "page_10.jpg" (o nº 10 vem depois do 9, não antes).
/// </summary>
public static class NaturalFileNameComparer
{
    public static int NaturalCompare(string? a, string? b, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        if (string.Equals(a, b, comparison)) return 0;
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            char ca = a[i], cb = b[j];
            if (char.IsDigit(ca) && char.IsDigit(cb))
            {
                // Extrai os blocos numéricos completos de cada lado
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                int byLen = na.Length.CompareTo(nb.Length);
                if (byLen != 0) return byLen;
                int cmp = string.CompareOrdinal(na, nb);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
                if (cmp != 0) return cmp;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }
}
