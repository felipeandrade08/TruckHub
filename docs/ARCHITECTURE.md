# Arquitetura TruckHub V1

## Objetivo

Construir um computador de bordo pessoal para jogadores de **Euro Truck Simulator 2 (ETS2)**, com aplicativo Windows, telemetria, painel web, histórico de viagens, despesas, documentos, licença e atualizações.

## Componentes

```text
TruckHub
├── desktop
│   ├── TruckHub          # interface tablet/overlay Windows
│   ├── TruckHubConnector # integração ETS2/SCS Telemetry
│   └── TruckHubUpdater   # atualização segura do aplicativo
├── web
│   └── site              # landing, autenticação e dashboard inicial
├── api                   # backend HTTP
├── database              # schema e migrations PostgreSQL
├── assets                # identidade visual e recursos
└── docs                  # documentação técnica
```

## Princípios da V1

1. Priorizar o motorista individual.
2. Começar com infraestrutura gratuita durante os testes.
3. O servidor é a fonte de verdade para licença e pagamentos.
4. Telemetria deve alimentar automaticamente os dados sempre que o SDK disponibilizar o campo.
5. Dados locais devem continuar funcionando durante pequenas interrupções de internet e sincronizar depois.
6. Atualizações devem ser feitas por um processo separado do aplicativo principal.
7. Nenhum segredo deve entrar no repositório.
8. A identidade visual oficial deve ser preservada em todas as interfaces.

## Serviços planejados

- GitHub: código e histórico
- Cloudflare: camada web/API na fase inicial
- Neon PostgreSQL: banco na fase inicial
- Mercado Pago: pagamentos quando a etapa financeira for implementada

## Banco V1

O schema inicial está em `database/schema.sql` e contempla:

- usuários e credenciais armazenadas como hashes;
- licença Trial/Lifetime;
- vínculo de um dispositivo por licença;
- caminhões pessoais;
- viagens e eventos de telemetria;
- despesas;
- documentos internos do simulador;
- tacógrafos;
- pagamentos;
- versões e registros de atualização.

## Licenciamento

Estados principais:

```text
trial -> active/lifetime
trial -> expired
active/lifetime -> active/lifetime
```

A licença vitalícia usa `expires_at = NULL`.

O Trial começa no instante de criação da conta e termina exatamente 7 dias depois. A aplicação nunca deve confiar somente no relógio local do cliente.

## Próximas etapas

- [x] Estrutura do workspace
- [x] Projeto web inicial
- [x] Schema PostgreSQL
- [ ] API inicial
- [ ] Autenticação real
- [ ] Trial de 7 dias + geração de PIN
- [ ] Dashboard conectado à API
- [ ] Aplicativo Windows
- [ ] Integração Telemetry SDK
- [ ] Viagens/despesas/documentos/tacógrafo
- [ ] Mercado Pago + webhook
- [ ] Device binding real
- [ ] Atualizador automático
