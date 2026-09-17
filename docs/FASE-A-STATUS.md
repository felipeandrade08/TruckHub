# FASE A — Corrigir e estabilizar o que já existe

Base de desenvolvimento: **V1.0.13**.

## Correções concluídas nesta fase

- O workflow de desktop deixou de publicar/atualizar uma Release automaticamente a cada push em `main`.
- Pushes em `main` passam a ser apenas builds de validação; publicação fica reservada para uma tag explícita de versão.
- Corrigido o módulo `V13Fixes.cs` para usar uma implementação de scanner de save que compila de forma estável, mantendo a leitura do save em modo somente leitura.
- Nenhuma alteração foi feita no arquivo `VERSION`; a base continua em `1.0.13`.

## Critério de saída

A Fase A só é considerada concluída após o build do desktop, updater, connector e instalador terminar sem erro no GitHub Actions.

Depois disso, o desenvolvimento segue exclusivamente para a **FASE B — Telemetria completa**.
