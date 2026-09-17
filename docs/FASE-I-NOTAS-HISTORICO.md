# FASE I — NOTAS + HISTÓRICO

## Objetivo
Concluir os módulos de Notas e Histórico dentro do tablet TransPoli, mantendo a experiência no tamanho físico da tela e sem criar novo atalho de teclado.

## Notas
- Criada a tabela `driver_notes` na migration `040_phase_i_notes.sql`.
- Cada nota pertence ao motorista autenticado.
- Título limitado a 120 caracteres.
- Conteúdo limitado a 10.000 caracteres.
- Criar e excluir notas pelo tablet.
- Persistência no backend TruckHub.
- Autenticação aceita sessão web ou token Bearer do aplicativo desktop.
- Rotas: `GET/POST /me/notes`, `PUT/DELETE /me/notes/:id`.

## Histórico
- O módulo Histórico fica dentro do tablet.
- Exibe viagens finalizadas que o computador de bordo já registrou localmente.
- Cada registro apresenta início/fim, rota, carga, distância, combustível e consumo médio quando houver dados suficientes.
- A captura existente permanece idempotente e limitada para evitar crescimento local indefinido.

## Integração
- `TabletPhaseI.cs` conecta os botões `NOTAS` e `HIST.` da navegação da Fase G.
- Não há janela externa.
- F10 continua exclusivamente responsável por abrir/fechar o tablet.
- Fases J–M não foram alteradas.

## Regra de build
Nenhum build foi disparado nesta fase. A versão permanece V1.0.13 até a conclusão de A–M.
