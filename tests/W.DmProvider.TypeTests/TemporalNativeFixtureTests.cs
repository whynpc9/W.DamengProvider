using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeFixtureTests
{
    [Fact]
    public void IndependentExpectedOnlyVectorsExerciseEveryDeclaredOperation()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "temporal-native-expectations.json")));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.True(root.GetProperty("expected_only").GetBoolean());
        Assert.False(root.GetProperty("server_evidence").GetBoolean());
        Assert.Equal("not_run", root.GetProperty("execution_status").GetString());
        var ids = new HashSet<string>();
        int count = 0;
        foreach (JsonElement vector in root.GetProperty("cases").EnumerateArray())
        {
            string id = vector.GetProperty("id").GetString()!;
            Assert.True(ids.Add(id), id);
            Assert.True(vector.GetProperty("expected_only").GetBoolean());
            Assert.False(vector.GetProperty("server_evidence").GetBoolean());
            Assert.Equal("not_run", vector.GetProperty("status").GetString());
            Exercise(vector);
            count++;
        }
        Assert.Equal(77, count);
    }

    private static long Ticks(JsonElement value, string property = "ticks") => long.Parse(value.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
    private static DmTemporalFields Fields(JsonElement value) => new(value.GetProperty("year").GetInt32(),
        value.GetProperty("month").GetInt32(), value.GetProperty("day").GetInt32(), value.GetProperty("hour").GetInt32(),
        value.GetProperty("minute").GetInt32(), value.GetProperty("second").GetInt32(), value.GetProperty("nanosecondsWithinSecond").GetInt32(),
        value.TryGetProperty("offsetMinutes", out var offset) && offset.ValueKind != JsonValueKind.Null ? offset.GetInt32() : null);

    // Call only around one target operation. Parsing, setup and positive controls stay outside this boundary.
    private static void AssertTarget(JsonElement expected, string id, Action target, string exceptionProperty = "exception")
    {
        string? exceptionName = expected.TryGetProperty(exceptionProperty, out var exception) ? exception.GetString() : null;
        if (exceptionName is null) target();
        else AssertTargetThrows(exceptionName, id, target);
    }

    private static void AssertTargetThrows(string exceptionName, string id, Action target)
    {
        Exception? actual = Record.Exception(target);
        Assert.True(actual?.GetType().Name == exceptionName, id + ": expected " + exceptionName + ", got " + actual?.GetType().Name);
    }

    private static void Exercise(JsonElement vector)
    {
        JsonElement input = vector.GetProperty("input"), expected = vector.GetProperty("expected");
        string id = vector.GetProperty("id").GetString()!;
        switch (vector.GetProperty("operation").GetString())
        {
            case "project_datetime":
            {
                DmTemporalFields fields = DmTemporalInteger.FromDateTime(new DateTime(Ticks(input), DateTimeKind.Unspecified));
                Assert.Equal(Fields(expected.GetProperty("fields")), fields);
                Assert.Equal(Ticks(expected), DmTemporalInteger.ProjectDateTime(fields).Ticks);
                break;
            }
            case "validate_fractional_ticks":
            case "validate_interval_fraction":
            {
                long ticks = Ticks(input);
                int scale = input.GetProperty("scale").GetInt32();
                if (expected.TryGetProperty("scale7_exact_control", out var control))
                {
                    DmTemporalInteger.ValidateFractionalTicks(ticks, 7);
                    var raw = DmTemporalInteger.FromDateTime(new DateTime(ticks));
                    Assert.Equal(Ticks(control), DmTemporalInteger.ProjectDateTime(raw).Ticks);
                    Assert.Equal(Ticks(control, "fractionalTicks"), DmTemporalInteger.FractionalTicksExact(raw.NanosecondsWithinSecond));
                }
                AssertTarget(expected, id, () => DmTemporalInteger.ValidateFractionalTicks(ticks, scale));
                break;
            }
            case "format":
            {
                DmTemporalFields fields = Fields(input.GetProperty("fields"));
                int scale = input.GetProperty("scale").GetInt32();
                if (expected.TryGetProperty("format_rejection", out var rejection))
                {
                    Assert.Equal("declared_scale_violation", rejection.GetString());
                    AssertTargetThrows("OverflowException", id, () => DmTemporalInteger.FormatExact(fields, scale, DmTemporalTextKind.Timestamp));
                    break;
                }
                string text = DmTemporalInteger.FormatExact(fields, scale, DmTemporalTextKind.Timestamp);
                Assert.Equal(expected.GetProperty("format").GetString(), text[(text.IndexOf('.') + 1)..]);
                if (expected.TryGetProperty("ticks", out _)) Assert.Equal(Ticks(expected), DmTemporalInteger.FractionalTicksExact(fields.NanosecondsWithinSecond));
                else AssertTarget(expected, id, () => DmTemporalInteger.ProjectDateTime(fields), "clr_exception");
                break;
            }
            case "validate_user_scale":
                if (input.GetProperty("scaleSet").GetBoolean())
                {
                    int scale = input.GetProperty("scale").GetInt32();
                    AssertTarget(expected, id, () => DmTemporalInteger.ValidateUserScale(scale));
                }
                break;
            case "validate_time_precision":
                if (input.GetProperty("precisionSet").GetBoolean())
                {
                    int precision = input.GetProperty("precision").GetInt32();
                    AssertTarget(expected, id, () => DmTemporalInteger.ValidateUserPrecision(precision));
                }
                break;
            case "construct_datetime_offset_and_check_current_guard":
            {
                var value = new DateTimeOffset(new DateTime(Ticks(input, "localTicks")), TimeSpan.FromTicks(input.GetProperty("offsetMinutes").GetInt32() * TimeSpan.TicksPerMinute));
                var projected = DmTemporalInteger.ProjectDateTimeOffset(DmTemporalInteger.FromDateTimeOffset(value));
                Assert.Equal(Ticks(expected, "localTicks"), projected.Ticks);
                Assert.Equal(Ticks(expected, "utcTicks"), projected.UtcTicks);
                var guard = expected.GetProperty("currentWWriterGuard");
                AssertTarget(guard, id, () => DmTemporalCodec.NormalizeForWire(value, 27, 7, "8.1.5.60"));
                break;
            }
            case "construct_datetime_offset":
            {
                var local = new DateTime(Ticks(input, "localTicks"));
                var offset = TimeSpan.FromTicks(input.GetProperty("offsetMinutes").GetInt32() * TimeSpan.TicksPerMinute);
                AssertTarget(expected, id, () => _ = new DateTimeOffset(local, offset));
                break;
            }
            case "project_raw_fields_to_datetime_offset":
            {
                var fields = Fields(input.GetProperty("fields"));
                AssertTarget(expected, id, () => DmTemporalInteger.ProjectDateTimeOffset(fields), "clr_exception");
                break;
            }
            case "project_timeonly":
                if (input.TryGetProperty("fields", out var timeFields))
                {
                    var fields = Fields(timeFields);
                    AssertTarget(expected, id, () => DmTemporalInteger.ProjectTimeOnly(fields), "clr_exception");
                }
                else
                {
                    var fields = DmTemporalInteger.FromTimeOnly(new TimeOnly(Ticks(input)));
                    Assert.Equal(expected.GetProperty("fields").GetProperty("nanosecondsWithinSecond").GetInt32(), fields.NanosecondsWithinSecond);
                    Assert.Equal(Ticks(expected), DmTemporalInteger.ProjectTimeOnly(fields).Ticks);
                }
                break;
            case "project_timespan":
            case "validate_interval_leading_precision":
            {
                int leading = input.TryGetProperty("leadingPrecision", out var precision) ? precision.GetInt32() : 9;
                int scale = input.GetProperty("scale").GetInt32();
                var span = TimeSpan.FromTicks(Ticks(input));
                if (expected.TryGetProperty("exception", out _))
                {
                    AssertTarget(expected, id, () => DmTemporalIntervalFields.FromTimeSpanExact(span, scale, leading));
                    break;
                }
                var fields = DmTemporalIntervalFields.FromTimeSpanExact(span, scale, leading);
                Assert.Equal(Ticks(expected), fields.ToTimeSpanExact(scale, leading).Ticks);
                break;
            }
            case "decode_legacy12":
            {
                JsonElement raw = input.GetProperty("legacy12Fields");
                var bytes = new byte[12];
                BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)raw.GetProperty("year").GetInt32());
                bytes[2] = (byte)raw.GetProperty("month").GetInt32(); bytes[3] = (byte)raw.GetProperty("day").GetInt32();
                bytes[4] = (byte)raw.GetProperty("hour").GetInt32(); bytes[5] = (byte)raw.GetProperty("minute").GetInt32(); bytes[6] = (byte)raw.GetProperty("second").GetInt32();
                int micros = raw.GetProperty("microsecondsWithinSecond").GetInt32();
                bytes[7] = (byte)micros; bytes[8] = (byte)(micros >> 8); bytes[9] = (byte)(micros >> 16);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), (short)raw.GetProperty("offsetMinutes").GetInt32());
                var fields = DmTemporalWireCodec.FromLegacyMicroseconds(bytes, DmTemporalTextKind.Timestamp, true);
                Assert.Equal(expected.GetProperty("nanosecondsWithinSecond").GetInt32(), fields.NanosecondsWithinSecond);
                Assert.Equal(Ticks(expected, "fractionalTicks"), DmTemporalInteger.FractionalTicksExact(fields.NanosecondsWithinSecond));
                break;
            }
            case "encode_legacy12":
            {
                var fields = new DmTemporalFields(2000, 1, 1, 0, 0, 0, checked((int)Ticks(input, "fractionalTicks") * 100));
                if (expected.TryGetProperty("exception", out _))
                {
                    AssertTarget(expected, id, () => DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Timestamp));
                    break;
                }
                byte[] bytes = DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Timestamp);
                Assert.Equal(expected.GetProperty("microsecondsWithinSecond").GetInt32(), bytes[7] | (bytes[8] << 8) | (bytes[9] << 16));
                break;
            }
            case "project_raw_fields_to_internal_ticks":
                Assert.Equal(Ticks(expected, "fractionalTicks"), DmTemporalInteger.FractionalTicksExact(Fields(input.GetProperty("fields")).NanosecondsWithinSecond));
                break;
            case "decode_raw_fields_to_legacy12":
            {
                var fields = Fields(input.GetProperty("fields"));
                AssertTarget(expected, id, () => DmTemporalWireCodec.ToLegacyMicroseconds(fields, DmTemporalTextKind.Timestamp));
                break;
            }
            default:
                throw new InvalidOperationException("Unconsumed independent fixture operation.");
        }
    }
}
