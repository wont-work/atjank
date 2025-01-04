using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Atjank.Web.Reactions;

static class Routes
{
	public static IEndpointRouteBuilder MapXrpc(this IEndpointRouteBuilder app)
	{
		var reactions = app.MapGroup("").WithGroupName("Reactions");
		reactions.MapGet("/xrpc/work.on-t.w.reaction.getReactions", async ([FromQuery] string[] objects, HttpContext ctx) =>
		{
			Debugger.Break();
		});
		return app;
	}
}
