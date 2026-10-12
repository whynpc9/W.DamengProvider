using System;
using System.Globalization;
using System.Text.RegularExpressions;
using W.Dm.Internal.Types;
using W.Dm.util;

namespace W.Dm;

public class DmDateTime
{
	private static int[] global_day_by_month = new int[13]
	{
		31, 31, 28, 31, 30, 31, 30, 31, 31, 30,
		31, 30, 31
	};

	private static int[] global_days_befor_month = new int[12]
	{
		0, 31, 59, 90, 120, 151, 181, 212, 243, 273,
		304, 334
	};

	private const int DMDT_YEAR_POS = 0;

	private const int DMDT_YEAR_LEN = 15;

	private const int DMDT_MONTH_POS = 15;

	private const int DMDT_MONTH_LEN = 4;

	private const int DMDT_DAY_POS = 19;

	private const int DMDT_DAY_LEN = 5;

	private const int DMDT_DATE_SIZE = 24;

	private const int DMDT_HOUR_POS = 0;

	private const int DMDT_HOUR_LEN = 5;

	private const int DMDT_MIN_POS = 5;

	private const int DMDT_MIN_LEN = 6;

	private const int DMDT_SEC_POS = 11;

	private const int DMDT_SEC_LEN = 6;

	private const int DMDT_MSEC_POS = 17;

	private const int DMDT_MSEC_LEN = 20;

	private const int DMDT_TZ_POS = 37;

	private const int DMDT_TZ_LEN = 15;

	private const int LOCAL_TIME_ZONE_MASK = 4096;

	private byte[] dateTime;

	public const int DMDT_DATE = 0;

	public const int DMDT_TIME = 1;

	public const int DMDT_TIMESTAMP = 2;

	public const int DMDT_TIMESTAMP_TZ = 3;

	public const int DMDT_TIME_TZ = 4;

	private int prec;

	public const int MAX_YEAR = 9999;

	public const int MIN_YEAR = -4712;

	internal const int OFFSET_YEAR = 0;

	internal const int OFFSET_MONTH = 1;

	internal const int OFFSET_DAY = 2;

	internal const int OFFSET_HOUR = 3;

	internal const int OFFSET_MINUTE = 4;

	internal const int OFFSET_SECOND = 5;

	internal const int OFFSET_NANOSECOND = 6;

	internal const int OFFSET_TIMEZONE = 7;

	internal const int DT_LEN = 8;

	public const int INVALID_VALUE = int.MinValue;

	public const int NANOSECOND_DIGITS = 9;

	public const string ZEROS = "000000000";

	public int[] dt;

	public int dtype;

	public int scale;

	public static int LTZ_GET_REAL_PREC(int prec)
	{
		return prec & -4097;
	}

	public static bool NTYPE_IS_LOCAL_TIME_ZONE(int sql_pl_type, int prec)
	{
		if (sql_pl_type == 16 && (prec & 0x1000) != 0)
		{
			return true;
		}
		return false;
	}

	public static void DmTimeGetDateByNdaysFromZero(int ndays, ref int ret_year, ref int ret_month, ref int ret_day)
	{
		if (ndays <= 365 || ndays > 3652424) { ret_year = ret_month = ret_day = 99; return; }
		DateOnly date = DateOnly.FromDayNumber(ndays - 366);
		ret_year = date.Year; ret_month = date.Month; ret_day = date.Day;
	}

	public static int DmtimeCalcNDaysFromZero(int year, int month, int day)
	{
		var date = DmTemporalInteger.ProjectDateOnly(new DmTemporalFields(year, month, day, 0, 0, 0, 0));
		return checked(date.DayNumber + 366);
	}

	public static void DmTimeDdateAdd(ref int year, ref int month, ref int day, int n)
	{
		int[] values = { year, month, day, 0, 0, 0, 0, int.MinValue };
		DmTemporalLegacyAdapter.AddClock(values, n, 1);
		year = values[0]; month = values[1]; day = values[2];
	}

