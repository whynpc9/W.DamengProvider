using System.Globalization;
using System.Text;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;
using ReaderFixture = W.DmProvider.TypeTests.TemporalNativeReaderContractTests.ReaderFixture;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeReadTextRegressionTests
{
    [Theory]
    [InlineData(26, 7, 100, "0000001")]
    [InlineData(27, 7, 100, "0000001")]
    [InlineData(26, 8, 123456780, "12345678")]
    [InlineData(27, 8, 123456780, "12345678")]
    [InlineData(26, 9, 123456789, "123456789")]
    [InlineData(27, 9, 123456789, "123456789")]
    [InlineData(26, 7, 0, "0000000")]
    [InlineData(27, 9, 100, "000000100")]
    public void LoginDefaultTimestampDisplayPreservesDeclaredColumnPrecision(int type, int scale, int nanoseconds, string fraction)
    {
        bool zone = type == 27;
        byte[] bytes = DmTemporalWireCodec.EncodeExact(new(2026, 7, 23, 14, 15, 16, nanoseconds, zone ? 330 : null),
            DmTemporalWireCodec.ForCType(type), scale);
        using var fixture = new ReaderFixture(type, scale, bytes);
        var property = fixture.Physical.ConnProperty;
        string template = zone ? DmConnProperty.DefaultOracleTimestampTZFormat : DmConnProperty.DefaultOracleTimestampFormat;
        property.SetLoginTimestampFormat(template, zone);
        property.nlsDateLang = 0;
        // Existing YY formatting takes the first two year digits; only fractional display is repaired here.
        string expected = "23-7月-20 02.15.16." + fraction + " 下午" + (zone ? " ++05:30" : "");
        Assert.Equal(expected, fixture.Reader.GetString(0));
        Assert.Equal(template, zone ? property.oracleTimestampTZFormat : property.oracleTimestampFormat);
        var clone = property.Clone();
        Assert.Equal(property.AutomaticTimestampDisplay, clone.AutomaticTimestampDisplay);
        Assert.Equal(property.AutomaticTimestampTZDisplay, clone.AutomaticTimestampTZDisplay);
        ReaderFixture.Set(fixture.Physical, "m_ConnPro", clone);
        Assert.Equal(expected, fixture.Reader.GetString(0));
    }

    [Theory]
    [InlineData(26)] [InlineData(27)]
    public void ExplicitCanonicalTemplateAndLoginCustomTemplateKeepTheirProjection(int type)
    {
        bool zone = type == 27;
        using var fixture = new ReaderFixture(type, 9, DmTemporalWireCodec.EncodeExact(
            new(2026, 7, 23, 14, 15, 16, 123456789, zone ? 330 : null), DmTemporalWireCodec.ForCType(type), 9));
        var property = fixture.Physical.ConnProperty; property.nlsDateLang = 1;
        string canonical = zone ? DmConnProperty.DefaultOracleTimestampTZFormat : DmConnProperty.DefaultOracleTimestampFormat;
        property.SetLoginTimestampFormat(canonical, zone);
        Assert.Contains(".123456789 ", fixture.Reader.GetString(0));
        if (zone) property.oracleTimestampTZFormat = canonical;
        else property.oracleTimestampFormat = canonical;
        Assert.False(zone ? property.AutomaticTimestampTZDisplay : property.AutomaticTimestampDisplay);
        Assert.Equal("23-JUL-20 02.15.16.123456 PM" + (zone ? " ++05:30" : ""), fixture.Reader.GetString(0));
        property.SetLoginTimestampFormat("YYYY/MM/DD HH24:MI:SS.FF3" + (zone ? " TZH:TZM" : ""), zone);
        Assert.False(zone ? property.AutomaticTimestampTZDisplay : property.AutomaticTimestampDisplay);
        Assert.Equal("2026/07/23 14:15:16.123" + (zone ? " +05:30" : ""), fixture.Reader.GetString(0));
    }

    [Theory]
    [InlineData("en-US", "2026-07-23T14:15:16.0000001", "2026-07-23T14:15:16.0000001")]
    [InlineData("en-US", "2026-07-23T14:15:16.000000100", "2026-07-23T14:15:16.0000001")]
    [InlineData("en-US", "2026-07-23T14:15:16.123456700Z", "2026-07-23T14:15:16.1234567Z")]
    [InlineData("en-US", "2026-07-23T14:15:16.12345670+05:30", "2026-07-23T14:15:16.1234567+05:30")]
    [InlineData("en-US", "2026-07-23T14:15:16.1234567-00:30", "2026-07-23T14:15:16.1234567-00:30")]
    [InlineData("en-US", "7/23/2026 2:15:16.1234567 PM", "7/23/2026 2:15:16.1234567 PM")]
    [InlineData("en-GB", "23/07/2026 14:15:16.00000010", "23/07/2026 14:15:16.0000001")]
    [InlineData("de-DE", "23.07.2026 14:15:16.123456700", "23.07.2026 14:15:16.1234567")]
    [InlineData("zh-CN", "2026/7/23 14:15:16.123456", "2026/7/23 14:15:16.123456")]
    [InlineData("fi-FI", "23.7.2026 14.15.16.000000100", "23.7.2026 14.15.16.0000001")]
    [InlineData("en-US", "7/23/2026 2 : 15 : 16.000000100 PM", "7/23/2026 2 : 15 : 16.0000001 PM")]
    [InlineData("en-US", "7/23/2026 14\u200F:15\u200F:16.1234567", "7/23/2026 14\u200F:15\u200F:16.1234567")]
    [InlineData("en-US", "7/23/2026 14\u200F:15\u200F:16.123456700", "7/23/2026 14\u200F:15\u200F:16.1234567")]
    public void CharacterColumnTimestampConversionKeepsAcceptedCultureOffsetAndKind(string culture, string text, string exactText)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var fixture = new ReaderFixture(1, 0, Encoding.UTF8.GetBytes(text));
#if !NET7_0_OR_GREATER
            if (text.Contains('\u200F'))
            {
                // .NET 6 DateTime parsing rejects RLM inside the clock. The driver keeps
                // the runtime's culture verdict after its own fraction normalization.
                Assert.Throws<FormatException>(() => Convert.ToDateTime(exactText));
                Assert.Throws<FormatException>(() => fixture.Reader.GetDateTime(0));
                Assert.Throws<FormatException>(() => DmDateTime.GetTimestampByString(text));
                return;
            }
#endif
            DateTime expected = Convert.ToDateTime(exactText);
            DateTime actual = fixture.Reader.GetDateTime(0);
            Assert.Equal(expected.Ticks, actual.Ticks); Assert.Equal(expected.Kind, actual.Kind);
            // The generic getter retains its typed value contract; character-to-date conversion uses GetDateTime.
            Assert.Throws<InvalidCastException>(() => fixture.Reader.GetFieldValue<DateTime>(0));
            Assert.Equal(actual, DmDateTime.GetTimestampByString(text));
            DateTime dateHelper = DmDateTime.GetDateByString(text);
            Assert.Equal(expected.Ticks, dateHelper.Ticks); Assert.Equal(expected.Kind, dateHelper.Kind);
            var getter = new DmGetValue("UTF-8", fixture.Reader.m_Statement, false, fixture.Columns);
            DateTime dateGetter = getter.GetDate(0, Encoding.UTF8.GetBytes(text), 1, fixture.Columns[0].GetPrecision(), 0);
            Assert.Equal(expected.Ticks, dateGetter.Ticks); Assert.Equal(expected.Kind, dateGetter.Kind);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("2026-07-23T14:15:16.00000001")]
    [InlineData("2026-07-23T14:15:16.000000001Z")]
    [InlineData("2026-07-23T14:15:16.999999999-00:30")]
    [InlineData("7/23/2026 2:15:16.12345678 PM")]
    [InlineData("2026-07-23T00014:015:016.12345678")]
    [InlineData("7/23/2026 14\u200F:15\u200F:16.12345678")]
    [InlineData("7/23/2026 14\u200F:15\u200F:16.123456789")]
    public void CharacterColumnRejectsSubtickFractionsInsteadOfRounding(string text)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            using var fixture = new ReaderFixture(1, 0, Encoding.UTF8.GetBytes(text));
            Assert.Throws<OverflowException>(() => fixture.Reader.GetDateTime(0));
            Assert.Throws<InvalidCastException>(() => fixture.Reader.GetFieldValue<DateTime>(0));
            Assert.Throws<OverflowException>(() => DmDateTime.GetTimestampByString(text));
            Assert.Throws<OverflowException>(() => DmDateTime.GetDateByString(text));
            var getter = new DmGetValue("UTF-8", fixture.Reader.m_Statement, false, fixture.Columns);
            Assert.Throws<OverflowException>(() => getter.GetDate(0, Encoding.UTF8.GetBytes(text), 1, fixture.Columns[0].GetPrecision(), 0));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LeadingZeroClockFieldsKeepTheBclFormatDecisionWithoutFractionRounding()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            const string exact = "2026-07-23T00014:015:016.1234567";
            using var fixture = new ReaderFixture(1, 0, Encoding.UTF8.GetBytes(exact + "00"));
            // The runtime owns acceptance of this numeric shape; the driver's guard owns exact precision.
            if (DateTime.TryParse(exact, out DateTime expected))
            {
                var actual = fixture.Reader.GetDateTime(0);
                Assert.Equal(expected.Ticks, actual.Ticks); Assert.Equal(expected.Kind, actual.Kind);
            }
            else Assert.Throws<FormatException>(() => fixture.Reader.GetDateTime(0));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("2026-07-23T14:15:16.1234567890")]
    [InlineData("2026-07-23T14:15:16.1234567 trailing")]
    [InlineData("invalid timestamp")]
    public void CharacterColumnRejectsMalformedTemporalText(string text)
    {
        using var fixture = new ReaderFixture(1, 0, Encoding.UTF8.GetBytes(text));
        Assert.Throws<FormatException>(() => fixture.Reader.GetDateTime(0));
        Assert.Throws<FormatException>(() => DmDateTime.GetDateByString(text));
    }
}
