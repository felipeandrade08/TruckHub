# Atualização automática do TransPoli

Criado por Felipe Andrade.

A partir desta versão o app se atualiza sozinho, por cima da instalação
existente. Ninguém precisa desinstalar nada.

## Como funciona

1. Ao abrir (12 segundos depois) e a cada 6 horas, o app baixa um arquivo
   `manifest.json` publicado na release mais recente do GitHub.
2. Se a versão do manifesto for maior que a instalada, o app avisa o motorista.
3. Aceitando, ele baixa o `TransPoli-Setup.exe`, confere o SHA-256 e executa o
   instalador em modo silencioso.
4. Como o instalador usa o mesmo `AppId`, o Inno Setup substitui os arquivos e
   reabre o TransPoli sozinho (`/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`).

Também existe o botão **⭳ ATUALIZAR APP** no painel rápido, para verificar na
hora, e o botão **ℹ SOBRE**, que mostra a versão instalada.

## O que você precisa ajustar uma única vez

No arquivo `desktop/TransPoli/UpdateService.cs`, linha do topo:

```csharp
private const string UpdateRepository = "felipe-pessoall2026/TransPoli";
```

Troque pelo `usuario/repositorio` real do GitHub onde ficam as releases.
Para testar apontando para outro lugar, dá para usar a variável de ambiente
`TRANSPOLI_UPDATE_MANIFEST` sem recompilar.

## Como lançar uma atualização

1. Suba a versão criando uma tag:

```bash
git tag v1.0.1
git push origin v1.0.1
```

2. O workflow `Build TransPoli Desktop` faz o resto:
   - compila o app com a versão `1.0.1`;
   - gera o `TransPoli-Setup.exe` com essa mesma versão;
   - calcula o SHA-256 e cria o `manifest.json`;
   - publica os dois na release da tag.

3. Os apps instalados detectam sozinhos, em no máximo 6 horas.

> A versão sempre vem da tag (`v1.0.1` → `1.0.1`). Builds fora de tag usam
> `1.0.0`, ou seja, não disparam atualização.

## Atualização obrigatória

Se quiser forçar uma versão (por exemplo, uma correção de segurança), edite o
`manifest.json` da release e troque `"mandatory": false` por `true`. Nesse caso
o app avisa e atualiza sem perguntar.

## Segurança

- O download só é aceito por HTTPS.
- O SHA-256 publicado no manifesto é conferido antes de executar o instalador;
  se não bater, o arquivo é apagado e nada é instalado.
- Limite de 500 MB por pacote.
- A instalação é por usuário (`%LOCALAPPDATA%\TransPoli`), então não pede UAC.
