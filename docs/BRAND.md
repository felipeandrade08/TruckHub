# Brand — TransPoli • TruckHub

## Nome

**TruckHub** — computador de bordo da **TransPoli**.

## Posicionamento

Computador de bordo inteligente para **Euro Truck Simulator 2 (ETS2)**, focado no motorista individual.

## Tagline

**Seu computador de bordo para ETS2.**

## Identidade oficial V2

A identidade oficial é a logo circular TP (dourada sobre disco azul-noite) e a arte da frota TransPoli.

### Paleta

| Uso | Cor |
|---|---|
| Fundo principal | `#02080F` |
| Painéis (azul-noite da logo) | `#0A1A2B` / `#10263C` |
| Painel translúcido (sobre o fundo) | `#E00A1A2B` |
| Linhas | `#1B3550` |
| Dourado da logo (primário) | `#FFC41F` |
| Dourado claro | `#FFE04F` |
| Dourado profundo | `#E0A100` |
| Branco | `#F5F7FA` |
| Texto secundário | `#8EA0B5` |
| Estado conectado | `#42E59A` |

## Assets oficiais

- `assets/brand/transpoli-logo.png` — logo oficial, **fundo transparente**, 1024×1024. Usada no site, no app Windows e como base dos ícones.
- `assets/brand/transpoli-bg.jpg` — fundo oficial (frota TransPoli). Usado no site e no app.
- `assets/brand/transpoli-bg-mobile.jpg` — versão leve do fundo para telas pequenas.
- `assets/brand/icons/` — ícones derivados da logo:
  - `favicon.ico` (site), `transpoli.ico` (executável Windows)
  - `icon-16..512.png`, `apple-touch-icon.png`, `maskable-192.png`, `maskable-512.png`
- `assets/brand/truckhub-badge.png` — alias legado da logo (mesmo arquivo), mantido para referências antigas.

## Uso do fundo

O fundo oficial nunca aparece em opacidade cheia atrás de texto:

- **Web**: `web/site/brand.css` aplica o fundo fixo com opacidade `.34` e um véu escuro por cima (`body::after`). Deve ser o **primeiro** CSS carregado em toda página nova.
- **App Windows**: `App.xaml` expõe `BrandBackground`, `BrandLogo` e `BrandOverlay`. Toda janela nova deve usar o padrão `Image` (Opacity `0.34`) + `Rectangle` com `BrandOverlay` atrás do conteúdo.

## Direção visual

- ETS2 e estrada devem ser perceptíveis imediatamente.
- Visual escuro, automotivo, premium e tecnológico.
- O dourado da logo é a única cor de ação/destaque; o azul-noite da logo é a base dos painéis.
- Somente dois materiais de imagem são oficiais: a logo transparente e a arte da frota. Não usar fotos de banco de imagens.
- Evitar aparência de SaaS corporativo genérico.

## Regra de implementação

Toda nova tela do site, dashboard ou aplicativo Windows deve preservar esta identidade: logo transparente, fundo oficial com véu, paleta acima e ícones da pasta `assets/brand/icons/`.
