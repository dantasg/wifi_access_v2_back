# Proposta — Como o portal descobre de qual unidade ele é

> **Status (2026-09-20): implementada.** Aprovada a **Opção A**, com um ajuste em relação ao texto
> original: em vez de o front deduzir a unidade lendo o subdomínio, **cada unidade guarda o seu
> endereço no banco** (`Units.PortalHost`) e o back resolve `host → unidade`.
>
> Ficou melhor por dois motivos: funciona com **qualquer** endereço (inclusive o
> `wifi-access-v2-front.vercel.app` de hoje, o que permitiu testar antes de existir domínio
> próprio), e trocar o endereço de uma unidade vira edição no painel, não mudança de código.
>
> O `?unit=` continua com prioridade (**D5**), então nada do que já estava configurado mudou.

## 1. Entendimento do problema

Descobrimos na implantação da Itaituba que **a UniFi não deixa passar o `?unit=slug`** para o
portal. O campo de configuração é explícito:

> "Insira o IP ou Domínio (FQDN) do seu servidor de portal externo."

Só o **endereço do servidor** — sem caminho, sem query string. A UniFi monta a URL final sozinha:

```
http://{FQDN}/guest/s/{site}/?ap=8c:30:66:4e:9b:58&id=60:45:2e:57:0d:c3&t=…&url=…&ssid=PIX%20REGIONAL
```

Repare: tem MAC do aparelho, MAC do ponto de acesso, SSID — **e nenhum `unit`**.

Só que o front lê a unidade exatamente de lá (`AccessWifi_V2_FRONT/src/lib/api.ts:38`):

```ts
unit: q.get('unit'),
```

Sem `unit`, o portal não sabe qual tema carregar (`GET /settings?unit=`) nem o que mandar no
`POST /authorize` — que responde `"Unidade não informada."` (`AuthorizeController.cs:39`).

> ⚠️ **Isto não é um problema da Itaituba, é do produto.** Vale para qualquer cliente novo. A nota
> em `FRONT_CHANGES.md` ("apontar o captive portal para a URL com o slug da unidade") descreve algo
> que a UniFi não permite. **Antes de decidir, vale confirmar se o portal da Dôce já rodou por um
> captive portal real** ou se sempre foi aberto com a URL digitada à mão.

---

## 2. Como está hoje

| Ponto | Onde | Situação |
| --- | --- | --- |
| Leitura da unidade | `AccessWifi_V2_FRONT/src/lib/api.ts:34-42` | Só `?unit=`, sem alternativa |
| Tema do portal | `GET /settings?unit={slug}` | Exige o slug |
| Autorização | `AuthorizeController.cs:37-47` | Exige `unit` no corpo, senão `400` |
| Roteamento na Vercel | `AccessWifi_V2_FRONT/vercel.json` | `"/(.*)" → /index.html`, então `/guest/s/default/` **carrega a página normalmente** |
| Sinais que a UniFi entrega | — | `ap` (MAC do ponto de acesso), `id` (MAC do aparelho), `ssid`, `t`, `url` |

A boa notícia: a página **carrega**. Falta só ela saber onde está.

---

## 3. Antes de tudo: o teste de 1 minuto

O texto do campo diz "IP ou FQDN", mas nem sempre o campo **valida** o que diz. Vale o TI tentar
colar mesmo assim:

```
wifi-access-v2-front.vercel.app/?unit=itaituba
```

Se a UniFi aceitar e a URL final preservar o `unit`, **nada disto aqui é necessário**. Custa um
minuto e elimina a proposta inteira. Só que não dá para *contar* com isso: mesmo funcionando numa
versão, some numa atualização.

---

## 4. Os caminhos possíveis

### Opção A — um endereço (FQDN) por unidade  ⭐ recomendada

Cada unidade ganha um subdomínio, e o front descobre a unidade pelo próprio endereço:

```
itaituba.wifi.seudominio.com.br   → unidade "itaituba"
doce-matriz.wifi.seudominio.com.br → unidade "doce-matriz"
```

No front, uma linha: se não houver `?unit=`, usa o primeiro pedaço do hostname.

