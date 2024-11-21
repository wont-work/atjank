using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atjank.Core.Util;
using Atjank.Core.Util.Extensions;
using JetBrains.Annotations;
using MessagePack;
using MessagePack.Formatters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atjank.Core;

static class Base62Int
{
	const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

	static readonly FrozenDictionary<char, int> DecodeMap = Alphabet
		.Select(KeyValuePair.Create)
		.ToFrozenDictionary();

	static readonly int Base = Alphabet.Length;

	public static string ToBase62Int(this long value, int pad = 0)
	{
		var builder = Pools.StringPool.Get();
		var i = value;

		while (i > 0)
		{
			i = Math.DivRem(i, Base, out var idx);
			builder.Append(Alphabet[(int)idx]);
		}

		if (builder.Length < pad)
			builder.Append(new string(Alphabet[0], pad - builder.Length));

		var ret = builder.ToString().ToReversed();
		Pools.StringPool.Return(builder);
		return ret;
	}

	public static long ParseBase62Int(this string value)
	{
		long result = 0;

		foreach (var c in value)
		{
			if (DecodeMap.TryGetValue(c, out var idx))
				result = result * Base + idx;
		}

		return result;
	}
}

[JsonConverter(typeof(IdJsonConverter))]
public readonly record struct AtjankId(long Value) : IParsable<AtjankId>
{
	public AtjankId(string value) : this(value.ParseBase62Int())
	{
	}

	public static AtjankId Parse(string s, IFormatProvider? provider) => new(s);

	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		out AtjankId result
	)
	{
		if (s == null)
		{
			result = default;
			return false;
		}

		result = Parse(s, provider);
		return true;
	}

	public override string ToString() => Value.ToBase62Int(11);

	public static explicit operator string(AtjankId self) => self.Value.ToBase62Int(11);

	public static explicit operator long(AtjankId self) => self.Value;

	public static explicit operator AtjankId(long value) => new(value);

	public static explicit operator AtjankId(string value) => new(value);
}

[UsedImplicitly]
sealed class IdToBigintConverter() : ValueConverter<AtjankId, long>(
	x => x.Value,
	x => new AtjankId(x),
	mappingHints: DefaultHints.With(null)
)
{
	static readonly ConverterMappingHints DefaultHints = new(sizeof(long));
}

sealed class IdJsonConverter : JsonConverter<AtjankId>
{
	public override AtjankId Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options
	) => new(reader.GetString()!);

	public override void Write(
		Utf8JsonWriter writer,
		AtjankId value,
		JsonSerializerOptions options
	) => writer.WriteStringValue(value.ToString());
}

sealed class AtjankIdMessagePackFormatter : IMessagePackFormatter<AtjankId>
{
	public AtjankId Deserialize(
		ref MessagePackReader reader,
		MessagePackSerializerOptions options
	) => new(reader.ReadInt64());

	public void Serialize(
		ref MessagePackWriter writer,
		AtjankId value,
		MessagePackSerializerOptions options
	) => writer.Write(value.Value);
}

sealed class AtjankIdMessagePackResolver : IFormatterResolver
{
	public static readonly IFormatterResolver Instance = new AtjankIdMessagePackResolver();

	AtjankIdMessagePackResolver()
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
			if (typeof(T) == typeof(AtjankId))
				Formatter = (IMessagePackFormatter<T>)(object)new AtjankIdMessagePackFormatter();
		}
	}
}