	public static DateTime DmtimeAddByFmt(int prec, DmDateTime dt_new, int fmt, int n)
	{
		DateTime value = dt_new.GetTimestamp();
		try
		{
			return fmt switch
			{
				1 => value.AddYears(n), 2 => value.AddMonths(n), 8 => value.AddMonths(checked(n * 3)),
				3 or 9 => value.AddTicks(checked((long)n * TimeSpan.TicksPerDay)),
				10 => value.AddTicks(checked((long)n * 7 * TimeSpan.TicksPerDay)),
				4 => value.AddTicks(checked((long)n * TimeSpan.TicksPerHour)),
				5 => value.AddTicks(checked((long)n * TimeSpan.TicksPerMinute)),
				6 => value.AddTicks(checked((long)n * TimeSpan.TicksPerSecond)),
				7 => value.AddTicks(checked((long)n * TimeSpan.TicksPerMillisecond)),
				_ => throw new NotSupportedException("Temporal arithmetic format is unsupported.")
			};
		}
		catch (ArgumentOutOfRangeException) { throw new OverflowException("Temporal arithmetic exceeds calendar range."); }
	}

	public static bool DmTimeIsLeapYear(int year)
	{
		if (year < 0)
		{
			year = -year;
		}
		if (year % 4 == 0)
		{
			if (year % 100 == 0 && year % 400 != 0)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool DmTimeDataValidate(int year, int month, int day)
	{
		if (year > 9999 || year < 1 || month > 12 || month < 1)
		{
			return true;
		}
		switch (month)
		{
		case 1:
		case 3:
		case 5:
		case 7:
		case 8:
		case 10:
		case 12:
			if (day > 31 || day < 1)
			{
				return true;
			}
			break;
		case 4:
		case 6:
		case 9:
		case 11:
			if (day > 30 || day < 1)
			{
				return true;
			}
			break;
		case 2:
			if (DmTimeIsLeapYear(year))
			{
				if (day > 29 || day < 1)
				{
					return true;
				}
			}
			else if (day > 28 || day < 1)
			{
				return true;
			}
			break;
		}
		return false;
	}

	public static bool DmTimeTzValidate(short tz)
	{
		if (tz < -779 || (tz > 780 && tz != 1000))
		{
			return true;
		}
		return false;
	}

	public static bool DmTimeTimeValidate(int hour, int min, int sec, int msec)
	{
		if (hour > 23 || min > 59 || sec > 59 || msec > 9999999)
		{
			return true;
		}
		return false;
	}

	public static DateTime DmTimeEncodeTz(int prec, int year, int month, int day, int hour, int min, int sec, int msec, short tz)
	{
		if (msec is < 0 or >= 10_000_000) throw new OverflowException("Temporal fraction exceeds tick range.");
		var fields = new DmTemporalFields(year, month, day, hour, min, sec, checked(msec * 100), tz);
		DmTemporalInteger.ProjectDateTimeOffset(fields);
		return DmTemporalInteger.ProjectDateTime(fields);
	}

	public DmDateTime()
	{
		dt = new int[8];
		dtype = 16;
		scale = 6;
	}

	public DmDateTime(byte[] dt, int prec)
	{
		DmTemporalLegacyAdapter.ReadHelper(dt);
		dateTime = dt;
		this.prec = prec;
	}

	public DmDateTime(string str, int prec, int flag)
	{
		this.prec = prec;
		var parsed = DmTemporalLegacyAdapter.Parse(str);
		var kind = flag switch { 0 => DmTemporalTextKind.Date, 2 or 3 => DmTemporalTextKind.Timestamp,
			4 => DmTemporalTextKind.Time, _ => throw new NotSupportedException("Temporal constructor flag is unsupported.") };
		var fields = parsed.Fields;
		if (kind == DmTemporalTextKind.Timestamp && parsed.Type is not (16 or 23))
			throw new InvalidCastException("TIMESTAMP constructor requires calendar and clock text.");
		if (kind == DmTemporalTextKind.Time)
		{
			if (parsed.Type != 22) throw new InvalidCastException("TIME constructor requires time-only text with an offset.");
			fields = fields with { Year = 0, Month = 0, Day = 0 };
		}
		if (flag is 3 or 4 && !fields.OffsetMinutes.HasValue) throw new InvalidCastException("Temporal text requires an offset.");
		if (flag is 0 or 2 && fields.OffsetMinutes.HasValue) throw new InvalidCastException("Temporal text has an unexpected offset.");
		dateTime = DmTemporalWireCodec.ToLegacyMicroseconds(fields, kind);
	}

	public DmDateTime(DateTime dt, int prec, int scale, int flag, short mTimeZone)
	{
		DmTemporalInteger.ValidateUserScale(scale);
		if (scale > 6) throw new NotSupportedException("Legacy constructor has microsecond precision.");
		DmTemporalInteger.ValidateFractionalTicks(dt.Ticks, scale);
		this.prec = prec;
		var kind = flag switch { 0 => DmTemporalTextKind.Date, 2 or 3 => DmTemporalTextKind.Timestamp,
			_ => throw new NotSupportedException("Temporal constructor flag is unsupported.") };
		var fields = DmTemporalInteger.FromDateTime(dt, flag == 3 ? mTimeZone : null);
		dateTime = DmTemporalWireCodec.ToLegacyMicroseconds(fields, kind);
	}

	internal static int GetMicroSecond(DateTime dt)
	{
		DmTemporalInteger.ValidateFractionalTicks(dt.Ticks, 6);
		return (int)(dt.Ticks % TimeSpan.TicksPerSecond / 10);
	}

	internal static int GetNanoSecond(DateTime dt)
	{
		return checked((int)(dt.Ticks % TimeSpan.TicksPerSecond * 100));
	}

	public byte[] GetByteArrayValue()
	{
		if (dateTime == null) throw new InvalidOperationException("This operation requires byte representation.");
		return dateTime;
	}

	public void GetByteArrayValue(ref byte[] ret)
	{
		ret = GetByteArrayValue();
	}

	private DmTemporalFields ReadByteFields() => DmTemporalLegacyAdapter.ReadHelper(dateTime);

	internal static DmTemporalFields DecodeForReader(byte[] bytes, DmField column, int ltz = 0, int dbtz = 0) =>
		DmTemporalLegacyAdapter.ReadNetwork(bytes, column);

	internal static DmTemporalFields DecodeForReader(byte[] bytes, DmTemporalReadDescriptor descriptor) =>
		DmTemporalLegacyAdapter.ReadNetwork(bytes, descriptor);

	internal byte[] EncodeWithTarget(DmField column, DmConnection connection, DmTemporalWriteTarget target)
		=> EncodeWithTarget(DmTemporalLegacyAdapter.Profile(connection), target);

	internal byte[] EncodeWithTarget(DmTemporalCapabilityProfile profile, DmTemporalWriteTarget target)
	{
		profile.ValidateWriteTarget(target);
		var fields = DmTemporalLegacyAdapter.FromSemantic(dt, dtype, scale);
		if (target.CType is 15 or 22 && dtype is not (15 or 22))
			throw new OverflowException("TIME cannot represent calendar fields from temporal text.");
		if (target.CType is not (22 or 23 or 27))
		{
			if (fields.OffsetMinutes.HasValue) throw new InvalidCastException("Timezone fields cannot be written as an ordinary temporal value.");
			fields = fields with { OffsetMinutes = null };
		}
		profile.ValidateWriteValue(target, fields);
		return DmTemporalWireCodec.EncodeExact(fields, target.Layout, target.EncodedScale);
	}

	private short GetYear()
	{
		return (short)ReadByteFields().Year;
	}

	private byte GetMonth()
	{
		return (byte)ReadByteFields().Month;
	}

	private byte GetDay()
	{
		return (byte)ReadByteFields().Day;
	}

	private byte GetHour(int offset)
	{
		return (byte)ReadByteFields().Hour;
	}

	private byte GetMinute(int offset)
	{
		return (byte)ReadByteFields().Minute;
	}

	private byte GetSecond(int offset)
	{
		return (byte)ReadByteFields().Second;
	}

	private short GetTZ(int offset)
	{
		return checked((short)(ReadByteFields().OffsetMinutes ?? throw new InvalidCastException("Temporal value has no offset.")));
	}

	private int GetNano(int offset)
	{
		return DmTemporalInteger.FractionalTicksExact(ReadByteFields().NanosecondsWithinSecond);
	}

	public DateTime GetDate()
	{
		return DmTemporalInteger.ProjectDateOnly(ReadByteFields()).ToDateTime(TimeOnly.MinValue);
	}

	public DateTime GetTime()
	{
		return DmTemporalInteger.ProjectTimeDateTime(ReadByteFields());
	}

	public DateTimeOffset GetTimeTZ()
	{
		return DmTemporalInteger.ProjectTimeOffset(ReadByteFields());
	}

	public string GetDateInString()
	{
		return DmTemporalInteger.FormatExact(ReadByteFields() with { Hour = 0, Minute = 0, Second = 0, NanosecondsWithinSecond = 0, OffsetMinutes = null }, 0, DmTemporalTextKind.Date);
	}

	public string GetDateInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		return OracleDateFormat.Format(new int[10] { year, month, day, 0, 0, 0, 0, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetDateTimeInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		int nano = ReadByteFields().NanosecondsWithinSecond;
		return OracleDateFormat.Format(new int[10] { year, month, day, hour, minute, second, nano, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetDateTimeTzInString()
	{
		return GetDateInString() + " " + GetTimeTZInString();
	}

	public string GetDateTimeTzInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int year = GetYear();
		int month = GetMonth();
		int day = GetDay();
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		int nano = ReadByteFields().NanosecondsWithinSecond;
		int tZ = GetTZ(24);
		int[] array = new int[10];
		array[0] = year;
		array[1] = month;
		array[2] = day;
		array[3] = hour;
		array[4] = minute;
		array[5] = second;
		array[6] = nano;
		array[OracleDateFormat.OFFSET_TIMEZONE_SIGN] = ((tZ < 0) ? 1 : 0);
		array[7] = tZ;
		return OracleDateFormat.Format(array, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeInString()
	{
		var fields = ReadByteFields() with { OffsetMinutes = null };
		DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, 7);
		return DmTemporalInteger.FormatExact(prec == 0 ? fields with { NanosecondsWithinSecond = 0 } : fields, prec == 0 ? 0 : 7, DmTemporalTextKind.Time);
	}

	public string GetTimeInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = ReadByteFields().NanosecondsWithinSecond;
		return OracleDateFormat.Format(new int[10] { 0, 0, 0, hour, minute, second, nano, 0, 0, 0 }, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeTZInString()
	{
		var fields = ReadByteFields();
		if (!fields.OffsetMinutes.HasValue) throw new InvalidCastException("Temporal value has no offset.");
		DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, 7);
		return DmTemporalInteger.FormatExact(prec == 0 ? fields with { NanosecondsWithinSecond = 0 } : fields, prec == 0 ? 0 : 7, DmTemporalTextKind.Time);
	}

	public string GetTimeTZInString(string oraclefmt, int datelang, string formatNumericChars)
	{
		int hour = GetHour(0);
		int minute = GetMinute(0);
		int second = GetSecond(0);
		int nano = ReadByteFields().NanosecondsWithinSecond;
		int tZ = GetTZ(0);
		int[] array = new int[10];
		array[3] = hour;
		array[4] = minute;
		array[5] = second;
		array[6] = nano;
		array[OracleDateFormat.OFFSET_TIMEZONE_SIGN] = ((tZ < 0) ? 1 : 0);
		array[7] = tZ;
		return OracleDateFormat.Format(array, oraclefmt, datelang, formatNumericChars);
	}

	public string GetTimeOfTimestamp()
	{
		int hour = GetHour(24);
		int minute = GetMinute(24);
		int second = GetSecond(24);
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		return text + ":" + text2 + ":" + text3;
	}

	public DateTime GetTimestamp()
	{
		return DmTemporalInteger.ProjectDateTime(ReadByteFields());
	}

	public DateTimeOffset GetTimestampTZ()
	{
		return DmTemporalInteger.ProjectDateTimeOffset(ReadByteFields());
	}

	private static DateTime BuildDateTime(int year, int month, int day, int hour, int minute, int second, int fractionalTicks)
	{
		if (fractionalTicks < 0 || fractionalTicks >= TimeSpan.TicksPerSecond)
			throw new OverflowException("Temporal fraction cannot be represented by .NET ticks.");
		try { return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionalTicks); }
		catch (ArgumentOutOfRangeException) { throw new OverflowException("Server temporal value is outside the .NET range."); }
	}

	private static DateTimeOffset BuildOffset(int year, int month, int day, int hour, int minute, int second, int fractionalTicks, short offsetMinutes)
	{
		DateTime local = BuildDateTime(year, month, day, hour, minute, second, fractionalTicks);
		try { return new DateTimeOffset(local, TimeSpan.FromTicks(offsetMinutes * TimeSpan.TicksPerMinute)); }
		catch (ArgumentOutOfRangeException) { throw new OverflowException("Server timezone offset is outside the .NET range."); }
	}

	public static DateTime GetDateByString(string s)
	{
		return DmTemporalInteger.ProjectDateOnly(DmTemporalLegacyAdapter.Parse(s).Fields).ToDateTime(TimeOnly.MinValue);
	}

	public static bool DmdtIsLeapYear(int year)
	{
		if (year < 0)
		{
			year = -year;
		}
		if (year % 4 == 0)
		{
			if (year % 100 == 0 && year % 400 != 0)
			{
				return false;
			}
			return true;
		}
		return false;
	}

	public static bool CheckDate(int year, int month, int day)
	{
		if (year == 0 && month == 0 && day == 0)
		{
			return false;
		}
		if (year > 10000 || year < -10000 || month > 12 || month < 1)
		{
			return false;
		}
		switch (month)
		{
		case 1:
		case 3:
		case 5:
		case 7:
		case 8:
		case 10:
		case 12:
			if (day > 31 || day < 1)
			{
				return false;
			}
			break;
		case 4:
		case 6:
		case 9:
		case 11:
			if (day > 30 || day < 1)
			{
				return false;
			}
			break;
		case 2:
			if (DmdtIsLeapYear(year))
			{
				if (day > 29 || day < 1)
				{
					return false;
				}
			}
			else if (day > 28 || day < 1)
			{
				return false;
			}
			break;
		}
		return true;
	}

	public static DateTime GetTimestampByString(string s)
	{
		// Keep the historical culture/offset/Kind conversion, but never let the BCL round sub-tick fractions.
		if (s != null)
		{
			string separator = Regex.Escape(CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator);
			string clockSeparator = "(?:[:]|" + separator + ")";
			var fraction = Regex.Match(s, @"(?<![0-9])[0-9]+[\s\u200F]*" + clockSeparator + @"[\s\u200F]*[0-9]+[\s\u200F]*"
				+ clockSeparator + @"[\s\u200F]*[0-9]+[.,](?<fraction>[0-9]+)").Groups["fraction"];
			if (fraction.Success)
			{
				if (fraction.Length > 9) throw new FormatException("Temporal fraction has unsupported precision.");
				if (fraction.Length > 7)
				{
					foreach (char digit in fraction.Value.AsSpan(7))
						if (digit != '0') throw new OverflowException("Temporal fraction is outside .NET tick precision.");
					s = s.Remove(fraction.Index + 7, fraction.Length - 7);
				}
			}
		}
		return Convert.ToDateTime(s);
	}

	public static string GetTimeFromTimestamp(DateTime ts)
	{
		int hour = ts.Hour;
		int minute = ts.Minute;
		int second = ts.Second;
		int millisecond = ts.Millisecond;
		string text = hour.ToString() ?? "";
		string text2 = minute.ToString() ?? "";
		string text3 = second.ToString() ?? "";
		if (text.Length < 2)
		{
			text = "0" + text;
		}
		if (text2.Length < 2)
		{
			text2 = "0" + text2;
		}
		if (text3.Length < 2)
		{
			text3 = "0" + text3;
		}
		return text + ":" + text2 + ":" + text3 + "." + millisecond;
	}

	private int GetBit(byte[] x, int start, int len)
	{
		int num = 0;
		int num2 = start & 7;
		int num3 = start >> 3;
		int num4 = (start + len - 1 >> 3) + 1 - num3;
		int num5 = start >> 3;
		switch (num4)
		{
		case 1:
		{
			byte[] array3 = new byte[4];
			Array.Copy(x, num5, array3, 0, 1);
			num = DmConvertion.FourByteToInt(array3);
			break;
		}
		case 2:
		{
			byte[] array2 = new byte[4];
			Array.Copy(x, num5, array2, 0, 1);
			int num9 = DmConvertion.FourByteToInt(array2);
			Array.Copy(x, num5 + 1, array2, 0, 1);
			int num10 = DmConvertion.FourByteToInt(array2);
			num10 = (int)((num10 << 8) & 0xFFFFFFFFu);
			num = num9 + num10;
			break;
		}
		case 3:
		{
			byte[] array = new byte[4];
			Array.Copy(x, num5, array, 0, 1);
			int num6 = DmConvertion.FourByteToInt(array);
			Array.Copy(x, num5 + 1, array, 0, 1);
			int num7 = DmConvertion.FourByteToInt(array);
			num7 = (int)((num7 << 8) & 0xFFFFFFFFu);
			Array.Copy(x, num5 + 2, array, 0, 1);
			int num8 = DmConvertion.FourByteToInt(array);
			num8 = (int)((num8 << 16) & 0xFFFFFFFFu);
			num = num6 + num7 + num8;
			break;
		}
		}
		return (int)((num >> num2) & (uint)(-1 >>> 32 - len));
	}

	public static byte[] DmDateDecodeFast(byte[] val)
	{
		if (val == null || val.Length is not (3 or 12)) throw new ArgumentException("DATE decoder requires an explicit date layout.");
		var fields = DmTemporalLegacyAdapter.ReadHelper(val, DmTemporalTextKind.Date) with { OffsetMinutes = null };
		return DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Date);
	}

	public static byte[] DmTimeDecodeFast(byte[] val)
	{
		if (val == null || val.Length is not (5 or 7 or 12)) throw new ArgumentException("TIME helper requires an explicit time layout.");
		return DmTemporalWireCodec.ToLegacyMicroseconds(DmTemporalLegacyAdapter.ReadHelper(val, DmTemporalTextKind.Time), DmTemporalTextKind.Time);
	}

	public static byte[] DmDtDecodeFast(byte[] value)
	{
		var fields = DmTemporalLegacyAdapter.ReadHelper(value);
		var kind = value.Length is 5 or 7 || fields.Year == 0 ? DmTemporalTextKind.Time : value.Length == 3 ? DmTemporalTextKind.Date : DmTemporalTextKind.Timestamp;
		if (kind == DmTemporalTextKind.Date) fields = fields with { OffsetMinutes = null };
		return DmTemporalWireCodec.ToLegacyMicroseconds(fields, kind);
	}

	private static byte[] NormalizeRawMicroseconds(byte[] raw)
	{
		DmTemporalLegacyAdapter.ReadHelper(raw);
		return (byte[])raw.Clone();
	}

	public static byte[] DmTimeFromRec4(byte[] dateTime, int CType)
	{
		var fields = DmTemporalLegacyAdapter.ReadHelper(dateTime, DmTemporalLegacyAdapter.Kind(CType));
		if (dateTime.Length != 12 && DmTemporalLegacyAdapter.PackedLayout(dateTime.Length) != DmTemporalWireCodec.ForCType(CType))
			throw new ArgumentException("Temporal helper type and layout do not match.");
		if (CType is not (22 or 23 or 27)) fields = fields with { OffsetMinutes = null };
		return DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalLegacyAdapter.Kind(CType));
	}

	public static byte[] DmdtEncodeFast(byte[] dateTime)
	{
		var fields = DmTemporalLegacyAdapter.ReadHelper(dateTime, DmTemporalTextKind.Timestamp) with { OffsetMinutes = null };
		if (dateTime.Length == 8) { DmTemporalWireCodec.ValidateFields(fields, DmTemporalWireLayout.Timestamp8, 6); return dateTime; }
		if (dateTime.Length != 12) throw new ArgumentException("Legacy timestamp encoder requires 12 or 8 bytes.");
		return DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.Timestamp8, 6);
	}

	public static byte[] DmdtEncodeFast(int[] dt)
	{
		return DmTemporalWireCodec.EncodeExact(DmTemporalLegacyAdapter.FromMicroseconds(dt, 16), DmTemporalWireLayout.Timestamp8, 6);
	}

	public static void DmdtEncodeFast(ref byte[] ret, ref byte[] dateTime)
	{
		ret = DmdtEncodeFast(dateTime);
	}

	public static void DmdtEncodeFast2(ref byte[] ret, DateTime x)
	{
		ret = DmTemporalWireCodec.EncodeExact(DmTemporalInteger.FromDateTime(x), DmTemporalWireLayout.Timestamp9, 7);
	}

	public static void Dmdt2TzEncodeFast2(ref byte[] ret, DateTime x, short timezone)
	{
		ret = DmTemporalWireCodec.EncodeExact(DmTemporalInteger.FromDateTime(x, timezone), DmTemporalWireLayout.TimestampTz11, 7);
	}

	public static byte[] DmdttzEncodeFast(byte[] dateTime)
	{
		if (dateTime == null || dateTime.Length is not (10 or 12)) throw new ArgumentException("Legacy timezone timestamp encoder requires 12 or 10 bytes.");
		return DmTemporalWireCodec.EncodeExact(DmTemporalLegacyAdapter.ReadHelper(dateTime, DmTemporalTextKind.Timestamp), DmTemporalWireLayout.TimestampTz10, 6);
	}

	public static byte[] DmdttzEncodeFast(int[] dt)
	{
		return DmTemporalWireCodec.EncodeExact(DmTemporalLegacyAdapter.FromMicroseconds(dt, 23), DmTemporalWireLayout.TimestampTz10, 6);
	}

	public static byte[] DateEncodeFast(byte[] dateTime)
	{
		if (dateTime == null || dateTime.Length is not (3 or 12)) throw new ArgumentException("DATE encoder requires 12 or 3 bytes.");
		var fields = DmTemporalLegacyAdapter.ReadHelper(dateTime, DmTemporalTextKind.Date) with { OffsetMinutes = null };
		DmTemporalWireCodec.ValidateFields(fields, DmTemporalWireLayout.Date3, 0);
		return dateTime.Length == 3 ? dateTime : DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireLayout.Date3, 0);
	}

	public static bool RevertToGlgl(short year, byte month, byte day)
	{
		if (year == 1582 && month == 10 && 5 <= day)
		{
			return day <= 14;
		}
		return false;
	}

	public static byte[] DateEncodeFast(int[] dt)
	{
		return DmTemporalWireCodec.EncodeExact(DmTemporalLegacyAdapter.FromMicroseconds(dt, 14), DmTemporalWireLayout.Date3, 0);
	}

	internal static DmDateTime valueOf(byte[] bytes, DmField column, DmConnection connection)
	{
		DmDateTime dmDateTime = new DmDateTime();
		dmDateTime.decode(bytes, column, connection.GetConnInstance().ConnProperty.TimeZone, connection.GetConnInstance().ConnProperty.DbTimeZone);
		return dmDateTime;
	}

	public static DmDateTime valueOf(string str)
	{
		DmDateTime dmDateTime = new DmDateTime();
		dmDateTime.parseString(str);
		return dmDateTime;
	}

	internal static DmDateTime valueOf(string str, DmField column, DmConnection connection)
	{
		DmDateTime dmDateTime = new DmDateTime();
		string format = connection.getFormat(column);
		if (StringUtil.isNotEmpty(format))
		{
			dmDateTime.dt = OracleDateFormat.ParseTemporal(str, format, connection.GetConnInstance().ConnProperty.nlsDateLang, column.type);
			dmDateTime.dtype = column.type;
			dmDateTime.scale = 9;
			return dmDateTime;
		}
		dmDateTime.parseString(str);
		return dmDateTime;
	}

	// The statement's format/language remain parsing context; type and write decisions
	// come from the binding rather than a mutable parameter field.
	internal static DmDateTime valueOf(string str, int cType, DmConnection connection)
	{
		var property = connection?.GetConnInstance()?.ConnProperty;
		string format = cType switch
		{
			14 => property?.oracleDateFormat, 15 => property?.oracleTimeFormat,
			22 => property?.oracleTimeTZFormat, 16 or 26 => property?.oracleTimestampFormat,
			23 or 27 => property?.oracleTimestampTZFormat, _ => null
		};
		if (!StringUtil.isNotEmpty(format)) return valueOf(str);
		return new DmDateTime
		{
			dt = OracleDateFormat.ParseTemporal(str, format, property.nlsDateLang, cType),
			dtype = cType, scale = 9
		};
	}

	private string toString(int dtype, int scale)
	{
		var fields = DmTemporalLegacyAdapter.FromSemantic(dt, this.dtype, this.scale);
		var kind = DmTemporalLegacyAdapter.Kind(dtype);
		if (kind == DmTemporalTextKind.Date) fields = fields with { Hour = 0, Minute = 0, Second = 0, NanosecondsWithinSecond = 0, OffsetMinutes = null };
		if (dtype is not (22 or 23 or 27)) fields = fields with { OffsetMinutes = null };
		return DmTemporalInteger.FormatExact(fields, DmTemporalLegacyAdapter.SemanticScale(dtype, scale), kind);
	}

	internal string toString(DmField column, DmConnection connection)
	{
		DmTemporalLegacyAdapter.FromSemantic(dt, dtype, scale);
		string format = connection.getFormat(column);
		if (StringUtil.isNotEmpty(format))
		{
			return OracleDateFormat.Format(dt, format, connection.GetConnInstance().ConnProperty.nlsDateLang, connection.GetConnInstance().ConnProperty.formatNumericChars);
		}
		int num = ((column.mask == 1) ? 14 : column.type);
		return toString(num, column.scale);
	}

	internal DateTime toDate(DmField column, DmConnection connection)
	{
		return DmTemporalInteger.ProjectDateOnly(DmTemporalLegacyAdapter.FromSemantic(dt, dtype, scale)).ToDateTime(TimeOnly.MinValue);
	}

	internal DateTime toTime(DmField column, DmConnection connection)
	{
		return DmTemporalInteger.ProjectTimeDateTime(DmTemporalLegacyAdapter.FromSemantic(dt, dtype, scale));
	}

	internal DateTime toTimestamp(DmField column, DmConnection connection)
	{
		return DmTemporalInteger.ProjectDateTime(DmTemporalLegacyAdapter.FromSemantic(dt, dtype, scale));
	}

	internal int[] decode(byte[] value, DmField column, int ltz, int dbtz)
	{
		var fields = DecodeForReader(value, column, ltz, dbtz);
		dt = DmTemporalLegacyAdapter.ToSemantic(fields);
		dtype = column.type; scale = DmTemporalLegacyAdapter.SemanticScale(column.type, column.scale);
		return dt;
	}

	private static int[] dmdtDecodeFast(byte[] value)
	{
		return DmTemporalLegacyAdapter.ToSemantic(DmTemporalLegacyAdapter.ReadHelper(value));
	}

	private static int[] dmdtDecodeBdta(byte[] value)
	{
		throw new NotSupportedException("Compact temporal decoding requires explicit column metadata.");
	}

	internal byte[] encode(DmField column, DmConnection connection)
	{
		int targetType = column?.type ?? dtype;
		int targetScale = DmTemporalLegacyAdapter.SemanticScale(targetType, column?.scale ?? scale);
		var target = DmTemporalLegacyAdapter.Target(column, targetType, targetScale);
		return EncodeWithTarget(column, connection, target);
	}

	public int parseString(string s)
	{
		var parsed = DmTemporalLegacyAdapter.Parse(s);
		int[] values = DmTemporalLegacyAdapter.ToSemantic(parsed.Fields);
		dt = values; dtype = parsed.Type; scale = parsed.Scale;
		return dtype;
	}

	public static int[] transformTZ(int[] dt, int defaultSrcTz, int destTz)
	{
		if (dt == null || dt.Length < 8) throw new InvalidOperationException("Timezone arithmetic requires semantic fields.");
		DmTemporalLegacyAdapter.FromSemantic(dt, 16, 9);
		int source = dt[7] == int.MinValue ? defaultSrcTz : dt[7];
		if (source is < short.MinValue or > short.MaxValue || destTz is < short.MinValue or > short.MaxValue)
			throw new OverflowException("Temporal offset exceeds the helper range.");
		if (destTz == source) return dt;
		int[] copy = (int[])dt.Clone();
		AddMinute(copy, checked(destTz - source));
		if (copy[7] != int.MinValue) copy[7] = destTz;
		return copy;
	}

	private static int[] roundHalfup(int[] dt, int dtype, int scale)
	{
		DmTemporalInteger.ValidateWireFraction(dt[6], scale);
		return dt;
	}

	public static int[] AddNanoSecond(int[] dt, int n)
	{
		return DmTemporalLegacyAdapter.AddClock(dt, n, 86400000000000L);
	}

	public static int[] AddSecond(int[] dt, int n)
	{
		return DmTemporalLegacyAdapter.AddClock(dt, n, 86400L);
	}

	public static int[] AddMinute(int[] dt, int n)
	{
		return DmTemporalLegacyAdapter.AddClock(dt, n, 1440L);
	}

	private static int[] AddHour(int[] dt, int n)
	{
		return DmTemporalLegacyAdapter.AddClock(dt, n, 24L);
	}

	private static int[] AddDay(int[] dt, int n)
	{
		return DmTemporalLegacyAdapter.AddClock(dt, n, 1L);
	}

	private static int[] AddMonth(int[] dt, int n)
	{
		DmTemporalLegacyAdapter.FromSemantic(dt, 16, 9);
		long total = checked((long)dt[0] * 12 + dt[1] - 1 + n);
		long year = Math.DivRem(total, 12, out long month);
		if (month < 0) { month += 12; year--; }
		if (year == 0) throw new OverflowException("Temporal calendar has no year zero.");
		int targetYear = checked((int)year), targetMonth = (int)month + 1;
		int days = targetMonth == 2 ? DmdtIsLeapYear(targetYear) ? 29 : 28 : targetMonth is 4 or 6 or 9 or 11 ? 30 : 31;
		dt[0] = targetYear; dt[1] = targetMonth; dt[2] = Math.Min(dt[2], days);
		return dt;
	}

	private static int[] AddYear(int[] dt, int n)
	{
		return AddMonth(dt, checked(n * 12));
	}
}
