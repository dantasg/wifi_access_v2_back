# AccessWifi_v2 — Guia do Backend em C# / ASP.NET Core (do zero)

Guia completo para construir o **backend** (`AccessWifi_V2_BACK`) em **C# / ASP.NET Core**,
já **alinhado ao frontend** que está pronto (`AccessWifi_V2_FRONT`).

Os contratos aqui são a **fonte da verdade** do que o front espera hoje (`src/lib/api.ts`).
Seguindo este guia, ao final é só apontar `VITE_API_URL` para a API e desligar o modo
demonstração — o front funciona sem alterações.

---

## 1. Visão geral

Captive portal UniFi para a **Dôce Cafeteria**. Dois "públicos":

- **Visitante** (portal aberto, **sem login nenhum**): conecta no Wi-Fi, a UniFi redireciona
  para o front com parâmetros na URL, ele preenche o formulário → o back grava o cadastro e
  **autoriza o dispositivo na controladora UniFi**.
- **Admin** (protegido por **JWT**): faz login e vê/gerencia os cadastros.

```
[Visitante] → UniFi → [Front /] → POST /authorize → [Back] → grava lead
                                                          └→ UniFi: authorize-guest (MAC)

[Admin] → [Front /admin] → POST /admin/login → JWT → GET /admin/leads (Bearer) → leads
```

> ⚠️ **O portal do visitante NÃO tem autenticação.** JWT vale só para `/admin/*`.

---

## 2. Endpoints (resumo)

| Método | Rota              | Auth         | Função                                            |
| ------ | ----------------- | ------------ | ------------------------------------------------- |
| POST   | `/authorize`      | pública      | Grava o lead + autoriza o dispositivo na UniFi    |
| POST   | `/admin/login`    | pública      | Valida credenciais → retorna `{ token }` (JWT)    |
| GET    | `/admin/leads`    | JWT (Bearer) | Lista os cadastros                                |
| GET    | `/settings`       | pública      | Tema/marca para o portal (fase futura)            |
| PUT    | `/admin/settings` | JWT (Bearer) | Salva tema + parâmetros (fase futura)             |

---

## 3. Stack e pré-requisitos

- **.NET 8 (LTS) ou superior** — ASP.NET Core **Web API**.
- **Entity Framework Core** + **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`) ou
  **SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`) para um VPS simples.
- **JWT**: `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **Hash de senha**: `BCrypt.Net-Next` (ou `PasswordHasher<T>` embutido do ASP.NET).
- **Rate limiting**: middleware embutido (`Microsoft.AspNetCore.RateLimiting`, .NET 7+).

> Serialização: o `System.Text.Json` do ASP.NET já usa **camelCase** por padrão e desserializa
> ignorando maiúsc/minúsc. Ou seja, propriedades C# em `PascalCase` (ex.: `Nome`, `Authorized`,
> `Timestamp`) viram exatamente o JSON que o front espera (`nome`, `authorized`, `timestamp`).
> **Não precisa configurar nada** para casar os nomes.

---

## 4. Setup do projeto

```bash
# na pasta AccessWifi_V2_BACK (guarde este .md se o template reclamar de pasta não vazia)
dotnet new webapi -n AccessWifi.Api
cd AccessWifi.Api

# EF Core (escolha um provider)
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL     # PostgreSQL
# ou: dotnet add package Microsoft.EntityFrameworkCore.Sqlite

# Auth + hash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package BCrypt.Net-Next

# ferramenta de migrations (uma vez, global)
dotnet tool install --global dotnet-ef
```

Estrutura sugerida:

```
AccessWifi.Api/
  Program.cs                 # setup: CORS, Auth, EF, RateLimiter, DI
  appsettings.json           # config (segredos via env / user-secrets)
  Controllers/
    AuthorizeController.cs    # POST /authorize
    AdminController.cs        # POST /admin/login, GET /admin/leads
    SettingsController.cs     # (futuro)
  Dtos/
    AuthorizeRequest.cs
    AuthorizeResponse.cs
    LoginRequest.cs
    LeadDto.cs
  Models/
    Lead.cs
  Data/
    AppDbContext.cs
  Services/
    LeadsService.cs
    UnifiService.cs           # HttpClient para a controladora
    TokenService.cs           # gera o JWT
