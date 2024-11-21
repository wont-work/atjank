using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using MessagePack;
using MessagePack.Formatters;

namespace Atjank.Core.Util;

sealed class JsonNodeMessagePackFormatter : IMessagePackFormatter<JsonNode?>
{
	public JsonNode? Deserialize(
		ref MessagePackReader reader,
		MessagePackSerializerOptions options
	)
	{
		var json = reader.ReadString();
		return json != null ? JsonNode.Parse(json) : null;
	}

	public void Serialize(
		ref MessagePackWriter writer,
		JsonNode? value,
		MessagePackSerializerOptions options
	)
	{
		if (value != null)
			writer.Write(value.ToJsonString());
		else
			writer.WriteNil();
	}
}

sealed class JsonNodeMessagePackResolver : IFormatterResolver
{
	public static readonly IFormatterResolver Instance = new JsonNodeMessagePackResolver();

	JsonNodeMessagePackResolver()
	{
	}

	public IMessagePackFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;

	static class Cache<T>
	{
		internal static readonly IMessagePackFormatter<T>? Formatter;

		[SuppressMessage("Minor Code Smell", "S3963:\"static\" fields should be initialized inline",
			Justification = "Other MessagePack FormatterResolvers do it too so I presume there's a good reason for it")]
		static Cache()
		{
			if (typeof(T) == typeof(JsonNode))
				Formatter = (IMessagePackFormatter<T>)(object)new JsonNodeMessagePackFormatter();
		}
	}
}
