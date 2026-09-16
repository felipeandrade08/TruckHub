# TruckHub API — Secrets e Deploy

Este arquivo não contém valores secretos. Os valores reais devem ficar somente em Neon, Cloudflare e GitHub Secrets.

## Secrets do Cloudflare Worker

- `DATABASE_URL`
- `RATE_LIMIT_PEPPER`
- `MERCADOPAGO_ACCESS_TOKEN`
- `MERCADOPAGO_WEBHOOK_SECRET`
- `TRUCKHUB_ADMIN_EMAIL` — e-mail administrativo inicial
- `TRUCKHUB_ADMIN_BOOTSTRAP_PASSWORD` — senha administrativa inicial forte; usada somente se o admin ainda não existir no banco

Depois do primeiro cadastro administrativo, a senha fica armazenada como hash no PostgreSQL. A senha de bootstrap não deve ser reutilizada e pode ser removida/rotacionada no Secret Manager.

## Vars de produção

- `APP_NAME=TruckHub API`
- `ENVIRONMENT=production`
- `WEB_ORIGIN=https://app.truckhub.com.br`
- `TRUCKHUB_PUBLIC_URL=https://truckhub.com.br`
- `TRUCKHUB_API_PUBLIC_URL=https://api.truckhub.com.br`
- `TRUCKHUB_LIFETIME_PRICE_BRL=49.90`

## Admin Center

Painel: `/admin/`

Rotas protegidas por sessão administrativa:
- `POST /admin/auth/login`
- `POST /admin/auth/logout`
- `GET /admin/me`
- `GET /admin/dashboard`
- `GET /admin/users`
- `POST /admin/users/:id/status`
- `POST /admin/licenses/:id/status`
- `GET /admin/audit`

A sessão administrativa usa cookie `HttpOnly`, `Secure`, `SameSite=Strict`, duração de 8 horas e token aleatório armazenado somente como hash.

Toda alteração administrativa relevante gera registro em `admin_audit_logs`.

O painel financeiro continua separado em `/admin/finance/` e não deve ser incorporado às rotas do cliente.

## GitHub Actions Secrets

- `DATABASE_URL`
- `CLOUDFLARE_API_TOKEN`
- `CLOUDFLARE_ACCOUNT_ID`

Nunca colocar Secrets no `wrangler.toml`, frontend ou Git.
