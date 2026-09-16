# 🗃️ Migrations (EF Core) — guia prático

Como criar, revisar e aplicar mudanças no banco **sem perder os dados que já existem**.

- **Projeto das migrations:** `src/Models` (pasta `Persistence/Migrations`) — é onde ficam as entidades e o `AppDbContext`.
- **Projeto de inicialização:** `src/AccessWifi.Api` — é de onde o EF lê a **connection string**.
- Por isso todo comando leva os dois: `--project src/Models --startup-project src/AccessWifi.Api`.

> Para o deploy em si (VPS, systemd, Nginx, segredos), veja o [DEPLOY_VPS.md](DEPLOY_VPS.md).
> Este documento cobre **só a parte do banco**.

---

## 1. O que você precisa entender antes

### O que é uma migration
É um **passo versionado de alteração do banco**. Você muda uma classe em C# (ex.: adiciona uma
propriedade em `Lead`), roda um comando, e o EF gera o código que aplica essa mudança no
PostgreSQL (`ALTER TABLE ...`). Cada migration tem um carimbo de tempo no nome e é aplicada
**uma única vez**, em ordem.

### Os arquivos gerados
Ao criar uma migration nascem 3 coisas em `src/Models/Persistence/Migrations/`:

| Arquivo | Para que serve |
| --- | --- |
| `<timestamp>_Nome.cs` | O passo em si: método `Up()` (aplica) e `Down()` (desfaz). **É este que você revisa.** |
| `<timestamp>_Nome.Designer.cs` | Foto do modelo naquele momento. Gerado, não mexa. |
| `AppDbContextModelSnapshot.cs` | Foto do modelo **atual**. O EF compara com suas classes para saber o que mudou. Gerado, não mexa. |

**Os três devem ir para o Git juntos.** Se você commitar o `Up()` sem o snapshot, a próxima
migration vai sair errada (o EF vai achar que a mudança ainda não foi feita e repetir tudo).

### Como o banco sabe o que já rodou
O EF cria automaticamente uma tabela chamada **`__EFMigrationsHistory`** com o nome de cada
migration já aplicada. Quando você manda aplicar, ele:

1. lê essa tabela,
2. roda só as migrations que **não** estão lá,
3. registra as novas.

👉 **É esse mecanismo que garante que aplicar migrations não apaga dados.** Rodar o comando
duas vezes na mesma versão não faz nada na segunda — ele simplesmente não tem o que aplicar.

### O que **realmente** apaga dados
Não é o comando de aplicar — é o **conteúdo** da migration. O EF gera `DROP COLUMN` / `DROP TABLE`
quando você:

- **remove** uma propriedade ou uma entidade;
- **renomeia** uma propriedade (o EF não adivinha: ele gera `DROP` da antiga + `ADD` da nova);
- **troca o tipo** de uma coluna de forma incompatível;
- adiciona uma coluna `NOT NULL` **sem valor padrão** numa tabela que já tem linhas (aí o comando falha);
- cria um **índice único** numa coluna que já tem valores repetidos (também falha).

Quando isso acontece, o EF avisa na hora de gerar:

> *An operation was scaffolded that may result in the loss of data. Please review the migration for accuracy.*

⚠️ **Esse aviso é para ser levado a sério.** Abra o arquivo `Up()` e confira antes de aplicar.
Se for um rename, troque o `DropColumn` + `AddColumn` gerados por um `migrationBuilder.RenameColumn(...)`
escrito à mão — assim os dados são preservados.

---

## 2. Pré-requisito (uma vez por máquina)

```bash
dotnet tool install --global dotnet-ef
```

Já tem instalado? Atualize:

```bash
dotnet tool update --global dotnet-ef
```

Confira:

```bash
dotnet ef --version
```

> No Linux, se o comando não for encontrado depois de instalar:
> `export PATH="$PATH:$HOME/.dotnet/tools"` (adicione ao seu `~/.bashrc`).

---

## 3. Fluxo no dia a dia (na sua máquina, dev)

### Passo 1 — Alterar o modelo
Mexa nas entidades em `src/Models/DataBase/` e, se precisar, nas configurações do
`src/Models/Persistence/AppDbContext.cs` (tamanhos, índices, FKs).

### Passo 2 — Parar a API
Se a API estiver rodando (`dotnet run` ou pelo Visual Studio), **pare**. Com ela no ar o Windows
trava o `Models.dll` e o comando falha com `MSB3027` / `MSB3021`.

