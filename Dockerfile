FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source
COPY global.json HandmadeMarketplace.sln ./
COPY src/HandmadeMarketplace.Domain/*.csproj src/HandmadeMarketplace.Domain/
COPY src/HandmadeMarketplace.Application/*.csproj src/HandmadeMarketplace.Application/
COPY src/HandmadeMarketplace.Infrastructure/*.csproj src/HandmadeMarketplace.Infrastructure/
COPY src/HandmadeMarketplace.Web/*.csproj src/HandmadeMarketplace.Web/
RUN dotnet restore src/HandmadeMarketplace.Web/HandmadeMarketplace.Web.csproj
COPY src/ src/
RUN dotnet publish src/HandmadeMarketplace.Web/HandmadeMarketplace.Web.csproj -c Release --no-restore -o /publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /publish .
RUN mkdir -p /app/wwwroot/uploads /app/keys && chown -R app:app /app/wwwroot/uploads /app/keys
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "HandmadeMarketplace.Web.dll"]
