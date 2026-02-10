# Use the official .NET 8.0 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["QRCodeAPI.csproj", "./"]
RUN dotnet restore "QRCodeAPI.csproj"

# Copy everything else and build
COPY . .
WORKDIR "/src"
RUN dotnet build "QRCodeAPI.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "QRCodeAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install Python 3.9+ and pip, plus system dependencies
# Include build-essential and additional dependencies for compiling InsightFace
# InsightFace requires C++ compiler, cmake, and various development libraries
RUN apt-get update --fix-missing && \
    apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-dev \
    build-essential \
    g++ \
    gcc \
    make \
    cmake \
    libgdiplus \
    libc6-dev \
    pkg-config \
    libopencv-dev \
    && rm -rf /var/lib/apt/lists/*

# Note: pip upgrade is handled automatically during package installation
# Skipping separate upgrade step to avoid build failures

# Copy published application
COPY --from=publish /app/publish .

# Copy Python face service
COPY face_service ./face_service

# Install Python dependencies for InsightFace service
# Install build dependencies first (Cython, numpy), then InsightFace and other packages
RUN cd face_service && \
    (python3 -m pip install --break-system-packages --no-cache-dir Cython numpy==1.24.3 || \
     python3 -m pip install --no-cache-dir Cython numpy==1.24.3) && \
    (python3 -m pip install --break-system-packages --no-cache-dir -r requirements.txt || \
     python3 -m pip install --no-cache-dir -r requirements.txt) && \
    echo "Python dependencies installed successfully"

# Download InsightFace models during Docker build
# This downloads models to ~/.insightface/models/buffalo_l/ and copies them to face_service/models/
# Models will be baked into the image, so container startup is instant
RUN cd face_service && \
    python3 -c "import insightface; from insightface.app import FaceAnalysis; app = FaceAnalysis(providers=['CPUExecutionProvider']); app.prepare(ctx_id=-1, det_size=(640, 640)); print('Models downloaded successfully')" && \
    mkdir -p models/buffalo_l && \
    cp -r /root/.insightface/models/buffalo_l/* models/buffalo_l/ 2>/dev/null || \
    (echo "Models downloaded to default location, copying to local directory..." && \
     find /root/.insightface/models -name "*.onnx" -exec cp {} models/buffalo_l/ \; 2>/dev/null || true) && \
    echo "Models prepared in face_service/models/buffalo_l/"

# Copy static files (wwwroot)
COPY wwwroot ./wwwroot

# Copy configuration files
COPY appsettings.json .
COPY appsettings.Development.json .

# Copy startup script
COPY start.sh ./start.sh
RUN chmod +x ./start.sh

# Expose port 8080 (Render default)
# Port 5001 is for Python service (internal only, not exposed)
EXPOSE 8080

# Set environment variables for ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080

# Run both Python and .NET services using startup script
ENTRYPOINT ["./start.sh"]

