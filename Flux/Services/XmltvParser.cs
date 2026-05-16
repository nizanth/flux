using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>
/// Streaming XMLTV EPG parser. Uses XmlReader (never XDocument/LoadXml) so files up to
/// 500 MB stay within the 256 MB working-set budget (NFR-PERF-004).
/// Each programme element is isolated in a try/catch so one malformed entry cannot
/// abort the whole sync (FR-EPG-002, NFR-REL-003).
/// </summary>
public sealed class XmltvParser
{
    // XMLTV date format variants seen in the wild
    private static readonly string[] DateFormats =
    [
        "yyyyMMddHHmmss zzz",
        "yyyyMMddHHmmss",
        "yyyyMMddHHmm zzz",
        "yyyyMMddHHmm",
        "yyyyMMddHH zzz",
        "yyyyMMddHH",
    ];

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private readonly ILogger<XmltvParser> _logger;

    /// <summary>Initializes a new instance of <see cref="XmltvParser"/>.</summary>
    public XmltvParser(ILogger<XmltvParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Asynchronously streams parsed programme entries from a raw (possibly gzip-compressed)
    /// XMLTV stream. Each entry is yielded immediately — no intermediate list is built.
    /// </summary>
    public async IAsyncEnumerable<XmltvProgramme> ParseAsync(
        Stream rawStream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Transparently decompress gzip responses
        await using var stream = IsGzip(rawStream) ? new GZipStream(rawStream, CompressionMode.Decompress) : rawStream;

        using var reader = XmlReader.Create(stream, ReaderSettings);

        var goodCount = 0;
        var badCount = 0;

        while (!ct.IsCancellationRequested)
        {
            bool read;
            try
            {
                read = await reader.ReadAsync().ConfigureAwait(false);
            }
            catch (XmlException ex)
            {
                _logger.LogError(ex, "XMLTV stream is malformed at position {Pos}; aborting parse after {Good} good entries",
                    reader.Settings?.LineNumberOffset, goodCount);
                yield break;
            }

            if (!read)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.Name != "programme")
            {
                continue;
            }

            XmltvProgramme? programme = null;
            try
            {
                programme = await ReadProgrammeAsync(reader, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                badCount++;
                _logger.LogDebug(ex, "Skipping malformed <programme> element (bad count so far: {Bad})", badCount);
            }

            if (programme is not null)
            {
                goodCount++;
                yield return programme;
            }
        }

        _logger.LogInformation(
            "XMLTV parse complete: {Good} programmes parsed, {Bad} entries skipped",
            goodCount, badCount);
    }

    /// <summary>
    /// Collects all programmes from a stream into a list. Convenience wrapper over
    /// <see cref="ParseAsync"/> for callers that need a materialized collection.
    /// </summary>
    public async Task<List<XmltvProgramme>> ParseToListAsync(Stream stream, CancellationToken ct = default)
    {
        var result = new List<XmltvProgramme>();
        await foreach (var p in ParseAsync(stream, ct).ConfigureAwait(false))
        {
            result.Add(p);
        }

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<XmltvProgramme?> ReadProgrammeAsync(XmlReader reader, CancellationToken ct)
    {
        var channelId = reader.GetAttribute("channel");
        var startStr = reader.GetAttribute("start");
        var stopStr = reader.GetAttribute("stop");

        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(startStr))
        {
            return null;
        }

        var start = ParseXmltvDate(startStr);
        if (start == DateTimeOffset.MinValue)
        {
            _logger.LogDebug("Skipping programme with unparseable start time: {Start}", startStr);
            return null;
        }

        var programme = new XmltvProgramme
        {
            ChannelId = channelId,
            StartUtc = start.UtcDateTime,
            StopUtc = string.IsNullOrWhiteSpace(stopStr) ? (DateTime?)null : ParseXmltvDate(stopStr).UtcDateTime,
        };

        // Use a subtree reader so we stay scoped to this <programme> element
        using var sub = reader.ReadSubtree();

        // Skip the opening <programme> element itself
        await sub.ReadAsync().ConfigureAwait(false);

        while (await sub.ReadAsync().ConfigureAwait(false) && !ct.IsCancellationRequested)
        {
            if (sub.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (sub.Name)
            {
                case "title":
                    programme.Title = StripHtml(sub.ReadElementContentAsString());
                    break;
                case "sub-title":
                    programme.SubTitle = StripHtml(sub.ReadElementContentAsString());
                    break;
                case "desc":
                    programme.Description = StripHtml(sub.ReadElementContentAsString());
                    break;
                case "category":
                    programme.Category ??= StripHtml(sub.ReadElementContentAsString());
                    break;
                case "icon":
                    programme.IconUrl = sub.GetAttribute("src");
                    sub.Skip();
                    break;
                case "episode-num":
                    // Prefer xmltv_ns format (S00E00) if present; otherwise take first value
                    var system = sub.GetAttribute("system");
                    var epNum = sub.ReadElementContentAsString();
                    if (programme.EpisodeNum is null || string.Equals(system, "xmltv_ns", StringComparison.OrdinalIgnoreCase))
                    {
                        programme.EpisodeNum = epNum;
                    }

                    break;
                case "rating":
                    var ratingValue = ReadChildText(sub, "value");
                    programme.Rating = ratingValue;
                    break;
                default:
                    sub.Skip();
                    break;
            }
        }

        // A programme with no title is useless
        return string.IsNullOrWhiteSpace(programme.Title) ? null : programme;
    }

    private static string? ReadChildText(XmlReader reader, string childElementName)
    {
        using var sub = reader.ReadSubtree();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element && sub.Name == childElementName)
            {
                return sub.ReadElementContentAsString();
            }
        }

        return null;
    }

