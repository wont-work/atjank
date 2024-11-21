using Atjank.Core;
using Atjank.Core.Hosting;
using Atjank.Firehose;

var builder = Host.CreateApplicationBuilder(args);

builder
	.UseCore()
	.UseOpenTelemetry()
	.UseFirehose();

builder.Services.AddHostedService<BackgroundFirehoseService>();

var app = builder.Build();

await app.Initialize();
await app.RunAsync();
