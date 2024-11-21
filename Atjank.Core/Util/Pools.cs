using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Atjank.Core.Util;

static class Pools
{
	public static readonly ObjectPool<StringBuilder> StringPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool();
}
