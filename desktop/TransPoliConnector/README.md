# TransPoli Connector — ETS2

Conector local do TransPoli para ler a telemetria do Euro Truck Simulator 2 e disponibilizar um snapshot em `127.0.0.1:17877`.

## O que esta etapa entrega

- leitura contínua da telemetria via `SCSSdkClient`;
- atualização a cada 100 ms;
- identificação do jogo e estado pausado;
- marca/modelo/ID/placa do caminhão;
- velocidade, RPM, marcha, combustível, autonomia e hodômetro;
- cruise control e motor ligado;
- carga, origem, destino, empresas e distância planejada quando o job estiver disponível;
- API local para o futuro `TransPoli.exe` consultar os dados sem expor essa telemetria na internet.

## Dependência de terceiros

O TransPoli não redistribui o `SCSSdkClient.dll` neste repositório. A implementação C# do cliente é uma dependência externa e deve ser colocada localmente em:

```text
desktop/TransPoliConnector/lib/SCSSdkClient.dll
```

O plugin de telemetria correspondente também precisa estar instalado no ETS2 conforme a documentação do SDK utilizado.

O SDK oficial da SCS disponibiliza a Telemetry SDK estável 1.14 para Windows, Linux e macOS. A implementação C# usada como referência expõe `SCSSdkTelemetry` e o mapa compartilhado `Local\\SCSTelemetry`.

## Build

No Windows com Visual Studio ou MSBuild instalado:

```powershell
dotnet build desktop/TransPoliConnector/TransPoliConnector.csproj -c Release
```

Se o projeto não encontrar a DLL, confirme o arquivo `lib\\SCSSdkClient.dll`.

## Execução

```powershell
dotnet run --project desktop/TransPoliConnector/TransPoliConnector.csproj
```

Com o conector aberto:

```text
GET http://127.0.0.1:17877/health
GET http://127.0.0.1:17877/telemetry
```

O endpoint é propositalmente limitado a `127.0.0.1`. Nenhum dado de telemetria é enviado para o servidor TransPoli nesta etapa.

## Próxima etapa

O `TransPoli.exe` WPF será conectado a este bridge local. Depois disso, iniciaremos/encerraremos viagens automaticamente a partir dos eventos e dados disponíveis da telemetria, mantendo o servidor responsável por licença, conta e histórico.
