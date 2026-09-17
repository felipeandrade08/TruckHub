# TruckHub / TransPoli — Fase J: Estatísticas

## Status

CONCLUÍDA.

A Fase J adiciona estatísticas reais do motorista dentro do tablet, sem alterar a versão do produto e sem criar novo atalho de teclado.

## API

Rota principal:

`GET /me/statistics?period=today|7d|30d|all`

Autenticação aceita sessão web ou desktop por cookie/token Bearer.

A resposta usa dados reais das tabelas de viagens, despesas, eventos operacionais e livro-caixa.

Indicadores:

- viagens concluídas;
- viagens com incidentes e sem incidentes quando os eventos operacionais permitem identificar o incidente;
- entregas limpas e danificadas;
- percentual de dano;
- penalidades de dano registradas na economia;
- km totais e km médio por viagem;
- toneladas transportadas;
- tipos de carga;
- litros consumidos;
- km/L;
- L/100 km;
- receita;
- despesas;
- combustível;
- manutenção;
- pedágios;
- lucro;
- receita/km;
- custo/km;
- lucro/km;
- lucro médio por viagem;
- distribuição por tipo de carga.

## Tablet

O botão `ESTAT.` da navegação da Fase G agora abre um módulo próprio da Fase J dentro da tela física do tablet.

Filtros disponíveis:

- Hoje;
- 7 dias;
- 30 dias;
- Total.

A tela usa cartões compactos e barras de distribuição por carga para não ultrapassar o espaço físico do tablet.

## Banco

Foi adicionada a migration `041_phase_j_statistics.sql`, contendo somente índices de leitura para acelerar consultas de estatísticas.

Nenhum número fictício é gravado no banco.

## Build

Nenhuma build foi executada nesta fase, conforme a regra do projeto. A versão permanece V1.0.13.

## Próxima fase

Fase K — Painel Web.
