# TruckHub Windows Installer

Este diretório contém os arquivos auxiliares para a distribuição do TruckHub no Windows.

O pacote portátil oficial é gerado pelo GitHub Actions. O instalador gráfico será adicionado em uma etapa posterior, mantendo a instalação simples e sem depender de software adicional.

Estrutura esperada da instalação:

- TruckHub.exe
- TruckHubConnector.exe
- TruckHubUpdater.exe
- assets/
- README.txt

O TruckHub inicia e supervisiona o Connector automaticamente.