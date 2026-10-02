# upkeep — Design da API

Data: 2026-10-02 · Status: aprovado

## Visão geral

API multiusuário para rastreio de manutenção de ativos (veículos, imóveis, aparelhos). O usuário registra ativos, define manutenções periódicas (por km e/ou tempo) e lança os serviços realizados. A API calcula quando cada manutenção vence e acumula custos.

**Objetivo primário do projeto**: aprender o ecossistema .NET com um problema real — por isso a stack é 100% nativa (sem frameworks de terceiros escondendo o ASP.NET Core).

## Requisitos funcionais

1. Registrar-se com email + senha; login com emissão de access token (JWT, 15 min) + refresh token (7 dias, rotativo).
2. CRUD de ativos (`veiculo` | `casa` | `aparelho`), com odômetro opcional (só faz sentido para veículo).
3. CRUD de templates de manutenção por ativo: título, `intervalo_km` e/ou `intervalo_meses`, custo estimado, categoria opcional.
4. Lançar serviços realizados (data, odômetro opcional, custo, notas) — reseta o ciclo do template correspondente.
5. Atualizar odômetro do ativo; status recalculado on-demand.
6. Cálculo de status por template: `ok` | `vence_em_breve` | `vencido`.
7. Relatório de custos por ativo e por período.

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| API | ASP.NET Core Minimal APIs (`MapGroup`, endpoint filters) |
| Dados | EF Core 10 + Npgsql · PostgreSQL 18 (Docker) |
| Auth | JWT Bearer + `PasswordHasher<T>` (Identity Core só p/ hash) · refresh tokens persistidos (hash) |
| Validação | FluentValidation |
| Docs | OpenAPI nativo + Scalar UI |
| Logging | Serilog (estructurado, console JSON em prod) |
| Testes | xUnit · `WebApplicationFactory` + Testcontainers |
| CI | GitHub Actions (build + testes) |
| Deploy | Docker multi-stage → megalan (nginx + TLS) |

## Estrutura

```
upkeep/
├── src/Upkeep.Api/             — endpoints, auth, middleware, DI
├── src/Upkeep.Core/            — entidades + regra de vencimento (pura, sem dependências)
├── src/Upkeep.Infrastructure/  — DbContext, migrations, repositórios
├── tests/Upkeep.UnitTests/     — regra de vencimento (rápido, sem container)
├── tests/Upkeep.IntegrationTests/ — WebApplicationFactory + Testcontainers
├── docker-compose.yml          — dev: postgres + api
└── README.md
```

## Modelo de dados

```
User           (id, email único, password_hash, created_at)
RefreshToken   (id, user_id, token_hash, expires_at, revoked_at?, created_at)
Asset          (id, user_id, nome, tipo, odometro_atual?, notas, created_at)
MaintenanceTemplate (id, asset_id, título, categoria?, intervalo_km?,
                    intervalo_meses?, custo_estimado?, baseline_odometer?,
                    baseline_data, created_at)
ServiceRecord  (id, asset_id, template_id?, data, odometro?, custo, notas,
                created_at)
```

Constraints: template exige **pelo menos um** dos intervalos (km ou meses); `odometro_atual`/`odometro` só em ativos tipo `veiculo`; service com `template_id` deve pertencer ao mesmo asset.

## Regra de vencimento (Upkeep.Core)

Dado: template + base (último `ServiceRecord` do template, senão baseline do template) + odômetro atual + hoje.

- **Por km**: vence quando `odometro_atual >= base.odometro + intervalo_km`. Sem odômetro informado → critério inativo.
- **Por tempo**: vence quando `hoje >= base.data + intervalo_meses`.
- **Status**: `vencido` se qualquer critério ativo estourou; `vence_em_breve` se falta ≤ 20% do intervalo total (km ou meses) ou ≤ 30 dias no critério tempo, o que disparar primeiro; `ok` caso contrário.

Função pura `DueCalculator.Evaluate(template, baseline, odometer, today) → DueResult { Status, KmRemaining?, DateDue? }` — testável isoladamente.

## Endpoints

```
POST   /auth/register          201 { accessToken, refreshToken, user }
POST   /auth/login             200 { accessToken, refreshToken, user }
POST   /auth/refresh           200 { accessToken, refreshToken }  (rotaciona)
GET    /assets                 200 [ asset + status agregado dos templates ]
POST   /assets                 201
PUT    /assets/{id}            200
DELETE /assets/{id}            204
POST   /assets/{id}/odometer   200 { asset }  (body: { odometer })
GET    /assets/{id}/templates  200 [ template + status ]
POST   /assets/{id}/templates  201
PUT    /templates/{id}         200
DELETE /templates/{id}         204
POST   /assets/{id}/services   201  (body: templateId?, data, odometer?, custo, notas?)
GET    /assets/{id}/services   200 [ histórico ]
GET    /reports/costs          200  (query: assetId?, from?, to?)
GET    /health                 200  (liveness; db check em /health/ready)
```

Multi-tenant: todo endpoint autenticado filtra por `user_id` do claim `sub`. Acesso a recurso de outro usuário → 404 (não vaza existência).

## Tratamento de erros

- Middleware global de exceções → `ProblemDetails` (RFC 7807) com `traceId`; 500 sem stack em prod.
- Validação (FluentValidation) → 400 com `errors[]` por campo.
- Auth expirada → 401 padrão; recurso alheio → 404.

## Testes

- **Unit** (`Upkeep.Core`): `DueCalculator` — só tempo, só km, ambos, sem serviço prévio (baseline), limites de "vence em breve", veículo sem odômetro.
- **Integração**: fluxo completo de auth (register/login/refresh/rotação/revogação), CRUD ativo+template+service, isolamento multiusuário (404 cruzado), odômetro → recálculo de status, relatório de custos.
- Regra WSL2: testes rodam no CI (GitHub Actions) ou no megalan via Docker — nunca local.

## CI/CD

- GitHub Actions: `dotnet build` + `dotnet test` (runner ubuntu tem Docker p/ Testcontainers).
- Docker multi-stage (SDK → runtime); imagem final ~120 MB.
- Deploy (fase futura): megalan, `docker compose`, porta alta em 127.0.0.1 (ex. 14010), nginx + certbot seguindo a convenção padrão.

## Fora de escopo (YAGNI)

- Anexos/fotos em serviços · frontend (fase futura, React consumindo a API) · notificações push (fase futura: ntfy) · API pública/3rd-party · rate limiting sofisticado (só o nativo básico) · múltiplos idiomas.
