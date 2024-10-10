# Stage 1: Build stage
FROM python:3.12.5-slim AS builder

# Set the working directory
WORKDIR /app

# Copy the requirements file
COPY requirements.txt .

# Install virtualenv
RUN python -m venv venv

# Activate virtual environment and install dependencies
RUN . venv/bin/activate && pip install --no-cache-dir -r requirements.txt

# Stage 2: Final stage
FROM python:3.12.5-slim

# Set the working directory
WORKDIR /app

# Copy the virtual environment from the builder stage
COPY --from=builder /app/venv /app/venv

# Copy the application files
COPY . .

ENV UVICORN_HOST=0.0.0.0

# Activate the virtual environment and set the entry point to run the application
CMD ["sh", "-c", ". venv/bin/activate && python -m uvicorn main:app"]
