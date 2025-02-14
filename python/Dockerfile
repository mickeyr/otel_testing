# Use an official lightweight Python image.
FROM python:3.13-slim AS base

# Install system dependencies required by Poetry and build tools.
RUN apt-get update && apt-get install -y --no-install-recommends \
  curl build-essential && \
  rm -rf /var/lib/apt/lists/*

# Install Poetry
ENV POETRY_VERSION=1.8.5
RUN curl -sSL https://install.python-poetry.org | python3 - --version $POETRY_VERSION

# Ensure Poetry installs to PATH
ENV PATH="/root/.local/bin:${PATH}"

# Set the working directory.
WORKDIR /app

# Copy only the Poetry configuration files first to leverage Docker cache.
COPY pyproject.toml poetry.lock* .
# Install project dependencies. The --no-root flag avoids installing the current project as a package.
RUN poetry install --no-dev --no-root

# Copy the rest of the application code.
COPY app.py .

# Set environment variables for Flask.
ENV FLASK_APP=app.py
ENV FLASK_RUN_HOST=0.0.0.0

# Expose the port that Flask is running on.
EXPOSE 5000

# Command to run the Flask app.
CMD ["poetry", "run", "python", "app.py"]

