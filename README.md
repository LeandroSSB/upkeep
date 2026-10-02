# upkeep 🔧

API para rastreio de manutenção de ativos — carro, casa, aparelhos. Registre o que precisa de manutenção periódica (troca de óleo a cada 10.000 km, filtro de ar a cada 6 meses, revisão do boiler anual...) e a API calcula **o que está vencendo, o que vence em breve e quanto você já gastou**.

## Como funciona

1. **Ativos** — cadastre o que você mantém: `Corsa 2012` (veículo, com odômetro), `Apartamento` (casa), `Geladeira Brastemp` (aparelho).
2. **Templates de manutenção** — defina a periodicidade: por **km**, por **meses**, ou ambos (vale o que vencer primeiro).
3. **Serviços realizados** — lance o que foi feito (data, km, custo). Isso reseta o ciclo daquele template.
4. **Status automático** — a API responde por template: `ok` · `vence_em_breve` · `vencido`, combinando km rodado e tempo decorrido desde a última manutenção.
5. **Relatórios** — custo total e por período, por ativo.

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | [.NET 10](https://dotnet.microsoft.com/) (LTS) |
| API | ASP.NET Core **Minimal APIs** (`MapGroup`, endpoint filters) |
| Dados | **EF Core 10** + Npgsql · PostgreSQL 18 |
| Auth | **JWT Bearer** + refresh tokens rotativos (hash no banco) |
| Validação | FluentValidation |
| Docs | OpenAPI nativo + Scalar UI |
| Logging | Serilog (JSON estruturado) |
| Testes | xUnit + `WebApplicationFactory` + **Testcontainers** |
| CI/CD | GitHub Actions · Docker multi-stage |

## Modelo de dados

```
User                id, email, password_hash
Asset               id, user_id, nome, tipo(veiculo|casa|aparelho), odometro_atual?
MaintenanceTemplate id, asset_id, título, intervalo_km?, intervalo_meses?,
                    custo_estimado?, categoria?, baseline_odometer?, baseline_data
ServiceRecord       id, asset_id, template_id?, data, odometro?, custo, notas
RefreshToken        id, user_id, token_hash, expires_at, revoked_at?
```

**Regra central** — `DueCalculator` (função pura em `Upkeep.Core`): dado o template + baseline (último serviço ou criação) + odômetro atual + hoje → status `vencido` / `vence_em_breve` (≤ 20% do intervalo ou ≤ 30 dias) / `ok`.

## Endpoints

```
POST   /auth/register · /auth/login · /auth/refresh
GET    /assets                          lista com status agregado
POST   /assets/{id}/odometer            atualiza km → recálculo
CRUD   /assets · /assets/{id}/templates
POST   /assets/{id}/services            lança serviço (reseta ciclo)
GET    /reports/costs                   ?assetId&from&to
GET    /health · /health/ready
```

Docs interativas em `/scalar` quando a API sobe. Multiusuário: isolamento total por `user_id` do JWT.

## Estrutura

```
src/Upkeep.Api/             endpoints, auth, middleware
src/Upkeep.Core/            entidades + DueCalculator (puro, sem dependências)
src/Upkeep.Infrastructure/  DbContext, migrations, repositórios
tests/Upkeep.UnitTests/     DueCalculator (rápido)
tests/Upkeep.IntegrationTests/  API real + Postgres via Testcontainers
```

## Rodando

```bash
# dev (no megalan ou onde houver Docker):
docker compose up -d          # postgres + api

# CI (GitHub Actions): build + testes com Testcontainers

# deploy (futuro, megalan):
docker compose -f docker-compose.yml -f docker-compose.prod.yml --env-file .env.production up -d --build
```

## Roadmap

- [x] **Fase 1** — Setup da solution + auth completa (register/login/refresh rotativo)
- [x] **Fase 2** — Ativos, templates e serviços (CRUD + validação)
- [x] **Fase 3** — `DueCalculator` + status agregado + relatório de custos
- [x] **Fase 4** — Docker multi-stage, CI GitHub Actions, deploy no megalan
- [x] **Lembretes via ntfy** quando algo vence (opt-in por usuário, varredura diária)
- [ ] **Frontend React** consumindo a API
