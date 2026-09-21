# Proposta — Autorizar o visitante pela nuvem da Ubiquiti (Site Manager Connector Proxy)

## 1. Entendimento do pedido

A unidade **Itaituba (Lojas Regional)** tem uma controladora UniFi, mas **não tem IP público nem DDNS**
— o que temos é só o link do console na nuvem:

```
https://unifi.ui.com/consoles/58D61F5E15310000000009A4719B000000000A2C62C10000000069211BF2:896725606/network/default
```

Hoje nosso back-end só sabe falar **direto** com a controladora (`https://ip-da-loja`), então não
conseguimos autorizar ninguém nessa unidade. A proposta é ensinar o back-end a falar com a
controladora **através da nuvem da Ubiquiti**: nosso servidor chama `api.ui.com`, e a Ubiquiti
repassa o comando para o console da loja pelo túnel que o próprio equipamento já mantém aberto.

**Nada de abrir porta no roteador da loja, nada de VPN, nada de senha de administrador.**

---

## 2. Como está hoje

| Ponto | Onde | Situação |
| --- | --- | --- |
| Chamada à UniFi | `src/AccessWifi.Api/Infrastructure/Unifi/UnifiClient.cs:24` | Uma única implementação, sempre local |
| Login | `UnifiClient.cs:93` | `POST /api/auth/login` com **usuário e senha de admin** da controladora |
| Autorização | `UnifiClient.cs:52-59` | `POST /proxy/network/api/s/{site}/cmd/stamgr` com `{cmd: "authorize-guest", mac, minutes}` (API antiga, não oficial) |
| Configuração | `src/Models/DataBase/CompanyUnifi.cs:9` | `Host, Site, Username, Password, UnifiOs, VerifySsl` |
| Onde fica salvo | `src/Models/Persistence/AppDbContext.cs:48` | Colunas `Unifi_*` na tabela `Units`; a senha é cifrada (`UnitsController.cs:152`) |
| Quem chama | `src/AccessWifi.Api/Controllers/AuthorizeController.cs:92` | Passa `objUnit.Unifi` + MAC + minutos |
| Erro atual na Itaituba | — | `502 "Falha ao autorizar na UniFi."` — o `Host` está vazio e não há como preenchê-lo |

O ponto importante: **`AuthorizeController` não sabe como a autorização acontece**, ele só chama
`IUnifiClient.AuthorizeGuestAsync(...)` (`IUnifiClient.cs:8`). Isso torna a mudança barata e isolada.

---

## 3. O que proponho

### 3.1 O caminho novo (confirmado na documentação oficial da Ubiquiti)

Três chamadas HTTP, todas para `https://api.ui.com`, todas com o cabeçalho `X-API-KEY`:

```
BASE = https://api.ui.com/v1/connector/consoles/{consoleId}/proxy/network/integration/v1
```

| # | Chamada | Para quê |
| --- | --- | --- |
| 1 | `GET {BASE}/sites` | Descobrir o **siteId** (um UUID, não o "default" de hoje) |
| 2 | `GET {BASE}/sites/{siteId}/clients?filter=macAddress.eq('aa:bb:cc:dd:ee:ff')` | Descobrir o **clientId** do celular do visitante (também UUID) |
| 3 | `POST {BASE}/sites/{siteId}/clients/{clientId}/actions` | Autorizar |

Corpo da chamada 3:
```json
{ "action": "AUTHORIZE_GUEST_ACCESS", "timeLimitMinutes": 1440 }
```

Resposta de sucesso traz `grantedAuthorization` (e `revokedAuthorization`, se o aparelho já estava
liberado — a própria Ubiquiti cancela a anterior e cria uma nova).

O `consoleId` é exatamente o pedaço do link que o cliente mandou:
`58D61F5E15310000000009A4719B000000000A2C62C10000000069211BF2:896725606`.

### 3.2 Requisitos e limites (da documentação oficial)

| Item | Valor | Comentário |
| --- | --- | --- |
| Firmware do console | **≥ 5.0.3** | Precisa confirmar com o TI |
| Chave de API | Criada em `unifi.ui.com → Settings → API Keys` | Aparece **uma única vez**; é da **conta**, não do console |
| Acesso | Chave comum só alcança **consoles da própria conta** | Ver **D5** abaixo |
| Limite de uso | **100 requisições/min por console** | Cada visitante gasta 3 → ~33 logins/min. Folgado para uma loja |
| Tempo limite | 25 s por requisição | Nosso timeout hoje é 15 s (`UnifiClient.cs:47`) |
| Latência | ~800 ms a mais por chamada (o pulo pela nuvem) | Como são 3 chamadas, o visitante espera ~2 s a mais que no modo local. Aceitável, mas é bom saber |

