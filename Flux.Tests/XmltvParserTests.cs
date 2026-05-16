using System.IO.Compression;
using System.Text;
using Jellyfin.Plugin.Flux.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flux.Tests;

public class XmltvParserTests
{
    private static readonly XmltvParser Parser = new(NullLogger<XmltvParser>.Instance);

    private static Stream MakeXmltvStream(string xmlContent)
    {
        var bytes = Encoding.UTF8.GetBytes(xmlContent);
        return new MemoryStream(bytes);
    }

    private const string ThreeProgrammesXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <programme start="20240115130000 +0000" stop="20240115140000 +0000" channel="ch1">
            <title>News Hour</title>
            <desc>Daily news</desc>
            <category>News</category>
          </programme>
          <programme start="20240115140000 +0000" stop="20240115150000 +0000" channel="ch1">
            <title>Sports Tonight</title>
          </programme>
          <programme start="20240115150000 +0000" stop="20240115160000 +0000" channel="ch2">
            <title>Movie: The Return</title>
            <sub-title>Episode 1</sub-title>
            <episode-num system="xmltv_ns">0.0.0</episode-num>
            <rating><value>TV-PG</value></rating>
          </programme>
        </tv>
        """;

    [Fact]
    public async Task ParseAsync_ValidXmltv_YieldsProgrammes()
    {
        using var stream = MakeXmltvStream(ThreeProgrammesXml);
        var programmes = await Parser.ParseToListAsync(stream);

        Assert.Equal(3, programmes.Count);

        var first = programmes[0];
        Assert.Equal("News Hour", first.Title);
        Assert.Equal("ch1", first.ChannelId);
        Assert.Equal(new DateTime(2024, 1, 15, 13, 0, 0, DateTimeKind.Utc), first.StartUtc);
    }

    [Fact]
    public async Task ParseAsync_MalformedEntry_SkipsAndContinues()
    {
        // The second programme has no 'start' attribute — it should be skipped
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115130000 +0000" stop="20240115140000 +0000" channel="ch1">
                <title>Good Programme One</title>
              </programme>
              <programme stop="20240115150000 +0000" channel="ch1">
                <title>Bad Programme No Start</title>
              </programme>
              <programme start="20240115150000 +0000" stop="20240115160000 +0000" channel="ch2">
                <title>Good Programme Two</title>
              </programme>
            </tv>
            """;

        using var stream = MakeXmltvStream(xml);
        var programmes = await Parser.ParseToListAsync(stream);

        Assert.Equal(2, programmes.Count);
        Assert.Equal("Good Programme One", programmes[0].Title);
        Assert.Equal("Good Programme Two", programmes[1].Title);
    }

    [Fact]
    public async Task ParseAsync_HtmlInTitle_IsStripped()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115130000 +0000" stop="20240115140000 +0000" channel="ch1">
                <title><b>News</b></title>
              </programme>
            </tv>
            """;

        using var stream = MakeXmltvStream(xml);
        var programmes = await Parser.ParseToListAsync(stream);

        Assert.Single(programmes);
        Assert.Equal("News", programmes[0].Title);
    }

    [Fact]
    public async Task ParseAsync_TimezoneOffset_ConvertedToUtc()
    {
        // start="20240115140000 +0100" → UTC 13:00
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115140000 +0100" stop="20240115150000 +0100" channel="ch1">
                <title>Offset Show</title>
              </programme>
            </tv>
            """;

        using var stream = MakeXmltvStream(xml);
        var programmes = await Parser.ParseToListAsync(stream);

        Assert.Single(programmes);
        Assert.Equal(new DateTime(2024, 1, 15, 13, 0, 0, DateTimeKind.Utc), programmes[0].StartUtc);
    }

    [Fact]
    public async Task ParseAsync_GzipStream_ParsedCorrectly()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115130000 +0000" stop="20240115140000 +0000" channel="ch1">
                <title>Gzipped Show</title>
              </programme>
            </tv>
            """;

        // Compress the XML with gzip
        var rawBytes = Encoding.UTF8.GetBytes(xml);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await gzip.WriteAsync(rawBytes);
        }

        compressed.Seek(0, SeekOrigin.Begin);
        var programmes = await Parser.ParseToListAsync(compressed);

        Assert.Single(programmes);
        Assert.Equal("Gzipped Show", programmes[0].Title);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsEmpty()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv></tv>
            """;

        using var stream = MakeXmltvStream(xml);
        var programmes = await Parser.ParseToListAsync(stream);

        Assert.Empty(programmes);
    }

    [Fact]
    public async Task ParseAsync_ProgrammeWithSubTitleEpisodeRating_AllParsed()
    {
        using var stream = MakeXmltvStream(ThreeProgrammesXml);
        var programmes = await Parser.ParseToListAsync(stream);

        // Third programme has sub-title, episode-num, and rating
        var third = programmes[2];
        Assert.Equal("Episode 1", third.SubTitle);
        Assert.Equal("0.0.0", third.EpisodeNum);
        Assert.Equal("TV-PG", third.Rating);
    }
}
