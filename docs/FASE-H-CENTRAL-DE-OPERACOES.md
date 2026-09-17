# FASE H — CENTRAL DE OPERAÇÕES

## Objetivo
Consolidar a operação da viagem dentro do tablet TransPoli, sem criar novo atalho de teclado e sem abrir uma janela externa gigante.

## Entregue
- Central acessível pelo próprio tablet.
- Atualização periódica dos dados operacionais.
- Bloco de viagem: rota, carga, peso, distância total, distância percorrida e restante.
- Bloco do caminhão: velocidade, combustível, autonomia, consumo, odômetro, motor e marcha.
- Bloco financeiro: receita da carga, combustível e resultado operacional disponível.
- Status de telemetria e veículo.
- Indicador de horário da última atualização.
- Valores indisponíveis permanecem como aguardando dados, sem inventar informação.
- F10 permanece exclusivamente responsável por abrir/fechar o tablet.

## Integração
A Central reaproveita os dados já existentes no cliente e na telemetria. Regras econômicas de mercado e fechamento financeiro não são duplicadas nesta camada.

## Escopo preservado
Notas, histórico documental, estatísticas completas, painel web e teste integrado continuam pertencendo às fases seguintes do roadmap A–M.

## Build e versão
- Nenhuma build disparada nesta fase.
- V1.0.13 permanece como base.
- V1.0.14 somente será definida após a conclusão das fases A–M.
