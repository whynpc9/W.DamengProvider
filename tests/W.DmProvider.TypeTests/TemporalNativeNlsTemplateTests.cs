using System.Data;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;
using BindingFixture = W.DmProvider.TypeTests.TemporalNativeBindingContractTests.BindingFixture;
using WriterFixture = W.DmProvider.TypeTests.TemporalNativeWriterAdapterTests.WriterFixture;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeNlsTemplateTests
{
    public static IEnumerable<object[]> BindingCases()
    {
        foreach (int language in new[] { 0, 1 })
        foreach (string month in new[] { "MON", "MONTH" })
        foreach (int type in new[] { 14, 15, 16, 22, 23, 26, 27 })
            yield return [language, month, type];
    }

    [Theory]
    [MemberData(nameof(BindingCases))]
    public async Task NegotiatedMonthAndShortYearTemplatesReachTheRealStringParameterBinding(int language, string month, int type)
    {
        var fixture = new BindingFixture();
        int scale = type == 14 ? 0 : type is 26 or 27 ? 7 : 6;
        var layout = DmTemporalWireCodec.ForCType(type);
        await fixture.Describe([new(type, DmTemporalWireCodec.GetLength(layout), scale, 1)]);
        var property = fixture.Physical.ConnProperty;
        property.nlsDateLang = language;
        string calendar = "DD-" + month + "-YY";
        string clock = "HH12.MI.SS.FF" + scale + " AM";
        bool time = type is 15 or 22, zone = type is 22 or 23 or 27;
        string pattern = type == 14 ? calendar : (time ? "" : calendar + " ") + clock + (zone ? " +TZH:TZM" : "");
        SetTemplate(property, type, pattern);
        string monthText = language == 0 ? "7月" : month == "MON" ? "JUL" : "July";
        string text = type == 14 ? "23-" + monthText + "-26" :
            (time ? "" : "23-" + monthText + "-26 ") + "02.15.16." + (scale == 7 ? "0000001" : "123456")
            + (language == 0 ? " 下午" : " PM") + (zone ? " +08:00" : "");
        DbType dbType = type switch
        {
            14 => DbType.Date, 15 => DbType.Time, 22 or 23 or 27 => DbType.DateTimeOffset,
            26 => DbType.DateTime2, _ => DbType.DateTime
        };
        var parameter = new DmParameter("p", text) { Scale = (byte)scale };
        if (type == 22) parameter.DmSqlType = DmDbType.TimeOffset;
        else parameter.DbType = dbType;
        Assert.Equal(text, DmSysTypeConvertion.TypeConvertion(parameter));
        using var plan = fixture.Bind("SELECT :p FROM DUAL", parameter);
        Assert.True(Assert.Single(plan.TemporalBindings).ReliableFixed);
        var fields = DmTemporalWireCodec.DecodeRaw(fixture.Fields[0].GetInValue(0), layout, scale);
        // YY keeps the existing current-century rule; parsing remains controlled by the negotiated NLS language.
        Assert.Equal(new DmTemporalFields(time ? 0 : DateTime.Today.Year / 100 * 100 + 26, time ? 0 : 7, time ? 0 : 23,
            type == 14 ? 0 : 14, type == 14 ? 0 : 15, type == 14 ? 0 : 16,
            scale == 7 ? 100 : scale == 6 ? 123456000 : 0, zone ? 480 : null), fields);
    }

    [Theory]
    [InlineData("MON-YY", "JUL-26", 14)]
    [InlineData("DD-YY", "23-26", 14)]
    [InlineData("DD-MON", "23-JUL", 14)]
    [InlineData("DD-MON-YY HH12.MI AM", "23-JUL-26 02.15 PM", 26)]
    [InlineData("DD-MON-YY HH12.MI.SS.FF7 AM +TZH", "23-JUL-26 02.15.16.0000001 PM +08", 27)]
    public void RecognizedButIncompleteTemplatesRemainUnavailable(string pattern, string text, int type)
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        fixture.Physical.ConnProperty.nlsDateLang = 1;
        SetTemplate(fixture.Physical.ConnProperty, type, pattern);
        var field = fixture.Parameter(type, type == 14 ? 0 : 7, fixedSource: true);
        var output = new DmParamValue();
        Assert.Throws<NotSupportedException>(() => fixture.WriteInto(output, text, field));
        Assert.False(output.GetInDataBound()); Assert.Null(output.m_InValue);
    }

    [Fact]
    public void MonthNameTemplatesRetainActualFieldTrailingLanguageAndPrecisionChecks()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var property = fixture.Physical.ConnProperty; property.nlsDateLang = 1;
        property.oracleDateFormat = "DD-MON-YY";
        Assert.Throws<FormatException>(() => fixture.Write("23-JUL", fixture.Parameter(14, 0, fixedSource: true)));
        Assert.Throws<FormatException>(() => fixture.Write("23-JUL-26 trailing", fixture.Parameter(14, 0, fixedSource: true)));
        property.oracleTimestampFormat = "DD-MON-YY HH12.MI.SS.FF6 AM";
        var six = fixture.Parameter(26, 6, fixedSource: true);
        Assert.Throws<FormatException>(() => fixture.Write("23-JUL-26 02.15", six));
        Assert.Throws<DmException>(() => fixture.Write("23-JUL-26 02.15.16.1234567 PM", six));
        Assert.Throws<DmException>(() => fixture.Write("23-7月-26 02.15.16.123456 下午", six));
        property.nlsDateLang = 0;
        Assert.Throws<DmException>(() => fixture.Write("23-JUL-26 02.15.16.123456 PM", six));
        property.nlsDateLang = 1;
        property.oracleTimestampFormat = "DD-MONTH-YY HH12.MI.SS.FF7 AM";
        Assert.Throws<OverflowException>(() => fixture.Write("23-July-26 02.15.16.0000001 PM", six));
        property.oracleTimestampTZFormat = "DD-MON-YY HH12.MI.SS.FF7 AM +TZH:TZM";
        Assert.Throws<FormatException>(() => fixture.Write("23-JUL-26 02.15.16.0000001 PM", fixture.Parameter(27, 7, fixedSource: true)));
        // A template without a literal '+' leaves the offset sign for TZH's existing lexical parser.
        property.oracleTimestampTZFormat = "DD-MON-YY HH12.MI.SS.FF7 AM TZH:TZM";
        var negative = DmTemporalWireCodec.DecodeRaw(fixture.Write("23-JUL-26 02.15.16.0000001 PM -00:30",
            fixture.Parameter(27, 7, fixedSource: true)), DmTemporalWireLayout.TimestampTz11, 7);
        Assert.Equal(-30, negative.OffsetMinutes); Assert.Equal(100, negative.NanosecondsWithinSecond);
        property.oracleDateFormat = "DD-RR-YY";
        Assert.Equal(6015, Assert.Throws<DmException>(() => fixture.Write("23-JUL-26", fixture.Parameter(14, 0, fixedSource: true))).Number);
    }

    private static void SetTemplate(DmConnProperty property, int type, string pattern)
    {
        switch (type)
        {
            case 14: property.oracleDateFormat = pattern; break;
            case 15: property.oracleTimeFormat = pattern; break;
            case 22: property.oracleTimeTZFormat = pattern; break;
            case 16 or 26: property.oracleTimestampFormat = pattern; break;
            case 23 or 27: property.oracleTimestampTZFormat = pattern; break;
        }
    }
}
