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
# Não roda como root: usuário app da imagem base (ENV APP_UID=1654). API não escreve
# em disco (Serilog Console) e escuta na 8080 (>1024) — sem necessidade de root.
USER $APP_UID
ENTRYPOINT ["dotnet", "Upkeep.Api.dll"]
