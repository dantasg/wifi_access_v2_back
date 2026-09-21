# Proposta — Portal rápido na boca do caixa

> **Status (2026-09-21) — fase 2 em produção.** A busca adiantada (parte A abaixo) **não serviu no
> uso real** e foi trocada por uma autorização numa ida só. As partes B (sem espera fixa) e C
> (imagens menores) continuam valendo.
>
> ### O que aconteceu com a parte A
>
> Medi a fase 1 com aparelhos conectados havia horas, e ela funcionou (média de 0,66 s). O visitante
> real é diferente: o teste do TI às 11:07 mostrou que ele abre o portal **2 s depois de conectar**.
> Nesse momento a controladora ainda não lista o aparelho, a busca adiantada voltou vazia, e a
> autorização levou **2,2 s** pelo caminho completo. A melhora que o TI sentiu veio só da parte B.
>
> ### Fase 2 — autorizar numa ida só, pelo MAC
>
> A UniFi tem uma API **clássica** (`cmd/stamgr`, `authorize-guest`) que libera **pelo MAC**, sem
> precisar do ID do aparelho e sem depender de a controladora já listá-lo. Testei pela nuvem da
> Ubiquiti, com a mesma chave:
>
> | Teste em produção (21/09) | Resultado |
> | --- | --- |
> | Leitura (`stat/sta`) | `HTTP 200`, `rc: ok`, 9 aparelhos — 0,6 a 1 s |
> | **Autorização real** (celular de teste do TI) | `HTTP 200`, `rc: ok`, `authorized_by: api` — **0,93 s** |
> | Conferência pela API oficial | vencimento estendido para +24 h a partir do comando ✓ |
>
> - **Caminho principal:** clássica, **uma** ida à loja. Toque → resposta em ~0,6–1 s, sem depender
>   de timing.
> - **Plano B:** se a clássica falhar (a Ubiquiti não a documenta oficialmente), o sistema cai na
>   API oficial, com duas idas. Fica mais lento, mas não para.
> - **Sem plano B** quando não adianta tentar de novo: chave recusada (401/403), limite de chamadas
>   (429) ou nuvem fora do ar. Nesses casos a oficial falharia igual, e só dobraria a espera.
> - **Saiu o `POST /authorize/prepare`** (back e front): com uma ida só, não há o que adiantar.
> - **Log de diagnóstico:** cada autorização registra o caminho (clássico/oficial) e o tempo
>   (`journalctl -u accesswifi-api | grep "Autorização UniFi"`). Sem MAC nem dado pessoal.
>
> O piso que sobra, ~0,6–1 s, é a ida pelo túnel da Ubiquiti. Para baixo disso, só com um túnel
> direto até a loja (WireGuard): ver [TUNEL_LOJA_TI.md](TUNEL_LOJA_TI.md).
>
> ---
>
> *Abaixo, a proposta original da fase 1, mantida como registro.*

## 1. Entendimento do pedido

O TI que testou em Itaituba achou o portal **lento**. E o cenário que importa é o pior possível: o
cliente **na fila do caixa**, com o celular na mão, querendo conectar para pagar. Ali, cada segundo
parece dez.

**Meta:** do toque em "Conectar" até a internet liberada em **cerca de 1 segundo**. O portal deve
abrir já com a marca, sem piscar.

---

## 2. O que foi medido (visita real do celular do TI, 21/09)

Tempos tirados do log do nginx da produção. Nenhum é estimativa:

| Hora | Etapa | Tempo no servidor | Tamanho |
| --- | --- | --- | --- |
| 08:59:38 | página + CSS + JS | ~0 s | 94 KB (compactado) |
| 08:59:40 | tema (`/settings`) | 0,2 s | **193 KB** |
| 09:00:53 | toque em "Conectar" (`/authorize`) | **1,6 s** | — |

Medições complementares, feitas a partir da VPS:

| O quê | Resultado |
| --- | --- |
| Conexão até o `api.ui.com` | 68 ms (+ 70 ms de TLS) — **rápido** |
| Cada chamada ao console da loja, pela nuvem | **0,4 a 0,98 s** — é a volta até Itaituba pelo túnel da Ubiquiti |
| Chamadas por autorização hoje | **2**: descobrir o ID do aparelho pelo MAC, depois autorizar |

E um tempo que não aparece em log nenhum, porque é espera de propósito no front
(`AccessWifi_V2_FRONT/src/pages/GuestPortal.tsx:80`): depois que a API responde, o portal aguarda
**1,2 segundo** só para exibir "Conectado!" antes de redirecionar.

### Para onde vai o tempo, do toque até o redirecionamento

```
toque ──► busca o ID pelo MAC ──► autoriza ──► espera fixa ──► redireciona
          ~0,5–1 s (loja)         ~0,5–1 s     1,2 s
          └──────────── 1,6 s medido ─────────┘
                                                      total ≈ 3 s
```

### E por que o tema pesa 193 KB

As imagens vão **embutidas no JSON**, e estão muito maiores do que aparecem na tela:

