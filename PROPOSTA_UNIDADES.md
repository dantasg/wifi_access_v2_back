# 🏬 Proposta — Unidades por Empresa (multi-unidade)

> **Status: proposta para aprovação.** Nada foi implementado ainda. Leia, marque o que
> aprova/ajusta nas "Decisões para aprovação" (seção 9) e me devolva — aí eu implemento.

---

## 1. O que você pediu (meu entendimento)

- Uma **empresa** passa a ter **várias unidades** (como franquias do mesmo dono).
- **Tema e configurações continuam por empresa** (todas as unidades da empresa
  compartilham o mesmo visual/portal).
- **Os leads (cadastros) passam a ser por unidade** — cada unidade tem os seus.
- Tudo bem separado.

Se algo abaixo não bater com a sua ideia, é só apontar.

---

## 2. Vocabulário (nome da entidade)

Você disse não saber o nome certo ("é como se fosse franquias"). Opções comuns:
**Unidade** (`Unit`), **Filial** (`Branch`), **Local** (`Location`), **Loja** (`Store`).

➡️ **Proposta:** usar **`Unit`** no código (entidades em inglês, como `Company`/`Lead`) e
exibir **"Unidade"** na interface. *(ver Decisão D1)*

---

## 3. Modelo hoje × proposto

**Hoje** (tudo pendurado na empresa):

```
Company (empresa/tenant)
 ├── Unifi (controladora)         ← config da UniFi fica na empresa
 ├── PortalSettings (tema)        ← 1 por empresa
 ├── AdminUser (login)            ← IDCompany
 ├── Report (e-mail mensal)       ← ReportEmail/ReportSendDay
 └── Lead (cadastros)             ← IDCompany
```

**Proposto** (entra a Unidade entre a empresa e o lead):

```
Company (empresa)
 ├── PortalSettings (tema)        ← continua 1 por EMPRESA  ✅ (sem mudança)
 ├── AdminUser (login)            ← continua por EMPRESA    ✅
 ├── Report (e-mail mensal)       ← continua por EMPRESA    ✅ (ver D5)
 └── Unit (unidade)   ← NOVO, várias por empresa
      ├── Unifi (controladora)    ← a controladora passa a ser da UNIDADE (ver D2)
      └── Lead (cadastros)        ← o lead passa a ser da UNIDADE  ⭐
```

Resumo: **tema/login/relatório = empresa; controladora/leads = unidade.**

---

## 4. Entidades (esboço)

### 4.1 Nova: `Unit`
```csharp
public class Unit
{
    public Guid Id { get; set; }
    public Guid IDCompany { get; set; }        // empresa dona
    public string Name { get; set; }           // ex.: "Shopping Central"
    public string Slug { get; set; }           // usado na URL do portal (ver D3/D4)
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public CompanyUnifi Unifi { get; set; }     // controladora desta unidade (ver D2)
}
```

### 4.2 `Company` — **perde a UniFi** (vai para a unidade)
```csharp
public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public bool Active { get; set; }
    // Report* e PortalSettings continuam iguais
    // ❌ remove: public CompanyUnifi Unifi  → migra para Unit
}
```

### 4.3 `Lead` — troca `IDCompany` por `IDUnit`
```csharp
public class Lead
{
    public Guid Id { get; set; }
    public Guid IDUnit { get; set; }   // ⭐ antes era IDCompany
    public DateTime Timestamp { get; set; }
    // Nome, Instagram, Telefone, Nascimento, Mac, Ap, Ssid  → sem mudança
}
```
A empresa do lead é obtida por `Lead → Unit → Company` (join). *(ver D8)*

*(`PortalSettings` e `AdminUser` não mudam de forma.)*

---

## 5. Portal do visitante (o que muda para o front)

Hoje o portal se identifica por `?company=slug`. Como o **lead é da unidade** e a
**controladora também**, o portal precisa saber **qual unidade**.

➡️ **Proposta:** a UniFi de cada unidade aponta o portal para `…/?unit=<slugDaUnidade>`.
O back resolve a empresa a partir da unidade (para carregar o tema). *(ver D3)*

| Antes | Depois |
| ----- | ------ |
| `GET /settings?company=doce` | `GET /settings?unit=doce-shopping` → devolve o **tema da empresa** dona da unidade |
| `POST /authorize` com `company` no body | `POST /authorize` com **`unit`** no body → grava o lead na unidade e autoriza na **controladora da unidade** |

