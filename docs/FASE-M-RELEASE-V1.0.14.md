# FASE M — FINALIZAÇÃO E RELEASE V1.0.14

## Objetivo
Consolidar o TruckHub após as fases A–L, corrigindo a interface do desktop e preparando a versão 1.0.14 para a validação final no Windows.

## Estado atual
- [x] Fases A–L concluídas no planejamento do projeto.
- [x] `VERSION` em `1.0.14`.
- [x] `TransPoli.csproj` em `1.0.14`.
- [x] Novo cockpit desktop com layout moderno e responsivo.
- [x] Nova tela de acesso/ativação.
- [x] Identidade visual escura e consistente do TruckHub.
- [x] Controles existentes de telemetria, viagem, abastecimento, paradas, ocorrências, documentos, atualização e desbloqueio preservados pelos mesmos handlers e nomes de controles.
- [ ] Build final do desktop executada com sucesso no runner Windows.
- [ ] Teste real com ETS2/ATS + Connector no Windows.
- [ ] Validação de instalação limpa.
- [ ] Validação de atualização sobre instalação existente.
- [ ] Validação final de execução do aplicativo.
- [ ] Geração/validação do instalador Inno Setup.
- [ ] Publicação da tag/release GitHub `v1.0.14` com instalador e manifesto.

## Regra de release
A versão de código já está em **1.0.14**. A Release do GitHub é um artefato separado: a última release publicada ainda é `v1.0.13`. A publicação de `v1.0.14` deve ocorrer somente com o build final validado, usando a tag `v1.0.14` para que o workflow gere o instalador e o manifesto correspondentes.

## Observação
O build automatizado em Windows é a validação oficial de compilação. Testes com ETS2/ATS e Connector continuam dependendo da execução real em uma máquina Windows com o jogo instalado.
