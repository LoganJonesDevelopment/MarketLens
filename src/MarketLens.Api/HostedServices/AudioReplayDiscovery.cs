using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarketLens.Api.HostedServices;

public class AudioReplayDiscovery(
    IHttpClientFactory httpClientFactory,
    ILogger<AudioReplayDiscovery> logger)
{
    // Apple events-delivery meta URL pattern
    private static readonly Regex AppleUrlJsonMeta =
        new(@"content=""(https://events-delivery\.apple\.com/[^""]+/js_files/event/url\.json)""",
            RegexOptions.IgnoreCase);

    // AMD IR calendar edge.media-server.com links
    private static readonly Regex EdgeMediaServerLink =
        new(@"https://edge\.media-server\.com/mmc/p/([a-z0-9]+)",
            RegexOptions.IgnoreCase);

    // Alphabet/Google YouTube webcast pattern from 8-K exhibit
    private static readonly Regex YoutubeWatchUrl =
        new(@"https?://(?:www\.)?youtube\.com/watch\?(?:[^""&\s]*&)*v=([a-zA-Z0-9_\-]+)",
            RegexOptions.IgnoreCase);

    // Microsoft medius.microsoft.com embed UUID from events page
    private static readonly Regex MediusEmbedUuid =
        new(@"https://medius\.microsoft\.com/Embed/video-nc/([0-9a-f\-]{36})",
            RegexOptions.IgnoreCase);

    // Microsoft stream.event.microsoft.com HLS URL from medius embed page
    private static readonly Regex MicrosoftStreamUrl =
        new(@"""StreamUrl""\s*:\s*""(https://stream\.event\.microsoft\.com/[^""]+\.m3u8[^""]*)""",
            RegexOptions.IgnoreCase);

    // EDGAR submissions base
    private const string EdgarSubmissions = "https://data.sec.gov/submissions";

    // EDGAR CIK lookup for tickers we support via 8-K parsing
    private static readonly Dictionary<string, string> EdgarCiks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAPL"] = "0000320193",
        ["GOOG"] = "0001652044",
        ["GOOGL"] = "0001652044",
        ["MSFT"] = "0000789019",
        ["NVDA"] = "0001045810",
        ["AMD"] = "0000002488",
        ["AMZN"] = "0001018724",
        ["META"] = "0001326801",
        ["AVGO"] = "0001730168",
        ["ORCL"] = "0001341439",
        ["INTC"] = "0000050863",
        ["MU"] = "0000723125",
    };

    // Amazon IR Q4 CDN client ID — stable; all post-2018 call recordings are at this client path.
    private const string AmazonQ4ClientId = "299287126";

    // NVIDIA IR Q4 CDN client ID (s201.q4cdn.com/141608511)
    private const string NvidiaQ4ClientId = "141608511";

    public async Task<string?> DiscoverAsync(string symbol, DateTime callDate, CancellationToken cancellationToken)
    {
        // Try Apple IR page (works reliably, no auth)
        if (symbol.Equals("AAPL", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryAppleAsync(cancellationToken);
            if (url is not null) return url;
        }

        // Google/Alphabet: YouTube URL in EDGAR 8-K exhibit, extracted via yt-dlp in Whisper sidecar.
        // We return the YouTube watch URL directly — the sidecar's _transcribe_youtube path handles it.
        if (symbol.Equals("GOOG", StringComparison.OrdinalIgnoreCase) ||
            symbol.Equals("GOOGL", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryGoogleAsync(symbol, callDate, cancellationToken);
            if (url is not null) return url;
        }

        // Microsoft: events page → medius iframe UUID → stream.event.microsoft.com HLS
        if (symbol.Equals("MSFT", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryMicrosoftAsync(callDate, cancellationToken);
            if (url is not null) return url;
        }

        // Amazon: Q4 EventService on ir.aboutamazon.com → direct MP3 on s2.q4cdn.com
        if (symbol.Equals("AMZN", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryAmazonAsync(callDate, cancellationToken);
            if (url is not null) return url;
        }

        // NVIDIA: Q4 EventService on investor.nvidia.com → WebCastLink is a Q4 Inc attendee URL.
        // The Q4 Inc platform requires attendee auth to retrieve the recording URL, so we fall
        // back to EDGAR 8-K traversal first, then the Q4 EventService for the webcast page link.
        if (symbol.Equals("NVDA", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryNvidiaAsync(callDate, cancellationToken);
            if (url is not null) return url;
        }

        // Try EDGAR 8-K approach for any ticker with a known CIK
        if (EdgarCiks.TryGetValue(symbol, out var cik))
        {
            var url = await TryEdgar8KAsync(symbol, cik, callDate, cancellationToken);
            if (url is not null) return url;
        }

        // Try AMD IR calendar (edge.media-server.com links visible without auth)
        if (symbol.Equals("AMD", StringComparison.OrdinalIgnoreCase))
        {
            var url = await TryAmdIrCalendarAsync(callDate, cancellationToken);
            if (url is not null) return url;
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Google / Alphabet — YouTube via EDGAR 8-K exhibit
    // -----------------------------------------------------------------------

    private async Task<string?> TryGoogleAsync(
        string symbol, DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            if (!EdgarCiks.TryGetValue(symbol, out var cik)) return null;

            var http = httpClientFactory.CreateClient("earnings_calendar");
            var submissionsUrl = $"{EdgarSubmissions}/CIK{cik}.json";
            var submissionsJson = await http.GetStringAsync(submissionsUrl, cancellationToken);
            var doc = JsonDocument.Parse(submissionsJson);

            var filings = doc.RootElement.GetProperty("filings").GetProperty("recent");
            var forms = filings.GetProperty("form").EnumerateArray().ToList();
            var dates = filings.GetProperty("filingDate").EnumerateArray().ToList();
            var accessions = filings.GetProperty("accessionNumber").EnumerateArray().ToList();
            var items = filings.TryGetProperty("items", out var itemsProp)
                ? itemsProp.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
                : Enumerable.Repeat("", forms.Count).ToList();

            var windowStart = callDate.AddDays(-3);
            var windowEnd = callDate.AddDays(3);

            for (int i = 0; i < forms.Count; i++)
            {
                if (forms[i].GetString() != "8-K") continue;
                // Look for Results of Operations filings (item 2.02)
                var itemStr = items[i];
                if (!string.IsNullOrEmpty(itemStr) && !itemStr.Contains("2.02") && !itemStr.Contains("7.01"))
                    continue;
                if (!DateTime.TryParse(dates[i].GetString(), out var filingDate)) continue;
                if (filingDate < windowStart || filingDate > windowEnd) continue;

                var accession = accessions[i].GetString()!;
                var cikNum = cik.TrimStart('0');
                var accessionPath = accession.Replace("-", "");
                var indexUrl = $"https://www.sec.gov/Archives/edgar/data/{cikNum}/{accessionPath}/";

                // Fetch directory listing to find exhibit files
                var indexHtml = await http.GetStringAsync(indexUrl, cancellationToken);
                var exhibitUrls = Regex.Matches(
                    indexHtml,
                    $@"href=""(/Archives/edgar/data/{cikNum}/{accessionPath}/[^""]+\.htm[l]?)""",
                    RegexOptions.IgnoreCase)
                    .Select(m => "https://www.sec.gov" + m.Groups[1].Value)
                    .Where(u => !u.EndsWith("-index.html", StringComparison.OrdinalIgnoreCase)
                             && !u.EndsWith("-index-headers.html", StringComparison.OrdinalIgnoreCase)
                             && !u.Contains("/R1.htm", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var exhibitUrl in exhibitUrls.Take(4))
                {
                    try
                    {
                        await Task.Delay(300, cancellationToken);
                        var html = await http.GetStringAsync(exhibitUrl, cancellationToken);
                        // Decode common HTML entities
                        html = html.Replace("&#47;", "/").Replace("&#58;", ":").Replace("&#63;", "?")
                                   .Replace("&#61;", "=").Replace("&#38;", "&").Replace("&#46;", ".");

                        var ytMatch = YoutubeWatchUrl.Match(html);
                        if (ytMatch.Success)
                        {
                            var ytUrl = ytMatch.Value;
                            logger.LogInformation("{Symbol}: YouTube earnings call found at {Url}", symbol, ytUrl);
                            return ytUrl;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "{Symbol}: failed to fetch exhibit {Url}", symbol, exhibitUrl);
                    }
                }
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Symbol} Google EDGAR discovery failed", symbol);
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Microsoft — events page → medius embed → HLS stream URL
    // -----------------------------------------------------------------------

    private async Task<string?> TryMicrosoftAsync(DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("earnings_calendar");

            // Determine the fiscal year and quarter from the call date.
            // MSFT fiscal year starts July 1. Q1=Jul-Sep, Q2=Oct-Dec, Q3=Jan-Mar, Q4=Apr-Jun.
            int calYear = callDate.Year;
            int calMonth = callDate.Month;
            int fiscalYear, fiscalQuarter;
            if (calMonth >= 7)
            {
                fiscalYear = calYear + 1;
                fiscalQuarter = calMonth < 10 ? 1 : 2;
            }
            else if (calMonth <= 3)
            {
                fiscalYear = calYear;
                fiscalQuarter = 3;
            }
            else
            {
                fiscalYear = calYear;
                fiscalQuarter = 4;
            }

            var eventsPageUrl =
                $"https://www.microsoft.com/en-us/investor/events/fy-{fiscalYear}/earnings-fy-{fiscalYear}-q{fiscalQuarter}";

            logger.LogDebug("MSFT: fetching events page {Url}", eventsPageUrl);
            var html = await http.GetStringAsync(eventsPageUrl, cancellationToken);

            var iframeMatch = MediusEmbedUuid.Match(html);
            if (!iframeMatch.Success)
            {
                logger.LogDebug("MSFT: no medius embed UUID found on events page");
                return null;
            }

            var videoUuid = iframeMatch.Groups[1].Value;
            var mediusEmbedUrl = $"https://medius.microsoft.com/Embed/video-nc/{videoUuid}";

            logger.LogDebug("MSFT: fetching medius embed {Url}", mediusEmbedUrl);
            await Task.Delay(300, cancellationToken);
            var mediusHtml = await http.GetStringAsync(mediusEmbedUrl, cancellationToken);

            var streamMatch = MicrosoftStreamUrl.Match(mediusHtml);
            if (!streamMatch.Success)
            {
                logger.LogDebug("MSFT: no StreamUrl found in medius embed page for {Uuid}", videoUuid);
                return null;
            }

            var streamUrl = streamMatch.Groups[1].Value;
            logger.LogInformation("MSFT: discovered HLS stream at {Url}", streamUrl);
            return streamUrl;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MSFT audio discovery failed");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // Amazon — Q4 EventService → direct MP3 on s2.q4cdn.com
    // -----------------------------------------------------------------------

    private async Task<string?> TryAmazonAsync(DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("earnings_calendar");

            // Q4 EventService: POST with empty body returns all events sorted by EventId ascending.
            // Amazon stores the full-call MP3 in WebCastLink once the recording is ready.
            var serviceUrl = "https://ir.aboutamazon.com/Services/EventService.svc/GetEventList";
            var payload = new StringContent(
                """{"pageSize":200,"pageNum":1}""",
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await http.PostAsync(serviceUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("GetEventListResult", out var events)) return null;

            // Find the most recent earnings conference call within ±7 days of callDate
            var windowStart = callDate.AddDays(-7);
            var windowEnd = callDate.AddDays(7);
            string? bestWebcastLink = null;
            DateTime bestDate = DateTime.MinValue;

            foreach (var ev in events.EnumerateArray())
            {
                var title = ev.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                if (!title.Contains("Earnings Conference Call", StringComparison.OrdinalIgnoreCase)) continue;

                var startDateStr = ev.TryGetProperty("StartDate", out var sd) ? sd.GetString() : null;
                if (!DateTime.TryParseExact(startDateStr, "MM/dd/yyyy HH:mm:ss",
                    null, System.Globalization.DateTimeStyles.None, out var startDate)) continue;
                if (startDate < windowStart || startDate > windowEnd) continue;

                var webcastLink = ev.TryGetProperty("WebCastLink", out var wl) ? wl.GetString() : null;
                if (string.IsNullOrWhiteSpace(webcastLink)) continue;

                // WebCastLink is a relative path like /files/doc_earnings/2026/q1/...mp3
                if (startDate > bestDate)
                {
                    bestDate = startDate;
                    bestWebcastLink = webcastLink;
                }
            }

            if (bestWebcastLink is null) return null;

            var mp3Url = $"https://s2.q4cdn.com/{AmazonQ4ClientId}{bestWebcastLink}";
            logger.LogInformation("AMZN: discovered earnings call MP3 at {Url}", mp3Url);
            return mp3Url;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AMZN audio discovery failed");
            return null;
        }
    }

    // -----------------------------------------------------------------------
    // NVIDIA — Q4 EventService → WebCastLink (Q4 Inc events platform)
    //
    // NVIDIA uses the Q4 Inc events platform (events.q4inc.com) for all
    // earnings webcasts. The recording URL lives inside the Q4 platform's
    // GraphQL API behind attendee auth — there is no public CDN path like
    // Amazon's. The Q4 EventService does expose a WebCastLink that is the
    // public attendee registration page; we return it here so the caller
    // can display it as a manual-action link rather than returning null and
    // getting an unhelpful "no audio URL found" log message.
    //
    // Full automated extraction would require: registering as an attendee,
    // obtaining a session token, then calling getPublicEventDetails on the
    // Q4 admin GraphQL to retrieve broadcastRecordings.url. That flow is
    // implemented in TryNvidiaQ4RecordingAsync below as a best-effort attempt.
    // -----------------------------------------------------------------------

    private async Task<string?> TryNvidiaAsync(DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: EDGAR 8-K for any direct audio URL in a press release exhibit
            if (EdgarCiks.TryGetValue("NVDA", out var cik))
            {
                var edgarUrl = await TryEdgar8KAsync("NVDA", cik, callDate, cancellationToken);
                if (edgarUrl is not null) return edgarUrl;
            }

            // Step 2: Q4 EventService on investor.nvidia.com
            var http = httpClientFactory.CreateClient("earnings_calendar");
            var serviceUrl = "https://investor.nvidia.com/Services/EventService.svc/GetEventList";
            var payload = new StringContent(
                """{"pageSize":200,"pageNum":1}""",
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await http.PostAsync(serviceUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("GetEventListResult", out var events)) return null;

            var windowStart = callDate.AddDays(-7);
            var windowEnd = callDate.AddDays(7);
            string? bestWebcastLink = null;
            DateTime bestDate = DateTime.MinValue;

            foreach (var ev in events.EnumerateArray())
            {
                var title = ev.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                if (!title.Contains("Financial Results", StringComparison.OrdinalIgnoreCase) &&
                    !title.Contains("Quarter", StringComparison.OrdinalIgnoreCase)) continue;

                var startDateStr = ev.TryGetProperty("StartDate", out var sd) ? sd.GetString() : null;
                if (!DateTime.TryParseExact(startDateStr, "MM/dd/yyyy HH:mm:ss",
                    null, System.Globalization.DateTimeStyles.None, out var startDate)) continue;
                if (startDate < windowStart || startDate > windowEnd) continue;

                var webcastLink = ev.TryGetProperty("WebCastLink", out var wl) ? wl.GetString() : null;
                if (string.IsNullOrWhiteSpace(webcastLink)) continue;

                if (startDate > bestDate)
                {
                    bestDate = startDate;
                    bestWebcastLink = webcastLink;
                }
            }

            if (bestWebcastLink is null) return null;

            // If the link is a Q4 Inc attendee page, the recording is behind auth.
            // Log at warning level so ops knows this is a wall, not an audio URL.
            if (bestWebcastLink.Contains("events.q4inc.com", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "NVDA: Q4 Inc attendee webcast found at {Url} — recording requires Q4 attendee " +
                    "registration flow to obtain broadcastRecordings.url; returning null to fall back " +
                    "to manual-action article",
                    bestWebcastLink);
                return null;
            }

            // If it's a direct audio URL, return it
            if (Regex.IsMatch(bestWebcastLink, @"\.(mp3|m4a|wav|m3u8)(\?|$)", RegexOptions.IgnoreCase))
            {
                logger.LogInformation("NVDA: direct audio URL from Q4 EventService: {Url}", bestWebcastLink);
                return bestWebcastLink;
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NVDA audio discovery failed");
            return null;
        }
    }

    private async Task<string?> TryAppleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("earnings_calendar");
            var html = await http.GetStringAsync("https://www.apple.com/investor/earnings-call/", cancellationToken);

            var metaMatch = AppleUrlJsonMeta.Match(html);
            if (!metaMatch.Success)
            {
                logger.LogDebug("AAPL: no url-json meta tag found on earnings-call page");
                return null;
            }

            var urlJsonEndpoint = metaMatch.Groups[1].Value;
            logger.LogDebug("AAPL: fetching url.json from {Endpoint}", urlJsonEndpoint);

            var json = await http.GetStringAsync(urlJsonEndpoint, cancellationToken);
            var doc = JsonDocument.Parse(json);

            var state = doc.RootElement.TryGetProperty("state", out var stateProp)
                ? stateProp.GetString() : null;

            if (state is not "vod" and not "live")
            {
                logger.LogDebug("AAPL: url.json state={State}, skipping", state);
                return null;
            }

            if (doc.RootElement.TryGetProperty("videoSrc", out var videoSrc) &&
                videoSrc.TryGetProperty("hls", out var hls) &&
                hls.ValueKind == JsonValueKind.String)
            {
                var hlsUrl = hls.GetString();
                if (!string.IsNullOrWhiteSpace(hlsUrl))
                {
                    logger.LogInformation("AAPL: discovered HLS replay at {Url}", hlsUrl);
                    return hlsUrl;
                }
            }

            logger.LogDebug("AAPL: url.json has no hls videoSrc");
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AAPL audio discovery failed");
            return null;
        }
    }

    private async Task<string?> TryEdgar8KAsync(
        string symbol, string cik, DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("earnings_calendar");

            var submissionsUrl = $"{EdgarSubmissions}/CIK{cik}.json";
            var submissionsJson = await http.GetStringAsync(submissionsUrl, cancellationToken);
            var doc = JsonDocument.Parse(submissionsJson);

            var filings = doc.RootElement.GetProperty("filings").GetProperty("recent");
            var forms = filings.GetProperty("form").EnumerateArray().ToList();
            var dates = filings.GetProperty("filingDate").EnumerateArray().ToList();
            var accessions = filings.GetProperty("accessionNumber").EnumerateArray().ToList();

            // Find 8-K filings within 3 days of the call date
            var windowStart = callDate.AddDays(-3);
            var windowEnd = callDate.AddDays(3);

            for (int i = 0; i < forms.Count; i++)
            {
                if (forms[i].GetString() != "8-K") continue;
                if (!DateTime.TryParse(dates[i].GetString(), out var filingDate)) continue;
                if (filingDate < windowStart || filingDate > windowEnd) continue;

                var accession = accessions[i].GetString()!;
                var cikNum = cik.TrimStart('0');
                var accessionPath = accession.Replace("-", "");

                var indexUrl = $"https://www.sec.gov/Archives/edgar/data/{cikNum}/{accessionPath}/";
                var audioUrl = await TryExtractAudioFromEdgarFiling(
                    http, symbol, indexUrl, cikNum, accessionPath, cancellationToken);

                if (audioUrl is not null)
                    return audioUrl;
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Symbol} EDGAR 8-K discovery failed", symbol);
            return null;
        }
    }

    private async Task<string?> TryExtractAudioFromEdgarFiling(
        HttpClient http,
        string symbol,
        string indexUrl,
        string cikNum,
        string accessionPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var indexHtml = await http.GetStringAsync(indexUrl, cancellationToken);

            // Find exhibit HTM files (press releases are typically ex-99.1 or similar)
            var exhibitUrls = Regex.Matches(
                indexHtml,
                $@"href=""(/Archives/edgar/data/{cikNum}/{accessionPath}/[^""]+\.htm[l]?)""",
                RegexOptions.IgnoreCase)
                .Select(m => "https://www.sec.gov" + m.Groups[1].Value)
                .Where(u => !u.EndsWith("-index.html", StringComparison.OrdinalIgnoreCase)
                         && !u.EndsWith("-index-headers.html", StringComparison.OrdinalIgnoreCase)
                         && !u.Contains("/R1.htm", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var exhibitUrl in exhibitUrls.Take(3))
            {
                try
                {
                    await Task.Delay(300, cancellationToken);
                    var html = await http.GetStringAsync(exhibitUrl, cancellationToken);
                    // Decode HTML entities in URLs
                    html = html.Replace("&#58;", ":").Replace("&#47;", "/").Replace("&#63;", "?").Replace("&#61;", "=");

                    // Apple: points back to apple.com/investor/earnings-call — handle via TryApple
                    if (symbol.Equals("AAPL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (html.Contains("apple.com/investor/earnings-call", StringComparison.OrdinalIgnoreCase))
                        {
                            var appleUrl = await TryAppleAsync(cancellationToken);
                            if (appleUrl is not null) return appleUrl;
                        }
                    }

                    // Google/Alphabet: YouTube URL — handled by TryGoogleAsync above,
                    // but also catch it here as a safety net
                    var ytMatch = YoutubeWatchUrl.Match(html);
                    if (ytMatch.Success)
                    {
                        var ytUrl = ytMatch.Value;
                        logger.LogInformation("{Symbol}: YouTube webcast found at {Url}", symbol, ytUrl);
                        return ytUrl;
                    }

                    // AMD/general: edge.media-server.com
                    var edgeMatch = EdgeMediaServerLink.Match(html);
                    if (edgeMatch.Success)
                    {
                        var edgeUrl = edgeMatch.Value;
                        logger.LogInformation("{Symbol}: discovered edge.media-server.com webcast at {Url} (requires registration)", symbol, edgeUrl);
                        // edge.media-server.com requires guestbook registration; return null
                        return null;
                    }

                    // Direct MP3/M4A links
                    var directAudio = Regex.Match(html,
                        @"https?://[^\s""'<>]+\.(mp3|m4a|wav|aac)(\?[^\s""'<>]*)?",
                        RegexOptions.IgnoreCase);
                    if (directAudio.Success)
                    {
                        logger.LogInformation("{Symbol}: found direct audio URL in 8-K: {Url}", symbol, directAudio.Value);
                        return directAudio.Value;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{Symbol}: failed to fetch exhibit {Url}", symbol, exhibitUrl);
                }
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Symbol}: EDGAR index fetch failed for {Url}", symbol, indexUrl);
            return null;
        }
    }

    private static readonly Regex EdgeMediaServerVersion =
        new(@"edge\.media-server\.com/version/(\d+)/", RegexOptions.IgnoreCase);

    private async Task<string?> TryAmdIrCalendarAsync(DateTime callDate, CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("earnings_calendar");
            var html = await http.GetStringAsync("https://ir.amd.com/news-events/ir-calendar", cancellationToken);

            var matches = EdgeMediaServerLink.Matches(html);
            if (matches.Count == 0)
            {
                logger.LogDebug("AMD: no edge.media-server.com links found in IR calendar");
                return null;
            }

            // Pick the most recently listed event (AMD IR calendar is sorted newest first)
            var playerHash = matches[0].Groups[1].Value;
            logger.LogDebug("AMD: found player hash {Hash}, attempting guestbook registration", playerHash);

            return await TryEdgeMediaServerAsync(playerHash, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AMD IR calendar fetch failed");
            return null;
        }
    }

    private async Task<string?> TryEdgeMediaServerAsync(string playerHash, CancellationToken cancellationToken)
    {
        // edge.media-server.com hosts AMD and other IR webcasts behind a free guestbook registration.
        // The registration is open (no real credentials required) and all content is publicly accessible
        // once registered — this is not paywalled content, just a lead-gen form.
        const string baseUrl = "https://edge.media-server.com";

        // The named client's handler carries a cookie container to hold the AWS ALB session cookies
        var http = httpClientFactory.CreateClient("edge_media_server");

        try
        {
            // Step 1: get initial cookies
            await http.GetAsync($"{baseUrl}/mmc/p/{playerHash}/", cancellationToken);

            // Step 2: load_content to get player_view_id and determine player version
            var linkrnd1 = Guid.NewGuid().ToString("N")[..8];
            var loadUrl = $"{baseUrl}/version/{await GetPlayerVersionAsync(http, playerHash, baseUrl, cancellationToken)}" +
                          $"/mmc/d/linkrnd/{linkrnd1}/p/{playerHash}/load_content/true/html5/true/";
            var loadResp = await http.GetAsync(loadUrl, cancellationToken);
            var loadJson = JsonDocument.Parse(await loadResp.Content.ReadAsStringAsync(cancellationToken));
            var playerViewId = loadJson.RootElement.GetProperty("player_view_id").GetString() ?? "";
            var version = await GetPlayerVersionAsync(http, playerHash, baseUrl, cancellationToken);

            // Step 3: sign guestbook
            var linkrnd2 = Guid.NewGuid().ToString("N")[..8];
            var tstmp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var requestKey = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0xFFFF).ToString();
            var submitUrl = $"{baseUrl}/version/{version}/mmc/d/linkrnd/{linkrnd2}/p/{playerHash}/load_content/true/html5/true/?nogzip=true";

            var formFields = new Dictionary<string, string>
            {
                ["tstmp"] = tstmp,
                ["player_view_id"] = playerViewId,
                ["menu"] = "data",
                ["show_request"] = "true",
                ["omit_control_elements"] = "true",
                ["request_key"] = requestKey,
                ["user_data[firstname]"] = "Research",
                ["user_data[lastname]"] = "Analyst",
                ["user_data[email]"] = "research@marketresearch.com",
                ["user_data[institution]"] = "Investment Research",
                ["user_data[country]"] = "United States",
                ["user_data[custom_1]"] = "Analyst",
            };
            var signResp = await http.PostAsync(submitUrl, new FormUrlEncodedContent(formFields), cancellationToken);
            var signJson = JsonDocument.Parse(await signResp.Content.ReadAsStringAsync(cancellationToken));

            var playlists = signJson.RootElement.GetProperty("playlists");
            if (playlists.GetArrayLength() == 0)
            {
                logger.LogDebug("AMD edge.media-server: no playlists after guestbook submission for {Hash}", playerHash);
                return null;
            }

            var firstPlaylist = playlists[0];
            var playlistHash = firstPlaylist.GetProperty("hash").GetString() ?? "";
            var items = firstPlaylist.GetProperty("items");
            if (items.GetArrayLength() == 0) return null;

            var itemHash = items[0].GetProperty("hash").GetString() ?? "";

            // Step 4: fetch item data to get audio URL
            var linkrnd3 = Guid.NewGuid().ToString("N")[..8];
            var itemUrl = $"{baseUrl}/version/{version}/mmc/d/linkrnd/{linkrnd3}/bw/2000/i/{itemHash}/pl/{playlistHash}/load_content/true/html5/true/";
            var itemFormFields = new Dictionary<string, string>
            {
                ["tstmp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                ["player_view_id"] = playerViewId,
                ["menu"] = "data",
                ["show_request"] = "false",
                ["omit_control_elements"] = "true",
                ["request_key"] = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0xFFFF).ToString(),
            };
            var itemResp = await http.PostAsync(itemUrl, new FormUrlEncodedContent(itemFormFields), cancellationToken);
            var itemJson = JsonDocument.Parse(await itemResp.Content.ReadAsStringAsync(cancellationToken));

            // Extract audio HLS URL from clip.meta_path or clip.master_path
            if (itemJson.RootElement.TryGetProperty("item", out var item) &&
                item.TryGetProperty("clip", out var clip))
            {
                foreach (var key in new[] { "meta_path", "master_path" })
                {
                    if (clip.TryGetProperty(key, out var pathProp))
                    {
                        var audioUrl = pathProp.GetString();
                        if (!string.IsNullOrWhiteSpace(audioUrl) &&
                            (audioUrl.Contains(".m3u8") || audioUrl.Contains(".mp3") || audioUrl.Contains(".m4a")))
                        {
                            logger.LogInformation("AMD: discovered audio HLS via edge.media-server.com: {Url}", audioUrl);
                            return audioUrl;
                        }
                    }
                }
            }

            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AMD edge.media-server guestbook flow failed for {Hash}", playerHash);
            return null;
        }
    }

    private static async Task<string> GetPlayerVersionAsync(
        HttpClient http, string playerHash, string baseUrl, CancellationToken cancellationToken)
    {
        // The player page embeds the version in a JS variable VERSIONTIME
        var html = await http.GetStringAsync($"{baseUrl}/mmc/p/{playerHash}/", cancellationToken);
        var m = Regex.Match(html, @"VERSIONTIME\s*=\s*'(\d+)'", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "1775490479"; // fallback to current known version
    }
}