O `AccessMinutes` continua vindo das configurações **da empresa**. O tema idem.

---

## 6. Painel admin (o que muda)

- **Login/JWT:** sem mudança — o admin continua logando por empresa e enxergando **todas
  as unidades** da sua empresa.
- **`GET /admin/leads`:** passa a devolver o **campo `unit`** em cada lead e aceita filtro
  opcional **`?unit=slug`**. Sem filtro, traz os leads de **todas as unidades** da empresa.
- **Novo CRUD de unidades** (`/admin/units`): criar/editar/listar unidades e a controladora
  UniFi de cada uma. *(quem gerencia: ver D6)*
- **Configurações/tema (`/admin/settings`):** **sem mudança** — continua por empresa.

---

## 7. Relatório mensal por e-mail

Hoje é por empresa (agrega os leads da empresa). Com unidades, os leads são das unidades.

➡️ **Proposta (D5):** manter o relatório **por empresa**, **agregando todas as unidades**,
e o CSV ganha uma coluna **`unidade`** para distinguir. A configuração (`ReportEmail`,
`ReportSendDay`) continua na empresa. Alternativa: um relatório por unidade.

---

## 8. Migração dos dados

Como o schema muda bastante (leads saem da empresa e vão para a unidade), a migração:

1. Cria a tabela `Unit`.
2. Para **cada empresa existente**, cria **uma unidade padrão** (ex.: "Matriz") e move para
   ela a controladora UniFi (que hoje está na empresa) e todos os leads da empresa.
3. Remove a UniFi da tabela `Company` e troca `Lead.IDCompany` por `Lead.IDUnit`.

Em desenvolvimento é trivial (recrio o banco). **Em produção ainda não há dados**, então
sem risco. Se um dia houver, esse passo 2 preserva o histórico.

---

## 9. ✅ Decisões para aprovação

Marque cada uma (concordo / quero diferente). A **negrito** está minha recomendação.

| # | Decisão | Opção recomendada | Alternativa |
| - | ------- | ----------------- | ----------- |
| **D1** | Nome da entidade | **`Unit` (UI: "Unidade")** | Branch / Location / Store |
| **D2** | Controladora UniFi fica em… | **Unidade** (cada franquia tem a sua) | Empresa (todas compartilham) |
| **D3** | Portal identifica por… | **`?unit=slug`** (empresa é derivada) | `?company=X&unit=Y` |
| **D4** | Slug da unidade é único… | **globalmente** (URL curta `?unit=slug`) | por empresa |
| **D5** | Relatório mensal | **por empresa, com coluna "unidade"** | um por unidade |
| **D6** | Quem gerencia unidades | **Super admin** | também o admin da empresa |
| **D7** | Login por unidade (admin que só vê 1 unidade) | **Não agora** (admin vê todas as unidades da empresa) | Sim, criar papel de unidade |
| **D8** | `Lead` guarda `IDCompany` além de `IDUnit`? | **Não** (empresa vem por join Unit→Company) | Sim (desnormalizado, consultas mais rápidas) |

---

## 10. Impacto e esforço

- **Back:** nova entidade `Unit` + CRUD, mover UniFi para a unidade, `Lead.IDUnit`,
  `/authorize` e `/settings` por unidade, `/admin/leads` com unidade, relatório agregando
  unidades, migração. Atualizar os testes.
- **Front:** portal passa a usar `unit` na URL; painel ganha tela de unidades e a coluna/
  filtro de unidade nos leads. (Eu atualizo o `FRONT_CHANGES.md` com o contrato exato
  **depois** que você aprovar este desenho.)
- **Sem quebrar** o que já existe conceitualmente: empresa, tema, login, relatório e a
  tabela `Configuration` seguem como estão.

---

## 11. O que NÃO muda

- Tema/paleta/imagens: continuam por empresa (`PortalSettings`).
- Login, papéis (super admin / admin de empresa) e JWT.
- Tabela `Configuration` (SMTP global) e o serviço de e-mail (só ganha a coluna unidade).
- Contratos de `Configuration`, e a lógica de idempotência do relatório.

---

### Próximo passo
Me diga **D1–D8** (aprovo tudo, ou troco X e Y) e qualquer ajuste do texto. Com o "ok",
implemento e te entrego com testes + verificação, como nas etapas anteriores.
