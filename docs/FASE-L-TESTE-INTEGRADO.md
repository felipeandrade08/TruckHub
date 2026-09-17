# FASE L — TESTE INTEGRADO COMPLETO

## Objetivo
Validar a cadeia completa A–K antes da build final da Fase M, sem alterar a versão e sem gerar build de release.

## Estado
Implementação da auditoria integrada concluída na `main`.

## Cadeia validada estruturalmente
`ETS2/ATS → Connector → TransPoli → Tablet → viagem → carga → odômetro → combustível → abastecimento → despesa → finalização → economia → garagem → histórico → estatísticas → API → banco → painel web`

## Auditoria automatizada
Foi criado `api/scripts/phase-l-audit.mjs` e o comando:

```bash
cd api
npm run phase:l
```

A auditoria verifica:
- versão ainda em `1.0.13`;
- módulos principais registrados na API;
- rotas de telemetria e eventos;
- rejeição de odômetro regressivo;
- intervalo mínimo de telemetria;
- cálculo server-side de distância/fuel na finalização;
- liquidação econômica da viagem;
- deduplicação de eventos;
- sincronização desktop → `/me/events`;
- envio de telemetria;
- recuperação de viagem ativa;
- F10 registrado exclusivamente no `MainWindow`;
- F10 ligado ao abrir/fechar o tablet;
- dashboard web usando `/me/dashboard-advanced`.

## Checklist integrado
1. Conectar ETS2/ATS — estruturalmente preparado; teste físico depende do PC/jogo.
2. Detectar caminhão — implementado via telemetria/save scanner.
3. Marca/modelo/placa — propagados pela telemetria e garagem.
4. Exclusividade — validada pela camada de garagem.
5. Iniciar viagem — criação automática no servidor.
6. Distância — derivada do odômetro/telemetria.
7. Combustível — telemetria e fechamento server-side.
8. Abastecimento — detecção automática + confirmação de preço/posto.
9. Preço por litro — informado somente na confirmação.
10. Despesa — lançamento na API.
11. Finalizar viagem — endpoint fecha e liquida a economia.
12. Economia — `settleTripEconomy` no fechamento.
13. Uso do caminhão — histórico da garagem.
14. Histórico — viagens finalizadas e dados financeiros.
15. Estatísticas — módulo de estatísticas registrado na API/tablet.
16. Web — dashboard avançado consome dados reais.
17. Perda de conexão — fila local de eventos preserva sincronização.
18. Reconexão — supervisor do connector e fila de sincronização.
19. Reinício do app — estado local e recuperação de viagem ativa.
20. Recuperação de viagem ativa — busca viagem `active` e reconstrói o estado do tablet.
21. F10 — exclusivo para abrir/fechar o tablet.

## Testes que não podem ser simulados remotamente
A execução real de ETS2/ATS, perda física da conexão com o jogo, reconexão do Connector, reinício do executável e validação visual do tablet exigem o ambiente Windows do usuário. Não foram marcados como “passou” sem executar o jogo.

## Build e versão
- Nenhuma build foi disparada nesta fase.
- `VERSION` permanece `1.0.13`.
- V1.0.14 continua reservada exclusivamente para a Fase M.

## Próxima fase
Fase M — revisão final, build, correções finais de compilação, testes de instalação/atualização, Inno Setup e liberação da V1.0.14.
