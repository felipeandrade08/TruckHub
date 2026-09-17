# Etapa 14 — Identidade do computador e ativação por PIN

## Objetivo

Preparar o TruckHub para reconhecer o computador do motorista e vincular uma licença a um único dispositivo ativo.

## Fluxo

```text
Primeiro uso
    ↓
Tela de ativação
    ↓
E-mail + PIN de 6 dígitos
    ↓
POST /auth/activate
    ↓
Validação do PIN no servidor
    ↓
Validação do trial/licença
    ↓
Vinculação do device_id
    ↓
Criação da sessão do desktop
    ↓
TruckHub liberado
```

## Identidade do dispositivo

O desktop cria um UUID aleatório uma única vez e grava em:

`%LOCALAPPDATA%\TruckHub\device-id.txt`

Não é utilizado fingerprint invasivo de hardware.

## Sessão

Após a ativação, a API cria um token de sessão aleatório. O desktop salva o token localmente para não pedir o PIN em todos os inícios.

O token bruto não é salvo no banco: apenas seu hash SHA-256 é armazenado na tabela `sessions`.

## Regra de dispositivo

A tabela `devices` possui uma licença por dispositivo ativo. Se a licença já estiver vinculada a outro dispositivo, a API retorna `DEVICE_ALREADY_BOUND` e não libera uma segunda máquina.

A troca de computador será implementada pelo painel do motorista em uma etapa posterior, com revogação controlada do dispositivo anterior.

## Regras de licença

- Trial: 7 dias contados do cadastro.
- Trial expirado: ativação recusada.
- Licença lifetime ativa: ativação permitida.
- Licença bloqueada/expirada: ativação recusada.

## Endpoint criado

`POST /auth/activate`

Exemplo de entrada:

```json
{
  "email": "motorista@exemplo.com",
  "pin": "123456",
  "deviceId": "uuid-do-computador",
  "deviceName": "PC-ETS2"
}
```

O endpoint retorna o token de sessão somente após uma ativação válida.

## Próxima etapa

Integrar o token ao cliente desktop para que as operações de viagens, caminhões, despesas e telemetria sejam autenticadas pela conta ativada. Depois disso, implementar a tela de licença, troca de computador e validação periódica/offline grace.
