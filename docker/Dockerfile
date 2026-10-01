# syntax=docker/dockerfile:1
# Baut den selbstenthaltenden PicCompressor-Webdienst samt nativem Jpegli-Wrapper und liefert ihn
# als Synology-tauglichen Dauerbetrieb-Container aus. Der Kontext ist das Repository-Wurzelverzeichnis:
#   docker build -f docker/Dockerfile -t piccompressor .
# Die Desktop-GUI ist nicht Teil des Images; Browseroberfläche und Scan-Worker laufen gemeinsam.
# Match global.json: the floating 10.0 image can move to an incompatible SDK feature band.
ARG DOTNET_VERSION=10.0.302

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
# TARGETARCH füllt buildx; ohne buildx ist es die Architektur des Build-Rechners.
ARG TARGETARCH=amd64
# build-native-linux.sh erzeugt „Unix Makefiles“ und übersetzt neben C++ auch C (zlib, libpng):
# `make` und `gcc` gehören deshalb genauso dazu wie `g++`.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        cmake make gcc g++ git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .

# Der native Wrapper wird für die Ziel-RID gebaut; ein nicht abgedecktes RID ist ein Buildfehler
# statt eines Pakets ohne Encoder (D-054).
RUN set -eu; \
    case "$TARGETARCH" in \
        amd64) architecture=x64 ;; \
        arm64) architecture=arm64 ;; \
        *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 2 ;; \
    esac; \
    echo "$architecture" > /tmp/architecture; \
    bash scripts/build-native-linux.sh Release --arch "$architecture"

# PicCompressorTargetRid wird als globale Eigenschaft gesetzt, nicht nur über die
# AdditionalProperties der Projektreferenz: sonst baut MSBuild Domain und Application unter zwei
# Sätzen globaler Eigenschaften und beide Instanzen schreiben gleichzeitig dieselbe deps.json.
RUN set -eu; \
    architecture="$(cat /tmp/architecture)"; \
    runtime="linux-$architecture"; \
    dotnet publish src/PicCompressor.Web/PicCompressor.Web.csproj \
        --configuration Release \
        --runtime "$runtime" \
        --self-contained true \
        --output /app \
        "-p:PicCompressorTargetRid=$runtime" \
        "-p:PicCompressorNativeLibrary=/src/artifacts/native/$runtime/piccompressor_native.so"; \
    test -x /app/PicCompressor.Web; \
    test -f /app/piccompressor_native.so; \
    jpegli="/src/native/build-$runtime/_deps/jpegli-src"; \
    mkdir -p /app/licenses; \
    cp LICENSE /app/licenses/PicCompressor-LICENSE; \
    cp "$jpegli/LICENSE" /app/licenses/jpegli-LICENSE; \
    cp "$jpegli/third_party/highway/LICENSE" /app/licenses/highway-LICENSE; \
    cp "$jpegli/third_party/skcms/LICENSE" /app/licenses/skcms-LICENSE; \
    cp "$jpegli/third_party/libpng/LICENSE" /app/licenses/libpng-LICENSE; \
    cp "$jpegli/third_party/zlib/LICENSE" /app/licenses/zlib-LICENSE

# Bookworm liefert glibc 2.36 und libstdc++ 12; die native Bibliothek verlangt GLIBC_2.29 und
# GLIBCXX_3.4.29 (Abschnitt 19). libicu wird für die Kulturformatierung gebraucht.
FROM debian:bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libicu72 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Der Lauf erfolgt ohne root. UID/GID sind Build-Argumente, damit die Kennung zu den
# eingehängten Ordnern des Hosts passt.
ARG UID=1000
ARG GID=1000
RUN groupadd --gid "$GID" piccompressor \
    && useradd --uid "$UID" --gid "$GID" --create-home --shell /usr/sbin/nologin piccompressor \
    && mkdir -p /config /data /state \
    && chown piccompressor:piccompressor /config /state

COPY --from=build /app /app
USER piccompressor
VOLUME ["/config", "/data", "/state"]
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    PicCompressor__ConfigurationPath=/config/piccompressor.json \
    PicCompressor__DataRoot=/data \
    PicCompressor__StateRoot=/state

# Exec-Form: Kestrel und der Scan-Worker sind gemeinsam PID 1 und empfangen SIGTERM direkt.
ENTRYPOINT ["/app/PicCompressor.Web"]
