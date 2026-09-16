# Mercado Pago — TruckHub V1

A integração usa o Checkout Pro e mantém o **webhook como fonte de verdade** para liberar a licença vitalícia.

## Variáveis de ambiente

Configure no ambiente da API somente quando formos conectar o Mercado Pago:

```text
MERCADOPAGO_ACCESS_TOKEN=SEU_ACCESS_TOKEN
MERCADOPAGO_WEBHOOK_SECRET=SUA_CHAVE_SECRETA_DO_WEBHOOK
TRUCKHUB_PUBLIC_URL=https://SEU_SITE_PUBLICO
TRUCKHUB_API_PUBLIC_URL=https://SUA_API_PUBLICA
TRUCKHUB_LIFETIME_PRICE_BRL=SEU_PRECO
```

Nenhum segredo deve ser salvo no GitHub.

## Fluxo implementado

1. Usuário autenticado chama `POST /payments/checkout`.
2. A API cria um registro `pending` em `payments`.
3. A API cria uma preferência no Mercado Pago e devolve `checkoutUrl`.
4. O usuário é redirecionado para o Checkout Pro.
5. Mercado Pago envia o evento para `POST /payments/mercadopago/webhook` na API pública.
6. A API valida `x-signature` com HMAC-SHA256.
7. A API consulta o pagamento diretamente no Mercado Pago antes de tomar qualquer decisão.
8. A API confere referência externa, valor e moeda.
9. Somente pagamento `approved` com valor/moeda corretos libera `licenses.license_type = lifetime`, `status = active` e `expires_at = NULL`.
10. Eventos repetidos são tratados de forma idempotente.

## Retorno do Checkout

As URLs de retorno preparadas no site são:

- `/pagamento-sucesso.html`
- `/pagamento-pendente.html`
- `/pagamento-falha.html`

O retorno do navegador **não libera a licença**. Ele serve apenas para UX; a confirmação real vem do webhook.

## Endpoints

```text
POST /payments/checkout
POST /payments/mercadopago/webhook
GET  /payments/status/:externalReference
```

A documentação oficial do Mercado Pago confirma que a preferência retorna um `init_point` para o Checkout Pro e que o estado do pagamento deve ser consultado após a notificação. Os Webhooks devem ser validados pela assinatura secreta `x-signature`.