### Passo 3 — Criar a migration

```bash
dotnet ef migrations add DescricaoDaMudanca --project src/Models --startup-project src/AccessWifi.Api --output-dir Persistence/Migrations
```

- `DescricaoDaMudanca` → **dê um nome de verdade**, em PascalCase, descrevendo o que muda:
  `AddLeadCreatedAt`, `AddRefreshToken`, `EncryptUnifiPassword`. Esse nome fica no histórico do
  banco para sempre.
- `--output-dir Persistence/Migrations` → mantém tudo na mesma pasta das outras.

### Passo 4 — Revisar o arquivo gerado
Abra `src/Models/Persistence/Migrations/<timestamp>_DescricaoDaMudanca.cs` e leia o `Up()`.
Procure por `DropColumn`, `DropTable`, `AlterColumn` com mudança de tipo e índices únicos novos.

### Passo 5 — Aplicar no banco de dev

```bash
dotnet ef database update --project src/Models --startup-project src/AccessWifi.Api
```

### Passo 6 — Rodar os testes e commitar

```bash
dotnet test
```

Commite os **3 arquivos** (migration, designer e snapshot) junto com a mudança nas entidades.

---

## 4. 🆕 Primeira vez no servidor (banco vazio)

O banco existe mas está **sem nenhuma tabela**. Aqui não há dado a perder, então é o cenário simples.

Na VPS, com os segredos carregados (o `.env` do [DEPLOY_VPS.md](DEPLOY_VPS.md), passo 8):

```bash
set -a; source /etc/accesswifi/accesswifi.env; set +a
```

```bash
dotnet ef database update --project src/Models/Models.csproj --startup-project src/AccessWifi.Api/AccessWifi.Api.csproj
```

Isso roda **todas** as migrations em ordem, da `InitialCreate` até a última, e cria todas as
tabelas (empresas, unidades, leads, usuários, refresh tokens, configurações).

Depois disso, ao subir a API pela primeira vez, ela cria **só o super admin** a partir de
`Admin__Username` / `Admin__PasswordHash`. Nenhuma empresa ou unidade é criada — isso você
cadastra pelo painel/API.

---

## 5. ⚠️ Alterações futuras no servidor (já tem dados)

Este é o cenário crítico. A regra é: **backup → script → aplicar → publicar → reiniciar.**

### 5.1 Backup ANTES de qualquer coisa (obrigatório)

```bash
sudo -u postgres pg_dump accesswifi > ~/backup-accesswifi-$(date +%F-%H%M).sql
```

Confira que o arquivo não está vazio (`ls -lh ~/backup-*.sql`). Sem backup confirmado, **não
prossiga**. Guarde uma cópia fora da VPS.

### 5.2 Atualizar o código

```bash
cd /opt/accesswifi && git pull
```

### 5.3 Gerar o script SQL idempotente (recomendado em produção)

Em vez de deixar o EF aplicar direto, gere o SQL e **leia** antes de rodar:

```bash
set -a; source /etc/accesswifi/accesswifi.env; set +a
```

```bash
dotnet ef migrations script --idempotent --project src/Models/Models.csproj --startup-project src/AccessWifi.Api/AccessWifi.Api.csproj -o migrate.sql
```

- `--idempotent` → o script vem com `IF NOT EXISTS` por migration: aplicar duas vezes é seguro,
  e ele funciona independente de qual versão o banco está.
- Abra o `migrate.sql` e **procure por `DROP`**. Se aparecer algo que não deveria sumir, pare e
  ajuste a migration antes.

Aplicar:

```bash
psql "postgresql://accesswifi:SUA_SENHA@localhost/accesswifi" -f migrate.sql
```

> **Alternativa mais curta** (aceitável enquanto o volume de dados é pequeno): pular o script e
> rodar `dotnet ef database update` direto na VPS. Faz a mesma coisa, mas você não vê o SQL antes.

### 5.4 Publicar e reiniciar

```bash
dotnet publish src/AccessWifi.Api/AccessWifi.Api.csproj -c Release -o /opt/accesswifi/publish/api
```

```bash
dotnet publish src/AccessWifiService/AccessWifiService.csproj -c Release -o /opt/accesswifi/publish/worker
```

