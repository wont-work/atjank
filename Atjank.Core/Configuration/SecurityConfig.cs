namespace Atjank.Core.Configuration;

public sealed record SecurityConfig
{
	public const string Section = "Security";

	public required bool AllowLocalConnections { get; set; }
}
