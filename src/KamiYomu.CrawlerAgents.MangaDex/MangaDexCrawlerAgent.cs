using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;
using KamiYomu.CrawlerAgents.Core.Inputs;

namespace KamiYomu.CrawlerAgents.MangaDex;

[DisplayName("KamiYomu Crawler Agent – mangadex.org")]
[CrawlerCheckBox("ContentRating", "The content rating for a series is based on the highest level of sexual content in the series", true, "safe", 2, ["safe", "suggestive", "erotica", "pornographic"])]
[CrawlerSelect("Language", "Chapter Translation language, translated fields such as Titles and Descriptions", true, 1, [
    "en", "pt", "pt-br", "it", "de", "ru", "aa", "ab", "ae", "af", "ak", "am", "an", "ar-ae", "ar-bh", "ar-dz", "ar-eg", "ar-iq", "ar-jo", "ar-kw", "ar-lb", "ar-ly", "ar-ma", "ar-om",
    "ar-qa", "ar-sa", "ar-sy", "ar-tn", "ar-ye", "ar", "as", "av", "ay", "az", "ba", "be", "bg", "bh", "bi", "bm", "bn", "bo", "br", "bs", "ca", "ce", "ch", "co",
    "cr", "cs", "cu", "cv", "cy", "da", "de-at", "de-ch", "de-de", "de-li", "de-lu", "dv", "dz", "ee", "el", "en-au", "en-bz", "en-ca", "en-cb", "en-gb", "en-ie", "en-jm", "en-nz",
    "en-ph", "en-tt", "en-us", "en-za", "en-zw", "eo", "es-ar", "es-bo", "es-cl", "es-co", "es-cr", "es-do", "es-ec", "es-es", "es-gt", "es-hn", "es-la", "es-mx", "es-ni", "es-pa", "es-pe", "es-pr", "es-py", "es-sv",
    "es-us", "es-uy", "es-ve", "es", "et", "eu", "fa", "ff", "fi", "fj", "fo", "fr-be", "fr-ca", "fr-ch", "fr-fr", "fr-lu", "fr-mc", "fr", "fy", "ga", "gd", "gl", "gn", "gu",
    "gv", "ha", "he", "hi", "ho", "hr-ba", "hr-hr", "hr", "ht", "hu", "hy", "hz", "ia", "id", "ie", "ig", "ii", "ik", "in", "io", "is", "it-ch", "it-it", "iu",
    "iw", "ja", "ja-ro", "ji", "jv", "jw", "ka", "kg", "ki", "kj", "kk", "kl", "km", "kn", "ko", "ko-ro", "kr", "ks", "ku", "kv", "kw", "ky", "kz", "la",
    "lb", "lg", "li", "ln", "lo", "ls", "lt", "lu", "lv", "mg", "mh", "mi", "mk", "ml", "mn", "mo", "mr", "ms-bn", "ms-my", "ms", "mt", "my", "na", "nb",
    "nd", "ne", "ng", "nl-be", "nl-nl", "nl", "nn", "no", "nr", "ns", "nv", "ny", "oc", "oj", "om", "or", "os", "pa", "pi", "pl", "ps", "pt-pt", "qu-bo", "qu-ec",
    "qu-pe", "qu", "rm", "rn", "ro", "rw", "sa", "sb", "sc", "sd", "se-fi", "se-no", "se-se", "se", "sg", "sh", "si", "sk", "sl", "sm", "sn", "so", "sq", "sr-ba",
    "sr-sp", "sr", "ss", "st", "su", "sv-fi", "sv-se", "sv", "sw", "sx", "syr", "ta", "te", "tg", "th", "ti", "tk", "tl", "tn", "to", "tr", "ts", "tt", "tw",
    "ty", "ug", "uk", "ur", "us", "uz", "ve", "vi", "vo", "wa", "wo", "xh", "yi", "yo", "za", "zh-cn", "zh-hk", "zh-mo", "zh-ro", "zh-sg", "zh-tw", "zh", "zu"
])]
public class MangaDexCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private bool _disposed = false;
    private readonly Lazy<HttpClient> _httpClient;
    private readonly string _language = CultureInfo.CurrentCulture.Name;

    public MangaDexCrawlerAgent(IDictionary<string, object> options) : base(options)
    {
        _httpClient = new Lazy<HttpClient>(CreateHttpClient);
        _language = Options.TryGetValue("Language", out object language) && language is string languageValue ? languageValue : "en";
    }

    private HttpClient CreateHttpClient()
    {
        HttpClient httpClient = new(new LoggingHandler(Logger, new HttpClientHandler()))
        {
            BaseAddress = new Uri("https://api.mangadex.org"),
            Timeout = TimeSpan.FromMilliseconds(TimeoutMilliseconds),
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpClientDefaultUserAgent);
        return httpClient;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            if (_httpClient.IsValueCreated)
            {
                _httpClient.Value.Dispose();
            }
        }
        _disposed = true;
    }

    ~MangaDexCrawlerAgent()
    {
        Dispose(false);
    }

    /// <inheritdoc/>
    public Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested ? null : Task.FromResult(new Uri("https://mangadex.org/favicon.svg"));
    }


    /// <inheritdoc/>
    public async Task<PagedResult<Manga>> SearchAsync(string titleName, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        List<Manga> mangaList = [];

        int total;

        StringBuilder queryBuilder = new StringBuilder()
          .Append("manga")
          .Append($"?limit={paginationOptions.Limit}")
          .Append($"&offset={paginationOptions.OffSet}")
          .Append($"&title={titleName}")
          .Append($"&includes%5B%5D=manga")
          .Append($"&includes%5B%5D=cover_art")
          .Append($"&includes%5B%5D=author")
          .Append($"&includes%5B%5D=artist")
          .Append($"&includes%5B%5D=tag")
          .Append($"&availableTranslatedLanguage%5B%5D={_language}");

        if (Options.TryGetValue("ContentRating.safe", out object safe) && safe is bool safeValue && safeValue)
        {
            _ = queryBuilder.Append($"&contentRating%5B%5D=safe");
        }
        if (Options.TryGetValue("ContentRating.suggestive", out object suggestive) && suggestive is bool suggestiveValue && suggestiveValue)
        {
            _ = queryBuilder.Append($"&contentRating%5B%5D=suggestive");
        }
        if (Options.TryGetValue("ContentRating.erotica", out object erotica) && erotica is bool eroticaValue && eroticaValue)
        {
            _ = queryBuilder.Append($"&contentRating%5B%5D=erotica");
        }
        if (Options.TryGetValue("ContentRating.pornographic", out object pornographic) && pornographic is bool pornographicValue && pornographicValue)
        {
            _ = queryBuilder.Append($"&contentRating%5B%5D=pornographic");
        }

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);

        total = rootNode["total"].GetValue<int>();
        foreach (JsonNode item in rootNode["data"]?.AsArray() ?? [])
        {
            Manga manga = ConvertToManga(item);
            if (!string.IsNullOrEmpty(manga?.Title))
            {
                mangaList.Add(ConvertToManga(item));
            }

        }
        return PagedResultBuilder<Manga>.Create()
                                        .WithData(mangaList)
                                        .WithPaginationOptions(new PaginationOptions(paginationOptions.OffSet, paginationOptions.Limit, total))
                                        .Build();
    }

    /// <inheritdoc/>
    public async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        StringBuilder queryBuilder = new StringBuilder()
       .Append($"manga/{id}")
       .Append($"?includes%5B%5D=manga")
       .Append($"&includes%5B%5D=cover_art")
       .Append($"&includes%5B%5D=author")
       .Append($"&includes%5B%5D=artist")
       .Append($"&includes%5B%5D=tag");

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);

        return ConvertToManga(rootNode["data"].AsObject());
    }

    /// <inheritdoc/>
    public async Task<PagedResult<Chapter>> GetChaptersAsync(Manga manga, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        StringBuilder queryBuilder = new StringBuilder()
                               .Append($"manga/{manga.Id}/feed")
                               .Append($"?limit={paginationOptions.Limit}")
                               .Append($"&offset={paginationOptions.OffSet}")
                               .Append($"&translatedLanguage%5B%5D={_language}")
                               .Append($"&contentRating%5B%5D=safe")
                               .Append($"&contentRating%5B%5D=suggestive")
                               .Append($"&contentRating%5B%5D=erotica")
                               .Append($"&contentRating%5B%5D=pornographic");

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);
        int total = rootNode["total"].GetValue<int>();
        List<Chapter> chapters = [];

        foreach (JsonNode item in rootNode["data"]?.AsArray() ?? [])
        {
            Chapter chapter = ConvertToChapter(manga, item);
            if (chapter.Pages > 0)
            {
                chapters.Add(chapter);
            }
        }

        return PagedResultBuilder<Chapter>.Create()
                                          .WithData(chapters)
                                          .WithPaginationOptions(new PaginationOptions(paginationOptions.OffSet, paginationOptions.Limit, total))
                                          .Build();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Page>> GetChapterPagesAsync(Chapter chapter, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, chapter.Uri);
        using HttpResponseMessage response = await _httpClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);

        string baseUrl = rootNode?["baseUrl"]?.GetValue<string>();
        JsonNode chapterNode = rootNode?["chapter"];
        string hash = chapterNode?["hash"]?.GetValue<string>();
        JsonArray dataArray = chapterNode?["data"]?.AsArray();

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(hash) || dataArray is null)
        {
            throw new InvalidOperationException("Invalid chapter metadata or missing image data.");
        }

        string parentId = chapter.ParentManga?.Id ?? "unknown";
        List<Page> pages = new(dataArray.Count);

        foreach (JsonNode item in dataArray)
        {
            string fileName = item?.ToString();
            if (!string.IsNullOrEmpty(fileName))
            {
                Uri uri = new($"{baseUrl}/data/{hash}/{fileName}");
                PageBuilder pageBuilder = PageBuilder.Create()
                    .WithChapterId(chapter.ParentManga.Id)
                    .WithId(DateTime.UtcNow.Ticks.ToString())
                    .WithImageUrl(uri)
                    .WithPageNumber(ExtractPageNumber(fileName))
                    .WithParentChapter(chapter);
                pages.Add(pageBuilder.Build());
            }
        }

        return pages;
    }

    public static decimal ExtractPageNumber(string fileName)
    {
        int dashIndex = fileName.IndexOf('-');
        return dashIndex > 0 && decimal.TryParse(fileName[..dashIndex], out decimal pageNumber) ? pageNumber : 0m;
    }

    private Manga ConvertToManga(JsonNode item, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        JsonNode attributes = item?["attributes"];
        JsonArray tags = attributes?["tags"].AsArray();
        // ALT TITLES
        JsonArray altTitles = attributes?["altTitles"]?.AsArray();
        string localizedAltTitle = altTitles?
            .Select(t => t?[_language]?.ToString())
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));

        Dictionary<string, string> altTitleDict = altTitles?
            .Select(innerArray => innerArray?.AsObject())
            .Where(obj => obj != null && obj.Count > 0)
            .OfType<JsonObject>()
            .Select<JsonObject, KeyValuePair<string, string>?>(obj =>
            {
                KeyValuePair<string, JsonNode> firstProp = obj.FirstOrDefault();
                string propertyName = firstProp.Key;
                string value = obj.TryGetPropertyValue(propertyName, out JsonNode titleNode) && titleNode != null
                    ? titleNode.ToString()
                    : string.Empty;

                return string.IsNullOrEmpty(propertyName)
                    ? null
                    : new KeyValuePair<string, string>(propertyName, value);
            })
            .Where(p => p != null)
            .GroupBy(p => p!.Value.Key)
            .ToDictionary(g => g.Key, g => g.First().Value.Value);

        // TITLE
        JsonObject titleObj = attributes?["title"]?.AsObject();
        string fallbackTitle = !string.IsNullOrWhiteSpace(titleObj?.FirstOrDefault().Value?.ToString()) ? titleObj?.FirstOrDefault().Value?.ToString()
                                                                                                     : "Untitled Manga";

        // DESCRIPTION
        JsonObject descriptionObj = attributes?["description"]?.AsObject();
        string localizedDescription = descriptionObj?[_language]?.ToString();
        localizedDescription ??= descriptionObj?.FirstOrDefault(kvp => !string.IsNullOrEmpty(kvp.Value?.ToString())).Value?.ToString();
        JsonArray relationships = item?["relationships"]?.AsArray();

        Dictionary<string, string> altDescriptionDict = descriptionObj?
            .Where(kvp => kvp.Key != null && kvp.Value != null)
            .Select(kvp =>
            {
                string propertyName = kvp.Key;
                string value = kvp.Value?.ToString() ?? string.Empty;
                return new KeyValuePair<string, string>(propertyName, value);
            })
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        List<string> tagList = [];
        foreach (JsonNode tag in tags)
        {
            JsonObject tagAttr = tag?["attributes"]?.AsObject();
            JsonObject tagAttrName = tagAttr?["name"]?.AsObject();
            string tagAttrNameLocalized = tagAttrName?[_language]?.ToString();
            tagAttrNameLocalized ??= tagAttrName?.FirstOrDefault(kvp => !string.IsNullOrEmpty(kvp.Value.ToString())).Value?.ToString();
            tagList.Add(tagAttrNameLocalized);
        }

        IEnumerable<string> authorList = relationships?.Where(r => r?["type"]?.ToString() == "author").Select(p => p?["attributes"]?["name"]?.ToString());
        IEnumerable<string> artistList = relationships?.Where(r => r?["type"]?.ToString() == "artist").Select(p => p?["attributes"]?["name"]?.ToString());
        MangaBuilder builder = MangaBuilder.Create();
        string mangaId = item?["id"]?.GetValue<string>();
        string coverFileName = relationships?.FirstOrDefault(r => r?["type"]?.ToString() == "cover_art")?["attributes"]?["fileName"]?.ToString();
        string[] unsafeRatings = ["erotica", "pornographic"];
        string contentRating = attributes?["contentRating"]?.ToString();
        bool isFamilySafe = string.IsNullOrWhiteSpace(contentRating) || !unsafeRatings.Contains(contentRating, StringComparer.OrdinalIgnoreCase);
        _ = builder.WithId(mangaId)
               .WithTitle(!string.IsNullOrEmpty(localizedAltTitle) ? localizedAltTitle : fallbackTitle)
               .WithAlternativeTitles(altTitleDict)
               .WithAlternativeDescriptions(altDescriptionDict)
               .WithAuthors([.. authorList])
               .WithArtists([.. artistList])
               .WithDescription(localizedDescription ?? string.Empty)
               .WithOriginalLanguage(attributes?["originalLanguage"]?.ToString())
               .WithTags([.. tagList])
               .WithLastVolumeAvailable(decimal.TryParse(attributes?["lastVolume"]?.ToString(), out decimal lastVolume) ? lastVolume : 0m)
               .WithLatestChapterAvailable(decimal.TryParse(attributes?["lastChapter"]?.ToString(), out decimal lastChapter) ? lastChapter : 0m)
               .WithReleaseStatus(attributes?["status"]?.ToString().ToLower() switch
               {
                   "ongoing" => ReleaseStatus.Continuing,
                   "completed" => ReleaseStatus.Completed,
                   "hiatus" => ReleaseStatus.OnHiatus,
                   "cancelled" => ReleaseStatus.Cancelled,
                   _ => ReleaseStatus.Unreleased
               })
               .WithYear(attributes?["year"]?.GetValue<int?>() ?? 0)
               .WithIsFamilySafe(isFamilySafe)
               .WithWebsiteUrl($"https://mangadex.org/title/{mangaId}")
               .WithCoverFileName(coverFileName)
               .WithCoverUrl(!string.IsNullOrEmpty(coverFileName) ? new Uri($"https://uploads.mangadex.org/covers/{mangaId}/{coverFileName}") : null);
        return builder.Build();
    }

    private Chapter ConvertToChapter(Manga manga, JsonNode item)
    {
        JsonNode attributes = item?["attributes"];
        string titleStr = attributes?["title"]?.GetValue<string>();
        string title = !string.IsNullOrWhiteSpace(titleStr) ? titleStr : "Untitled Chapter";
        ChapterBuilder builder = ChapterBuilder.Create()
                                    .WithParentManga(manga)
                                    .WithId(item?["id"]?.GetValue<string>() ?? "")
                                    .WithTitle(title)
                                    .WithVolume(decimal.TryParse(attributes?["volume"]?.GetValue<string>(), out decimal volume) ? volume : 0m)
                                    .WithNumber(decimal.TryParse(attributes?["chapter"]?.GetValue<string>(), out decimal number) ? number : 0m)
                                    .WithReleaseDate(attributes?["publishAt"]?.GetValue<DateTime>() ?? DateTime.MinValue)
                                    .WithTranslatedLanguage(attributes?["translatedLanguage"]?.ToString() ?? "")
                                    .WithPages(attributes?["pages"]?.GetValue<int?>().GetValueOrDefault(0) ?? 0)
                                    .WithUri(new Uri($"at-home/server/{item?["id"]?.GetValue<string>()}?forcePort443=false", UriKind.Relative));

        return builder.Build();
    }
}
