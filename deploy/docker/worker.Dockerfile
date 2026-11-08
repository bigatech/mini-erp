FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/hosts/MiniErp.Worker/MiniErp.Worker.csproj
RUN dotnet publish src/backend/hosts/MiniErp.Worker/MiniErp.Worker.csproj \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false /p:UseSharedCompilation=false /m:1

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=1654:1654 /app/publish .
USER 1654
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["sh", "-c", "kill -0 1"]
ENTRYPOINT ["dotnet", "MiniErp.Worker.dll"]
