FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ChatApp.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release --no-restore -o /out
RUN dotnet tool install dotnet-ef --version 10.0.4 --tool-path /tools \
    && /tools/dotnet-ef migrations bundle --configuration Release --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "ChatApp.dll"]
