# kbo on the co-creative platform: one Dockerfile, one final stage per component.
# platform.images.json builds '--target web' (web app) and '--target build' (job 'build').
# The platform never sets command/args: each ENTRYPOINT decides what runs.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS publish
WORKDIR /src
COPY . .
RUN dotnet publish Net.Code.Kbo.Web/Net.Code.Kbo.Web.csproj -c Release -o /out/web \
 && dotnet publish Net.Code.Kbo.Build/Net.Code.Kbo.Build.csproj -c Release -o /out/build

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS web
WORKDIR /app
COPY --from=publish /out/web .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Net.Code.Kbo.Web.dll"]

# Monthly release builder (Container Apps job). Builds on the container's local (ephemeral, 8 GiB at 4 vCPU) disk:
# SQLite can't write a database on the SMB share ("database is locked": no byte-range locks), and the build fits
# locally (4.46 GB db, journal off, temp store in memory). The finished file is then copied to the share.
FROM mcr.microsoft.com/dotnet/runtime:11.0 AS build
WORKDIR /app
COPY --from=publish /out/build .
USER $APP_UID
ENTRYPOINT ["dotnet", "kbo.dll", "release", "--share", "/mnt/kbo", "--work", "/tmp/kbo-build"]
