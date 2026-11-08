namespace ServiceLib.Tests.Aether;

// PattN: AetherDigits stands in for Kotlin's String.toIntOrNull/toLongOrNull and Char.digitToIntOrNull, which PattNG
// reads every Aether number by and needs no test of its own there. These pin the Kotlin results, overflow included:
// CheckForOverflowUnderflow is on, so a number past the limits must be refused here before a cast could throw.
public class AetherDigitsTests
{
    [Test]
    public async Task AnIntIsReadUpToItsLimitsAndNoFurther()
    {
        await AetherDigits.ToIntOrNull("2147483647").Should().BeEqualTo(int.MaxValue);
        await AetherDigits.ToIntOrNull("2147483648").Should().BeNull();
        await AetherDigits.ToIntOrNull("-2147483648").Should().BeEqualTo(int.MinValue);
        await AetherDigits.ToIntOrNull("-2147483649").Should().BeNull();
        await AetherDigits.ToIntOrNull("99999999999999999999999").Should().BeNull();
        await AetherDigits.TryParse("2147483648", out var value).Should().BeFalse();
        await value.Should().BeEqualTo(0);
    }

    [Test]
    public async Task ALongIsReadUpToItsLimitsAndNoFurther()
    {
        await AetherDigits.ToLongOrNull("9223372036854775807").Should().BeEqualTo(long.MaxValue);
        await AetherDigits.ToLongOrNull("9223372036854775808").Should().BeNull();
        await AetherDigits.ToLongOrNull("-9223372036854775808").Should().BeEqualTo(long.MinValue);
        await AetherDigits.ToLongOrNull("-9223372036854775809").Should().BeNull();
        await AetherDigits.ToLongOrNull("2147483648").Should().BeEqualTo(2147483648L);
    }

    [Test]
    public async Task ASignNeedsADigitAfterIt()
    {
        await AetherDigits.ToIntOrNull("+").Should().BeNull();
        await AetherDigits.ToIntOrNull("-").Should().BeNull();
        await AetherDigits.ToLongOrNull("+").Should().BeNull();
        await AetherDigits.ToLongOrNull("-").Should().BeNull();
        await AetherDigits.ToIntOrNull("+5").Should().BeEqualTo(5);
        await AetherDigits.ToIntOrNull("-5").Should().BeEqualTo(-5);
        await AetherDigits.ToIntOrNull("+-5").Should().BeNull();
        await AetherDigits.ToIntOrNull("5-").Should().BeNull();
        await AetherDigits.ToIntOrNull("*5").Should().BeNull();
    }

    [Test]
    public async Task NothingButDigitsIsANumber()
    {
        await AetherDigits.ToIntOrNull(null).Should().BeNull();
        await AetherDigits.ToIntOrNull("").Should().BeNull();
        await AetherDigits.ToIntOrNull(" 5").Should().BeNull();
        await AetherDigits.ToIntOrNull("5 ").Should().BeNull();
        await AetherDigits.ToIntOrNull("5.0").Should().BeNull();
        await AetherDigits.ToIntOrNull("0x10").Should().BeNull();
        await AetherDigits.ToLongOrNull(" 5").Should().BeNull();
    }

    [Test]
    public async Task ADigitOfAnyScriptReadsAsItsValue()
    {
        // Arabic-Indic, Persian and full-width digits, as Kotlin reads them.
        await AetherDigits.ToIntOrNull("٥").Should().BeEqualTo(5);
        await AetherDigits.ToIntOrNull("۵").Should().BeEqualTo(5);
        await AetherDigits.ToIntOrNull("５").Should().BeEqualTo(5);
        await AetherDigits.ToIntOrNull("۱۰۸۱۹").Should().BeEqualTo(10819);
        await AetherDigits.ToLongOrNull("-۱۷۹۰۰۰۰۰۰۰").Should().BeEqualTo(-1790000000L);
        // A superscript or a fraction is no decimal digit.
        await AetherDigits.ToIntOrNull("²").Should().BeNull();
        await AetherDigits.ToIntOrNull("½").Should().BeNull();
        await AetherDigits.DigitOf('7').Should().BeEqualTo(7);
        await AetherDigits.DigitOf('²').Should().BeEqualTo(-1);
        await AetherDigits.DigitOf('a').Should().BeEqualTo(-1);
    }

    [Test]
    public async Task AHexDigitIsADigitOrAToFInEitherCaseAndWidth()
    {
        await AetherDigits.HexDigitOf('9').Should().BeEqualTo(9);
        await AetherDigits.HexDigitOf('۹').Should().BeEqualTo(9);
        await AetherDigits.HexDigitOf('a').Should().BeEqualTo(10);
        await AetherDigits.HexDigitOf('F').Should().BeEqualTo(15);
        await AetherDigits.HexDigitOf('ａ').Should().BeEqualTo(10);
        await AetherDigits.HexDigitOf('ｆ').Should().BeEqualTo(15);
        await AetherDigits.HexDigitOf('Ａ').Should().BeEqualTo(10);
        await AetherDigits.HexDigitOf('Ｆ').Should().BeEqualTo(15);
        await AetherDigits.HexDigitOf('g').Should().BeEqualTo(-1);
        await AetherDigits.HexDigitOf('G').Should().BeEqualTo(-1);
        await AetherDigits.HexDigitOf('ｇ').Should().BeEqualTo(-1);
        await AetherDigits.HexDigitOf('Ｇ').Should().BeEqualTo(-1);
        await (AetherEndpoint.Parse("[ＦＥ８０::１]:443")?.ToString()).Should().BeEqualTo("[fe80::1]:443");
    }

    [Test]
    public async Task ANumberPastTheLimitsNeverThrowsWhereItIsRead()
    {
        // PattNG's callers get null from toIntOrNull and fall back or refuse; none may throw here.
        await AetherPorts.ListenPortOf("99999999999").Should().BeEqualTo(AetherPorts.DefaultListenPort);
        await AetherPorts.ListenPortProblem("99999999999", [10808]).Should().BeEqualTo(AetherListenPortProblem.NotAPort);
        await AetherRange.Parse("99999999999", AetherRange.FragmentSize).Should().BeNull();
        await AetherRange.Parse("1-99999999999", AetherRange.FragmentSize).Should().BeNull();
        await PsiphonServerList.PublishedAt("99999999999999999999").Should().BeEqualTo(0L);
    }
}
