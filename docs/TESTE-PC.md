# TruckHub — checklist de testes no PC

## Etapa 12 — Telemetria desktop
1. Abrir ETS2.
2. Confirmar `scs-telemetry.dll` em `bin/win_x64/plugins`.
3. Compilar `TruckHubConnector`.
4. Executar `TruckHubConnector.exe`.
5. Confirmar `http://127.0.0.1:17877/health`.
6. Confirmar `http://127.0.0.1:17877/telemetry`.
7. Compilar `TruckHub`.
8. Executar `TruckHub.exe`.
9. Confirmar `ETS2 CONECTADO`.
10. Confirmar caminhão, rota, carga, combustível, autonomia, odômetro, RPM, marcha e velocidade.

## Etapa 13 — viagem automática
- Aceitar trabalho no ETS2.
- Ligar o caminhão.
- Sair da velocidade de 3 km/h.
- Confirmar `VIAGEM INICIADA AUTOMATICAMENTE`.
- Dirigir alguns quilômetros.
- Confirmar distância e duração aumentando.
- Finalizar o trabalho no ETS2.
- Confirmar `VIAGEM FINALIZADA AUTOMATICAMENTE`.

## Etapa 14 — conta/licença
- Criar conta no site.
- Confirmar trial de 7 dias.
- Confirmar PIN gerado.
- Testar ativação do computador.
- Testar bloqueio de segundo dispositivo.
- Testar licença lifetime após pagamento.

## Etapa 15 — persistência
- Confirmar viagem salva na API.
- Confirmar histórico no dashboard.
- Confirmar despesas vinculadas.
- Confirmar documentos e tacógrafo.

## Etapa 16 — atualização
- Executar `TruckHubUpdater --check`.
- Usar `TRUCKHUB_UPDATE_MANIFEST` para testes locais.
- Validar versão, changelog, URL e SHA-256.
- Depois adicionar download, assinatura, instalação e rollback.

## Observação
Durante os testes, não usar dados reais de pagamento. Mercado Pago deve ser conectado somente na etapa de webhook/teste controlado.
