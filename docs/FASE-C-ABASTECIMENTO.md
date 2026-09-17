# TruckHub — FASE C: Abastecimento automático

## Objetivo
Registrar abastecimentos a partir da telemetria, sem o motorista digitar a quantidade de litros.

## Fluxo concluído

1. O Connector fornece o nível de combustível em tempo real.
2. O desktop cria uma linha de base do tanque.
3. O sistema exige o veículo parado e identifica aumento real de combustível.
4. O aumento precisa estabilizar antes de ser confirmado, reduzindo falsos positivos.
5. Os litros são calculados automaticamente:
   `litros detectados = combustível final - combustível inicial`
6. O tablet abre a confirmação do abastecimento.
7. O motorista informa somente:
   - preço por litro;
   - posto.
8. A localização é aproveitada da telemetria quando disponível.
9. O total é calculado automaticamente:
   `litros × preço por litro = total`
10. A API valida novamente litros, preço e total antes de registrar.
11. O abastecimento vira uma despesa `fuel` vinculada à viagem quando existir.
12. O valor é lançado no livro financeiro e descontado do saldo do banco.
13. O histórico local mantém litros, combustível antes/depois, odômetro, caminhão e placa.

## Segurança e consistência

- Sessões `web` e `desktop` autenticadas são aceitas pela rota financeira.
- Litros, preço e valor total têm limites e validação no servidor.
- O servidor recalcula o valor esperado e rejeita divergências.
- A viagem, quando informada, precisa pertencer ao motorista autenticado.
- O preço não é aceito como texto livre sem validação numérica.

## Regra importante

Não existe mais fluxo de confirmação automática em que o motorista precise informar os litros. A quantidade vem da telemetria.

## Fora desta fase

A FASE C não altera versão e não dispara build. Melhorias amplas de economia, financiamento, manutenção e relatórios permanecem nas fases posteriores do roadmap.

A base continua **V1.0.13**. A versão **V1.0.14** será criada somente no encerramento da FASE M, junto do build final integrado.
