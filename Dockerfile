FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Restore do csproj da Api (não upkeep.sln): tests/ está fora do build context
# (.dockerignore) e o restore da solution falharia por projetos ausentes; o csproj
# puxa Core+Infrastructure transitivamente.
RUN dotnet restore src/Upkeep.Api/Upkeep.Api.csproj \
 && dotnet publish src/Upkeep.Api/Upkeep.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Upkeep.Api.dll"]
