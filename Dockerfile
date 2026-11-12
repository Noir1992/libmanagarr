# syntax=docker/dockerfile:1

# ---- Stage 1: build the Angular frontend -------------------------------------------
FROM node:22 AS frontend-build
WORKDIR /build
# npm 10 (bundled) crashes with "Exit handler never called!" on large trees; the repo pins npm 11.
RUN npm install -g npm@11.12.1
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---- Stage 2: publish the backend and merge the SPA into wwwroot -------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /build
# The running instance reports this value through /api/releases; the release workflow passes
# the release version, the CI workflow an "edge-sha-<short>" identifier for edge installs.
ARG LIBMANAGARR_VERSION=0.0.0-dev
COPY backend/ ./
RUN dotnet publish LibManagarr.Api/LibManagarr.Api.csproj -c Release -o /publish --nologo -p:InformationalVersion=${LIBMANAGARR_VERSION}
COPY --from=frontend-build /build/dist/libmanagarr/browser /publish/wwwroot

# ---- Stage 3: minimal runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend-build /publish ./
# HTTP_PORTS=8080 is already provided by the aspnet base image; setting ASPNETCORE_URLS
# here would only add a startup warning about overriding it.
EXPOSE 8080
# Persistent state: the config directory (settings.json incl. API keys, libmanagarr.db, log
# files) lives in /config via LIBMANAGARR_CONFIG_DIR so it can be mounted on its own; the
# poster cache stays in /app/data below the content root. Mount volumes on both.
ENV LIBMANAGARR_CONFIG_DIR=/config
VOLUME /config
VOLUME /app/data
ENTRYPOINT ["dotnet", "LibManagarr.Api.dll"]
