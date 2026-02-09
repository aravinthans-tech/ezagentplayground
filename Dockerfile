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
RUN dotnet publish "QRCodeAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:IncludeNativeLibrariesForSelfExtract=true

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

# Verify and ensure OpenCvSharp native libraries are in the correct location
# The publish should include them automatically, but we verify and provide diagnostics
RUN echo "=== Verifying OpenCvSharp native libraries ===" && \
    if [ -f "runtimes/linux-x64/native/libOpenCvSharpExtern.so" ] || [ -f "runtimes/linux-x64/native/OpenCvSharpExtern.so" ]; then \
        echo "✓ OpenCvSharp native libraries found in runtimes/linux-x64/native/"; \
        ls -lh runtimes/linux-x64/native/*OpenCvSharp* 2>/dev/null || true; \
    else \
        echo "⚠ Warning: OpenCvSharp native libraries not found in runtimes/linux-x64/native/"; \
        echo "Searching entire publish output for OpenCvSharp libraries:"; \
        find . -name "*OpenCvSharp*" -type f 2>/dev/null | head -10 || echo "No OpenCvSharp libraries found"; \
        echo "Attempting to copy from build output if available..."; \
        if [ -d "/app/build/runtimes/linux-x64/native" ]; then \
            mkdir -p runtimes/linux-x64/native && \
            cp /app/build/runtimes/linux-x64/native/*OpenCvSharp* runtimes/linux-x64/native/ 2>/dev/null && \
            echo "✓ Copied libraries from build output" || echo "Could not copy from build output"; \
        else \
            echo "Build output not available in this stage"; \
        fi; \
    fi && \
    echo "Final verification:" && \
    (ls -lh runtimes/linux-x64/native/*OpenCvSharp* 2>/dev/null && echo "✓ Libraries confirmed" || echo "⚠ Libraries still not found - face detection will be unavailable") && \
    echo "LD_LIBRARY_PATH will be: /app/runtimes/linux-x64/native"

# Copy static files (wwwroot)
COPY wwwroot ./wwwroot

# Copy configuration files
COPY appsettings.json .
COPY appsettings.Development.json .

# Expose port 8080 (Render default)
EXPOSE 8080

# Set environment variables for ASP.NET Core and library loading
ENV ASPNETCORE_URLS=http://+:8080
# Set LD_LIBRARY_PATH to include both the app's native directory and system paths
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native:/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH}

# Run the application
ENTRYPOINT ["dotnet", "QRCodeAPI.dll"]

