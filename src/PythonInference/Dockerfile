FROM python:3.12.5-slim AS base

# Set the working directory, the app files could be bind-mounted here
WORKDIR /app

# Copy the requirements file
COPY requirements.txt .

# Install dependencies
RUN pip install --no-cache-dir -r requirements.txt

# Ensure the server is accessible from outside the container
ENV UVICORN_HOST=0.0.0.0

# Set the entry point to run the application 
ENTRYPOINT ["python", "-m", "uvicorn", "main:app"]

CMD []

FROM base AS publish

COPY . .