> ⚠️ **Um ponto que só o teste real resolve:** a documentação diz que a chave do Site Manager é
> "somente leitura" hoje, mas o próprio contrato oficial da API publica `POST/PUT/PATCH/DELETE` no
> connector e usa **`AUTHORIZE_GUEST_ACCESS` como exemplo do corpo do POST**. Ou seja: está previsto
> para funcionar. Proponho **validar com uma chamada real antes de escrever o código** (ver §6).

### 3.3 Como fica no nosso sistema

Cada unidade passa a ter um **modo de conexão**:

- **`Local`** — o que existe hoje (Dôce Cafeteria continua igual, sem mexer em nada).
- **`Cloud`** — o caminho novo, para quem não tem IP público (Itaituba).

Campos novos em `CompanyUnifi` (viram colunas `Unifi_*` em `Units`, migration não destrutiva):

| Campo | Tipo | Para quê |
| --- | --- | --- |
| `Mode` | texto (`Local`/`Cloud`) | Qual caminho usar. Default `Local` → nada muda para quem já está no ar |
| `ConsoleId` | texto (120) | O identificador do console no link do `unifi.ui.com` |
| `ApiKey` | texto (512), **cifrado** | A chave `X-API-KEY`, protegida igual à senha de hoje |
| `SiteId` | texto (60) | UUID do site, preenchido automaticamente na primeira vez (cache) |

`Host`, `Username`, `Password`, `UnifiOs` e `VerifySsl` continuam existindo e só são usados no modo
`Local`. **A `ApiKey` nunca é devolvida em nenhum DTO**, mesma regra da senha (`UnitDtos.cs:31`).

### 3.4 Código

- `IUnifiClient` continua igual — `AuthorizeController` **não muda**.
- `UnifiClient` atual vira `UnifiLocalClient` (mesmo código, só renomeado).
- Nasce `UnifiCloudClient` com as três chamadas da §3.1.
- Nasce `UnifiClientRouter` (registrado como `IUnifiClient`): olha `objConfig.Mode` e delega.

---

## 4. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Manter o modo local? | **Sim, os dois convivem** — a Dôce já está funcionando e não pode parar; o modo é por unidade | Migrar tudo para a nuvem e apagar o código local |
| **D2** | Onde guardar a chave de API | **No mesmo bloco `Unifi` da unidade, cifrada com o `IEncryptor` que já existe** (`UnifiClient.cs:95`) | Em `appsettings`/variável de ambiente (não serve: é uma chave por cliente) |
| **D3** | Como descobrir o `siteId` | **Automático**: na primeira autorização chamamos `GET /sites`, gravamos o UUID em `Unifi_SiteId` e reusamos. Se houver mais de um site, erro claro pedindo para escolher | Pedir o UUID para o TI e digitar na mão |
| **D4** | Tempo de liberação | **Enviar `timeLimitMinutes` com o `AccessMinutes` da empresa** (`AuthorizeController.cs:87`), igual ao modo local | Omitir e deixar a UniFi usar o padrão do site |
| **D5** | De quem é a chave de API | **Uma chave com escopo restrito ao console da unidade** — conta Ubiquiti de serviço que só administre aquele console. Ver o alerta abaixo | Usar a chave pessoal do TI (funciona, mas abre a carteira inteira dele) |
| **D6** | Se o aparelho ainda não aparecer na lista de clientes | **Tentar de novo 1 vez após ~1,5 s** e, falhando, devolver erro claro ("aparelho não encontrado na rede") | Falhar de primeira |
| **D7** | Botão "Testar conexão" no painel | **Sim** — uma rota `POST /admin/units/{id}/unifi/test` que só faz o `GET /sites` e diz se a chave e o console estão certos. Evita descobrir que está errado só quando o cliente reclamar | Sem teste; descobrir no uso real |
| **D8** | Timeout | **20 s no modo nuvem** (a Ubiquiti corta em 25 s) e 15 s no local | Manter 15 s nos dois |
| **D9** | Mensagens de erro | **Mensagem genérica para o visitante** ("Falha ao autorizar") e o motivo técnico só no log, como hoje (`AuthorizeController.cs:96`) | Mostrar o erro da Ubiquiti na tela |
| **D11** | Erros que o log precisa distinguir | **Tratar os três casos observados no teste real:** `401` (chave inválida/revogada), `403` (chave fora do escopo daquele console) e `422 api.client.not-guest` (SSID não é de visitantes). Cada um tem causa e solução diferentes | Um `UnifiException` genérico para tudo |
| **D10** | Quando implementar | **Só depois do teste manual da §6 dar certo** | Implementar já e testar depois |