```

---

## 5. Configuração (`appsettings.json` + segredos)

Estrutura de config (deixe **segredos** fora do arquivo — use variáveis de ambiente ou
`dotnet user-secrets` em dev):

```jsonc
{
  "FrontOrigin": "https://portal.seudominio.com.br",
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=accesswifi;Username=user;Password=..."
  },
  "Admin": {
    "Username": "admin",
    "PasswordHash": "<hash bcrypt da senha>"     // NUNCA em texto puro
  },
  "Jwt": {
    "Secret": "<string longa e aleatória>",
    "ExpiresInHours": 8,
    "Issuer": "accesswifi",
    "Audience": "accesswifi-admin"
  },
  "Unifi": {
    "Host": "https://192.168.1.1",               // inclua :8443 se controller clássico
    "Site": "default",
    "Username": "usuario-unifi",
    "Password": "senha-unifi",
    "UnifiOs": true,                             // true: UDM/Cloud Gateway/UCG | false: clássico
    "VerifySsl": false,                          // UDM usa certificado self-signed
    "AccessMinutes": 1440                        // tempo de liberação do guest
  }
}
```

Gerar o hash da senha do admin (script rápido em C#):
```csharp
Console.WriteLine(BCrypt.Net.BCrypt.HashPassword("SUA_SENHA"));
```

---

## 6. Ordem de implementação sugerida

1. **Scaffold + `Program.cs`** (CORS, EF, `[ApiController]`, Swagger opcional).
2. **`AppDbContext` + `Lead`** e primeira migration.
3. **`POST /authorize`** gravando o lead (ainda sem UniFi) — já dá pra testar com o front.
4. **`UnifiService`** (HttpClient) e plugar no `/authorize`.
5. **JWT**: `POST /admin/login` (bcrypt + gera token) + `AddAuthentication().AddJwtBearer(...)`.
6. **`GET /admin/leads`** com `[Authorize]`.
7. **Segurança**: `AddRateLimiter`, limite de payload, HTTPS.
8. **(Futuro) Configurações/tema**: `GET /settings` + `PUT /admin/settings` + upload.

---

## 7. Contratos de API (exatos)

### 7.1 `POST /authorize` — pública

**Request body** (JSON):

| Campo           | Tipo         | Notas                                              |
| --------------- | ------------ | -------------------------------------------------- |
| `nome`          | string       | obrigatório                                        |
| `instagram`     | string       | pode vir vazio                                     |
| `telefone`      | string       | já mascarado, ex.: `(91) 90000-0000`               |
| `nascimento`    | string       | formato `dd/mm/aaaa`                                |
| `consentimento` | bool         | **precisa ser `true`** (LGPD)                      |
| `mac`           | string?      | MAC do cliente (param `id` da UniFi)               |
| `ap`            | string?      | MAC do access point                                |
| `ssid`          | string?      | nome da rede                                       |
| `url`           | string       | destino do redirect (default `https://www.google.com`) |

**Validações → 400 `{ error }`:**
- `mac` ausente → `"MAC do cliente ausente."`
- `consentimento != true` → `"É necessário aceitar os termos (LGPD)."`

**Fluxo:** grava o lead (`Timestamp = DateTime.UtcNow`) → chama `UnifiService.AuthorizeGuestAsync`.

**200:**
```json
{ "authorized": true, "redirect": "<url recebida>" }
```
**Erro (ex.: falha UniFi) — 4xx/5xx:**
```json
{ "authorized": false, "error": "Falha ao autorizar na UniFi." }
```
> O front considera sucesso só se `res.ok && authorized === true`; senão exibe `error`.

DTOs de exemplo:
```csharp
public record AuthorizeRequest(
    string Nome, string Instagram, string Telefone, string Nascimento,
    bool Consentimento, string? Mac, string? Ap, string? Ssid, string Url);

public record AuthorizeResponse(bool Authorized, string? Redirect = null, string? Error = null);
```

---

### 7.2 `POST /admin/login` — pública

**Request:** `{ "username": "admin", "password": "..." }`

- **401** se inválido → front mostra `"Usuário ou senha inválidos."`
- **200:** `{ "token": "<jwt>" }`

> O front guarda o token **só em memória** (F5 → novo login) e o envia como
> `Authorization: Bearer <token>` nas rotas admin.

```csharp
public record LoginRequest(string Username, string Password);
// resposta: new { token }
```

---

### 7.3 `GET /admin/leads` — JWT

Header: `Authorization: Bearer <token>` (controller/action com `[Authorize]`).

- **401** se token faltar/inválido/expirado → front mostra `"Sessão expirada. Entre novamente."`
- **200:** array de leads (ordem livre; o front reordena, filtra e exporta no cliente):

```json
[
  {
    "timestamp": "2026-07-15T14:30:00Z",
    "nome": "Ana Beatriz Souza",
    "instagram": "@anabsouza",
    "telefone": "(91) 98888-1234",
    "nascimento": "12/03/1998",
    "mac": "aa:bb:cc:dd:ee:ff",
    "ap": "...",
    "ssid": "Doce"
  }
]
```

