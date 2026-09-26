# TransPoli BrakeLab

Laboratório isolado para validar aquecimento e brake fade no ETS2 antes de qualquer integração com TransPoli.

## Objetivo
- Ler a temperatura nativa dos freios do ETS2.
- Observar pedal de freio e freio efetivo.
- Calcular uma curva experimental de fade.
- Validar se o mecanismo de input consegue reduzir a frenagem percebida no jogo.
- Registrar diagnóstico suficiente para comparar temperatura, entrada e resultado.

## Contrato
- Não integra com a v1.0.32.
- Não grava Banco, viagem, manutenção ou API.
- Não usa offsets privados nem escrita de memória do processo.
- Falha sempre para o comportamento normal do jogo.
- Os limiares são experimentais e não são valores finais de produção.

## Curva inicial de laboratório
- abaixo de 250 C: 100%
- 250–350 C: transição 100% -> 80%
- 350–450 C: transição 80% -> 50%
- acima de 450 C: piso experimental de 30%

A curva só será mantida se o teste real demonstrar comportamento estável e previsível.
