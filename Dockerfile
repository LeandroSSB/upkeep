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
COPY . .
# Restore do csproj da Api (não upkeep.sln): tests/ está fora do build context
# (.dockerignore) e o restore da solution falharia por projetos ausentes; o csproj
# puxa Core+Infrastructure transitivamente.
RUN dotnet restore src/Upkeep.Api/Upkeep.Api.csproj \
 && dotnet publish src/Upkeep.Api/Upkeep.Api.csproj -c Release -o /app --no-restore

# Chiseled: ~120MB a menos que o aspnet:10.0 completo; sem ICU/glibc apps extras —
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
