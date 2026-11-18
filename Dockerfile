# Troop finances as a container image, for any host that runs containers: Fly.io, Render, Railway,
# DigitalOcean App Platform, Azure Container Apps, Google Cloud Run, Cloudflare Containers, or a VPS
# with Docker. Settings come from environment variables (see infra/HOSTING.md); nothing secret is
# baked into the image. The Azure App Service deployment (infra/deploy.ps1) doesn't use this file.
#
#   docker build -t troop-finances .
#   docker compose up            # the app with SQL Server, MinIO and Mailpit, for trying it out

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on its own layer, so code changes don't re-download every package.
COPY global.json ./
COPY src/BsaTroopFinances/BsaTroopFinances.csproj src/BsaTroopFinances/
# PublishReadyToRun here too: restore is what downloads the ReadyToRun compiler (Crossgen2) that
# the publish step below needs, and that step runs with --no-restore.
RUN dotnet restore src/BsaTroopFinances/BsaTroopFinances.csproj -r linux-x64 -p:PublishReadyToRun=true

COPY src/BsaTroopFinances/ src/BsaTroopFinances/
# The version and commit shown in the footer (Services/AppVersion.cs). The GitHub build passes
# them in (--build-arg); a host that builds this file itself leaves them empty.
ARG APP_VERSION=
ARG SOURCE_COMMIT=

# ReadyToRun compiles ahead of time, so a host that stops the app when idle starts it again
# in a couple of seconds rather than five or more.
RUN dotnet publish src/BsaTroopFinances/BsaTroopFinances.csproj -c Release -r linux-x64 --self-contained false \
        --no-restore -p:PublishReadyToRun=true \
        -p:AppVersion="$APP_VERSION" -p:SourceRevisionId="$SOURCE_COMMIT" -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# fontconfig lets the PDF library (invoices, reports) find fonts; DejaVu is a small, complete set
# to find. Time zone data (for Pacific dates) and ICU are already in the base image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Plain HTTP on 8080 inside the container; the host's proxy serves HTTPS in front of it.
# FORWARDEDHEADERS_ENABLED makes the app trust that proxy's X-Forwarded-Proto/-For headers, so it
# knows requests really arrived over HTTPS (sign-in return addresses, secure cookies).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

EXPOSE 8080

# The base image's non-root user. A FileSystem store (Storage:Provider) needs a volume it can write to.
USER $APP_UID

# Hosts check GET /healthz themselves (the image has no curl to do it from inside).
ENTRYPOINT ["dotnet", "BsaTroopFinances.dll"]
