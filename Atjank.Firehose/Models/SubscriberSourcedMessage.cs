using System.Text.Json.Serialization;

namespace Atjank.Firehose.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SubscriberOptionsUpdateMessage), "options_update")]
record SubscriberSourcedMessage;

sealed record SubscriberOptionsUpdateMessage : SubscriberSourcedMessage
{
	public required Data Payload { get; init; }

	public record Data
	{
		[JsonPropertyName("wantedCollections")]
		public string[] WantedCollections { get; init; } = [];

		[JsonPropertyName("wantedDids")]
		public string[] WantedDids { get; init; } = [];

		[JsonPropertyName("maxMessageSizeBytes")]
		public required int MaxMessageSizeBytes { get; init; }
	}
}
