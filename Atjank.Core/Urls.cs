using Atjank.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Atjank.Core;

// IMPORTANT: new Uri() trailing slash behavior: https://stackoverflow.com/a/42744342
public sealed class Urls(IOptionsSnapshot<AppViewConfig> instanceCfg)
{
	public Uri InstanceUrl { get; } = instanceCfg.Value.Url;
}
