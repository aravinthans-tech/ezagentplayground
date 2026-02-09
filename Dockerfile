# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy all source files
COPY . .

# Publish the application
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install OpenCV and system dependencies
RUN apt-get update && apt-get install -y \
    libgdiplus \
    libopencv-dev \
    libopencv-contrib-dev \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Copy LEADTOOLS license files (if they exist)


# Ensure wwwroot is copied (static files)
COPY wwwroot ./wwwroot

# Expose port (PORT environment variable is handled in Program.cs)
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "QRCodeAPI.dll"]

