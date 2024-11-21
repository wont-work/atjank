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
	public required string Did { get; init; }
	public required ulong TimeUs { get; init; }
}

sealed record JetstreamCommit : JetstreamMessage
{
	public required Data Commit { get; init; }

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
		public required string Rev { get; init; }
		public required string Collection { get; init; }
		public required string Rkey { get; init; }
	}

	public record Create : Data
	{
		public required JsonObject Record { get; init; }
		public required string Cid { get; init; }
	}

	public record Update : Data
	{
		public required JsonObject Record { get; init; }
		public required string Cid { get; init; }
	}

	public record Delete : Data;
}

sealed record JetstreamIdentity : JetstreamMessage
{
	public required Data Identity { get; init; }

	[UsedImplicitly]
	public record Data
	{
		public required string Did { get; init; }
		public required string Handle { get; init; }
		public required ulong Seq { get; init; }
		public required DateTime Time { get; init; }
	}
}

sealed record JetstreamAccount : JetstreamMessage
{
	public required Data Account { get; init; }

	[UsedImplicitly]
	public record Data
	{
		public required bool Active { get; init; }
		public required string Did { get; init; }
		public required ulong Seq { get; init; }
		public required DateTime Time { get; init; }
	}
}
