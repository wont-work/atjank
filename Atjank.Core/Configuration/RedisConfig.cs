using System.ComponentModel.DataAnnotations;

namespace Atjank.Core.Configuration;

sealed class RedisConfig
{
	public const string Section = "Redis";

	[Required] public string ConnectionString { get; set; } = null!;
}
