# DLL de telemetria ETS2

O TruckHubConnector usa a biblioteca C# `scs-telemetry.dll` fornecida pelo projeto TruckHub.

## Instalação local

Coloque o arquivo:

`scs-telemetry.dll`

nesta pasta:

`desktop/TruckHubConnector/lib/`

A DLL não é versionada no GitHub porque é uma dependência binária de terceiros. O projeto já está configurado para procurar exatamente esse arquivo.

## Próximo teste

1. Abra a solução no Visual Studio 2022.
2. Confirme que `desktop/TruckHubConnector/lib/scs-telemetry.dll` existe.
3. Compile o projeto `TruckHubConnector` em **x64**.
4. Se compilar sem erros, o próximo passo será iniciar o conector e testar a leitura real do ETS2.
