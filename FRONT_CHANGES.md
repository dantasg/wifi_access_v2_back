# 🏢 AccessWifi_v2 — Multi empresa: o que o FRONT precisa mudar

O backend virou **multi empresa (multi tenant)**: tudo — leads, tema/settings, usuários
admin e controladora UniFi — é dividido por empresa (`IDCompany`) no mesmo banco.
Este doc lista **todas** as mudanças que o front precisa fazer. Contratos que não
aparecem aqui **não mudaram**.

---

## 1. Conceito

- Cada empresa tem um **slug** (ex.: `doce`) — identificador público, minúsculas/números/hífen.
- O **portal do visitante** identifica a empresa pelo slug na URL.
- O **admin** é por empresa: o login devolve o papel e a empresa do usuário; os endpoints
  admin já filtram tudo pela empresa do token (o front não precisa mandar nada extra).
- Existe um papel novo, **super admin** (`superadmin`), que enxerga todas as empresas
  e tem endpoints exclusivos de gestão (item 6 — UI nova, opcional nesta fase).

Credenciais de dev (seed automático):
| Usuário | Senha | Papel |
|---|---|---|
| `admin` | `admin` | admin da empresa **Dôce Cafeteria** (slug `doce`) |
| `root`  | `admin` | super admin |

---

## 2. Portal do visitante — slug obrigatório na URL ⚠️ (breaking)

A URL do portal agora carrega a empresa: `https://portal.com/?company=doce`
(cada empresa configura a própria URL, com seu slug, na controladora UniFi dela).

**O front precisa:**

1. Ler `company` da query string (junto com os params que a UniFi já manda: `id`, `ap`, `ssid`, `url`).
2. **`GET /settings?company={slug}`** — o parâmetro agora é obrigatório:
   - `400 { "error": "Informe a empresa (?company=slug)." }` — sem o parâmetro
   - `404 { "error": "Empresa não encontrada." }` — slug errado ou empresa inativa
   - Nesses casos, exibir os padrões da marca (comportamento atual de erro já serve).
3. **`POST /authorize`** — o body ganhou o campo **`company`** (o slug lido da URL):

```json
{
  "nome": "...", "instagram": "...", "telefone": "...", "nascimento": "...",
  "consentimento": true,
  "company": "doce",
  "mac": "...", "ap": "...", "ssid": "...", "url": "..."
}
```

Erros novos do `/authorize` (mesmo shape `{ authorized: false, error }` de sempre):
- `"Empresa não informada."` — sem `company` no body
- `"Empresa não encontrada ou inativa."` — slug inválido

---

## 3. Login do admin — resposta com papel e empresa

`POST /admin/login` recebe o mesmo `{ username, password }`. A resposta ganhou campos:

```json
{
  "token": "<jwt>",
  "role": "admin",
  "company": { "id": "<guid>", "name": "Dôce Cafeteria", "slug": "doce" }
}
```

- `role`: `"admin"` (admin de empresa) ou `"superadmin"`.
- `company`: **omitida/nula para super admin**; para admin comum, útil para exibir o
  nome da empresa no painel e para obter o slug sem hardcode.
- Quem só usa `token` continua funcionando sem mudança.

---

## 4. Painel admin de empresa — nada muda nas chamadas

Para o usuário `admin` (papel `admin`):

- `GET /admin/leads` — igual: devolve **só os leads da empresa do token**.
- `PUT /admin/settings` — igual: salva **as settings da empresa do token**.
- Preview do portal: se quiser abrir o portal a partir do painel, monte a URL com
  o slug vindo do login (`/?company=doce`).

---

## 5. Painel do super admin — chamadas existentes exigem `?company`

Se o usuário logado tiver `role === "superadmin"`, as rotas de sempre pedem a
empresa via query string:

- `GET /admin/leads?company={slug}` — sem o parâmetro → `400 { error }`
- `PUT /admin/settings?company={slug}` — idem

Sugestão de UI: um seletor de empresa no topo do painel quando `role === "superadmin"`
(a lista vem do endpoint abaixo).

---

## 6. Endpoints novos de gestão (só super admin) — para a UI nova

Todos exigem `Authorization: Bearer <token>` de super admin (`403` para admin comum).
Erros de validação: `400 { "error": "mensagem em português" }` (pronta para exibir).

### Empresas

- **`GET /admin/companies`** → lista:
```json
[{
  "id": "<guid>", "name": "Dôce Cafeteria", "slug": "doce", "active": true,
  "createdAt": "2026-07-22T14:14:10Z",
  "reportEmail": "gerente@doce.com.br", "reportSendDay": 1,
  "unifi": { "host": "https://192.168.1.1", "site": "default", "username": "u", "unifiOs": true, "verifySsl": false }
}]
```
  (a **senha da UniFi nunca é devolvida**; `reportEmail` vem omitido quando não configurado)

- **`POST /admin/companies`** → cria:
```json
{
  "name": "Padaria Teste",
  "slug": "padaria",
  "reportEmail": "gerente@padaria.com.br",
  "reportSendDay": 1,
  "unifi": { "host": "https://10.0.0.2", "site": "default", "username": "u", "password": "p", "unifiOs": true, "verifySsl": false }
}
```
  Regras: `slug` só `a-z 0-9 -` (2–40 chars), único e **imutável**; `unifi` é opcional
  (dá para configurar depois via PUT).

- **`PUT /admin/companies/{id}`** → atualiza `{ name, active, reportEmail?, reportSendDay?, unifi? }`.
  `unifi.password: null` = **manter a senha atual** (para editar sem redigitar).
  `active: false` desativa a empresa (portal e login dos admins dela param de funcionar).

