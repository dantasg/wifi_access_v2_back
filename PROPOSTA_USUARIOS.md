# Proposta — Gestão de usuários (editar, desativar e trocar senha)

> **Status (2026-09-16):** implementada **só a desativação** — D1, D4, D5, D6 e D7.
> Redefinir senha e trocar a própria senha (D2, D3, D8) ficaram **para depois**: haverá outra
> solução, a ser explicada. Por isso o `PUT /admin/users/{id}` hoje recebe só `{ "active": bool }`.

## 1. Entendimento do pedido

Hoje só é possível **listar** e **criar** usuários. Para colocar um cliente em produção faltam
três coisas do dia a dia:

1. **Desativar** o acesso de alguém (ex.: funcionário que saiu da empresa).
2. **Redefinir a senha** de um usuário que esqueceu (feito pelo super admin).
3. O próprio usuário **trocar a senha** (ex.: a senha provisória que você passou na implantação).

Hoje, qualquer um desses casos exige mexer direto no banco.

---

## 2. Como está hoje

| Ponto | Onde | Situação |
| --- | --- | --- |
| Rotas de usuário | `src/AccessWifi.Api/Controllers/UsersController.cs` | Só `GET` e `POST`, ambos exclusivos do super admin |
| Entidade | `src/Models/DataBase/AdminUser.cs` | `Id, IDCompany, Username, PasswordHash, CreatedAt` — **não existe campo de ativo/inativo** |
| Login | `AdminController.cs:50` | Bloqueia se a **empresa** estiver inativa, mas não tem como bloquear **um usuário** |
| Refresh | `AdminController.cs:84` | Mesma checagem da empresa ao renovar o token |
| Sessões | `RefreshToken.RevokedAt` (`AdminController.cs:76`) | Já existe revogação por token — dá para reaproveitar para derrubar as sessões de um usuário |

---

## 3. O que proponho

### Rotas novas

| Rota | Quem | O que faz |
| --- | --- | --- |
| `PUT /admin/users/{id}` | Super admin | Ativa/desativa e, opcionalmente, redefine a senha (`password: null` = mantém a atual) |
| `PUT /admin/me/password` | Qualquer admin logado | Troca a **própria** senha, informando a senha atual |

Corpo do `PUT /admin/users/{id}`:
```json
{ "active": false, "password": null }
```

Corpo do `PUT /admin/me/password`:
```json
{ "currentPassword": "senha-atual", "newPassword": "nova-senha-forte" }
```

O `GET /admin/users` passa a devolver também o campo `active`.

### Banco
Nova coluna `Active` (bool) em `Users`, via migration **não destrutiva**.

> ⚠️ **Cuidado importante na migration:** por padrão o EF cria uma coluna `bool` nova com valor
> `false` para as linhas que já existem. Isso **desativaria todos os usuários atuais, inclusive o
> `root`**, e ninguém conseguiria mais entrar. A migration será ajustada para usar
> `defaultValue: true`, e vou conferir o `Up()` antes de aplicar (regra do `MIGRATIONS.md`).

---

## 4. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Como "remover" um usuário | **Desativar (`Active = false`)**, mantendo o registro — igual a empresas e unidades | Apagar de verdade (`DELETE`) |
| **D2** | Redefinir senha pelo super admin | **Dentro do `PUT /admin/users/{id}`**, com `password: null` = manter (mesmo padrão da senha da UniFi) | Rota separada `PUT /admin/users/{id}/password` |
| **D3** | O próprio usuário trocar a senha | **Sim**, via `PUT /admin/me/password`, exigindo a senha atual | Só o super admin troca senhas |
| **D4** | Trocar a empresa de um usuário | **Não permitir** — se precisar, desativa e cria outro (evita um admin "levar" acesso de uma empresa para outra) | Permitir trocar `IDCompany` no `PUT` |
| **D5** | O que acontece com quem está logado ao ser desativado ou ter a senha trocada | **Revogar todos os refresh tokens dele.** O token de acesso atual ainda vale **até 1h** e depois ele cai | Checar no banco a cada requisição (derruba na hora, mas custa uma consulta por chamada) |
| **D6** | Proteções | **Bloquear** o super admin de desativar a si mesmo e de desativar o **último super admin ativo** | Sem proteção (risco de ficar sem nenhum acesso ao sistema) |
| **D7** | Mensagem no login de usuário inativo | **O mesmo `401` de senha errada** — não revela que o usuário existe | Mensagem específica "Usuário desativado." |
| **D8** | Regra da nova senha | **Mínimo de 8 caracteres**, igual ao cadastro de hoje | Exigir também letras + números |

---

## 5. O que muda em cada lugar

- `AdminUser.cs` — nova propriedade `Active`.
- Migration `AddUserActive` — coluna com `defaultValue: true`.
- `UsersController.cs` — `PUT /{id}` e `active` no `GET`/`POST`.
- `AdminController.cs` — login e refresh recusam usuário inativo; nova rota `PUT /admin/me/password`.
- `UserDtos.cs` — `UpdateUserRequest`, `ChangePasswordRequest` e `Active` no `UserDto`.
- Testes — desativar, reativar, redefinir senha, trocar a própria senha (senha atual errada → 400),
  login de inativo → 401, refresh revogado, e as proteções do D6.
- `AccessWifi_V2_FRONT/FRONT_CHANGES.md` — contrato das rotas novas + checklist para o front.

---

## 6. Próximo passo

Me responda **"ok"** para seguir com as recomendações, ou diga qual decisão (D1–D8) quer mudar.
