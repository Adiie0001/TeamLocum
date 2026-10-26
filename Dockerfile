# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY TeamLocum.Web/TeamLocum.Web.csproj TeamLocum.Web/
RUN dotnet restore TeamLocum.Web/TeamLocum.Web.csproj
COPY TeamLocum.Web TeamLocum.Web
RUN dotnet publish TeamLocum.Web/TeamLocum.Web.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The default (Debian-based) image ships ICU and tzdata, which the app needs: it formats dates and money for en-GB and
# judges "now" in the clinic's IANA time zone. Do not switch to a variant that sets invariant globalization.
# TLS is expected to end at a reverse proxy, so trust its X-Forwarded-* headers (client addresses for rate limiting and
# the Secure flag on cookies). The SQLite file and the cookie/token keys live on one volume; migrations run at start.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ConnectionStrings__DefaultConnection="Data Source=/data/teamlocum.db" \
    DataProtection__KeysPath=/data/keys \
    Database__ApplyMigrationsOnStartup=true

RUN mkdir /data && chown $APP_UID:$APP_UID /data
VOLUME /data

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "TeamLocum.Web.dll"]
