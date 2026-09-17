# TruckHub / TransPoli — Correções V1.1.0

## Como aplicar

1. **Banco de dados**: rode a migration nova antes de tudo.
   ```
   cd api
   npm run db:migrate
   ```
   Ela é a `database/migrations/033_bank_garage_v110.sql`. É segura de rodar
   em cima do banco existente — só adiciona colunas e tabelas novas, não
   apaga nada.

2. **API**: sem mudança de configuração. `npm run deploy` normalmente.
   Testei com `npm run typecheck` (limpo) e `npm test` (36 testes, todos
   passando, 21 deles novos, cobrindo cada regra do banco do motorista).

3. **Desktop**: abra `desktop/TransPoli/TransPoli.csproj` no Visual Studio
   (ou `dotnet build`) e compile normalmente. **Não tenho .NET/Windows
   neste ambiente**, então não consegui rodar o compilador real — fiz uma
   verificação estática rigorosa (balanceamento de chaves, escopo de
   classes, símbolos usados vs. declarados) mas pode haver algum detalhe
   que só o compilador do Visual Studio pega. Se der erro de build, me
   mande a mensagem exata que eu corrijo na hora.

---

## O que estava quebrado e o que fiz

### 🔒 Garagem exclusiva — não bloqueava nada
O loop de telemetria (a cada 500 ms) reabilitava o botão "DESBLOQUEAR"
por cima do bloqueio da garagem, e o botão em si nem verificava se havia
bloqueio. Na prática o "caminhão exclusivo" era decorativo.

**Agora:** a garagem tem prioridade sobre qualquer outro estado. O botão
de desbloqueio recusa o clique com uma mensagem explicando por quê. Além
disso:
- criei a rota **"vincular este caminhão a mim"** — antes não existia
  jeito de colocar um caminhão na garagem pelo tablet, então ela ficava
  sempre vazia e nunca bloqueava ninguém;
- a garagem tolera 3 minutos de instabilidade da API sem soltar um
  bloqueio já ativo — antes, derrubar o servidor liberava o caminhão;
- histórico de tentativas de acesso fica registrado (`garage_access_log`).

### 🔐 Bloqueio físico — desistia depois de 3 tentativas
Bastava soltar o freio de estacionamento uma quarta vez para o bloqueio
parar de agir. Agora o reforço é contínuo (a cada ~1s no início, a cada 3s
depois de identificar resistência), e só mexe no teclado quando o jogo
está em primeiro plano — antes ele roubava o foco de qualquer janela a
cada segundo.

### 🧾 Nota fiscal — reconstruí no formato DANFE real
Antes era um cartão genérico com "TRANSPOLI" e alguns campos soltos.
Reescrevi como uma DANFE de verdade: canhoto de recebimento, quadro do
emitente, bloco DANFE com código de barras, chave de acesso de 44 dígitos,
natureza da operação, destinatário/remetente, cálculo do imposto,
transportador/volumes e tabela de produtos.

Deixei visível que é simulação (é um documento de jogo, gerado a partir da
telemetria do ETS2) — remover essa marcação transformaria isso em um
gerador de documento fiscal falsificável, o que não faço independente do
contexto.

### 💰 Banco do motorista — a lista que você pediu, item por item
Reescrevi `api/src/economy.ts` do zero, com **21 testes automatizados**
provando cada regra:

| Item pedido | Onde está | Testado |
|---|---|---|
| Livro-caixa | aba "LIVRO-CAIXA", rota `/me/economy/cashbook` | manual |
| Pagamento por km | `kmRevenue = distância × tarifa` | ✅ |
| Tarifas diferentes por carga | 9 categorias (geral, milho, soja, carvão, veículos, pesada, especial, **perigosa**, **refrigerada** — as duas últimas eu adicionei) | ✅ |
| Adicional por peso | só cobra toneladas acima de 20t livres | ✅ |
| Combustível pela telemetria | litros reais × preço do diesel configurado | ✅ |
| Manutenção automática | por km rodado | ✅ |
| Margem mínima de segurança | eleva o frete se a tarifa pagaria menos que custo+margem | ✅ |
| Bônus de eficiência | consumo ≤ meta em L/km | ✅ |
| Bônus sem avaria | agora **depende da avaria real da carga** — antes era pago sempre, porque a avaria nunca chegava ao servidor | ✅ |
| Empréstimo 5k/10k | com parcelas explícitas (padrão 10x) e valor mínimo por parcela | manual |
| Desconto automático das parcelas | descontado da receita líquida a cada viagem finalizada | ✅ |
| Saldo e histórico | extrato + livro-caixa agrupado por dia | manual |

O tablet ganhou uma tela de 5 abas (Extrato / Livro-caixa / Viagem / Tarifas
/ Crédito). A aba "Viagem" mostra a conta aberta da viagem em andamento,
linha por linha, incluindo quando a margem mínima está sendo aplicada.

### Bugs menores corrigidos no caminho
- **Roteamento de botões**: o clique era interceptado pelo texto do botão
  com `handledEventsToo=true`. Um botão como "🧾 NOTA FISCAL" caía na regra
  de "contém NOTA" e abria a tela errada — o handler certo nunca rodava.
  Agora só botões sem `Tag` própria passam pelo roteador genérico.
- **Quatro implementações duplicadas do mesmo overlay de modal** (uma em
  cada arquivo: documentos, economia, garagem, nota). Consolidei num host
  único (`TransPoliModalHost.cs`).
- **Tela de abastecimento manual não existia** — só funcionava quando a
  detecção automática disparava. Agora dá pra lançar manualmente.
- **Leitura de JSON frágil**: o código antigo usava `GetProperty` direto,
  que lança exceção e derruba o modal inteiro se um campo faltar. Criei um
  helper (`TransPoliJson.cs`) com leitura tolerante.
- **Vazamento de escopo**: ao centralizar a URL da telemetria numa
  constante da MainWindow, duas classes auxiliares (monitor de freio e
  análise de direção) ficaram referenciando o nome sem qualificar — não
  compilava. Corrigido.

## O que eu não mudei
Tudo que não estava listado no seu pedido e não estava quebrado eu deixei
como estava: ativação por licença, Mercado Pago, telemetria bruta,
tacógrafo, sincronização com o servidor, painel web/admin. Não toquei
nisso para não introduzir risco em área que já funcionava.
