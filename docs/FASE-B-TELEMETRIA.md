# TruckHub — FASE B: Telemetria completa

## Objetivo
Consolidar a telemetria ETS2/ATS como fonte operacional do TruckHub, com leitura real do veículo, viagens automáticas e recuperação de conexão sem intervenção do motorista.

## Checklist concluído

- [x] Conexão ETS2/ATS via `scs-telemetry.dll` e `Local\\SCSTelemetry`.
- [x] Ponte local `TransPoliConnector` com endpoint `/telemetry`.
- [x] Reconhecimento de estado conectado/desconectado.
- [x] Velocidade em km/h e m/s.
- [x] RPM e marcha.
- [x] Combustível atual, autonomia e consumo médio.
- [x] Odômetro real e distância percorrida.
- [x] Estado do motor e sistema elétrico.
- [x] Carga, peso, origem, destino e empresas.
- [x] Marca, modelo, identificador e placa do caminhão.
- [x] Integridade básica dos dados (limites, NaN/Infinity, odômetro sem regressão e amostras fora de ordem rejeitadas pela API).
- [x] Amostras de viagem persistidas no banco.
- [x] Resumo de viagem com distância, combustível, tempo dirigindo/parado/pausado e velocidades.
- [x] Início automático da viagem somente com trabalho detectado, carga carregada, motor ligado, caminhão liberado e movimento real.
- [x] Fim automático após confirmação de descarregamento.
- [x] Recuperação de viagem ativa após reinício do aplicativo.
- [x] Telemetria ao vivo por dispositivo, mantendo somente a última leitura para o dashboard.
- [x] Supervisor que reinicia o connector quando o processo cai.
- [x] Supervisor que também verifica `/health`, cobrindo o caso de processo vivo porém sem resposta.
- [x] Recuperação automática quando uma sessão que estava conectada perde o fluxo de telemetria.
- [x] Reentrada automática do fluxo de leitura após a recuperação.

## Limites desta fase

A FASE B não implementa a regra comercial de abastecimento. O volume detectado pela telemetria permanece disponível para a próxima fase, **FASE C — Abastecimento automático**, onde será tratado o registro de litros, preço por litro e despesa.

## Regra de versão

A base permanece em **V1.0.13** durante as fases A–M. Nenhuma alteração de versão é feita nesta fase. A versão **V1.0.14** só será criada no encerramento da FASE M, junto do build final integrado.

## Build

Nenhum novo build foi disparado pela FASE B. A validação de build fica reservada para o final de todas as fases, conforme o plano do projeto.
