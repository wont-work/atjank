using System.Diagnostics;
using Atjank.Core.Configuration;
using Atjank.Core.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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
				Scheme = "Bearer",
				In = ParameterLocation.Header,
				BearerFormat = "Json Web Token",
				Name = "Authorization"
			};

			opt.AddDocumentTransformer((doc, ctx, ct) =>
			{
				doc.Components ??= new OpenApiComponents();
				doc.Components.SecuritySchemes.Add("Bearer", security);

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

		builder.Services
			.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(opt =>
			{
				opt.TokenValidationParameters = new TokenValidationParameters
				{
					ValidAlgorithms = ["ES256", "ES256K"],
					ValidTypes = ["JWT"],
				};

				opt.Events = new JwtBearerEvents
				{
					OnTokenValidated = async ctx =>
					{
						Debugger.Break();
					}
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
			.UseAuthentication()
			.Use((ctx, next) =>
			{
				ctx.Response.Headers.RequestId = ctx.TraceIdentifier;
				return next();
			});

		var url = ((IEndpointRouteBuilder)app).ServiceProvider.GetRequiredService<IOptions<GeneralConfig>>().Value.Url
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
