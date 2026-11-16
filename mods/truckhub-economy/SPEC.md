# TruckHub Economia Realista — especificação

Objetivo: fazer o jogo e o TruckHub falarem os mesmos números. O app já calcula
tudo pelo `TransPoliEconomy` (diesel regional, galão americano vs. litro, manutenção
por desgaste, pedágio, balsa, trem, multa). O mod só alinha os valores do lado do jogo.

## O que o mod deve cobrir (ordem de entrega)
1. **Multas** — valores de infração do jogo iguais à tabela do app (excesso de velocidade, sinal vermelho, colisão).
2. **Balsas e trens** — preço de travessia coerente com a tabela do app.
3. **Combustível** — preço do diesel por região coerente com os perfis ATS/ETS2 do app.
4. **Pedágios** — tabela por país/estado.

## Regras
- ATS e ETS2 têm pacotes, moedas e tabelas independentes.
- Nenhum valor é inventado: cada número do pacote precisa vir de uma tabela do app com fonte e data.
- Não altera memória do jogo nem save; só definições (`def/`) dentro do `.scs`.

## Pendências (precisam do jogo instalado)
- Extrair `def.scs` da versão instalada (ex.: com o extrator de SCS) e copiar para `def/` somente os arquivos que serão sobrescritos, alterando só os valores acima.
- Conferir `compatible_versions` no `manifest.sii` com a versão do jogo.
- Testar carregando o mod num perfil descartável antes de ativar no perfil principal.

## Build
`python build_mod.py` gera `truckhub_economia.scs`; copie para `Documents/Euro Truck Simulator 2/mod` (ou `American Truck Simulator/mod`).
