# Build from the repository root:
#   docker build -f src/CrushRoyale.Server/Dockerfile -t crushroyale-api .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /repo
COPY src/CrushRoyale.Core/ src/CrushRoyale.Core/
COPY src/CrushRoyale.Contracts/ src/CrushRoyale.Contracts/
COPY src/CrushRoyale.Server/ src/CrushRoyale.Server/
RUN dotnet publish src/CrushRoyale.Server/CrushRoyale.Server.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CrushRoyale.Server.dll"]
