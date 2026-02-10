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
RUN apt-get update --fix-missing && \
    apt-get install -y --no-install-recommends \
    python3 \
    python3-pip \
    python3-dev \
    libgdiplus \
    libc6-dev \
    && rm -rf /var/lib/apt/lists/*

# Upgrade pip
RUN python3 -m pip install --upgrade pip setuptools wheel

# Copy published application
COPY --from=publish /app/publish .

# Copy Python face service
COPY face_service ./face_service

# Install Python dependencies for InsightFace service
RUN cd face_service && \
    python3 -m pip install --no-cache-dir -r requirements.txt && \
    echo "Python dependencies installed successfully"

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

