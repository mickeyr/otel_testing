using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using OpenTelemetry.Exporter.OpenTelemetryProtocol;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
const string serviceName = "rolldice";
var rollMeter = new Meter("dotnet-api", "1.0.0");
var countRolls = rollMeter.CreateCounter<long>("rolls");
var anonymousRolls = rollMeter.CreateCounter<long>("anonymous_rolls");
var namedRolls = rollMeter.CreateCounter<long>("named_rolls");
var rollActivitySource = new ActivitySource("dotnet-api");


var otel = builder
  .Services
  .AddOpenTelemetry();

otel.ConfigureResource(r => r
      .AddService(
        serviceName: serviceName,
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        serviceInstanceId: Environment.MachineName));
otel.WithTracing(tracing => tracing
      .AddSource("TestApp.AspNetCore")
      .SetSampler(new AlwaysOnSampler())
      .AddHttpClientInstrumentation()
      .AddAspNetCoreInstrumentation()
      .AddSource(rollActivitySource.Name)
    );
otel.WithMetrics(metrics => metrics
      .AddMeter("TestApp.AspNetCore", "1.0.0")
      .SetExemplarFilter(ExemplarFilterType.TraceBased)
      .AddMeter(rollMeter.Name)
      .AddMeter("Microsoft.AspNetCore.Hosting")
      .AddMeter("Microsoft.AspNetCore.Http")
      .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
      .AddHttpClientInstrumentation()
      .AddAspNetCoreInstrumentation()
  );
otel.WithLogging(logging => logging
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName)));

var OtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(OtlpEndpoint))
{
  otel.UseOtlpExporter();
}

var app = builder.Build();


string HandleRollDice([FromServices] ILogger<Program> logger, string? player)
{
  using var activity = rollActivitySource.StartActivity("RollDice");
  countRolls.Add(1);
  var result = RollDice();

  if (string.IsNullOrWhiteSpace(player))
  {
    anonymousRolls.Add(1);
    logger.LogInformation("Anonymous player is rolling the dice: {result}", result);
  }
  else
  {
    namedRolls.Add(1);
    logger.LogInformation("{player} is rolling the dice: {result}", player, result);
  }

  return result.ToString(CultureInfo.InvariantCulture);
}

int RollDice() => Random.Shared.Next(1, 7);

app.MapGet("/rolldice/{player?}", HandleRollDice);

app.Run();