```bash
sudo systemctl restart accesswifi-api accesswifi-worker
```

### 5.5 Ordem importa

Aplique a migration **antes** de reiniciar a API com o código novo. Se inverter, a API nova vai
procurar colunas que ainda não existem e quebrar até o banco ser atualizado.

### 5.6 Se der errado

Restaure o backup:

```bash
sudo -u postgres psql -c "DROP DATABASE accesswifi;" -c "CREATE DATABASE accesswifi OWNER accesswifi;"
```

```bash
sudo -u postgres psql accesswifi < ~/backup-accesswifi-XXXX.sql
```

E volte o código para o commit anterior (`git checkout <commit>` + publish + restart).

---

## 6. Referência rápida dos comandos

Todos assumem que você está na raiz do repositório.

| O que faz | Comando |
| --- | --- |
| Criar uma migration | `dotnet ef migrations add Nome --project src/Models --startup-project src/AccessWifi.Api --output-dir Persistence/Migrations` |
| Aplicar tudo que falta | `dotnet ef database update --project src/Models --startup-project src/AccessWifi.Api` |
| Listar migrations e o que já foi aplicado | `dotnet ef migrations list --project src/Models --startup-project src/AccessWifi.Api` |
| Apagar a **última** migration ainda **não aplicada** | `dotnet ef migrations remove --project src/Models --startup-project src/AccessWifi.Api` |
| Voltar o banco para uma migration anterior | `dotnet ef database update NomeDaMigrationAnterior --project src/Models --startup-project src/AccessWifi.Api` |
| Gerar SQL idempotente (produção) | `dotnet ef migrations script --idempotent --project src/Models --startup-project src/AccessWifi.Api -o migrate.sql` |
| Apagar o banco inteiro (**só em dev!**) | `dotnet ef database drop -f --project src/Models --startup-project src/AccessWifi.Api` |

> 🚫 `database drop` e `database update 0` **apagam todos os dados**. Nunca em produção.

---

## 7. Regras de ouro

1. **Backup antes de aplicar qualquer migration em produção.** Sempre.
2. **Leia o `Up()` gerado.** O comando é só um atalho — quem decide o que acontece é o código.
3. **Nunca edite uma migration que já foi aplicada em produção.** Crie uma nova por cima.
4. **Nunca apague arquivos de migration manualmente.** Use `migrations remove` (e só se ela ainda
   não tiver sido aplicada em lugar nenhum).
5. **Coluna nova obrigatória?** Adicione com valor padrão (`defaultValue:`) ou crie como opcional,
   preencha os dados e só então torne obrigatória numa segunda migration.
6. **Renomear = escrever `RenameColumn` à mão.** O padrão gerado pelo EF (drop + add) perde os dados.
7. **`drop` só em desenvolvimento.**

---

## 8. Problemas comuns

| Erro / sintoma | Causa e solução |
| --- | --- |
| `MSB3027` / `MSB3021` — arquivo bloqueado | A API ou o Visual Studio está com o `Models.dll` aberto. Pare a API e rode de novo. |
| `No migrations were applied. The database is already up to date.` | Nada a fazer — o banco já está na última versão. |
| *"An operation was scaffolded that may result in the loss of data"* | Aviso de `DROP`. Abra o `Up()` e confirme se é isso que você quer. |
| `column ... contains null values` | Coluna `NOT NULL` adicionada em tabela com linhas. Use `defaultValue:` na migration. |
| `duplicate key value violates unique constraint` ao aplicar | Índice único criado sobre dados já repetidos. Limpe as duplicatas antes. |
| `ConnectionStrings:Default não configurada` | Faltou carregar o `.env` na VPS: `set -a; source /etc/accesswifi/accesswifi.env; set +a`. |
| Migration criada mas o banco não mudou | Você esqueceu do `database update` (ou aplicou em outro banco). |
| Migration vazia (`Up()` sem nada) | Não havia mudança no modelo. Remova com `migrations remove`. |

---

## 9. Resumo em 3 linhas

- **Mudei o modelo:** `migrations add Nome` → revisar o `Up()` → `database update` → `dotnet test` → commit.
- **Servidor novo:** `database update` (cria tudo) → subir a API (cria o super admin).
- **Servidor com dados:** `pg_dump` → `git pull` → `migrations script --idempotent` → ler → `psql -f` → `publish` → `restart`.
