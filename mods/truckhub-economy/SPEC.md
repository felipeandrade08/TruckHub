# TruckHub Economia Realista — especificação

Pacote SCS que alinha o ETS2 ao que o TruckHub mede e cobra. A economia do app (R$) continua
independente da do jogo (EUR); o mod só ajusta regras do jogo para que as duas conversem.

## Como funciona (sem copiar arquivos do jogo)
`build_mod.py` lê a pasta `def` extraída da **sua** instalação, aplica só os valores de
`economy_profile.json` e empacota cópias alteradas de `economy_data.sii` e `police_data.sii`.
Se uma chave não existir (jogo de outra versão), ele para com erro em vez de adivinhar.

```
python build_mod.py --game ets2 --def-dir CAMINHO/def --check   # valida e mostra o que muda
python build_mod.py --game ets2 --def-dir CAMINHO/def           # gera truckhub_economia_ets2.scs
python build_mod.py --game ats  --def-dir CAMINHO/def           # gera truckhub_economia_ats.scs
```
O script confere se o `def` é do jogo escolhido (pela moeda padrão) e recusa se não for.
Copie o `.scs` para `Documentos/Euro Truck Simulator 2/mod` (ou `American Truck Simulator/mod`) e ative no Gerenciador de Mods.

## ETS2 (validado contra o def.scs enviado)
| Arquivo | Chave | Jogo | Mod | Motivo |
|---|---|---|---|---|
| economy_data | maximum_driving_time | 660 | 540 | 9h/dia, como o painel do tacógrafo |
| economy_data | sleeping_time | 540 | 660 | descanso diário de 11h |
| police_data | fine_amounts[1] pausa obrigatória | 500 | 1500 | casa com aviso 4h00 / infração 4h30 |
| police_data | fine_amounts[7] fugir da balança | 700 | 1500 | casa com a balança do app |
| police_data | fine_amounts[9] fugir da inspeção | 900 | 1500 | |
| police_data | fine_amounts[12] veículo danificado | 500 | 800 | manutenção alerta com 25% de desgaste |
| police_data | fine_amounts[13] excesso de peso | 1500 | 3000 | |
| economy_data | moeda BRL (opcional na lista) | — | 6,0 R$/€ | exibir o dinheiro do jogo em R$ |

Valores em EUR, editáveis.

## ATS (validado contra o def.scs enviado)
O ATS já usa janela de 14h (`maximum_driving_time: 840`) e descanso de 10h (`sleeping_time: 600`), iguais ao FMCSA: **não alteramos**.

| Arquivo | Chave | Jogo | Mod |
|---|---|---|---|
| police_data | fine_amounts[1] pausa obrigatória | 500 | 1500 |
| police_data | fine_amounts[7] fugir da balança | 700 | 1500 |
| police_data | fine_amounts[12] veículo danificado | 500 | 800 |
| police_data | fine_amounts[13] excesso de peso | 1500 | 3000 |
| economy_data | moeda BRL (opcional na lista) | — | 5,5 R$/US$ |

Fugir da inspeção (3000) já é alto no ATS e fica como está. Valores em USD.

## No app
O painel do tacógrafo usa a regra do jogo ativo: ETS2 = CE 561/2006 (4h30 contínuas, 9h/dia, 56h/semana); ATS = FMCSA (8h contínuas com pausa de 30 min, 11h/dia, 60h em 7 dias). São uma proposta inicial de realismo, não números oficiais.

## Fora desta versão
- Receita por km, dano de carga e atrasos: dependem de fórmulas do jogo que ainda não foram mapeadas; ficam como estão.
- Combustível regional, pedágio e balsa (`ferry/*.sui`): próxima etapa, depois de mapear cada tabela.
- Versão do jogo: `compatible_versions` no `manifest.sii` é genérico; confirme na sua versão.
