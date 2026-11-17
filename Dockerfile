# syntax=docker/dockerfile:1.7

# --- Web build ---------------------------------------------------------------
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
# vite.config.ts writes to ../server/MiaoYan.Web/wwwroot (i.e. /src/server/...).
RUN npm run build

# --- Server build ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/MiaoYan.Web/MiaoYan.Web.csproj server/MiaoYan.Web/
RUN dotnet restore server/MiaoYan.Web/MiaoYan.Web.csproj
COPY server/ server/
COPY --from=web /src/server/MiaoYan.Web/wwwroot/ server/MiaoYan.Web/wwwroot/
RUN dotnet publish server/MiaoYan.Web/MiaoYan.Web.csproj \
      -c Release -o /out --no-restore /p:UseAppHost=false

# --- Runtime -----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG PLANTUML_VERSION=1.2025.4
ENV ASPNETCORE_URLS=http://0.0.0.0:5080 \
    DOTNET_EnableDiagnostics=0 \
    MIAOYAN_PLANTUML_JAR=/opt/plantuml/plantuml.jar

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl default-jre-headless ffmpeg gosu tini \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /opt/plantuml \
 && curl -fsSL --retry 3 \
      "https://github.com/plantuml/plantuml/releases/download/v${PLANTUML_VERSION}/plantuml-${PLANTUML_VERSION}.jar" \
      -o /opt/plantuml/plantuml.jar \
 # The .NET 10 base image may already use GID/UID 1000 (for example the
 # `ubuntu` user). Create the miaoyan group/user only when the IDs are free;
 # gosu below uses numeric 1000:1000, so an existing account is fine.
 && (getent group 1000 >/dev/null || groupadd -g 1000 miaoyan) \
 && (getent passwd 1000 >/dev/null || useradd -u 1000 -g 1000 -M -d /app -s /usr/sbin/nologin miaoyan) \
 && mkdir -p /data \
 && chown 1000:1000 /data

WORKDIR /app
COPY --from=build --chown=1000:1000 /out/ ./
COPY --chown=1000:1000 docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod 755 /usr/local/bin/entrypoint.sh

# No USER here: a plain `docker run` starts as root so the entrypoint can fix
# ownership of an empty /data volume, then drops to 1000. LazyCat sets
# run_as: "1000:1000" and the entrypoint sees uid 1000, so it does not chown.
EXPOSE 5080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:5080/healthz >/dev/null || exit 1

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "MiaoYan.Web.dll"]
