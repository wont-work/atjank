using Atjank.Core;
using Atjank.Core.Hosting;
using Atjank.Worker;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder
	.UseCore()
	.UseOpenTelemetry()
	.UseWorker();

builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

var app = builder.Build();

await app.Initialize();
await app.RunAsync();
