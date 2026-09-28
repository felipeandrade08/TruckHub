# TransPoli v1.0.32 — residência de dados e orçamento de API

Esta matriz é uma regra arquitetural da v1.0.32. A API é reservada para informação oficial/compartilhada; telemetria e estado operacional de alta frequência permanecem locais.

| Módulo | Local | Servidor | Sync | Temporário | Configuração |
|---|---|---|---|---|---|
| Identidade | token/user id protegido e cache de sessão | users.id, empresa, papel e permissões | autenticação/heartbeat | estado de login em andamento | dispositivo |
| Viagem | TripSession, telemetria resumida, fechamento e recuperação | contrato, viagem consolidada e settlement | trip.start/trip.finish idempotentes | amostras instantâneas | parâmetros operacionais |
| Mercado de Cargas | última seleção/contrato em uso | catálogo e tarifa oficial | aceite/início da viagem | filtros/contagem regressiva da tela | política de tarifas no servidor |
| Bank | cache/ledger operacional e pendências | saldo, ledger, empréstimos e settlements oficiais | despesas e fechamento via outbox | preview da viagem | parâmetros financeiros oficiais |
| PoliPass | recibo e evento físico do pedágio | débito/expense oficial | sourceKey determinístico via outbox | pulso ETS2 de pedágio | nenhuma tarifa inventada no cliente |
| Abastecimento | recibo, evento físico e despesa operacional | pagamento/expense oficial | sourceKey determinístico via outbox | variação de tanque/estado de detecção | preço informado na confirmação |
| Manutenção | desgaste ETS2 e registro pendente | histórico técnico + expense atômicos | sourceKey determinístico via outbox | leitura instantânea de desgaste | política de manutenção |
| Documentos/DANFE | documento operacional e carimbo | documento/evento oficial compartilhado | invoice-stamped:<invoiceId> via outbox | estado do modal | numeração/política documental |
| Tacógrafo | sessão, contadores e ticket/arquivo | somente consolidação que precise ser compartilhada | junto da viagem/documento quando aplicável | relógio/status visual | limites/regras |
| Ranking | cache de apresentação | ranking oficial | nenhuma pontuação local promovida | filtros da tela | regras oficiais |
| Diretoria | cache de tela | fonte oficial corporativa | não lê SQLite do motorista | seleção/filtros | permissões |
| Garagem | veículo ETS2 detectado/cache | frota oficial | vínculo explícito de ativo | telemetria do veículo atual | preferências locais |
| Outbox | fila durável por users.id | ACK da operação oficial | retry idempotente/backoff | estado de envio | intervalo/política de retry |

## Regras de orçamento

- Não enviar telemetria bruta de alta frequência para a API.
- Abrir/trocar abas não deve repetir GET enquanto o cache ainda é válido.
- Escritas operacionais usam outbox e chave idempotente; evitar HTTP direto + fallback.
- Invalidar cache quando um evento muda a verdade, mas buscar novamente somente quando uma tela/consumidor precisar.
- Agrupar/deduplicar operações; enviar delta/resumo em vez de estado completo quando possível.
- Polling remoto agressivo é proibido. Timers de UI/telemetria local não contam como polling de API.
- Meta operacional: permanecer confortavelmente abaixo do limite de aproximadamente 100.000 requests/dia.
