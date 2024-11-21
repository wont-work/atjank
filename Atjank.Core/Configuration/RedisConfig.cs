namespace Atjank.Core.Configuration;

sealed class RedisConfig
{
	public const string Section = "Redis";

	public string? ConnectionString { get; set; }
}
