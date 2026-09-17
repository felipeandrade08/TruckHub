# FASE E — Economia

## Objetivo
Consolidar o banco do motorista e o fechamento financeiro das viagens.

## Entregue
- Conta financeira por motorista com saldo e livro-caixa.
- Receita de frete gerada no fechamento da viagem.
- Despesas vinculadas à viagem entram no livro-caixa.
- Combustível pode ser lançado pela confirmação de abastecimento da Fase C.
- Manutenção operacional pode ser provisionada automaticamente no fechamento quando não houver lançamento específico.
- Resultado líquido da viagem considera receita, despesas e parcela de empréstimo.
- Empréstimo ativo pode receber pagamento automático proporcional ao resultado da viagem.
- Parcelas, saldo restante, quantidade paga e status são persistidos.
- Livro-caixa oferece totais de créditos, débitos, resultado diário e movimentos por tipo.
- Tarifas internas de carga continuam independentes da economia real do jogo e são consolidadas no fechamento.
- Receita de viagem possui índice único para impedir crédito duplicado em reprocessamentos.

## Regra financeira
A receita é creditada somente uma vez por viagem. Despesas são debitadas e o saldo final é atualizado no servidor. O fechamento usa distância, combustível, peso e dano registrados na viagem/telemetria quando disponíveis.

## Empréstimos
O pagamento automático respeita o percentual configurado, o mínimo da parcela e o saldo restante. Ao zerar o saldo, o empréstimo passa para `paid`.

## Fora do escopo da Fase E
- Garagem/exclusividade: Fase F.
- Tablet: Fase G.
- Central de Operações: Fase H.
- Notas/histórico documental: Fase I.
- Estatísticas: Fase J.
- Painel web: Fase K.
- Build final e versão 1.0.14: Fase M.

## Build e versão
Não foi disparado build nesta fase. A versão permanece V1.0.13. O bump para V1.0.14 fica reservado para o encerramento da Fase M.
