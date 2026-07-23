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

//using Microsoft.Extensions.Logging;




namespace Piman.CrawlerAgents.MangaFire;





[DisplayName("KamiYomu Crawler Agent – mangafire.to")]
[CrawlerCheckBox("ContentRating", "The content rating for a series is based on the highest level of sexual content in the series", true, "safe", 2, ["safe", "suggestive", "erotica", "pornographic"])]
[CrawlerSelect("Language", "Chapter Translation language, translated fields such as Titles and Descriptions", true, 1, [
    "en",
    "es",
    "es-la",
    "fr",
    "jp",
    "pt",
    "pt-br"
])]


public class MangaFireCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private bool _disposed = false;
    private readonly Uri _baseUri;
    private readonly Lazy<HttpClient> _httpClient;
    private readonly string _language = CultureInfo.CurrentCulture.Name;

    public MangaFireCrawlerAgent(IDictionary<string, object> options) : base(options)
    {
        _baseUri = new Uri("https://mangafire.to");
        _httpClient = new Lazy<HttpClient>(CreateHttpClient);
        _language = Options.TryGetValue("Language", out object language) && language is string languageValue ? languageValue : "en";
    }

    private HttpClient CreateHttpClient()
    {
        HttpClient httpClient = new(new LoggingHandler(Logger, new HttpClientHandler()))
        {
            BaseAddress = new Uri(_baseUri.ToString()),
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

    ~MangaFireCrawlerAgent()
    {
        Dispose(false);
    }

    /// <inheritdoc/>
    public Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested ? null : Task.FromResult(new Uri("https://s.mfcdn.nl/assets/sites/mangafire/favicon.png"));
    }


    /// <inheritdoc/>
    public async Task<PagedResult<Manga>> SearchAsync(string titleName, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var pageNumber = string.IsNullOrWhiteSpace(paginationOptions?.ContinuationToken)
                ? 1
                : int.Parse(paginationOptions.ContinuationToken);


        List<Manga> mangaList = [];

        int total;

        StringBuilder queryBuilder = new StringBuilder()
          .Append("api/titles")
          .Append($"?keyword={titleName}");


        if (Options.TryGetValue("ContentRating.safe", out object safe) && safe is bool safeValue && safeValue)
        {
            _ = queryBuilder.Append($"&content_rating%5B%5D=safe");
        }
        if (Options.TryGetValue("ContentRating.suggestive", out object suggestive) && suggestive is bool suggestiveValue && suggestiveValue)
        {
            _ = queryBuilder.Append($"&content_rating%5B%5D=suggestive");
        }
        if (Options.TryGetValue("ContentRating.erotica", out object erotica) && erotica is bool eroticaValue && eroticaValue)
        {
            _ = queryBuilder.Append($"&content_rating%5B%5D=erotica");
        }
        if (Options.TryGetValue("ContentRating.pornographic", out object pornographic) && pornographic is bool pornographicValue && pornographicValue)
        {
            _ = queryBuilder.Append($"&content_rating%5B%5D=pornographic");
        }

        //maintain order like their js to hide better
        queryBuilder.Append($"&order%5Brelevance%5D=desc");
        queryBuilder.Append($"&page={pageNumber}");
        queryBuilder.Append($"&limit={paginationOptions.Limit}");

        //Logger.LogInformation("queryBuilder {queryBuilder}", queryBuilder.ToString());

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);

        total = ((rootNode["meta"])?["total"]).GetValue<int>();
        foreach (JsonNode item in rootNode["items"]?.AsArray() ?? [])
        {
            Manga manga = ConvertToManga(item);
            if (!string.IsNullOrEmpty(manga?.Title))
            {
                mangaList.Add(manga);
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

        //Logger.LogInformation("id: {var}", id);

        StringBuilder queryBuilder = new StringBuilder()
       .Append($"/api/titles/{id}");

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);

        return ConvertToManga(rootNode["data"]);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<Chapter>> GetChaptersAsync(Manga manga, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var pageNumber = string.IsNullOrWhiteSpace(paginationOptions?.ContinuationToken)
                ? 1
                : int.Parse(paginationOptions.ContinuationToken);


        StringBuilder queryBuilder = new StringBuilder()
                               .Append($"/api/titles/{manga.Id}/chapters")
                               .Append($"?language={_language}")
                               .Append($"&sort=number")
                               .Append($"&order=desc")
                               .Append($"&page={pageNumber}")
                               //.Append($"&limit={paginationOptions.Limit}");//overrided as 300 gets blocked
                               .Append($"&limit=20");

        //Logger.LogInformation("queryBuilder {queryBuilder}", queryBuilder.ToString());

        HttpRequestMessage request = new(HttpMethod.Get, queryBuilder.ToString());
        HttpResponseMessage response = await _httpClient.Value.SendAsync(request, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json);
        int total = ((rootNode["meta"])?["total"]).GetValue<int>();
        List<Chapter> chapters = [];

        //Logger.LogInformation("Json: {var}", rootNode);
        if (total != 0)
        {
            foreach (JsonNode item in rootNode["items"]?.AsArray() ?? [])
            {
                Chapter chapter = ConvertToChapter(manga, item); //learning lession if no pages remove this
                ///if (chapter.Pages > 0)
                //{
                chapters.Add(chapter);
                //}
            }
        }

        return PagedResultBuilder<Chapter>.Create()
                                          .WithData(chapters)
                                          .WithPaginationOptions(new PaginationOptions(chapters.Count(), chapters.Count(), total))
                                          .Build();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Page>> GetChapterPagesAsync(Chapter chapter, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var chapterApiUri = new Uri(_baseUri.ToString() + "api/chapters/" + chapter.Id);

        //Logger.LogInformation("chapterapiurl test: {url}", chapterApiUri.ToString());

        using HttpRequestMessage request = new(HttpMethod.Get, chapterApiUri);
        using HttpResponseMessage response = await _httpClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        _ = response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();
        JsonNode rootNode = JsonNode.Parse(json)["data"];//offset into data node

        JsonArray dataArray = rootNode?["pages"]?.AsArray();//prob could go direct but might need data in future


        if (dataArray is null)
        {
            throw new InvalidOperationException("Invalid chapter metadata or missing image data.");
        }

        //string parentId = chapter.ParentManga?.Id ?? "unknown";//not needed here
        List<Page> pages = new(dataArray.Count);

        int pageNum = 0;
        foreach (JsonNode item in dataArray)
        {
            Uri uri = new Uri(item?["url"]?.GetValue<string>());


            //Logger.LogInformation("imageuri: {url}", uri.ToString());
            //Logger.LogInformation("pagenum: {url}", pageNum);

            if (!string.IsNullOrEmpty(uri.ToString()))
            {
                //Uri uri = new($"{baseUrl}/data/{hash}/{fileName}");
                PageBuilder pageBuilder = PageBuilder.Create()
                    .WithChapterId(chapter.ParentManga.Id)
                    .WithId(DateTime.UtcNow.Ticks.ToString())
                    .WithImageUrl(uri)
                    .WithPageNumber(pageNum)
                    .WithParentChapter(chapter);
                pages.Add(pageBuilder.Build());
                pageNum++;
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

        //Logger.LogInformation("Json: {var}", item);

        ///////////////////////////////////////////////////TITLE///////////////////////////////////////////////////////////////////////////
        string title = item?["title"].ToString();
        //string fallbackTitle = !string.IsNullOrWhiteSpace(titleObj?.FirstOrDefault().Value?.ToString()) ? titleObj?.FirstOrDefault().Value?.ToString()
        //: "Untitled Manga";


        ///////////////////////////////////////////////////ALT TITLES//////////////////////////////////////////////////////////////////////
        JsonArray altTitlesList = item?["altTitles"]?.AsArray();
        var altTitleDict = new Dictionary<string, string>();
        if (altTitlesList != null)
        {
            int i = 1;
            foreach (var part in altTitlesList)
            {
                var alt = part.ToString();
                if (!string.IsNullOrEmpty(alt))
                    altTitleDict[$"Alt{i++}"] = alt;
                //Logger?.LogInformation("Alt_title: {alt}", alt);
            }
        }

        ///////////////////////////////////////////////////DESCRIPTION/////////////////////////////////////////////////////////////////////
        // localized description arn't available atm
        string description = item?["synopsisHtml"]?.ToString();
        //string localizedDescription = descriptionObj?[_language]?.ToString();
        //string localizedDescription = descriptionObj?.FirstOrDefault(kvp => !string.IsNullOrEmpty(kvp.Value?.ToString())).Value?.ToString();
        //string localizedDescription


        ///////////////////////////////////////////////////TAGS/////////////////////////////////////////////////////////////////////////////

        //themes and genres are gonna be merged here as they are in the ui and backend of KamiYomu
        //JsonNode attributes = item;
        JsonArray genres = item?["genres"]?.AsArray();
        JsonArray themes = item?["themes"]?.AsArray();

        List<string> tagList = [];
        if (genres != null)
        {
            foreach (JsonNode tag in genres)
            {
                tagList.Add(tag?["title"]?.GetValue<string>());
            }
        }

        if (themes != null)
        {
            foreach (JsonNode tag in themes)
            {
                tagList.Add(tag?["title"]?.GetValue<string>());
            }
        }

        ///////////////////////////////////////////////////AUTHORS AND ARTIST//////////////////////////////////////////////////////////////
        ///authors
        JsonArray jsonAuthors = item?["authors"]?.AsArray();

        List<string> authorList = [];
        if (jsonAuthors != null)
        {
            foreach (JsonNode author in jsonAuthors)
            {
                authorList.Add(author?["title"]?.GetValue<string>());
            }
        }

        ///artists
        JsonArray jsonArtist = item?["artists"]?.AsArray();

        List<string> artistList = [];
        if (jsonArtist != null)
        {
            foreach (JsonNode artist in jsonArtist)
            {
                artistList.Add(artist?["title"]?.GetValue<string>());
            }
        }


        ///////////////////////////////////////////////////POSTER//////////////////////////////////////////////////////////////////////////

        var coverUrlStr = (item?["poster"])?["large"]?.ToString();
        Uri coverUrl = string.IsNullOrEmpty(coverUrlStr) ? null : new Uri(coverUrlStr);
        var coverFileName = coverUrl != null ? System.IO.Path.GetFileName(coverUrl.LocalPath) : null;

        ///////////////////////////////////////////////////CHAPTER/////////////////////////////////////////////////////////////////////////

        _ = (decimal.TryParse(item?["latestChapter"]?.ToString(), out decimal lastChapter) ? lastChapter : 0m);//pulled from inline for testing

        ///////////////////////////////////////////////////BUILDER/////////////////////////////////////////////////////////////////////////


        MangaBuilder builder = MangaBuilder.Create();
        string mangaId = item?["hid"]?.GetValue<string>();
        string mangaUrl = item?["url"]?.GetValue<string>();
        //string coverFileName = item?["poster"](r => r?["type"]?.ToString() == "cover_art")?["attributes"]?["fileName"]?.ToString();
        string[] unsafeRatings = ["erotica", "pornographic"];
        string contentRating = item?["contentRating"]?.ToString();
        bool isFamilySafe = string.IsNullOrWhiteSpace(contentRating) || !unsafeRatings.Contains(contentRating, StringComparer.OrdinalIgnoreCase);

        /* debug
        Logger.LogInformation("title: {var}", title);
        Logger.LogInformation("authls: {var}", authorList);
        Logger.LogInformation("artls: {var}", artistList);
        Logger.LogInformation("description: {var}", description);
        Logger.LogInformation("tagls: {var}", tagList);
        Logger.LogInformation("year: {var}", item?["year"]?.GetValue<int?>() ?? 0);
        Logger.LogInformation("coverurl: {var}", coverUrl);
        Logger.LogInformation("coverfile: {var}", coverFileName);
        Logger.LogInformation("lastchap: {var}", lastChapter);*/



        _ = builder.WithId(mangaId)
               .WithTitle(title)
               .WithAlternativeTitles(altTitleDict)
               .WithAuthors([.. authorList])
               .WithArtists([.. artistList])
               .WithDescription(description ?? string.Empty)
               .WithTags([.. tagList])
               .WithLatestChapterAvailable(lastChapter)
               .WithReleaseStatus(item?["status"]?.ToString().ToLower() switch
               {
                   "releasing" => ReleaseStatus.Continuing,
                   "finished" => ReleaseStatus.Completed,
                   "on_hiatus" => ReleaseStatus.OnHiatus,
                   "discontinued" => ReleaseStatus.Cancelled,
                   _ => ReleaseStatus.Unreleased
               })
               .WithYear(item?["year"]?.GetValue<int?>() ?? 0)
               .WithIsFamilySafe(isFamilySafe)
               .WithWebsiteUrl(_baseUri.ToString() + mangaUrl.Remove(0,1))//trim / off url
               .WithCoverFileName(coverFileName)
               .WithCoverUrl(coverUrl);
        return builder.Build();
    }

    private Chapter ConvertToChapter(Manga manga, JsonNode item)
    {
        JsonNode attributes = item?["attributes"];

        string id = (item?["id"]?.GetValue<int>().ToString());

        string titleStr = item?["name"]?.GetValue<string>();
        string title = !string.IsNullOrWhiteSpace(titleStr) ? titleStr : "Untitled Chapter";

        var releaseDate = DateTimeOffset.FromUnixTimeSeconds(item?["createdAt"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        decimal chapterNum = item?["number"]?.GetValue<int>() ?? 0m;

        /* debug
        Logger.LogInformation("id: {var}", id);
        Logger.LogInformation("id type: {var}", id.GetType());
        Logger.LogInformation("title: {var}", title);
        Logger.LogInformation("ChNumber: {var}", chapterNum);
        Logger.LogInformation("time: {var}", releaseDate);
        Logger.LogInformation("lang: {var}", item?["language"]?.ToString());*/


        ChapterBuilder builder = ChapterBuilder.Create()
                                    .WithParentManga(manga)
                                    .WithId(id)
                                    .WithTitle(title)
                                    //.WithVolume(volume)//missing but they have volume data they use for some other crap check in future if added
                                    .WithNumber(chapterNum)
                                    .WithReleaseDate(releaseDate.DateTime)
                                    .WithUri(new Uri(manga.WebSiteUrl + "/chapter/" + id));


        return builder.Build();
    }
}
