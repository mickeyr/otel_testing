using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Types;
using dotnet;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
TelemetryExtensions.UseTelemetry(builder);

var app = builder.Build();

Meter meter = new("AircraftLookup");
var aircraftLookupCounter = meter.CreateCounter<int>("AircraftLookupCounter");
var aircraftLookupDuration = meter.CreateHistogram<double>("AircraftLookupDuration");
var aircraftLookupStatusCodeCounter = meter.CreateCounter<int>("AircraftLookupStatusCodeCounter");
var aircraftLookupErrorCounter = meter.CreateCounter<int>("AircraftLookupErrorCounter");
var aircraftLookupSuccessCounter = meter.CreateCounter<int>("AircraftLookupSuccessCounter");
var aircraftLookupPayloadSizeCounter = meter.CreateCounter<long>("AircraftLookupPayloadSizeCounter");

var airportLookupCounter = meter.CreateCounter<int>("AirportLookupCounter");
var airportLookupDuration = meter.CreateHistogram<double>("AirportLookupDuration");
var airportLookupStatusCodeCounter = meter.CreateCounter<int>("AirportLookupStatusCodeCounter");
var airportLookupErrorCounter = meter.CreateCounter<int>("AirportLookupErrorCounter");
var airportLookupSuccessCounter = meter.CreateCounter<int>("AirportLookupSuccessCounter");
var airportLookupPayloadSizeCounter = meter.CreateCounter<long>("AirportLookupPayloadSizeCounter");

ActivitySource activitySource = new("AircraftLookup");

async Task<Aircraft> GetAircraft(string registration, ILogger<Program> logger)
{
  using var activity = activitySource.StartActivity("GetAircraft");
  logger.LogInformation("Getting aircraft info for {Registration}", registration);
  aircraftLookupCounter.Add(1, new KeyValuePair<string, object?>("registration", registration));

  using var client = new HttpClient();
  client.BaseAddress = new Uri("https://api.adsbdb.com/v0/");
  client.DefaultRequestHeaders.Add(HeaderNames.Accept, "application/json");
  var route = $"aircraft/{registration}";
  var timer = Stopwatch.StartNew();
  var response = await client.GetAsync(route);
  timer.Stop();
  aircraftLookupDuration.Record(timer.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("registration", registration));
  aircraftLookupStatusCodeCounter.Add(1,
      new KeyValuePair<string, object?>("registration", registration),
      new KeyValuePair<string, object?>("statusCode", (int)response.StatusCode)
    );
  if (!response.IsSuccessStatusCode)
  {
    aircraftLookupErrorCounter.Add(1, new KeyValuePair<string, object?>("registration", registration));
    logger.LogError("Failed to get aircraft info: {route}", route);
    throw new Exception("Failed to get aircraft info");
  }

  aircraftLookupSuccessCounter.Add(1, new KeyValuePair<string, object?>("registration", registration));
  aircraftLookupPayloadSizeCounter.Add(response.Content.Headers.ContentLength ?? 0, new KeyValuePair<string, object?>("registration", registration));

  var payload = await response.Content.ReadFromJsonAsync<AircraftResponsePayload>();
  
  if (payload?.Response?.Aircraft != null) return payload.Response.Aircraft;
  
  logger.LogError("Failed to get aircraft info");
  throw new Exception("Failed to get aircraft info");


}

async Task<List<string>> GetRegistrationCodesForAirport(string airportCode, DateTimeOffset begin, DateTimeOffset end, ILogger<Program> logger)
{
  using var activity = activitySource.StartActivity("GetRegistrationCodesForAiport");
  logger.LogInformation("Getting registrations for airport {AirportCode}", airportCode);
  airportLookupCounter.Add(1, new KeyValuePair<string, object?>("airportCode", airportCode));

  using var client = new HttpClient();
  var arrivalsUrl = configuration.GetValue<string>("ArrivalsUrl");
  if (string.IsNullOrWhiteSpace(arrivalsUrl))
  {
    airportLookupErrorCounter.Add(1, new KeyValuePair<string, object?>("airportCode", airportCode));
    throw new Exception("ArrivalsUrl is not configured");
  }
  client.BaseAddress = new Uri(arrivalsUrl);
  var timer = Stopwatch.StartNew();
  var response = await client.GetAsync($"arrivals?airport={airportCode}&begin={begin.ToUnixTimeSeconds()}&end={end.ToUnixTimeSeconds()}");
  timer.Stop();
  airportLookupDuration.Record(timer.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("airportCode", airportCode));
  airportLookupPayloadSizeCounter.Add(response.Content.Headers.ContentLength ?? 0, new KeyValuePair<string, object?>("airportCode", airportCode));
  airportLookupStatusCodeCounter.Add(1,
      new KeyValuePair<string, object?>("airportCode", airportCode),
      new KeyValuePair<string, object?>("statusCode", (int)response.StatusCode)
    );
  if (!response.IsSuccessStatusCode)
  {
    airportLookupErrorCounter.Add(1, new KeyValuePair<string, object?>("airportCode", airportCode));
    throw new Exception("Failed to get arrivals");
  }

  airportLookupSuccessCounter.Add(1, new KeyValuePair<string, object?>("airportCode", airportCode));
  var aircraft = await response.Content.ReadFromJsonAsync<List<Dictionary<string, string>>>() ?? [];
  var registrations = aircraft.Select(a => a["icao24"]).ToList();

  return registrations;
}

async Task<List<Aircraft>> HandleGetPlaneInfo([FromServices] ILogger<Program> logger, string airportCode = "KBNA")
{
  using var activity = activitySource.StartActivity("HandleGetPlaneInfo");
  logger.LogInformation("Getting plane info for airport {AirportCode}", airportCode);

  var registrations = await GetRegistrationCodesForAirport(airportCode, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, logger);
  var planes = new List<Aircraft>();
  foreach (var registration in registrations)
  {
    try
    {
      var aircraft = await GetAircraft(registration, logger);
      planes.Add(aircraft);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to get aircraft info for {Registration}", registration);
    }
  }

  return planes;
}

app.MapGet("/planes", HandleGetPlaneInfo);

app.Run();

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Types
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
  public record AircraftResponsePayload(
      AircraftResponse Response
      )
  { }

  public record AircraftResponse(
      Aircraft Aircraft
  )
  { }

  public record Aircraft(
      string Type,
      [property: JsonPropertyName("icao_type")]
      string IcaoType,
      string Manufacturer,
      [property: JsonPropertyName("mode_s")]
      string Mode,
      string Registration,
      [property: JsonPropertyName("registered_owner_country_iso_name")]
      string RegisteredOwnerCountryIsoName,
      [property: JsonPropertyName("registered_owner_country_name")]
      string RegisteredOwnerCountryName,
      [property: JsonPropertyName("registered_owner_operator_flag_code")]
      string RegisteredOwnerOperatorFlagClode,
      [property: JsonPropertyName("registered_owner")]
      string RegisteredOwner,
      [property: JsonPropertyName("url_photo")]
      string UrlPhoto,
      [property: JsonPropertyName("url_photo_thumbnail")]
      string UrlPhotoThumbnail)
  { }
}