> 🔐 **Alerta de escopo da chave (descoberto no teste real).** A chave do Site Manager é da **conta**,
> não do console. A chave usada no teste dá acesso `owner`/`admin` a **43 consoles** do integrador —
> incluindo clientes que não são nossos e a própria Dôce Cafeteria.
>
> Guardar uma chave dessas no nosso banco significa que um vazamento do nosso servidor entrega a rede
> de dezenas de empresas. **Isso é risco de terceiro que não queremos carregar.** Antes de ir para
> produção, exigir uma chave gerada por uma conta que administre **somente** o console daquela
> unidade.

---

## 5. O que muda em cada lugar

- `src/Models/DataBase/CompanyUnifi.cs` — campos `Mode`, `ConsoleId`, `ApiKey`, `SiteId`.
- Migration `AddUnifiCloud` — 4 colunas novas, todas opcionais, `Mode` com `defaultValue: "Local"`
  (mesmo cuidado do `MIGRATIONS.md`: nenhuma unidade existente pode mudar de comportamento).
- `AppDbContext.cs:48` — tamanhos das colunas novas.
- `Infrastructure/Unifi/` — `UnifiLocalClient.cs` (renomeado), `UnifiCloudClient.cs` (novo),
  `UnifiClientRouter.cs` (novo).
- `Program.cs:123` — registrar o router no lugar do client atual.
- `Features/Units/UnitDtos.cs` — `Mode`, `ConsoleId` e `SiteId` na leitura; `ApiKey` só na escrita
  (`null` = manter a atual).
- `Controllers/UnitsController.cs:137` — gravar e cifrar os campos novos + rota de teste (D7).
- `AuthorizeController.cs` — **sem alteração**.
- Testes — router escolhe o cliente certo pelo modo; fluxo das 3 chamadas com `HttpMessageHandler`
  falso; site não encontrado; MAC não encontrado (com o retry do D6); chave inválida (401).
- `AccessWifi_V2_FRONT/FRONT_CHANGES.md` — novos campos da unidade e o seletor de modo na tela.

---

## 6. Teste real na Itaituba — resultado (2026-09-20)

**O caminho pela nuvem está aberto.** Verificado com chave real do Site Manager:

| Item | Resultado |
| --- | --- |
| `GET /v1/hosts` | `200` — o console **ITAITUBA** aparece, ID igual ao do link |
| Firmware do console | **5.1.33** (requisito: ≥ 5.0.3) ✅ |
| Hardware | UniFi Cloud Gateway Ultra (UDRULT) |
| Versão do Network | **10.6.106** ✅ |
| `GET {BASE}/info` pelo connector | `200 {"applicationVersion":"10.6.106"}` |
| `GET {BASE}/sites` pelo connector | `200` — **1 site só** |
| `siteId` | `88f7af54-98f8-306a-a1c7-c9349722b1f6` (`internalReference: "default"`) |
| Filtro `clients?filter=macAddress.eq('...')` | `200` — 1 resultado, `clientId` correto |
| `POST .../clients/{id}/actions` | **`422 api.client.not-guest`** — ver abaixo |

> ✅ **A chave permite ESCRITA — dúvida encerrada.** O `POST` atravessou a nuvem, o túnel e chegou a
> ser processado pelo Network 10.6.106 do gateway; só foi recusado por **regra de negócio** (o alvo
> do teste não era um guest). Se a chave fosse somente leitura, teríamos parado num `403` antes de
> sair do `api.ui.com`. O formato do corpo que projetamos na §3.1 foi aceito, incluindo
> `timeLimitMinutes`.

### ✅ Autorização real de um visitante — funcionou (2026-09-20 19:40 UTC)

Depois de o TI criar a SSID **PIX REGIONAL** (guest + captive portal) e liberar o portal na
allowlist, um aparelho apareceu como `GUEST / authorized: false` e o fluxo das 3 chamadas da §3.1
rodou inteiro:

```
POST .../sites/{siteId}/clients/{clientId}/actions
{"action":"AUTHORIZE_GUEST_ACCESS","timeLimitMinutes":60}

HTTP 200
{"action":"AUTHORIZE_GUEST_ACCESS","grantedAuthorization":{
  "authorizedAt":"2026-09-20T19:40:57Z",
  "authorizationMethod":"API",
  "expiresAt":"2026-09-20T20:40:57Z"}}
```

