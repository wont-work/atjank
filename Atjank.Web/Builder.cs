using Atjank.Core.Configuration;
using Atjank.Core.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace Atjank.Web;

static class Builder
{
	public static IHostApplicationBuilder UseWeb(this IHostApplicationBuilder builder)
	{
		builder
			.UseCore();

		builder.Services.AddOpenApi(opt =>
		{
			opt.ShouldInclude = _ => true;

			var security = new OpenApiSecurityScheme
			{
				Type = SecuritySchemeType.Http,
				Name = "Authorization",
				Scheme = "Bearer",
				Reference = new OpenApiReference
				{
					Type = ReferenceType.SecurityScheme,
					Id = "AtjankAuth"
				}
			};

			opt.AddDocumentTransformer((doc, ctx, ct) =>
			{
				doc.Components ??= new OpenApiComponents();
				doc.Components.SecuritySchemes.Add("AtjankAuth", security);

				return Task.CompletedTask;
			});

			opt.AddOperationTransformer((op, ctx, ct) =>
			{
				if (ctx.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
					op.Security = [new OpenApiSecurityRequirement { [security] = [] }];

				return Task.CompletedTask;
			});
		});

		builder.Services
			.AddHttpContextAccessor()
			.AddRateLimiter(_ => { })
			.AddProblemDetails(opt =>
			{
				opt.CustomizeProblemDetails = pd =>
				{
					pd.ProblemDetails.Extensions.Add("requestId", pd.HttpContext.TraceIdentifier);
					pd.ProblemDetails.Extensions.Remove("traceId");
				};
			});

		return builder;
	}

	public static IApplicationBuilder UseCore(this WebApplication app)
	{
		app.UseRateLimiter()
			.UseForwardedHeaders()
			.UseStaticFiles()
			.UseRouting()
			.Use((ctx, next) =>
			{
				ctx.Response.Headers.RequestId = ctx.TraceIdentifier;
				return next();
			});

		var url = ((IEndpointRouteBuilder)app).ServiceProvider.GetRequiredService<IOptions<AppViewConfig>>().Value.Url
			.ToString();

		app.MapOpenApi();
		app.MapScalarApiReference(opt =>
		{
			opt.Servers = [new ScalarServer(url)];
			opt.DefaultFonts = false;
			opt.DarkMode = false;
		});

		return app;
	}
}