**Formato de cada lead:**

| Campo (JSON) | Tipo C#   | Obrig. | Notas                                                  |
| ------------ | --------- | ------ | ------------------------------------------------------ |
| `timestamp`  | DateTime  | sim    | serializa em **ISO 8601** (guarde em **UTC**)          |
| `nome`       | string    | sim    |                                                        |
| `instagram`  | string    | sim    | pode ser `""`                                          |
| `telefone`   | string    | sim    |                                                        |
| `nascimento` | string    | sim    | aceita `dd/mm/aaaa` ou `aaaa-mm-dd` (o front converte) |
| `mac`        | string?   | não    | uso interno                                            |
| `ap`         | string?   | não    |                                                        |
| `ssid`       | string?   | não    |                                                        |

> O `DateTime` do C# já serializa como ISO 8601 (`2026-07-15T14:30:00Z`), que o
> `new Date(...)` do front entende. **Guarde em UTC** (`DateTime.UtcNow`).

---

## 8. Autenticação JWT (o front já está pronto)

Fluxo que o front já implementa:
1. `POST /admin/login` `{ username, password }` → valida → `{ token }`.
2. Front guarda o token em memória e chama `GET /admin/leads` (e futuras rotas) com Bearer.

No back (ASP.NET):
- Comparar `Username` com `Admin:Username` e a senha com `BCrypt.Verify(senha, Admin:PasswordHash)`.
- Gerar o JWT (`JwtSecurityTokenHandler`/`SymmetricSecurityKey` com `Jwt:Secret`, `ExpiresInHours`).
- `builder.Services.AddAuthentication(JwtBearerDefaults...).AddJwtBearer(...)` validando issuer,
  audience e a chave; `[Authorize]` nas rotas admin → token inválido = **401** automático.
- Lembrar de `app.UseAuthentication(); app.UseAuthorization();` no `Program.cs`.
- (Opcional) tabela de usuários admin em vez de um par no config.

---

## 9. Integração UniFi (`UnifiService` com HttpClient)

Portar a lógica do protótipo v1 (`AcessoWifi/unifi.js`) para C#:

1. **HttpClient tolerante ao certificado self-signed + cookies de sessão:**
   ```csharp
   var handler = new HttpClientHandler {
       UseCookies = true,
       CookieContainer = new CookieContainer(),
       ServerCertificateCustomValidationCallback =
           Unifi.VerifySsl ? null : (_,_,_,_) => true
   };
   ```
2. **Login**
   - UniFi OS (UDM/Cloud Gateway): `POST {host}/api/auth/login` com `{ username, password }`.
   - Clássico: `POST {host}/api/login`.
   - Guardar o cookie de sessão (o `CookieContainer` cuida) + ler o header `x-csrf-token`.
3. **Autorizar guest**
   - UniFi OS: `POST {host}/proxy/network/api/s/{site}/cmd/stamgr`
   - Clássico: `POST {host}/api/s/{site}/cmd/stamgr`
   - Body: `{ "cmd": "authorize-guest", "mac": "<lowercase>", "minutes": <AccessMinutes> }`
   - Enviar o header `X-CSRF-Token` quando houver.
4. Erro de login/autorização → lançar exceção e o `/authorize` responde
   `{ authorized:false, error }`.

> Registre via `IHttpClientFactory` (`AddHttpClient`) ou instancie com o handler acima.

---

## 10. Persistência dos leads (EF Core)

