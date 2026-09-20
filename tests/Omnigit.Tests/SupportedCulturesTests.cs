using System.Globalization;
using Omnigit.Localization;

namespace Omnigit.Tests;

public class SupportedCulturesTests
{
    [Fact]
    public void EveryConfiguredCultureIsValidAndUnique()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in SupportedCultures.All)
        {
            Assert.True(seen.Add(name), $"Duplicate culture: {name}");

            var culture = CultureInfo.GetCultureInfo(name);
            Assert.Equal(name, culture.Name);
        }
    }

    [Fact]
    public void RightToLeftCulturesArePartOfTheSupportedSet()
    {
        foreach (var name in SupportedCultures.RightToLeft)
        {
            Assert.Contains(
                SupportedCultures.All,
                candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

            Assert.True(CultureInfo.GetCultureInfo(name).TextInfo.IsRightToLeft);
        }
    }

    [Fact]
    public void TheBaselineCoversLatinCjkAndRightToLeftScripts()
    {
        Assert.Contains("en-US", SupportedCultures.All);
        Assert.Contains("zh-CN", SupportedCultures.All);
        Assert.Contains("ja-JP", SupportedCultures.All);
        Assert.Contains("ko-KR", SupportedCultures.All);
        Assert.Contains("ar-SA", SupportedCultures.All);
    }
}
