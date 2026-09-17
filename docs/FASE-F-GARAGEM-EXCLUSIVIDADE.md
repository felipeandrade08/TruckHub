# FASE F — GARAGEM + EXCLUSIVIDADE

Base: V1.0.13

## Entregue

- Garagem exclusiva continua vinculando `motorista -> caminhão`.
- A chave de exclusividade permanece normalizada por `marca|modelo|placa`.
- Bloqueio contra uso por outro motorista continua sendo decidido pelo backend em `/me/garage/authorize`.
- O caminhão passa a ter estado persistido no cadastro:
  - quilometragem atual;
  - combustível atual;
  - estado operacional;
  - desgaste (`wear_pct`);
  - última telemetria recebida.
- Cada amostra de `trip_telemetry_samples` atualiza automaticamente o estado do caminhão relacionado à viagem.
- Cada viagem finalizada com caminhão gera uma entrada em `garage_usage_history`.
- O histórico registra motorista, caminhão, placa, início/fim, quilometragens, distância, combustível, carga, origem e destino.
- O histórico é idempotente por `trip_id`.
- Foram adicionadas consultas de garagem real:
  - `GET /me/garage/fleet`
  - `GET /me/garage/:truckId/history`
  - `GET /me/garage/usage-summary`
- O scanner local de save e o vínculo pelo tablet existentes continuam sendo usados para cadastrar o caminhão.
- O bloqueio existente permanece integrado à autorização periódica do desktop.

## Banco

Migration: `039_phase_f_garage.sql`

Ela adiciona estado operacional em `trucks`, cria `garage_usage_history` e instala triggers para sincronizar telemetria e histórico.

## Exclusividade

O servidor continua sendo a autoridade. Um caminhão já vinculado a outro motorista é recusado no vínculo e retorna `foreign_truck` na autorização. Um caminhão fora da garagem do motorista retorna `not_in_garage`.

## Fora da Fase F

Tablet completo, manutenção avançada, estatísticas, painel web e build final permanecem para as fases seguintes do roadmap.

## Regra de entrega

Nenhum build foi disparado. A versão continua V1.0.13. O build final e o bump para V1.0.14 ficam somente depois da Fase M.