Releitura do cliente logo depois:
`{"type":"GUEST","authorized":true,"authorization":{"authorizationMethod":"API",
"usage":{"rxBytes":7023,"txBytes":6091}}}` — ou seja, **o aparelho saiu do bloqueio e navegou.**

**O caminho pela nuvem está validado de ponta a ponta. A §3.1 é exatamente o que o código deve
implementar.** Não há mais incógnita técnica nesta proposta.

### Chave com escopo restrito — funciona (validado)

A tela `unifi.ui.com/settings/api-keys` permite restringir a chave a **um site** e a **uma
aplicação** (recurso do Site Manager 5.5.0, abr/2026). Com a chave restrita ao site da Itaituba:

| Chamada | Resposta | Leitura |
| --- | --- | --- |
| `GET /v1/hosts` | **403** `insufficient permissions` | Esperado — listar a conta inteira está fora do escopo |
| Connector → console **ITAITUBA** | **200** | ✅ |
| Connector → console de outro cliente | **403** | ✅ o escopo realmente isola |

> ⚠️ **Não confundir 401 com 403 ao diagnosticar:**
> `401 unauthorized` = a Ubiquiti não conhece a chave (chave errada, revogada, ou criada na tela
> local do Network). `403 forbidden` = a chave é válida, só não alcança aquele recurso.
> Um `403` no `/v1/hosts` com `200` no connector é o **comportamento correto** de uma chave
> restrita — não é defeito.

> **Pendência:** os 9 dispositivos conectados estão todos com acesso `DEFAULT`. Falta configurar a
> **SSID de visitantes com Hotspot/Captive Portal** apontando para o nosso portal. Sem um cliente
> guest, a API recusa (`Client must be a guest`) e não dá para confirmar se a chave permite escrita.

---

## 6.1. Antes de codar: o teste de 5 minutos

Com a chave em mãos, isto responde se o caminho funciona **sem escrever uma linha de código**:

```bash
curl -sS -H "X-API-KEY: SUA_CHAVE" \
  "https://api.ui.com/v1/connector/consoles/58D61F5E15310000000009A4719B000000000A2C62C10000000069211BF2:896725606/proxy/network/integration/v1/sites"
```

Se voltar a lista de sites, o caminho está aberto e seguimos para o `POST` de autorização com um
celular de teste conectado no Wi-Fi da loja.

---

## 7. O que preciso pedir ao TI da unidade

1. A **versão do UniFi Network** e a **versão do firmware do console** (precisa ser ≥ 5.0.3).
2. Uma **API Key** criada em **`https://unifi.ui.com/settings/api-keys`** (copiar na hora, só
   aparece uma vez).
3. Confirmação de que a conta que gerou a chave é dona/administradora **desse** console.

> ⚠️ **Armadilha das duas telas (já nos custou um dia na Itaituba).** O UniFi tem dois lugares
> chamados "API Keys", e a chave errada dá `401` em tudo:
>
> | URL da tela | Tipo | Serve? |
> | --- | --- | --- |
> | `unifi.ui.com/consoles/{id}/network/default/integrations` | Chave **local** do Network | ❌ Só funciona dentro da rede da loja |
> | `unifi.ui.com/settings/api-keys` | Chave do **Site Manager** (da conta) | ✅ É a da nuvem |
>
> **Regra prática: se tem `/consoles/` na URL, é a chave errada.**
>
> Peça para o TI testar antes de enviar — no PowerShell tem que ser `curl.exe`, porque `curl` puro
> é apelido do `Invoke-WebRequest` e falha com outro erro:
>
> ```
> curl.exe -i -H "X-API-KEY: SUA_CHAVE" https://api.ui.com/v1/hosts
> ```
>
> `HTTP 200` = chave certa. `401` = ainda é a local.

---

## 8. Próximo passo

Me responda **"ok"** para eu seguir com as recomendações (começando pelo teste da §6), ou diga qual
decisão (D1–D10) quer mudar.

---

### Fontes

- [Site Manager API — Getting Started](https://developer.ui.com/site-manager/v1.0.0/gettingstarted)
- [Site Manager API — contrato oficial (OpenAPI)](https://developer.ui.com/site-manager/v1.0.0/openapi.json) — endpoint `/v1/connector/consoles/{id}/*path`
- [Network API v10.4.57 — contrato oficial (OpenAPI)](https://developer.ui.com/network/v10.4.57/openapi.json) — `AUTHORIZE_GUEST_ACCESS`
- [Network API — Filtering](https://developer.ui.com/network/v10.4.57/filtering) — sintaxe `macAddress.eq('...')`