#### ⭐ Campos novos: relatório mensal por e-mail (`reportEmail` + `reportSendDay`)

Um serviço separado (**AccessWifiService**) envia mensalmente, por e-mail, um CSV com os
cadastros do **mês anterior** de cada empresa. Dois campos por empresa controlam isso — ambos
editáveis no CRUD de empresas do super admin:

| Campo | Tipo | Regra | Significado |
| ----- | ---- | ----- | ----------- |
| `reportEmail` | string \| null | e-mail válido; vazio/`null` = empresa **não** recebe relatório | destinatário do relatório |
| `reportSendDay` | int | **1 a 28** (default `1`) | dia do mês em que o e-mail é disparado |

- Erros de validação (400 `{ error }`, prontos para exibir): `"E-mail do relatório inválido."`
  e `"Dia de envio do relatório deve ficar entre 1 e 28."`.
- No `POST`, ambos são **opcionais**: sem `reportEmail` a empresa não recebe relatório; sem
  `reportSendDay` assume `1`.
- Nada disso afeta o portal do visitante nem os fluxos existentes — é só mais dois campos no
  formulário de empresa do super admin. **Se o painel de empresas ainda não existe no front,
  esses campos entram junto quando ele for criado** (nenhuma tela atual quebra sem eles).

### Usuários do painel

- **`GET /admin/users`** (opcional `?company={slug}`) → lista:
```json
[{ "id": "<guid>", "username": "admin", "role": "admin", "idCompany": "<guid>", "companyName": "Dôce Cafeteria", "createdAt": "..." }]
```

- **`POST /admin/users`** → cria:
```json
{ "username": "novoadmin", "password": "minimo-8-chars", "idCompany": "<guid>" }
```
  `idCompany: null` cria **outro super admin**. Username é único no sistema, sem espaços,
  guardado em minúsculas. Senha mínima de 8 caracteres.

> Ainda **não existem** editar/excluir usuário nem excluir empresa (use `active: false`).
> Se a UI precisar, é pedir que o back adiciona.

---

## 7. Proxy do Vite (dev)

Adicionar as rotas novas ao `vite.config.ts` (as atuais continuam):

```ts
proxy: {
  '/authorize': 'http://localhost:5000',
  '/settings': 'http://localhost:5000',
  '/admin/login': 'http://localhost:5000',
  '/admin/leads': 'http://localhost:5000',
  '/admin/settings': 'http://localhost:5000',
  '/admin/companies': 'http://localhost:5000',
  '/admin/users': 'http://localhost:5000',
},
```

Em dev, teste o portal com `http://localhost:5173/?company=doce`.

---

## 8. ✅ Checklist de progresso — o FRONT preenche

> Marque `[x]` conforme for concluindo cada item e **devolva este arquivo atualizado** (ou
> cole o checklist no chat) para o back saber o que já está pronto. Use a coluna "Obs." para
> dúvidas, bloqueios ou combinações (ex.: "feito, mas falta tratar erro X").
> Legenda de status: ⬜ a fazer · 🟨 em andamento · ✅ feito · ❌ não se aplica.

### Portal do visitante
| Status | Item | Obs. |
| :----: | ---- | ---- |
| ⬜ | Ler `company` da URL (`?company=slug`) junto com `id`/`ap`/`ssid`/`url` | |
| ⬜ | `GET /settings?company={slug}` (parâmetro agora obrigatório) | |
| ⬜ | Enviar `company` no body do `POST /authorize` | |
| ⬜ | Tratar `400/404` de empresa inválida (cair nos padrões da marca) | |

### Login / sessão
| Status | Item | Obs. |
| :----: | ---- | ---- |
| ⬜ | Guardar `role` e `company` da resposta do login (além do `token`) | |
| ⬜ | Usar `company.name`/`company.slug` no painel do admin comum (título, preview do portal) | |

### Painel do super admin (telas novas)
| Status | Item | Obs. |
| :----: | ---- | ---- |
| ⬜ | Seletor de empresa quando `role === "superadmin"` + enviar `?company=` em leads/settings | |
| ⬜ | Tela de empresas (`GET/POST /admin/companies`, `PUT /admin/companies/{id}`) | |
| ⬜ | No formulário de empresa: campos `reportEmail` e `reportSendDay` (1–28) | |
| ⬜ | Tela de usuários (`GET/POST /admin/users`) | |

### Infra do front
| Status | Item | Obs. |
| :----: | ---- | ---- |
| ⬜ | `vite.config.ts`: proxy para `/admin/companies` e `/admin/users` | |
| ⬜ | Desligar modo mock (`.env.local` com `VITE_USE_MOCK=false` ou remover o arquivo) | |

> Campos **só de leitura** que o back devolve e o painel pode exibir (sem ação obrigatória):
> `company.createdAt`, `company.lastReportSentAt` (quando o último relatório foi enviado).

---

## Notas de infra (sem ação imediata do front)

- A UniFi de **cada empresa** deve apontar o captive portal para a URL com o slug dela
  (`?company=slug`) e liberar o domínio do portal no walled garden.
- Rate limits continuam: 10 req/min no `/authorize`, 5 req/min no `/admin/login` (429 sem body).
- O relatório mensal por e-mail é responsabilidade de um serviço separado (**AccessWifiService**),
  não do front — este só cadastra `reportEmail`/`reportSendDay` no formulário de empresa.
- Referências no back: `FRONT_CHANGES.md` (este doc) e `BACKEND_SPEC.md` (contrato v1 — a
  parte de empresas substitui o que estiver conflitante lá).