| A favor | Contra |
| --- | --- |
| **Zero lógica de adivinhação** — não erra nunca | Exige um domínio próprio (que você vai precisar de qualquer jeito para a API) |
| Funciona igual nos modos Local e Cloud | Um registro de DNS por unidade — ou **um curinga `*.wifi.…`** e acaba o trabalho manual |
| O cliente vê o endereço da **sua** empresa, não "vercel.app" | Curinga na Vercel exige plano pago |
| Nenhuma mudança no back end | |

### Opção B — identificar pelo MAC do ponto de acesso (`ap`)

A UniFi já manda `ap=8c:30:66:4e:9b:58`, e cada loja tem seus próprios equipamentos. O back
guardaria a lista de APs de cada unidade e resolveria `GET /settings?ap=…`.

| A favor | Contra |
| --- | --- |
| Um endereço só para todos os clientes | Precisa cadastrar os APs de cada unidade |
| Nos modos Cloud, dá para **descobrir os APs sozinho** pela API (`GET /sites/{siteId}/devices`) | Um AP novo instalado na loja derruba o portal até alguém sincronizar |
| | Nas unidades Local, o cadastro é manual mesmo |

### Opção C — identificar pela SSID

A UniFi manda `ssid=PIX REGIONAL`, e já guardamos uma SSID por empresa em `PortalSettings`.

| A favor | Contra |
| --- | --- |
| Nada a cadastrar além do que já existe | **Dois clientes com a mesma SSID quebram tudo** ("Wifi Gratis", "Guest"…) |
| Configuração mais simples para o TI | O TI renomeia a rede e o portal para de funcionar, sem aviso |

---

## 5. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Como identificar a unidade | **Opção A — um FQDN por unidade.** É a única que não adivinha nada | Opção B (MAC do AP) ou C (SSID) |
| **D2** | Domínio a usar | **Registrar um domínio próprio e usar `unidade.wifi.dominio`** — serve também para a API na VPS e melhora a cara do produto | Continuar em `vercel.app` (aí só sobra a Opção B ou C) |
| **D3** | DNS | **Curinga `*.wifi.dominio`** se o plano permitir: cliente novo não exige mexer em DNS | Um registro por unidade (funciona no plano free) |
| **D4** | Plano B no código | **Sim: se faltar `?unit=`, tenta o subdomínio; se ainda faltar, tenta o `ap`** — assim uma configuração errada do TI não derruba o portal | Só o subdomínio |
| **D5** | Manter o `?unit=` funcionando | **Sim, ele continua tendo prioridade** — não quebra nada que já esteja configurado e facilita testar na mão | Remover e usar só o novo caminho |
| **D6** | Mensagem quando não identificar | **Tela clara ("portal não configurado para esta rede") em vez do erro genérico de hoje** | Manter `"Unidade não informada."` |
| **D7** | HTTPS | **Ligar o "Secure Portal" na UniFi**, para o redirecionamento sair em `https` e não depender do 308 da Vercel | Deixar em `http` |

---

## 6. O que muda em cada lugar

**Se a Opção A for aprovada, a mudança é pequena e quase toda no front:**

- `AccessWifi_V2_FRONT/src/lib/api.ts:34` — `readPortalContext` ganha o plano B do D4.
- `AccessWifi_V2_FRONT/vercel.json` — sem mudança (o curinga de rota já cobre `/guest/s/default/`).
- Vercel — adicionar o domínio (ou o curinga) ao projeto.
- DNS — apontar para a Vercel.
- `FRONT_CHANGES.md` — corrigir a nota de infra, que hoje descreve algo impossível.
- Back end — **nada**, a não ser que a Opção B entre como plano B do D4 (aí entra um cadastro de
  APs por unidade e um `GET /settings?ap=`, que viram uma etapa 2).

---

## 7. Próximo passo

Primeiro o teste de 1 minuto da §3 — ele pode encerrar o assunto.

Se não resolver, me responda **"ok"** para eu seguir com as recomendações, ou diga qual decisão
(D1–D7) quer mudar. O D2 é o que trava o resto: sem domínio próprio, a Opção A não existe e a
conversa muda para B ou C.
