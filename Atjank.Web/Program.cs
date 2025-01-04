using Atjank.Core;
using Atjank.Core.Configuration;
using Atjank.Core.Hosting;
using Atjank.Firehose;
using Atjank.Web;
using Atjank.Web.Debugging;
using Atjank.Web.Reactions;
using Atjank.Worker;
using Quartz.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	Args = args,
	WebRootPath = Path.Combine(AtjankApp.Dir, "wwwroot")
});

builder
	.UseWeb()
	.UseOpenTelemetry();

var proc = builder.Configuration
	.GetRequiredSection(ProcessConfig.Section)
	.Get<ProcessConfig>();

if (proc?.WebWithWorker == true)
{
	builder.UseWorker();
	builder.Services.AddQuartzServer(opt => opt.WaitForJobsToComplete = true);
}

if (proc?.WebWithFirehose == true)
{
	builder.UseFirehose();
	builder.Services.AddHostedService<BackgroundFirehoseService>();
}

var app = builder.Build();

app.UseCore();
app.MapDebug()
	.MapXrpc();

await app.Initialize();
await app.RunAsync();
