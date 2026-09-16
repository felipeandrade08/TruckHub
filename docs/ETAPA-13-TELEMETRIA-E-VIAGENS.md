# Etapa 13 — Telemetria e persistência de viagens

## Objetivo
Transformar a telemetria validada do ETS2 em viagens persistentes no TruckHub.

## Fluxo

ETS2 → scs-telemetry.dll → Local\\SCSTelemetry → TruckHubConnector → TruckHub.exe → API → PostgreSQL

## Regra de início
Uma viagem só deve ser criada quando houver trabalho detectado e o veículo estiver realmente em movimento, com motor ligado e velocidade absoluta >= 3 km/h.

## Regra de encerramento
Não finalizar por uma única leitura vazia. O desktop deve esperar alguns segundos sem dados de trabalho e então enviar o fechamento para a API.

## Dados da viagem
- caminhão / modelo
- placa
- carga
- peso da carga
- origem
- destino
- empresa de origem
- empresa de destino
- distância planejada
- odômetro inicial/final
- combustível inicial/final
- duração
- distância percorrida

## Telemetria contínua
Não salvar cada leitura de 100 ms. O Connector pode continuar atualizando em tempo real, enquanto o desktop registra amostras persistentes em intervalo de aproximadamente 5–10 segundos quando uma viagem estiver ativa.

## Próximo desenvolvimento
1. Identificar automaticamente o caminhão cadastrado pelo usuário usando marca/modelo/placa.
2. Criar a viagem via POST /me/trips.
3. Guardar o ID da viagem localmente durante a sessão.
4. Registrar amostras periódicas.
5. Finalizar via POST /me/trips/:id/finish.
6. Calcular distância pelo odômetro inicial/final.
7. Calcular combustível usado pelo combustível inicial/final, com proteção para abastecimentos durante a viagem.
8. Atualizar o dashboard após o fechamento.
