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
# Using hardcoded path to avoid dotnet --version command issues
RUN echo "=== Setting up OpenCvSharp native libraries ===" && \
    DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23" && \
    echo "Target path: ${DOTNET_SHARED_PATH}" && \
    mkdir -p "${DOTNET_SHARED_PATH}"

# Try to copy library from runtimes/linux-x64/native/libOpenCvSharpExtern.so
RUN DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23" && \
    if [ -f "runtimes/linux-x64/native/libOpenCvSharpExtern.so" ]; then \
        cp "runtimes/linux-x64/native/libOpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        cp "runtimes/linux-x64/native/libOpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        chmod 755 "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        chmod 755 "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        echo "Libraries copied successfully from runtimes/linux-x64/native/"; \
    else \
        echo "Library not found in runtimes/linux-x64/native/libOpenCvSharpExtern.so"; \
    fi

# Try alternative location if first attempt failed
RUN DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23" && \
    if [ ! -f "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" ] && [ -f "runtimes/linux-x64/native/OpenCvSharpExtern.so" ]; then \
        cp "runtimes/linux-x64/native/OpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        cp "runtimes/linux-x64/native/OpenCvSharpExtern.so" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        chmod 755 "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
        chmod 755 "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
        echo "Libraries copied from alternative location"; \
    fi

# Search entire directory if still not found
RUN DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23" && \
    if [ ! -f "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" ]; then \
        LIB_PATH=$(find . -name "*OpenCvSharpExtern.so" -type f 2>/dev/null | head -1) && \
        if [ -n "${LIB_PATH}" ]; then \
            cp "${LIB_PATH}" "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
            cp "${LIB_PATH}" "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
            chmod 755 "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" && \
            chmod 755 "${DOTNET_SHARED_PATH}/OpenCvSharpExtern.so" && \
            echo "Libraries copied from ${LIB_PATH}"; \
        else \
            echo "Warning: OpenCvSharp native libraries not found"; \
        fi; \
    fi

# Verify libraries were copied
RUN DOTNET_SHARED_PATH="/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.23" && \
    if [ -f "${DOTNET_SHARED_PATH}/libOpenCvSharpExtern.so" ]; then \
        echo "Verification: Libraries exist in ${DOTNET_SHARED_PATH}/" && \
        ls -lh "${DOTNET_SHARED_PATH}"/*OpenCvSharp* 2>/dev/null || true; \
    else \
        echo "Warning: Libraries not found in ${DOTNET_SHARED_PATH}/"; \
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
# Set LD_LIBRARY_PATH to include both the app's native directory and system paths
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native:/usr/lib/x86_64-linux-gnu:${LD_LIBRARY_PATH}

# Run the application
ENTRYPOINT ["dotnet", "QRCodeAPI.dll"]

