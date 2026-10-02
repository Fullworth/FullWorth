FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build

WORKDIR /src

COPY FullWorth.Core/FullWorth.Core.csproj FullWorth.Core/
COPY FullWorth.API/FullWorth.API.csproj FullWorth.API/
COPY FullWorth.ParserWorker/FullWorth.ParserWorker.csproj FullWorth.ParserWorker/
RUN dotnet restore FullWorth.API/FullWorth.API.csproj \
    && dotnet restore FullWorth.ParserWorker/FullWorth.ParserWorker.csproj

COPY FullWorth.Core/ FullWorth.Core/
COPY FullWorth.API/ FullWorth.API/
COPY FullWorth.ParserWorker/ FullWorth.ParserWorker/

RUN dotnet publish FullWorth.API/FullWorth.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    && dotnet publish FullWorth.ParserWorker/FullWorth.ParserWorker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/parser-worker-publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS parser-worker-final

ARG BILLWATCH_RELEASE_ID=unknown

LABEL org.opencontainers.image.revision="$BILLWATCH_RELEASE_ID"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        libc6-dev \
        libtesseract5 \
        tesseract-ocr-eng \
        util-linux \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/parser-worker-publish/ ./
COPY deploy/parser-worker-entrypoint.sh /usr/local/bin/fullworth-parser-worker-entrypoint

RUN chmod 0755 /usr/local/bin/fullworth-parser-worker-entrypoint \
    && mkdir --parents /app/x64 /app/tessdata /app/runtimes/linux-x64/native /var/run/fullworth-parser-tls \
    && test -f /lib/x86_64-linux-gnu/libdl.so.2 \
    && ln --symbolic --force /lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/libdl.so \
    && ln --symbolic --force /lib/x86_64-linux-gnu/libdl.so.2 /app/runtimes/linux-x64/native/libdl.so \
    && english_model="$(find /usr/share -type f -path '*/tessdata/eng.traineddata' -print -quit)" \
    && leptonica_library="$(find /usr/lib -type f -name 'liblept.so.5*' -print -quit)" \
    && tesseract_library="$(find /usr/lib -type f -name 'libtesseract.so.5*' -print -quit)" \
    && test -n "$english_model" \
    && test -n "$leptonica_library" \
    && test -n "$tesseract_library" \
    && ln --symbolic "$leptonica_library" /app/x64/libleptonica-1.83.1.dll.so \
    && ln --symbolic "$tesseract_library" /app/x64/libtesseract53.dll.so \
    && ln --symbolic --force "$english_model" /app/tessdata/eng.traineddata \
    && chown --recursive "$APP_UID:$APP_UID" \
        /app \
        /var/run/fullworth-parser-tls

ENV ASPNETCORE_HTTP_PORTS=8081 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8081

HEALTHCHECK NONE

ENTRYPOINT ["/usr/local/bin/fullworth-parser-worker-entrypoint"]
CMD ["dotnet", "FullWorth.ParserWorker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS final

ARG BILLWATCH_RELEASE_ID=unknown

LABEL org.opencontainers.image.revision="$BILLWATCH_RELEASE_ID"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish/ ./

RUN mkdir --parents /var/lib/billwatch/keys /var/lib/billwatch/statements \
    && chown --recursive "$APP_UID:$APP_UID" /var/lib/billwatch

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --header "Host: $AllowedHosts" http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "FullWorth.API.dll"]