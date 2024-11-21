using System.ComponentModel.DataAnnotations;

namespace Atjank.Core.Configuration;

public sealed record AppViewConfig
{
	public const string Section = "AppView";

	[Required] public required Uri Url { get; set; }
	[Required] public required Uri Jetstream { get; set; }
}
