namespace ServiceLib.Tests.Aether;

public class AetherRangeTests
{
    [Test]
    public async Task ANumberOrARangeIsReadAndWrittenBack()
    {
        await (AetherRange.Parse("24", AetherRange.FragmentSize)?.ToString()).Should().BeEqualTo("24");
        await (AetherRange.Parse("16-32", AetherRange.FragmentSize)?.ToString()).Should().BeEqualTo("16-32");
        await (AetherRange.Parse(" 32 - 16 ", AetherRange.FragmentSize)?.ToString()).Should().BeEqualTo("16-32");
        await (AetherRange.Parse("0-10", AetherRange.FragmentDelay)?.ToString()).Should().BeEqualTo("0-10");
        await (AetherRange.Parse("۱۶-۳۲", AetherRange.FragmentSize)?.ToString()).Should().BeEqualTo("16-32");
    }

    [Test]
    public async Task ValuesOutsideTheLimitsAreRefused()
    {
        await AetherRange.Parse("0", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("16-5000", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("2-2000", AetherRange.FragmentDelay).Should().BeNull();
    }

    [Test]
    public async Task AnythingElseIsNotARange()
    {
        await AetherRange.Parse("", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse(null, AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("-5", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("1-2-3", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("16-", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("abc", AetherRange.FragmentSize).Should().BeNull();
    }

    // PattN: trimmed by Kotlin's whitespace, as PattNG trims: the separators U+001C to U+001F are spaces, U+0085 is not.
    [Test]
    public async Task TheSpacesAroundTheNumbersAreKotlins()
    {
        await (AetherRange.Parse("\u001F32\u001C-\u001D16\u001E", AetherRange.FragmentSize)?.ToString()).Should().BeEqualTo("16-32");
        await AetherRange.Parse("16\u0085", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("\u0085", AetherRange.FragmentSize).Should().BeNull();
    }
}
