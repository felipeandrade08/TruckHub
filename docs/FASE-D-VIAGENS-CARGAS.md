# Fase D — Viagens + Cargas

## Objetivo
Criar o Mercado de Cargas interno do TruckHub e conectar a carga real detectada no ETS2/ATS à viagem do motorista.

## Mercado
- O preço é interno ao TruckHub e fica entre R$ 4,00 e R$ 6,00/km.
- Cada categoria possui uma tarifa própria.
- A oferta pode aparecer como **ALTA**, **NORMAL** ou **BAIXA**.
- O valor não depende de uma API externa de mapa ou de preços.
- O mercado pode crescer sem cadastro manual: uma carga nova detectada em uma viagem é descoberta automaticamente.

## Descoberta automática
Quando uma viagem é criada com uma carga que ainda não existe no mercado:
1. o TruckHub normaliza o nome da carga;
2. cria a categoria no mercado;
3. atribui uma tarifa interna entre R$ 4,00 e R$ 6,00/km;
4. registra a descoberta;
5. adiciona a tarifa ao catálogo econômico existente;
6. cria o contrato associado à viagem.

Exemplo: se um motorista levar **Bois / Gado vivo** e essa carga ainda não existir, ela entra automaticamente no mercado com uma tarifa definida pelo sistema.

## Contrato
Cada viagem pode ter um contrato com:
- carga;
- tarifa/km congelada no contrato;
- origem;
- destino;
- distância;
- peso;
- bônus;
- penalidade por dano;
- bônus de entrega sem dano;
- horário de aceite/início/entrega;
- motorista e viagem vinculados.

A tarifa do contrato é preservada mesmo se o mercado mudar depois. Isso evita alterar o pagamento de uma viagem já aceita.

## Integração com telemetria
A viagem continua usando os dados reais do ETS2/ATS para origem, destino, distância e peso quando esses dados estiverem disponíveis. O servidor também valida os dados de distância/combustível ao finalizar a viagem.

## Fechamento
Ao finalizar a viagem, o contrato é marcado como entregue e os dados finais de origem, destino, distância e peso são sincronizados. A liquidação econômica existente continua responsável por calcular receita, combustível, manutenção, dano e resultado líquido.

## Rotas da API
- `GET /me/cargo-market` — mercado atual.
- `POST /me/cargo-market/discover` — cadastra/descobre uma carga nova.
- `GET /me/cargo-market/contracts` — contratos do motorista.
- `POST /me/cargo-market/contracts` — aceita uma oferta.
- `POST /me/cargo-market/contracts/:id/link-trip` — vincula contrato a uma viagem.
- `POST /me/cargo-market/contracts/:id/deliver` — conclui contrato.

## Regra de versão/build
A base continua **V1.0.13**. A Fase D não altera a versão e não dispara build. O build completo permanece reservado para a conclusão das Fases A–M, quando a versão será promovida para V1.0.14.
