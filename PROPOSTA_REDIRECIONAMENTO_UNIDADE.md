# Proposta — Redirecionamento (Instagram) por unidade, com o da empresa como "Geral"

> **Status (2026-09-28): implementada com as recomendações (D1–D6).** Unidades existentes começaram
> vazias: Itaituba continua indo para o Instagram que está na Geral da Lojas Regional.

## 1. Entendimento do pedido

Depois de liberar o Wi-Fi, o visitante é levado para uma página, hoje o Instagram da loja. Essa URL
é **uma só por empresa**. O pedido:

- cada **unidade** pode ter a sua própria URL;
- a da **empresa** continua existindo e vale como **"Geral"**: se a unidade não tiver URL
  cadastrada, usa a da empresa.

---

## 2. Como está hoje

| O quê | Onde |
| --- | --- |
| A URL é guardada **por empresa** | `PortalSettings.RedirectUrl` — `src/Models/DataBase/PortalSettings.cs:30` |
| É editada em **Configurações → Acesso à internet** (admin da empresa e super admin) | `AccessWifi_V2_FRONT/src/components/admin/SettingsPanel.tsx:315` |
| Quem decide para onde o visitante vai é o back, na liberação | `Controllers/AuthorizeController.cs:121` — empresa → URL que a UniFi mandou → Google |

O que está gravado na produção agora:

| Empresa | URL "geral" | Unidades |
| --- | --- | --- |
| Lojas Regional | `https://www.instagram.com/lojasregionalitaituba/` | Itaituba |
| Dôce Cafeteria | *(vazio — vai para a URL da UniFi)* | Matriz |

---

## 3. Como fica

A ordem para decidir o destino do visitante passa a ser:

```
1. URL da unidade        (nova)
2. URL da empresa        ("Geral", a de hoje)
3. URL que a UniFi mandou
4. Google
```

Na tela **Configurações → Acesso à internet**:

```
URL de redirecionamento — Geral     [ https://instagram.com/lojasregional       ]
Vale para as unidades sem URL própria.

Por unidade  (vazio = usa a Geral)
  Itaituba                          [                                          ]  usando a Geral
  Santarém                          [ https://instagram.com/regionalsantarem   ]
```

Um único **Salvar alterações** grava a Geral e as das unidades, como já é hoje.

---

## 4. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Ordem do destino | **Unidade → Geral (empresa) → URL da UniFi → Google** — é o que você pediu | — |
| **D2** | Onde editar | **Em Configurações, logo abaixo da URL Geral, uma linha por unidade.** Tudo que decide "para onde o visitante vai" fica numa tela só | No formulário de cada unidade (tela Unidades) |
| **D3** | Quem edita | **Admin da empresa e super admin, como a Geral hoje.** O dono da loja troca o Instagram sem depender de nós | Só o super admin (a tela Unidades é só dele) |
| **D4** | Como salva | **No mesmo "Salvar alterações" das Configurações** | Um botão de salvar por unidade |
| **D5** | O que já está gravado | **Nada muda:** as unidades começam vazias e a Lojas Regional continua com o Instagram de Itaituba como Geral. O visitante de Itaituba vai para o mesmo lugar de hoje | Passar o Instagram de Itaituba para a **unidade** Itaituba e deixar a Geral vazia (ou com o Instagram da rede, se existir) |
| **D6** | Validação | **A mesma da Geral:** endereço `http://` ou `https://` completo, até 2.048 caracteres | — |

> ⚠️ **Sobre a D5:** com a recomendação, quando a Lojas Regional abrir uma segunda unidade sem URL
> própria, o visitante dela vai para o Instagram **de Itaituba**, porque ele é a Geral. Se existir um
> Instagram da rede, a alternativa evita isso. Posso ajustar depois, pela própria tela.

---

## 5. O que muda em cada lugar

**Back**

- `src/Models/DataBase/Unit.cs` — campo novo `RedirectUrl` (vazio = usa a Geral) e migration
  `AddUnitRedirectUrl`: **só cria a coluna**, não altera nenhum dado. O script de publicação faz
  backup do banco antes, como sempre.
- `Controllers/AuthorizeController.cs:119` — a ordem nova da D1.
- `Features/Units/UnitDtos.cs` — a unidade passa a trazer `redirectUrl`, para a tela mostrar.
- `Controllers/SettingsController.cs:80` (`PUT /admin/settings`) — aceita, opcionalmente, a lista
  `unitRedirects: [{ unitId, redirectUrl }]`. Confere que cada unidade é **da empresa** (um admin
  nunca mexe na unidade de outra), aplica a mesma validação da Geral e grava tudo junto. Sem a lista,
  as URLs das unidades ficam como estão.
- `GET /settings` (público) **não muda**: o portal não precisa saber a URL, quem decide é o back.
- Testes: a ordem da D1 nos quatro casos; unidade de outra empresa recusada; URL inválida
  recusada; "vazio" volta a usar a Geral.

**Front**

- `src/components/admin/SettingsPanel.tsx` — a seção "Por unidade" do desenho acima.
- `src/lib/api.ts` — os tipos novos.
- `FRONT_CHANGES.md` — Parte 10.

**Publicação:** `./deploy/publicar-producao.sh tudo` (back com a migration, mais o front), cerca de
2 minutos. A API reinicia por alguns segundos.

---

## 6. Próximo passo

Me responda **"ok"** para eu seguir com as recomendações, ou diga qual decisão (D2–D5) quer mudar.
