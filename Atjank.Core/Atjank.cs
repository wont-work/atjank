using System.Reflection;
using Atjank.Core.Configuration;
using Atjank.Core.Database;
using Atjank.Core.Util.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Atjank.Core;

public static class AtjankApp
{
	static readonly Assembly Asm = Assembly.GetExecutingAssembly();
	static readonly string FullVersion = Asm.GetProductVersion() ?? "unknown";

	public static readonly string Version = FullVersion.Length > 16 ? FullVersion[..16] : FullVersion;
	public static readonly string Dir = AppContext.BaseDirectory;

	public static readonly string[] Flags =
	[
#if DEBUG
		"-p:Configuration=Debug",
#else
		"-p:Configuration=Release",
#endif
	];

	public static async Task Initialize(this IHost app)
	{
		await using var boot = app.Services.CreateAsyncScope();

		var cfg = boot.ServiceProvider.GetRequiredService<IOptions<AppViewConfig>>();
		var logFactory = boot.ServiceProvider.GetRequiredService<ILoggerFactory>();
		var log = logFactory.CreateLogger("Atjank");

		log.LogInformation("Atjank {Version} {Flags} ({Instance})", Version,
			string.Join(' ', Flags), cfg.Value.Url);

		// warmup

		var db = boot.ServiceProvider.GetRequiredService<DatabaseContext>();
		_ = db.Model;
		await db.Database.ExecuteSqlRawAsync("SELECT 1");

		var cache = boot.ServiceProvider.GetRequiredService<IFusionCache>();
		await cache.GetOrDefaultAsync<bool>("fake-key-to-warm-up-backplane");
	}
}
