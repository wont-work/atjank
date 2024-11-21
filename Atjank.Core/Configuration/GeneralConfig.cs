using System.ComponentModel.DataAnnotations;

namespace Atjank.Core.Configuration;

public sealed record GeneralConfig
{
	public const string Section = "General";

	[Required] public required Uri Url { get; set; }
	[Required] public required Uri Jetstream { get; set; }
	[Required] public required string WantedCollections { get; set; }
}
