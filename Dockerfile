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

# Install dependencies individually (continue even if some fail)
# This ensures the build succeeds even if some packages aren't available
RUN apt-get update --fix-missing && \
    (apt-get install -y --no-install-recommends libgdiplus || echo "libgdiplus not available") && \
    (apt-get install -y --no-install-recommends libc6-dev || echo "libc6-dev not available") && \
    (apt-get install -y --no-install-recommends libtbb2 || echo "libtbb2 not available") && \
    (apt-get install -y --no-install-recommends libgomp1 || echo "libgomp1 not available") && \
    (apt-get install -y --no-install-recommends libopencv-dev || \
     apt-get install -y --no-install-recommends libopencv-core libopencv-imgproc libopencv-imgcodecs libopencv-objdetect || \
     echo "OpenCV packages not available") && \
    rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Copy OpenCvSharp native libraries to where .NET expects them
RUN echo "Checking for OpenCvSharp native libraries..." && \
    if [ -f "runtimes/linux-x64/native/OpenCvSharpExtern.so" ]; then \
        mkdir -p /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23 && \
        cp runtimes/linux-x64/native/OpenCvSharpExtern.so /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23/ && \
        cp runtimes/linux-x64/native/libOpenCvSharpExtern.so /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23/ 2>/dev/null || true && \
        echo "OpenCvSharp libraries copied to .NET runtime directory"; \
    else \
        echo "Warning: OpenCvSharp native libraries not found in runtimes/linux-x64/native/"; \
        find . -name "*OpenCvSharp*" -type f 2>/dev/null | head -10; \
    fi

# Copy static files (wwwroot)
COPY wwwroot ./wwwroot

# Copy configuration files
COPY appsettings.json .
COPY appsettings.Development.json .

# Expose port 8080 (Render default)
EXPOSE 8080

# Set environment variables for ASP.NET Core and library loading
ENV ASPNETCORE_URLS=http://+:8080
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native:${LD_LIBRARY_PATH}

# Run the application
ENTRYPOINT ["dotnet", "QRCodeAPI.dll"]

