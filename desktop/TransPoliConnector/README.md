# TransPoli Connector — ETS2 + ATS

Conector local do TransPoli para ler a telemetria do Euro Truck Simulator 2 (ETS2) e do American Truck Simulator (ATS), disponibilizando um snapshot em `127.0.0.1:17877`.

## O que esta etapa entrega

- leitura contínua da telemetria via `SCSSdkClient`;
- atualização a cada 100 ms;
- identificação automática do simulador (`ETS2` ou `ATS`) e estado pausado;
- velocidade e limite apresentados em km/h no ETS2 e mph no ATS pela HUD;
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

O plugin TransPoli.VehicleControl correspondente deve estar instalado em `bin/win_x64/plugins` do ETS2 ou do ATS. O instalador do TransPoli procura automaticamente os dois simuladores nas bibliotecas Steam e instala o módulo compatível em cada instalação encontrada.

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

## Compatibilidade e unidades

O campo `Game` identifica o simulador informado pelo SDK. A HUD usa esse valor para apresentar o nome correto e converter as leituras de velocidade (armazenadas internamente em km/h) para mph no ATS. As demais leituras mantêm as unidades fornecidas pela telemetria.

## Próxima etapa

O `TransPoli.exe` WPF consome este bridge local. Viagens, alertas e indicadores devem continuar usando os dados confirmados pelo SDK, sem preencher campos ausentes com valores fictícios.
