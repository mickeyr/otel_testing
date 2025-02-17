using OpenTelemetry;
using OpenTelemetry.Exporter.OpenTelemetryProtocol;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace dotnet;

internal static class TelemetryExtensions
{
  const string serviceName = "AircraftLookup";
  internal static void UseTelemetry(this WebApplicationBuilder builder)
  {
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
          .AddSource("AircraftLookup")
          .SetSampler(new AlwaysOnSampler())
          .AddHttpClientInstrumentation()
          .AddAspNetCoreInstrumentation()
        );
    otel.WithMetrics(metrics => metrics
          .AddMeter("TestApp.AspNetCore", "1.0.0")
          .SetExemplarFilter(ExemplarFilterType.TraceBased)
          .AddMeter("Microsoft.AspNetCore.Hosting")
          .AddMeter("Microsoft.AspNetCore.Http")
          .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
          .AddMeter("AircraftLookup")
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
  }

}
