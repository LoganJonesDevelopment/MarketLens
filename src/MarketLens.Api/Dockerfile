FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/MarketLens.Core/MarketLens.Core.csproj                       src/MarketLens.Core/
COPY src/MarketLens.Infrastructure/MarketLens.Infrastructure.csproj   src/MarketLens.Infrastructure/
COPY src/MarketLens.Api/MarketLens.Api.csproj                         src/MarketLens.Api/
RUN dotnet restore src/MarketLens.Api/MarketLens.Api.csproj

COPY src/ src/
RUN dotnet publish src/MarketLens.Api/MarketLens.Api.csproj \
    -c Release \
    -o /app \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

EXPOSE 8080

ENTRYPOINT ["dotnet", "MarketLens.Api.dll"]
