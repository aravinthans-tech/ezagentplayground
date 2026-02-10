#!/bin/bash

# Startup script to run both Python InsightFace service and .NET application

echo "=== Starting InsightFace Face Matching Service ==="

# Start Python FastAPI service in the background
cd /app/face_service
python3 -m uvicorn app:app --host 0.0.0.0 --port 5001 &
PYTHON_PID=$!

echo "Python InsightFace service started with PID: $PYTHON_PID"

# Wait a few seconds for Python service to initialize
sleep 5

# Check if Python service is running
if ! kill -0 $PYTHON_PID 2>/dev/null; then
    echo "ERROR: Python service failed to start"
    exit 1
fi

echo "=== Starting .NET Application ==="

# Change back to app directory and run .NET application
cd /app

# Run .NET application (this will block)
dotnet QRCodeAPI.dll

# If .NET app exits, kill Python service
echo "=== Shutting down Python service ==="
kill $PYTHON_PID 2>/dev/null || true

