# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS native-build

ARG LEPTONICA_VERSION=1.85.0
ARG LEPTONICA_SHA256=3745ae3bf271a6801a2292eead83ac926e3a9bc1bf622e9cd4dd0f3786e17205

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        autoconf \
        automake \
        ca-certificates \
        curl \
        g++ \
        libjpeg-turbo8-dev \
        libpng-dev \
        libtiff-dev \
        libtool \
        make \
        pkg-config \
        zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /native-src

RUN curl --fail --location --silent --show-error \
        "https://github.com/DanBloomberg/leptonica/releases/download/${LEPTONICA_VERSION}/leptonica-${LEPTONICA_VERSION}.tar.gz" \
        --output leptonica.tar.gz \
    && printf '%s  %s\n' "$LEPTONICA_SHA256" leptonica.tar.gz | sha256sum --check --strict \
    && tar --extract --gzip --file leptonica.tar.gz \
    && cd "leptonica-${LEPTONICA_VERSION}" \
    && ./configure --prefix=/usr/local --disable-static \
    && make --jobs="$(nproc)" \
    && make install DESTDIR=/native-root

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS parser-worker-final

ARG BILLWATCH_RELEASE_ID=unknown

LABEL org.opencontainers.image.revision="$BILLWATCH_RELEASE_ID"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        libtesseract-dev \
        tesseract-ocr-eng \
    && rm -rf /var/lib/apt/lists/*

COPY --from=native-build /native-root/usr/local/ /usr/local/

WORKDIR /app
COPY --from=build /app/parser-worker-publish/ ./

RUN ldconfig \
    && mkdir --parents /app/x64 /app/tessdata /var/run/fullworth-parser-tls \
    && english_model="$(find /usr/share -type f -path '*/tessdata/eng.traineddata' -print -quit)" \
    && test -n "$english_model" \
    && ln --symbolic /usr/local/lib/libleptonica.so /app/x64/libleptonica-1.85.0.dll.so \
    && ln --symbolic "$(find /usr/lib -type f -name 'libtesseract.so.*' -print -quit)" /app/x64/libtesseract55.dll.so \
    && ln --symbolic --force "$english_model" /app/tessdata/eng.traineddata \
    && chown --recursive "$APP_UID:$APP_UID" \
        /app \
        /var/run/fullworth-parser-tls

ENV ASPNETCORE_HTTP_PORTS=8081 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8081

USER $APP_UID

HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://localhost:8081/health/ready || exit 1

ENTRYPOINT ["dotnet", "FullWorth.ParserWorker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

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