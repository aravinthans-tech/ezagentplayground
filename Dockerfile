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

# Verify and copy OpenCvSharp native libraries to where OpenCV expects them
# OpenCV looks in /usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23/ first
RUN echo "=== Setting up OpenCvSharp native libraries ===" && \
    DOTNET_VERSION=$(dotnet --version) && \
    DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/${DOTNET_VERSION}" && \
    echo "Target .NET version: ${DOTNET_VERSION}" && \
    echo "Target path: ${DOTNET_SHARED_PATH}" && \
    mkdir -p "${DOTNET_SHARED_PATH}" && \
    LIBRARY_FOUND=false && \
    if [ -f "runtimes/linux-x64/native/libOpenCvSharpExtern.so" ]; then \
        echo "✓ Found libOpenCvSharpExtern.so in runtimes/linux-x64/native/"; \
        cp "runtimes/linux-x64/native/libOpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        cp "runtimes/linux-x64/native/libOpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        LIBRARY_FOUND=true; \
    elif [ -f "runtimes/linux-x64/native/OpenCvSharpExtern.so" ]; then \
        echo "✓ Found OpenCvSharpExtern.so in runtimes/linux-x64/native/"; \
        cp "runtimes/linux-x64/native/OpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        cp "runtimes/linux-x64/native/OpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        LIBRARY_FOUND=true; \
    else \
        echo "⚠ Library not found in runtimes/linux-x64/native/, searching..."; \
        LIB_PATH=$(find . -name "*OpenCvSharpExtern.so" -type f 2>/dev/null | head -1); \
        if [ -n "${LIB_PATH}" ]; then \
            echo "✓ Found library at: ${LIB_PATH}"; \
            cp "${LIB_PATH}" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
            cp "${LIB_PATH}" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
            LIBRARY_FOUND=true; \
        fi; \
    fi && \
    if [ "$LIBRARY_FOUND" = true ]; then \
        chmod 755 "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        chmod 755 "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        echo "✓ Libraries copied to ${DOTNET_SHARED_PATH}/" && \
        ls -lh "${DOTNET_SHARED_PATH}"/*OpenCvSharp* 2>/dev/null || true; \
    else \
        echo "⚠ Warning: OpenCvSharp native libraries not found - face detection will be unavailable"; \
    fi && \
    echo "Also ensuring libraries are in runtimes/linux-x64/native/ for LD_LIBRARY_PATH:" && \
    mkdir -p runtimes/linux-x64/native && \
    if [ "$LIBRARY_FOUND" = true ] && [ -f "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" ]; then \
        cp "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" runtimes/linux-x64/native/ 2>/dev/null || true; \
    fi && \
    echo "LD_LIBRARY_PATH: /app/runtimes/linux-x64/native"

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

