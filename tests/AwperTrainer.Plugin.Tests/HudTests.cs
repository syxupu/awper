using System.Text;
using System.Xml.Linq;
using AwperTrainer.Plugin;
using Xunit;

namespace AwperTrainer.Plugin.Tests;

public sealed class HudTests
{
    [Fact]
    public void EveryHudButtonHasOneWhitelistedAction()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "awper_hud.xml"));
        var actual = document.Descendants("Button")
            .Select(element => (string?)element.Attribute("id"))
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var expected = HudActions.ConsoleCommands.Keys
            .Append(HudActions.LoadFirstButton)
            .Append(HudActions.CloseButton)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.All(HudActions.ConsoleCommands.Values, command => Assert.StartsWith("css_", command));
    }

    [Fact]
    public void HudContainsAllSevenMapButtonsInPreferredOrder()
    {
        var expected = new[]
        {
            "de_dust2", "de_inferno", "de_mirage", "de_anubis", "de_ancient", "de_nuke", "de_cache"
        };
        var actual = HudActions.ConsoleCommands.Values
            .Where(command => command.StartsWith("css_map ", StringComparison.Ordinal))
            .Select(command => command["css_map ".Length..]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CompiledHudResourcesUseTheirDeployedBaseGamePaths()
    {
        var script = ReadBinaryText("awper_hud.vjs_c");
        var layout = ReadBinaryText("awper_hud.vxml_c");
        var style = ReadBinaryText("awper_hud.vcss_c");

        Assert.Contains("scripts/awper", script, StringComparison.Ordinal);
        Assert.DoesNotContain("maps/scripts", script, StringComparison.Ordinal);
        Assert.Contains("panorama/layout/custom_game", layout, StringComparison.Ordinal);
        Assert.Contains("panorama/styles/custom_game", style, StringComparison.Ordinal);
        Assert.DoesNotContain("csgo_addons", script, StringComparison.Ordinal);
        Assert.DoesNotContain("csgo_addons", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("csgo_addons", style, StringComparison.Ordinal);
    }

    private static string ReadBinaryText(string name)
        => Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name)));
}
