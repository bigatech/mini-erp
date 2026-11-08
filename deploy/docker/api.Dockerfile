FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/hosts/MiniErp.Api/MiniErp.Api.csproj
RUN dotnet publish src/backend/hosts/MiniErp.Api/MiniErp.Api.csproj \
    --configuration $BUILD_CONFIGURATION \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false /p:UseSharedCompilation=false /m:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=1654:1654 /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER 1654
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "MiniErp.Api.dll"]
