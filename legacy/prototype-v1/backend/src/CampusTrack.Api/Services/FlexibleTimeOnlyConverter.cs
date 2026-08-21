using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// System.Text.Json's built-in TimeOnly converter only accepts
/// "HH:mm:ss[.fffffff]". Timetable times arrive as "HH:mm" (that's what
/// HTML time inputs and most clients send), so parse leniently.
/// </summary>
public class FlexibleTimeOnlyConverter : JsonConverter<TimeOnly>
{
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert,
                                  JsonSerializerOptions options) =>
        TimeOnly.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, TimeOnly value,
                               JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm:ss"));
}
