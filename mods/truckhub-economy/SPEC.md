# TruckHub Economia Realista — especificação

Pacote SCS que alinha o ETS2 ao que o TruckHub mede e cobra. A economia do app (R$) continua
independente da do jogo (EUR); o mod só ajusta regras do jogo para que as duas conversem.

## Como funciona (sem copiar arquivos do jogo)
`build_mod.py` lê a pasta `def` extraída da **sua** instalação, aplica só os valores de
`economy_profile.json` e empacota cópias alteradas de `economy_data.sii` e `police_data.sii`.
Se uma chave não existir (jogo de outra versão), ele para com erro em vez de adivinhar.

```
python build_mod.py --def-dir CAMINHO/def --check     # valida e mostra o que muda
python build_mod.py --def-dir CAMINHO/def             # gera truckhub_economia.scs
```
Copie o `.scs` para `Documentos/Euro Truck Simulator 2/mod` e ative no Gerenciador de Mods.

## O que muda (validado contra o def.scs enviado)
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

Valores em EUR, editáveis. São uma proposta inicial de realismo, não números oficiais.

## Fora desta versão
- Receita por km, dano de carga e atrasos: dependem de fórmulas do jogo que ainda não foram mapeadas; ficam como estão.
- Combustível regional, pedágio e balsa (`ferry/*.sui`): próxima etapa, depois de mapear cada tabela.
- Versão do jogo: `compatible_versions` no `manifest.sii` é genérico; confirme na sua versão.
