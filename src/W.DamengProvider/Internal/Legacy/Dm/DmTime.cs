using System;
using W.Dm.util;
using W.Dm.Internal.Types;

namespace W.Dm;

public class DmTime
{
	public const int DMDT_HOUR_POS = 0;

	public const int DMDT_HOUR_LEN = 5;

	public const int DMDT_MIN_POS = 5;

	public const int DMDT_MIN_LEN = 6;

	public const int DMDT_SEC_POS = 11;

	public const int DMDT_SEC_LEN = 6;

	public const int DMDT_MSEC_POS = 17;

	public const int DMDT_MSEC_LEN = 20;

	private byte[] m_Times = new byte[7];
	private int prec;

	public DmTime() { }
	public DmTime(byte[] bt)
	{
		ReadFields(bt);
		m_Times = bt;
	}
	public DmTime(DateTime dt, int prec, int scale, short mTimeZone)
	{
		DmTemporalInteger.ValidateUserScale(scale);
		if (scale > 6) throw new NotSupportedException("TIME helper has microsecond precision.");
		DmTemporalInteger.ValidateFractionalTicks(dt.Ticks, scale);
		this.prec = prec;
		m_Times = DmTemporalWireCodec.EncodeExact(TimeFields(dt, mTimeZone), DmTemporalWireLayout.TimeTz7, scale);
	}
	private static DmTemporalFields TimeFields(DateTime value, int? offset = null) =>
		DmTemporalInteger.FromDateTime(value, offset) with { Year = 0, Month = 0, Day = 0 };
	private static DmTemporalFields ReadFields(byte[] bytes)
	{
		if (bytes == null || bytes.Length is not (5 or 7)) throw new ArgumentException("TIME packed helper requires five or seven bytes.");
		return DmTemporalWireCodec.DecodeRaw(bytes, bytes.Length == 5 ? DmTemporalWireLayout.Time5 : DmTemporalWireLayout.TimeTz7, 6);
	}
	public byte[] GetByteArrayValue()
	{
		byte[] bytes = DmTemporalWireCodec.ToLegacyMicroseconds(ReadFields(m_Times) with { OffsetMinutes = null }, DmTemporalTextKind.Time);
		Array.Clear(bytes, 0, 4); // This helper's historical raw date slots are zero, unlike DmTimeDecodeFast.
		return bytes;
	}
	public byte[] GetTzByteArrayValue()
	{
		var fields = ReadFields(m_Times);
		if (!fields.OffsetMinutes.HasValue) throw new InvalidCastException("TIME helper has no offset.");
		byte[] bytes = DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Time);
		Array.Clear(bytes, 0, 4);
		return bytes;
	}
	public byte GetHour() => (byte)ReadFields(m_Times).Hour;
	public byte GetMinute() => (byte)ReadFields(m_Times).Minute;
	public byte GetSecond() => (byte)ReadFields(m_Times).Second;
	public int GetNano() => ReadFields(m_Times).NanosecondsWithinSecond / 1000;
	public short GetTZ() => checked((short)(ReadFields(m_Times).OffsetMinutes ?? throw new InvalidCastException("TIME helper has no offset.")));
	public string GetTimeWithoutNano() => DmTemporalInteger.FormatExact(ReadFields(m_Times) with { NanosecondsWithinSecond = 0, OffsetMinutes = null }, 0, DmTemporalTextKind.Time);
	public string GetTimeWithNano() => DmTemporalInteger.FormatExact(ReadFields(m_Times) with { OffsetMinutes = null }, 6, DmTemporalTextKind.Time);
	public override string ToString() => prec == 0 ? GetTimeWithoutNano() : GetTimeWithNano();
	public static byte[] TimeTzEncodeFast(byte[] dt) => DmTemporalWireCodec.EncodeExact(
		DmTemporalWireCodec.FromLegacyMicroseconds(dt, DmTemporalTextKind.Time, true), DmTemporalWireLayout.TimeTz7, 6);
	public static byte[] TimeTzEncodeFast(int[] dt) => DmTemporalWireCodec.EncodeExact(
		DmTemporalLegacyAdapter.FromMicroseconds(dt, 22), DmTemporalWireLayout.TimeTz7, 6);
	public static byte[] TimeTzEncodeFast(DateTime dt, short mTimeZone) =>
		DmTemporalWireCodec.EncodeExact(TimeFields(dt, mTimeZone), DmTemporalWireLayout.TimeTz7, 6);
	public static byte[] TimeEncodeFast(int[] dt) => DmTemporalWireCodec.EncodeExact(
		DmTemporalLegacyAdapter.FromMicroseconds(dt, 15), DmTemporalWireLayout.Time5, 6);
	public static byte[] TimeEncodeFast(byte[] dt) => DmTemporalWireCodec.EncodeExact(
		DmTemporalWireCodec.FromLegacyMicroseconds(dt, DmTemporalTextKind.Time, false), DmTemporalWireLayout.Time5, 6);
	public static byte[] TimeEncodeFast(DateTime dt) => DmTemporalWireCodec.EncodeExact(TimeFields(dt), DmTemporalWireLayout.Time5, 6);
}
