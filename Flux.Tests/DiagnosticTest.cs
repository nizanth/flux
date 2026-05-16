using System.Text;
using Jellyfin.Plugin.Flux.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Flux.Tests;

public class DiagnosticTest
{
    private readonly ITestOutputHelper _output;

    public DiagnosticTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Diagnostic_SubTitle()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115150000 +0000" stop="20240115160000 +0000" channel="ch2">
                <title>Movie: The Return</title>
                <sub-title>Episode 1</sub-title>
                <episode-num system="xmltv_ns">0.0.0</episode-num>
                <rating><value>TV-PG</value></rating>
              </programme>
            </tv>
            """;

        var bytes = Encoding.UTF8.GetBytes(xml);
        using var stream = new MemoryStream(bytes);
        var parser = new XmltvParser(NullLogger<XmltvParser>.Instance);
        var programmes = await parser.ParseToListAsync(stream);

        _output.WriteLine($"Count: {programmes.Count}");
        if (programmes.Count > 0)
        {
            _output.WriteLine($"Title: {programmes[0].Title}");
            _output.WriteLine($"SubTitle: {programmes[0].SubTitle}");
            _output.WriteLine($"EpisodeNum: {programmes[0].EpisodeNum}");
            _output.WriteLine($"Rating: {programmes[0].Rating}");
        }

        Assert.True(true); // always pass, just for diagnostics
    }

    [Fact]
    public async Task Diagnostic_HtmlTitle()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <programme start="20240115130000 +0000" stop="20240115140000 +0000" channel="ch1">
                <title>&lt;b&gt;News&lt;/b&gt;</title>
              </programme>
            </tv>
            """;

        var bytes = Encoding.UTF8.GetBytes(xml);
        using var stream = new MemoryStream(bytes);
        var parser = new XmltvParser(NullLogger<XmltvParser>.Instance);
        var programmes = await parser.ParseToListAsync(stream);

        _output.WriteLine($"Count: {programmes.Count}");
        if (programmes.Count > 0)
        {
            _output.WriteLine($"Title: '{programmes[0].Title}'");
        }

        Assert.True(true);
    }
}
