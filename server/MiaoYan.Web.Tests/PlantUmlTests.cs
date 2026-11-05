using MiaoYan.Web.Services;

namespace MiaoYan.Web.Tests;

public sealed class PlantUmlTests
{
    [Fact]
    public void Validate_AcceptsPlainDiagram()
    {
        Assert.Null(PlantUmlRenderer.Validate("@startuml\nAlice -> Bob: hi\n@enduml"));
    }

    [Theory]
    [InlineData("!include /etc/passwd")]
    [InlineData("!includeurl https://example.com/x.puml")]
    [InlineData("!import /lzcapp/documents/other/note.md")]
    [InlineData("!theme amiga")]
    public void Validate_RejectsLocalAndRemoteIncludes(string source)
    {
        var error = PlantUmlRenderer.Validate($"@startuml\n{source}\n@enduml");
        Assert.NotNull(error);
        Assert.Contains("disabled", error);
    }

    [Fact]
    public void Validate_RejectsRemoteUrls()
    {
        var error = PlantUmlRenderer.Validate("@startuml\nBob -> Alice: https://evil.example\n@enduml");
        Assert.NotNull(error);
        Assert.Contains("remote", error);
    }

    [Fact]
    public void Validate_RejectsEmptyAndOversized()
    {
        Assert.NotNull(PlantUmlRenderer.Validate(null));
        Assert.NotNull(PlantUmlRenderer.Validate("   "));
        Assert.NotNull(PlantUmlRenderer.Validate(new string('a', PlantUmlRenderer.MaxSourceBytes + 1)));
    }
}
