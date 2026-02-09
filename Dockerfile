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

# Install essential dependencies first
RUN apt-get update --fix-missing && \
    apt-get install -y --no-install-recommends \
    libgdiplus \
    libc6-dev \
    libtbb2 \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Try to install OpenCV packages (optional - continue if not available)
# OpenCvSharp4 may work with system libraries or NuGet runtime packages
RUN apt-get update --fix-missing && \
    (apt-get install -y --no-install-recommends libopencv-dev || \
     apt-get install -y --no-install-recommends libopencv-core libopencv-imgproc libopencv-imgcodecs libopencv-objdetect || \
     true) && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Copy static files (wwwroot)
COPY wwwroot ./wwwroot

# Copy configuration files
COPY appsettings.json .
COPY appsettings.Development.json .

# Expose port 8080 (Render default)
EXPOSE 8080

# Set environment variable for ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080

# Run the application
ENTRYPOINT ["dotnet", "QRCodeAPI.dll"]

