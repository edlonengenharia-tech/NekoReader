using Newtonsoft.Json;

namespace MangaReader.Models;

/// <summary>Metadados de um quadrinho/mangá. Prioridade: ComicInfo.xml > tags do arquivo > nome do arquivo.</summary>
public sealed class BookMetadata
{
    [JsonProperty("title")]          public string Title { get; set; } = "";
    [JsonProperty("series")]         public string? Series { get; set; }
    [JsonProperty("number")]         public string? Number { get; set; }
    [JsonProperty("volume")]         public string? Volume { get; set; }
    [JsonProperty("year")]           public int? Year { get; set; }
    [JsonProperty("month")]          public int? Month { get; set; }
    [JsonProperty("writer")]         public string? Writer { get; set; }
    [JsonProperty("author")]         public string? Author { get; set; }
    [JsonProperty("publisher")]      public string? Publisher { get; set; }
    [JsonProperty("manga")]          public bool IsManga { get; set; }          // ComicInfo.xml: <Manga>Yes</Manga>
    [JsonProperty("pageCount")]      public int PageCount { get; set; }
    [JsonProperty("coverPath")]      public string? CoverImagePath { get; set; } // cache em disco da capa

    /// <summary>Nome amigável para exibição na biblioteca.</summary>
    [JsonIgnore]
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title) ? Title
        : !string.IsNullOrWhiteSpace(Series) && !string.IsNullOrWhiteSpace(Number)
            ? $"{Series} #{Number}"
            : Path.GetFileNameWithoutExtension(EbookPath ?? "");

    [JsonIgnore] public string? EbookPath { get; set; }
}
