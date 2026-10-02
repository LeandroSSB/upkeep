# --- SPA: build Vite/React em node (web/dist) ---
# Lockfile versionado é obrigatório p/ npm ci (build reproduzível).
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Layer caching: csprojs + Directory.Build.props ANTES do código → camada de restore
# só invalida quando projeto/deps mudam (commit de código não repaga o restore).
# tests/ está fora do build context (.dockerignore): restore pelo csproj da Api (puxa
# Core+Infrastructure transitivamente), não pelo upkeep.sln.
COPY Directory.Build.props .
COPY src/Upkeep.Core/Upkeep.Core.csproj src/Upkeep.Core/
COPY src/Upkeep.Infrastructure/Upkeep.Infrastructure.csproj src/Upkeep.Infrastructure/
COPY src/Upkeep.Api/Upkeep.Api.csproj src/Upkeep.Api/
RUN dotnet restore src/Upkeep.Api/Upkeep.Api.csproj
COPY . .
RUN dotnet publish src/Upkeep.Api/Upkeep.Api.csproj -c Release -o /app --no-restore

# Chiseled: ~160MB a menos que o aspnet:10.0 completo; sem ICU/glibc apps extras —
# exige <InvariantGlobalization>true</InvariantGlobalization> no csproj da Api.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
# SPA entra no wwwroot DEPOIS do publish da API: StaticFiles/Fallback servem o
# index.html; endpoints da API continuam intactos (fallback só pega não-match).
COPY --from=web /web/dist ./wwwroot
EXPOSE 8080
# Não roda como root: usuário app da imagem base (ENV APP_UID=1654, presente também
# no chiseled). API não escreve em disco (Serilog Console) e escuta na 8080 (>1024).
USER $APP_UID
ENTRYPOINT ["dotnet", "Upkeep.Api.dll"]