```csharp
public class Lead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // UTC!
    public string Nome { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Nascimento { get; set; } = "";              // dd/mm/aaaa, como veio
    public string? Mac { get; set; }
    public string? Ap { get; set; }
    public string? Ssid { get; set; }
}
```

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> o) : base(o) { }
    public DbSet<Lead> Leads => Set<Lead>();
}
```

Migrations:
```bash
dotnet ef migrations add Init
dotnet ef database update
```

> No `GET /admin/leads`, projete a entidade para um `LeadDto` (ou retorne a própria entidade) —
> o `Timestamp` sai em ISO 8601 automaticamente.

---

## 11. Configurações / tema (fase futura — a UI já existe no front)

A tela **Configurações** do admin já permite **upload de logo, favicon e banner**, um
**editor de paleta (8 cores)**, além de SSID e tempo de acesso. Hoje isso fica só no
`localStorage` do navegador. Para persistir:

- **`GET /settings`** (**pública**) → `{ colors, logo, favicon, banner }` — o portal do
  visitante precisa carregar o tema sem estar logado.
- **`PUT /admin/settings`** (**`[Authorize]`**) → salva tudo (inclui `ssid`, `accessMinutes`).
- **Upload**: aceitar `multipart/form-data` (`IFormFile`) **ou** data URL (o front já usa data URL).
- O `ssid`/`accessMinutes` salvos aqui devem alimentar o `/authorize` (no lugar do config fixo).

> Estrutura de `colors` e do tema: `AccessWifi_V2_FRONT/src/theme/theme.ts`. Quando o
> endpoint existir, o front passa a carregar o tema da API — me avise para fazer esse ajuste.

---

## 12. Segurança

- **Limite de payload** no `/authorize` (`[RequestSizeLimit]` ou config do Kestrel).
- **Rate limiting** (`builder.Services.AddRateLimiter(...)`) em `/authorize` e `/admin/login`.
- Senha do admin em **hash bcrypt**, nunca em texto puro.
- Validar entradas com **Data Annotations** nos DTOs (`[Required]`, etc.) — o `[ApiController]`
  já devolve 400 automático em modelo inválido.
- **HTTPS** obrigatório em produção (dados pessoais — LGPD); `app.UseHttpsRedirection()`.
- Não logar dados pessoais em texto claro.

---

## 13. CORS e como ligar no front

No `Program.cs`:
```csharp
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["FrontOrigin"]!)
    .WithMethods("GET", "POST", "PUT")
    .WithHeaders("Content-Type", "Authorization")));
// ...
app.UseCors();
```
- Não precisa `AllowCredentials` (o token vai no header, não em cookie).
- Em **produção**: definir no front `VITE_API_URL=https://api.seudominio.com.br`.
- **Desligar o modo demonstração** no front: remover `.env.local` (ou `VITE_USE_MOCK=false`).
- Em **dev**: rode o back em `http://localhost:3000` (ajuste o `applicationUrl` em
  `Properties/launchSettings.json`) — o proxy do Vite já encaminha `/authorize`, `/admin/login`
  e `/admin/leads`. Assim **CORS não é necessário localmente**. (Se preferir manter a porta
  padrão do .NET, ex. `5000`, me avise que eu ajusto o proxy do front.)

---

## 14. Deploy (VPS/Cloud)

- Publicar: `dotnet publish -c Release`.
- Rodar como **serviço** (systemd no Linux, ou Windows Service / IIS).
- **Nginx** (ou IIS) como reverse proxy: TLS (Let's Encrypt) + encaminhar `/authorize`,
  `/admin/*`, `/settings` para o Kestrel.
  - ⚠️ Configurar `client_max_body_size 10m;` — o `PUT /admin/settings` pode chegar a ~8 MB
    (3 imagens de 2 MB infladas pelo base64) e o padrão do Nginx (1 MB) barraria o upload.
- Se front e back no mesmo servidor: Nginx serve o `dist/` do front e faz proxy das rotas de API.
- Garantir que a UniFi consegue **alcançar o portal** (walled garden liberando o domínio).

---

## 15. Checklist de implementação

- [x] `dotnet new webapi` + `Program.cs` (CORS, EF, `[ApiController]`)
- [x] `AppDbContext` + `Lead` + migration (`InitialCreate`, aplicada no Postgres local)
- [x] `POST /authorize` (DTO com validação MAC + LGPD; grava lead)
- [x] `UnifiService.AuthorizeGuestAsync` (login + `cmd/stamgr`) plugado no `/authorize`
      → implementado como `UnifiClient` em `Infrastructure/Unifi` (typed HttpClient)
- [x] `POST /admin/login` (BCrypt.Verify + gera JWT)
- [x] `AddAuthentication().AddJwtBearer(...)` + `[Authorize]` nas rotas admin
- [x] `GET /admin/leads` (protegido, `Timestamp` em UTC/ISO)
- [x] `AddRateLimiter` + limite de payload (`[RequestSizeLimit(16 KB)]`)
- [x] `GET /settings` (pública) + `PUT /admin/settings` (JWT) + upload de imagens
      → imagens como data URL (formato que o front já usa); `accessMinutes` salvo
      alimenta o `/authorize`; falta o ajuste no front para carregar o tema da API
- [ ] Deploy: VPS + HTTPS + Nginx + `VITE_API_URL` no front + `VITE_USE_MOCK=false`

---

### Referências no front (`AccessWifi_V2_FRONT/`)

- `src/lib/api.ts` — contratos `authorize`, `adminLogin`, `fetchAdminData` (**fonte da verdade**)
- `src/lib/leads.ts` — formatação/filtros/exportação (client-side)
- `src/theme/theme.ts` — estrutura do tema (configurações futuras)
- `vite.config.ts` — proxy de dev (espera o back em `localhost:3000`)
- `.env.example` — variáveis do front (`VITE_API_URL`, `VITE_USE_MOCK`)
