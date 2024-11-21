using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Atjank.Firehose.Models;

[JsonPolymorphic(
	TypeDiscriminatorPropertyName = "kind",
	IgnoreUnrecognizedTypeDiscriminators = true,
	UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
[JsonDerivedType(typeof(JetstreamCommit), "commit")]
[JsonDerivedType(typeof(JetstreamIdentity), "identity")]
[JsonDerivedType(typeof(JetstreamAccount), "account")]
record JetstreamMessage
{
	[JsonRequired] public string Did { get; init; }
	[JsonRequired] public ulong TimeUs { get; init; }
}

sealed record JetstreamCommit : JetstreamMessage
{
	[JsonRequired] public Data Commit { get; init; }

	[JsonPolymorphic(
		TypeDiscriminatorPropertyName = "operation",
		IgnoreUnrecognizedTypeDiscriminators = true,
		UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType)]
	[JsonDerivedType(typeof(Create), "create")]
	[JsonDerivedType(typeof(Update), "update")]
	[JsonDerivedType(typeof(Delete), "delete")]
	[UsedImplicitly]
	public record Data
	{
		[JsonRequired] public string Rev { get; init; }
		[JsonRequired] public string Collection { get; init; }
		[JsonRequired] public string Rkey { get; init; }
	}

	public record Create : Data
	{
		[JsonRequired] public JsonObject Record { get; init; }
		[JsonRequired] public string Cid { get; init; }
	}

	public record Update : Data
	{
		[JsonRequired] public JsonObject Record { get; init; }
		[JsonRequired] public string Cid { get; init; }
	}

	public record Delete : Data
	{
	}
}

sealed record JetstreamIdentity : JetstreamMessage
{
	[JsonRequired] public Data Identity { get; init; }

	[UsedImplicitly]
	public record Data
	{
		[JsonRequired] public string Did { get; init; }
		[JsonRequired] public string Handle { get; init; }
		[JsonRequired] public ulong Seq { get; init; }
		[JsonRequired] public DateTime Time { get; init; }
	}
}

sealed record JetstreamAccount : JetstreamMessage
{
	[JsonRequired] public Data Account { get; init; }

	[UsedImplicitly]
	public record Data
	{
		[JsonRequired] public bool Active { get; init; }
		[JsonRequired] public string Did { get; init; }
		[JsonRequired] public ulong Seq { get; init; }
		[JsonRequired] public DateTime Time { get; init; }
	}
}
