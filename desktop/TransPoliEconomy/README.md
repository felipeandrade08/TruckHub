# TransPoli Real Economy Engine

Biblioteca compartilhada para os perfis econômicos ATS e ETS2.

## Responsabilidades

- Cálculo de despesas com diesel usando preço regional configurado.
- Conversão correta de galões americanos e litros.
- Apuração de receita, despesas, lucro líquido, margem e custo por quilômetro.
- Estimativa de manutenção a partir da variação confirmada dos canais de desgaste.
- Modelos de despesas para abastecimento, pedágios, balsas, trens, multas e manutenção.

## Regras de integridade

- Não inventa preço de combustível, multa, pedágio ou receita quando não houver dado configurado.
- Não confunde galão americano com galão imperial.
- Não altera memória do simulador nem arquivos de carreira.
- O registro financeiro usa eventos confirmados pela telemetria ou lançamento explícito do usuário.
- ATS e ETS2 mantêm moeda e tabela de preços independentes.

## Próximas integrações

1. Consumir eventos e snapshots do TransPoli Connector.
2. Persistir lançamentos no banco local do TransPoli com chave de evento para evitar duplicidade.
3. Criar perfis de preços regionais com fonte e data de atualização.
4. Gerar pacotes SCS separados, com definições validadas contra a versão instalada de cada jogo.
5. Instalar os pacotes nas pastas de mods e orientar a ativação no Gerenciador de Mods de cada perfil.

A DLL é o motor financeiro do TransPoli; a alteração dos valores aplicados pelo jogo é feita pelos pacotes de mod SCS, não por injeção na DLL de telemetria.
