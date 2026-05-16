using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>
/// Parses XMLTV-format EPG data from a URL or stream.
/// </summary>
public sealed class XmltvParser
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<XmltvParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XmltvParser"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create HTTP clients.</param>
    /// <param name="logger">Logger instance.</param>
    public XmltvParser(IHttpClientFactory httpClientFactory, ILogger<XmltvParser> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(XmltvParser));
        _logger = logger;
    }

    /// <summary>
    /// Downloads and parses an XMLTV EPG file from the given URL.
    /// </summary>
    /// <param name="xmltvUrl">URL of the XMLTV file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of parsed <see cref="XmltvProgramme"/> entries.</returns>
    public async Task<List<XmltvProgramme>> ParseFromUrlAsync(string xmltvUrl, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading XMLTV EPG from {Url}", xmltvUrl);

        try
        {
            await using var stream = await _httpClient.GetStreamAsync(xmltvUrl, cancellationToken).ConfigureAwait(false);
            return await ParseFromStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download or parse XMLTV from {Url}", xmltvUrl);
            return new List<XmltvProgramme>();
        }
    }

    /// <summary>
    /// Parses XMLTV EPG data from the given stream.
    /// </summary>
    /// <param name="stream">The XML stream to parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of parsed <see cref="XmltvProgramme"/> entries.</returns>
    public async Task<List<XmltvProgramme>> ParseFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var programmes = new List<XmltvProgramme>();

        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        using var reader = XmlReader.Create(stream, settings);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.Element && reader.Name == "programme")
            {
                var programme = ReadProgramme(reader);
                if (programme is not null)
                {
                    programmes.Add(programme);
                }
            }
        }

        _logger.LogDebug("Parsed {Count} XMLTV programme entries", programmes.Count);
        return programmes;
    }

    private static XmltvProgramme? ReadProgramme(XmlReader reader)
    {
        var channelId = reader.GetAttribute("channel");
        var startStr = reader.GetAttribute("start");
        var stopStr = reader.GetAttribute("stop");

        if (string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(startStr))
        {
            return null;
        }

        var programme = new XmltvProgramme
        {
            ChannelId = channelId,
            Start = ParseXmltvDate(startStr),
            Stop = string.IsNullOrEmpty(stopStr) ? null : ParseXmltvDate(stopStr)
        };

        using var subtree = reader.ReadSubtree();
        subtree.ReadToDescendant("title");

        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (subtree.Name)
            {
                case "title":
                    programme.Title = subtree.ReadElementContentAsString();
                    break;
                case "desc":
                    programme.Description = subtree.ReadElementContentAsString();
                    break;
                case "category":
                    programme.Category = subtree.ReadElementContentAsString();
                    break;
                case "icon":
                    programme.IconUrl = subtree.GetAttribute("src");
                    subtree.Skip();
                    break;
                default:
                    subtree.Skip();
                    break;
            }
        }

        return programme;
    }

    private static DateTimeOffset ParseXmltvDate(string value)
    {
        // XMLTV date format: YYYYMMDDHHmmss +HHMM
        var trimmed = value.Trim();
        if (DateTimeOffset.TryParseExact(
                trimmed,
                new[] { "yyyyMMddHHmmss zzz", "yyyyMMddHHmmss", "yyyyMMddHHmm zzz", "yyyyMMddHHmm" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result))
        {
            return result;
        }

        return DateTimeOffset.MinValue;
    }
}

/// <summary>
/// Represents a single programme entry from an XMLTV EPG file.
/// </summary>
public sealed class XmltvProgramme
{
    /// <summary>Gets or sets the channel identifier this programme belongs to.</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme start time.</summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>Gets or sets the programme end time (may be null if not provided).</summary>
    public DateTimeOffset? Stop { get; set; }

    /// <summary>Gets or sets the programme title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the programme category.</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the URL to a programme icon/poster.</summary>
    public string? IconUrl { get; set; }
}