    private static DateTimeOffset ParseXmltvDate(string value)
    {
        // XMLTV offsets look like "+0100", DateTimeOffset wants "+01:00"
        var trimmed = value.Trim();

        // Normalize: "20240115143000 +0100" → "20240115143000 +01:00"
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0 && spaceIdx < trimmed.Length - 1)
        {
            var datepart = trimmed[..spaceIdx];
            var offsetpart = trimmed[(spaceIdx + 1)..];
            if (offsetpart.Length == 5 && (offsetpart[0] == '+' || offsetpart[0] == '-'))
            {
                offsetpart = offsetpart[..3] + ":" + offsetpart[3..];
            }

            trimmed = datepart + " " + offsetpart;
        }

        if (DateTimeOffset.TryParseExact(
                trimmed,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var result))
        {
            return result;
        }

        return DateTimeOffset.MinValue;
    }

    private static bool IsGzip(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        Span<byte> buf = stackalloc byte[2];
        var read = stream.Read(buf);
        stream.Seek(0, SeekOrigin.Begin);
        return read == 2 && buf[0] == 0x1F && buf[1] == 0x8B;
    }

    /// <summary>Strips simple HTML tags from provider-supplied strings (NFR-SEC-004).</summary>
    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains('<'))
        {
            return input;
        }

        var sb = new System.Text.StringBuilder(input.Length);
        var inTag = false;
        foreach (var c in input)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

/// <summary>A single parsed programme from an XMLTV feed.</summary>
public sealed class XmltvProgramme
{
    /// <summary>Gets or sets the XMLTV channel ID this programme belongs to.</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC start time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Gets or sets the UTC stop time (null when not provided by the feed).</summary>
    public DateTime? StopUtc { get; set; }

    /// <summary>Gets or sets the programme title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode/sub-title.</summary>
    public string? SubTitle { get; set; }

    /// <summary>Gets or sets the programme description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the primary category.</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the icon/poster URL.</summary>
    public string? IconUrl { get; set; }

    /// <summary>Gets or sets the episode number string from the feed.</summary>
    public string? EpisodeNum { get; set; }

    /// <summary>Gets or sets the content rating (e.g. "TV-PG").</summary>
    public string? Rating { get; set; }
}
