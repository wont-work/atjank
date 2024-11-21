using System.Diagnostics;
using System.Reflection;

namespace Atjank.Core.Util.Extensions;

static class AssemblyExt
{
	public static string? GetProductVersion(this Assembly assembly) =>
		assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
		?? FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion;
}
