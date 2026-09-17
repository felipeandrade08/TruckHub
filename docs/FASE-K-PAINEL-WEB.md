# FASE K — PAINEL WEB

## Objetivo
Conectar o painel web do motorista ao mesmo backend e aos dados reais usados pelo TruckHub/TransPoli, sem criar números fictícios.

## Estado
Implementação concluída na branch `main`.

## Funcionalidades disponíveis
- visão geral do motorista;
- identificação da conta e licença;
- garagem e caminhões;
- viagens e histórico;
- detalhes financeiros das viagens;
- despesas;
- documentos;
- tacógrafo;
- configurações da conta e dispositivo;
- dashboard avançado;
- indicadores de quilometragem, viagens, combustível, receita e resultado;
- evolução diária de quilômetros;
- despesas por categoria;
- rotas mais utilizadas;
- resumo de desempenho;
- tabela das viagens analisadas;
- filtros de 7, 30, 90 dias e histórico completo no dashboard avançado.

## Integração
O painel usa a API pública configurada em `web/site/config.js`. O dashboard avançado consome `GET /me/dashboard-advanced`, autenticado pela sessão web.

As informações exibidas são provenientes do banco e das viagens/despesas registradas. Quando o valor real de um job não está disponível, a interface mostra `—` em vez de inventar um valor.

## APIs aproveitadas
- `/me`
- `/me/license`
- `/me/trucks`
- `/me/trips`
- `/me/trips/history`
- `/me/trips/:id/summary`
- `/me/trips/:tripId/financial-summary`
- `/me/financial-summary`
- `/me/dashboard-advanced`
- `/me/expenses`

## Segurança
- sessão web continua sendo validada no backend;
- consultas são limitadas ao usuário autenticado;
- o frontend não recebe credenciais de banco;
- CORS permanece controlado pelo `api/src/index.ts`;
- nenhum dado sensível foi colocado no frontend.

## Observação de escopo
A Fase K termina no painel web do motorista e sua integração com o backend existente. Não foi feita build nem alteração de versão. A Fase L será responsável pelo teste integrado completo.