| Imagem | Peso | Aparece com |
| --- | --- | --- |
| logo | 122 KB | no máximo 150 px de largura (o arquivo tem 2.668 px) |
| favicon | **148 KB** | 16 a 32 px (o ícone da aba) |
| banner | 61 KB | a largura do cartão |

A compactação do nginx já está ligada, mas PNG em base64 quase não comprime (338 KB → 193 KB). E a
rede de visitantes, antes da liberação, costuma ter banda limitada pela UniFi.

---

## 3. O que proponho

### A — Adiantar a busca do ID enquanto o cliente preenche o formulário ⭐ o maior ganho

O portal já tem o MAC do aparelho desde o primeiro segundo, porque a UniFi manda na URL. E o cliente
leva de 30 a 70 segundos preenchendo o formulário (o TI levou 73). A busca do ID pode acontecer
**nesse intervalo**, em segundo plano:

```
portal abre ──► POST /authorize/prepare  (em segundo plano, o cliente nem percebe)
                └─► busca o ID na loja e guarda na memória por 15 min
   … cliente preenche …
toque ──► /authorize ──► ID já está pronto ──► só autoriza (1 ida à loja)
```

- Usa **só a API oficial**. Nada de engenharia reversa: testei, e o ID **não** é derivável do MAC.
- **Não aumenta** o uso da nuvem: continuam 2 chamadas por visitante, só que uma sai do caminho crítico.
- Se a preparação não terminar a tempo ou falhar, o `/authorize` faz a busca como hoje. **Nunca fica
  pior que o atual.**
- Se as duas chegarem juntas, o `/authorize` aproveita a busca que já está em andamento, em vez de
  começar outra.

### B — Tirar a espera fixa de 1,2 s

Redirecionar assim que a API confirmar. A mensagem "Conectado!" aparece por um instante e o celular
já segue.

### C — Imagens do tema no tamanho em que aparecem

| Imagem | Hoje | Proposto |
| --- | --- | --- |
| logo | 2.668 px · 122 KB | 300 px (nítido em tela retina) · ~12 KB |
| favicon | 148 KB | 64 × 64 px · ~3 KB |
| banner | 1.600 px · 61 KB | mantém |

O `/settings` cai de **193 KB para uns 50 KB**. Esta parte é só dado: redimensiono no banco, sem
mudar código, do mesmo jeito que fizemos com o banner.

### Resultado esperado

| | Hoje | Depois |
| --- | --- | --- |
| Toque → internet liberada | **~3 s** | **~0,6–1 s** |
| Tema carregado | 193 KB | ~50 KB |

---

## 4. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Adiantar a busca do ID | **Sim, com `POST /authorize/prepare` chamado ao abrir o portal** | Guardar o ID só de quem volta (não ajuda na primeira visita, que é a do caixa) |
| **D2** | Onde guardar o ID adiantado | **Na memória da API, por 15 min** — some num reinício, e aí cai na busca normal | No banco (persistente, mas um cadastro a mais para um dado que vive minutos) |
| **D3** | Proteção da rota nova | **Limite próprio de 20 chamadas/min por IP** e validação de unidade e MAC — ela só lê, nunca autoriza | Usar o mesmo limite do `/authorize` (10/min), que seria dividido entre as duas rotas |
| **D4** | Espera após "Conectado!" | **Redirecionar na hora** | Manter uma pausa curta (300 ms) para a mensagem ser lida |
| **D5** | Redimensionar as imagens da Regional | **Sim, logo em 300 px e favicon em 64 px, direto no banco** | Pedir novas imagens ao cliente |
| **D6** | Unidades em modo Local (Dôce) | **A preparação não faz nada nelas** — lá a autorização já é local e rápida | Adiantar o login na controladora também |

---

## 5. O que muda em cada lugar

- `Infrastructure/Unifi/UnifiCloudClient.cs:165` — `FindClientIdAsync` ganha um cache em memória por
  unidade + MAC, que também guarda a busca em andamento.
- `IUnifiClient` + `UnifiClientRouter` — novo `PrepareAsync` (no modo Local não faz nada).
- `Controllers/AuthorizeController.cs` — nova rota `POST /authorize/prepare`. Responde na hora
  (`202`) e deixa a busca rodando em segundo plano.
- `Program.cs:98` — nova política de rate limit `authorize-prepare`.
- `AccessWifi_V2_FRONT/src/pages/GuestPortal.tsx:22` — chama o `prepare` assim que a unidade é
  identificada, sem esperar a resposta.
- `AccessWifi_V2_FRONT/src/pages/GuestPortal.tsx:80` — sai a espera de 1,2 s.
- Banco de produção — logo e favicon da Regional redimensionados.
- Testes: cache aproveitado no `/authorize`; busca em andamento reaproveitada; falha na preparação
  cai na busca normal; modo Local ignora; validação e limite da rota.

Depois de publicar, **meço de novo** com o mesmo método do §2. Os números de depois ficam registrados
aqui, ao lado dos de antes.

---

## 6. Próximo passo

Me responda **"ok"** para eu seguir com as recomendações, ou diga qual decisão (D1–D6) quer mudar.

A parte C (imagens) não depende de código e tem efeito imediato. Se quiser, faço ela primeiro, sozinha.
