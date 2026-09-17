# TruckHub — preparação Neon + Mercado Pago

Este documento deixa a integração pronta sem armazenar credenciais no GitHub.

## 1. Neon

Criar um projeto PostgreSQL no Neon somente quando for iniciar a infraestrutura real.

Depois de criar o projeto:

1. Copiar a `DATABASE_URL` de conexão.
2. Manter `sslmode=require` na URL.
3. Configurar `DATABASE_URL` como Secret no ambiente da API.
4. Executar:

```bash
cd api
npm install
npm run db:migrate
npm run db:audit
```

O migration runner usa `truckhub_schema_migrations` e um advisory lock para impedir duas implantações simultâneas de migrações. Não editar migrations que já tenham sido aplicadas; criar uma nova migration para qualquer alteração futura.

## 2. Mercado Pago

A API espera estas variáveis como Secrets:

```text
MERCADOPAGO_ACCESS_TOKEN
MERCADOPAGO_WEBHOOK_SECRET
```

E estas configurações públicas:

```text
TRUCKHUB_PUBLIC_URL=https://site-publico
TRUCKHUB_API_PUBLIC_URL=https://api-publica
TRUCKHUB_LIFETIME_PRICE_BRL=49.90
```

As URLs públicas devem ser HTTPS.

## 3. Fluxo de pagamento

```text
Usuário autenticado
  -> POST /payments/checkout
  -> TruckHub cria payment pending
  -> Mercado Pago Checkout
  -> Mercado Pago envia webhook
  -> assinatura HMAC validada
  -> API consulta /v1/payments/{id}
  -> external_reference conferida
  -> valor e moeda conferidos
  -> payment atualizado
  -> licença convertida para lifetime/active
```

O redirect de sucesso nunca é tratado como prova de pagamento. A confirmação oficial vem do webhook + consulta do pagamento no Mercado Pago.

## 4. Segurança

Nunca colocar Access Token, Webhook Secret ou `DATABASE_URL` em arquivos versionados.

No Cloudflare Workers, usar Secrets/Variables do ambiente de produção. No desenvolvimento local, usar `.dev.vars`, que deve permanecer fora do Git.

## 5. Checklist antes de produção

- [ ] Projeto Neon criado
- [ ] `DATABASE_URL` configurada como Secret
- [ ] `npm run db:migrate` executado
- [ ] `npm run db:audit` sem falhas críticas
- [ ] Aplicação Mercado Pago criada
- [ ] Access Token configurado como Secret
- [ ] Webhook Secret configurado como Secret
- [ ] URL pública da API configurada
- [ ] URL pública do site configurada
- [ ] preço vitalício conferido
- [ ] webhook apontando para `/payments/mercadopago/webhook`
- [ ] pagamento de teste aprovado e licença verificada no banco
- [ ] pagamento com valor divergente não libera licença

## 6. Regra importante

Credenciais reais entram somente no ambiente de execução. O repositório contém apenas código, migrations, exemplos e documentação com placeholders.
