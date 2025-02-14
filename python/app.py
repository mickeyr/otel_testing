from flask import Flask, request
from opentelemetry._logs import set_logger_provider
import requests
import json
import logging
from pythonjsonlogger.json import JsonFormatter

from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.instrumentation.flask import FlaskInstrumentor

from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.resources import Resource
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.metrics import (
    get_meter_provider,
    set_meter_provider,
)
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader

app = Flask(__name__)

FlaskInstrumentor().instrument(enable_commenter=True, commenter_options={})

trace_provider = TracerProvider()
trace_processor = BatchSpanProcessor(OTLPSpanExporter(insecure=True))
trace_provider.add_span_processor(trace_processor)
trace.set_tracer_provider(trace_provider)
tracer = trace.get_tracer(__name__)

logger_provider = LoggerProvider(
    resource=Resource.create({"service.name": "my-service"})
)
logger_provider.add_log_record_processor(
    BatchLogRecordProcessor(OTLPLogExporter(insecure=True))
)
set_logger_provider(logger_provider)
log_handler = LoggingHandler(level=logging.NOTSET, logger_provider=logger_provider)

metrics_exporter = OTLPMetricExporter(insecure=True)
metrics_reader = PeriodicExportingMetricReader(metrics_exporter)
meter_provider = MeterProvider(metric_readers=[metrics_reader])
set_meter_provider(meter_provider)

logging.basicConfig(level=logging.DEBUG)
logging.getLogger().addHandler(log_handler)
logger = logging.getLogger(__name__)
logHandler = logging.StreamHandler()
formatter = JsonFormatter()
logHandler.setFormatter(formatter)
logger.addHandler(logHandler)

tracer = trace.get_tracer(__name__)
meter = get_meter_provider().get_meter(__name__)
arrivals_counter = meter.create_counter("arrivals_requests")
planes_viewed_counter = meter.create_counter("planes_viewed")


def get_plane_data(flights: list):
    with tracer.start_as_current_span("get_plane_data"):
        plane_data = [
            {
                "icao24": flight["icao24"],
                "callsign": flight["callsign"].strip(),
            }
            for flight in flights
        ]
        planes_viewed_counter.add(len(plane_data))
        return plane_data


base_url = "https://opensky-network.org/api"


@app.route("/arrivals")
def arrivals():
    with tracer.start_as_current_span("arrivals"):
        airport_code = request.args.get("airport")
        begin = request.args.get("begin")
        end = request.args.get("end")
        if not airport_code or not begin or not end:
            return {"error": "Missing required parameters"}, 400
        labels = {"airport": airport_code}
        arrivals_counter.add(1, attributes=labels)
        logger.info(
            "Finding arrivals for airport",
            extra={"airport": airport_code, "begin": str(begin), "end": str(end)},
        )
        url = (
            f"{base_url}/flights/arrival?airport={airport_code}&begin={begin}&end={end}"
        )
        logger.info("Calling OpenSky API", extra={"url": url})
        response = requests.get(
            url,
            headers={"Accept": "application/json"},
        )
        if response.ok:
            logger.info(
                "Response from OpenSky API", extra={"status": response.status_code}
            )
            flights = json.loads(response.content)
            return get_plane_data(flights)
        return []


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)
