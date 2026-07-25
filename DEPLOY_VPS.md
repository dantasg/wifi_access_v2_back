# 🚀 Deploy do AccessWifi (Backend) numa VPS Linux — passo a passo completo

Guia 100% do zero: do **primeiro acesso SSH** até a **API rodando em produção** com HTTPS,
banco, serviço em segundo plano (relatórios/retenção) e todos os segredos configurados.

- **Alvo:** Ubuntu 22.04/24.04 LTS (Debian similar). Ajuste `apt` se usar outra distro.
- **Componentes:** API (`AccessWifi.Api`), worker (`AccessWifiService`), PostgreSQL, Nginx (TLS).
- **Convenção:** onde aparecer `SEU_DOMINIO`, `SUA_SENHA_*` etc., troque pelos seus valores.
- Comandos com `sudo` assumem um usuário comum com permissão de administrador (criado no passo 2).

---

## 0. O que você precisa antes de começar

- Uma VPS Linux com IP público e acesso `root` inicial (via senha ou chave SSH).
- Um domínio (ex.: `api.seunegocio.com.br`) com um registro **A** apontando para o IP da VPS.
- O repositório do back no GitHub: `https://github.com/dantasg/wifi_access_v2_back.git`.

---

## 1. Primeiro acesso via SSH

Do seu computador:

```bash
ssh root@IP_DA_SUA_VPS
```

(Se o provedor te deu uma senha, ele pede aqui. Se configurou chave SSH, ele entra direto.)

Atualize o sistema:

```bash
apt update && apt upgrade -y
```

---

## 2. Criar um usuário de trabalho (não usar root no dia a dia)

```bash
adduser deploy               # crie uma senha quando pedir
usermod -aG sudo deploy      # dá permissão de administrador
```

Copie sua chave SSH para o novo usuário (recomendado) e depois reconecte como ele:

```bash
rsync --archive --chown=deploy:deploy ~/.ssh /home/deploy   # reaproveita a chave do root
exit
ssh deploy@IP_DA_SUA_VPS
```

---

## 3. Firewall (só abrir o necessário)

```bash
sudo apt install -y ufw
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable            # confirme com 'y'
sudo ufw status
```

> A API **não** fica exposta direto na internet — ela escuta só em `localhost` e o Nginx (443)
> faz o proxy. Por isso não abrimos a porta da API no firewall.

---

## 4. Instalar o .NET 10 SDK

```bash
# Repositório oficial da Microsoft
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt update
sudo apt install -y dotnet-sdk-10.0
dotnet --version         # confirme que aparece 10.x
```

> Para Ubuntu 22.04, troque `24.04` pela `22.04` na URL acima.

---

## 5. Instalar e preparar o PostgreSQL

```bash
sudo apt install -y postgresql
sudo systemctl enable --now postgresql
```

Crie o banco e o usuário da aplicação (troque a senha!):

```bash
sudo -u postgres psql <<'SQL'
CREATE USER accesswifi WITH PASSWORD 'SUA_SENHA_DO_BANCO';
CREATE DATABASE accesswifi OWNER accesswifi;
GRANT ALL PRIVILEGES ON DATABASE accesswifi TO accesswifi;
SQL
```

O banco fica acessível só localmente (padrão do PostgreSQL) — a API o alcança por `localhost`.

---

## 6. Clonar o repositório

```bash
sudo apt install -y git apache2-utils        # apache2-utils traz o htpasswd (passo 7)
sudo mkdir -p /opt/accesswifi
sudo chown deploy:deploy /opt/accesswifi
git clone https://github.com/dantasg/wifi_access_v2_back.git /opt/accesswifi
cd /opt/accesswifi
```

---

## 7. Gerar os segredos

São **quatro** segredos. Gere e **guarde cada um** (você vai colá-los no arquivo do passo 8).
⚠️ **Guarde a chave de cifragem (`Encryption:Key`) num lugar seguro fora da VPS** — se perdê-la,
as senhas de UniFi/SMTP cifradas ficam irrecuperáveis.

```bash
# 7.1 Segredo do JWT (assina os tokens) — >= 32 bytes
openssl rand -base64 48

# 7.2 Chave de cifragem em repouso (AES-256) — exatamente 32 bytes
openssl rand -base64 32

# 7.3 Hash bcrypt da senha do super admin (troque SUA_SENHA_ADMIN)
htpasswd -bnBC 11 "" "SUA_SENHA_ADMIN" | cut -d: -f2

# 7.4 A senha do banco você já definiu no passo 5.
```

- 7.1 → vai em `Jwt__Secret`
- 7.2 → vai em `Encryption__Key`
- 7.3 → vai em `Admin__PasswordHash` (começa com `$2y$...`; é válido)
- 7.4 → vai na connection string

---

## 8. Arquivo de ambiente com os segredos (systemd EnvironmentFile)

Aqui ficam **todos os segredos**, fora do Git, lido pela API e pelo worker.

```bash
sudo mkdir -p /etc/accesswifi
sudo nano /etc/accesswifi/accesswifi.env
```

Cole o conteúdo abaixo trocando pelos seus valores (sem aspas, um por linha):

```ini
# Banco
ConnectionStrings__Default=Host=localhost;Database=accesswifi;Username=accesswifi;Password=SUA_SENHA_DO_BANCO

# JWT (passo 7.1)
Jwt__Secret=COLE_O_SEGREDO_JWT_AQUI

# Cifragem em repouso (passo 7.2)
Encryption__Key=COLE_A_CHAVE_AES_AQUI

# Super admin (passo 7.3) — criado no 1º start se ainda não existir
Admin__Username=root
Admin__PasswordHash=COLE_O_HASH_BCRYPT_AQUI

# Origem do front (para o CORS)
FrontOrigin=https://wifi-access-v2-front.vercel.app

# Atrás do Nginx: confia no proxy local e força HTTPS
ForwardedHeaders__KnownProxies__0=127.0.0.1
Security__EnforceHttpsRedirect=true

# Ambiente e porta local (a API escuta só no localhost; o Nginx expõe via 443)
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
```

Proteja o arquivo (só o dono lê):

```bash
sudo chmod 600 /etc/accesswifi/accesswifi.env
sudo chown deploy:deploy /etc/accesswifi/accesswifi.env
```

---

## 9. Publicar a API e o worker

```bash
cd /opt/accesswifi
dotnet publish src/AccessWifi.Api/AccessWifi.Api.csproj -c Release -o /opt/accesswifi/publish/api
dotnet publish src/AccessWifiService/AccessWifiService.csproj -c Release -o /opt/accesswifi/publish/worker
```

---

## 10. Aplicar as migrations (criar as tabelas)

Instale a ferramenta do EF e rode o update **com os segredos carregados** (a connection string
vem do arquivo do passo 8):

```bash
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"

set -a; source /etc/accesswifi/accesswifi.env; set +a
dotnet ef database update \
  --project src/Models/Models.csproj \
  --startup-project src/AccessWifi.Api/AccessWifi.Api.csproj
```

Deve terminar sem erro e criar todas as tabelas (empresas, unidades, leads, usuários,
refresh tokens, configurações etc.).

---

## 11. Serviço systemd da API

```bash
sudo nano /etc/systemd/system/accesswifi-api.service
```

```ini
[Unit]
Description=AccessWifi API
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/accesswifi/publish/api
ExecStart=/usr/bin/dotnet /opt/accesswifi/publish/api/AccessWifi.Api.dll
Restart=always
RestartSec=5
User=deploy
EnvironmentFile=/etc/accesswifi/accesswifi.env
KillSignal=SIGINT
SyslogIdentifier=accesswifi-api

[Install]
WantedBy=multi-user.target
```

---

## 12. Serviço systemd do worker (relatórios + retenção)

```bash
sudo nano /etc/systemd/system/accesswifi-worker.service
```

```ini
[Unit]
Description=AccessWifi Worker (relatorios e retencao)
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/accesswifi/publish/worker
ExecStart=/usr/bin/dotnet /opt/accesswifi/publish/worker/AccessWifiService.dll
Restart=always
RestartSec=10
User=deploy
EnvironmentFile=/etc/accesswifi/accesswifi.env
SyslogIdentifier=accesswifi-worker

[Install]
WantedBy=multi-user.target
```

Suba os dois serviços:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now accesswifi-api accesswifi-worker
sudo systemctl status accesswifi-api --no-pager
```

Se algo falhar, veja os logs:

```bash
journalctl -u accesswifi-api -n 50 --no-pager
```

> No **primeiro start**, a API cria o super admin a partir de `Admin__Username`/`Admin__PasswordHash`.

Teste local (deve responder, ainda que 400/404, provando que subiu):

```bash
curl -i http://127.0.0.1:5000/settings
```

---

## 13. Nginx como reverse proxy + HTTPS

```bash
sudo apt install -y nginx
sudo nano /etc/nginx/sites-available/accesswifi
```

```nginx
server {
    listen 80;
    server_name api.seunegocio.com.br;   # <-- seu domínio

    # Uploads do painel (tema/imagens) podem passar de 1 MB
    client_max_body_size 12m;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Ative e recarregue:

```bash
sudo ln -s /etc/nginx/sites-available/accesswifi /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

Emita o certificado TLS (Let's Encrypt) — o certbot ajusta o Nginx para 443 sozinho:

```bash
sudo apt install -y certbot python3-certbot-nginx
sudo certbot --nginx -d api.seunegocio.com.br
```

Aceite o redirecionamento HTTP→HTTPS quando o certbot perguntar. A renovação é automática.

---

## 14. Verificação final

```bash
curl -i https://api.seunegocio.com.br/settings?unit=alguma-unidade
```

Login do super admin (troque a senha):

```bash
curl -i -X POST https://api.seunegocio.com.br/admin/login \
  -H "Content-Type: application/json" \
  -d '{"username":"root","password":"SUA_SENHA_ADMIN"}'
```

Deve retornar `200` com `{ token, refreshToken, role, company }`. **Pronto — a API está no ar.**

Aponte o front (Vercel) para `https://api.seunegocio.com.br` (variável `VITE_API_URL`).

---

## 15. Atualizações futuras (deploy de uma nova versão)

```bash
cd /opt/accesswifi
git pull
dotnet publish src/AccessWifi.Api/AccessWifi.Api.csproj -c Release -o /opt/accesswifi/publish/api
dotnet publish src/AccessWifiService/AccessWifiService.csproj -c Release -o /opt/accesswifi/publish/worker

# Se a versão trouxe novas migrations:
set -a; source /etc/accesswifi/accesswifi.env; set +a
dotnet ef database update --project src/Models/Models.csproj --startup-project src/AccessWifi.Api/AccessWifi.Api.csproj

sudo systemctl restart accesswifi-api accesswifi-worker
```

---

## 16. Configurações que ficam no banco (não no arquivo de ambiente)

- **Empresas, unidades e a controladora UniFi de cada unidade:** cadastradas pelo super admin
  via API/painel. A **senha da UniFi é cifrada** automaticamente com a `Encryption__Key`.
- **SMTP (envio dos relatórios):** fica na tabela `Configuration` (chaves `SMTP_*`). A senha do
  SMTP também é lida de forma cifrada — se você inserir em texto puro, ela funciona (modo
  compatível), mas o ideal é guardá-la cifrada.
- **Retenção de leads (LGPD):** padrão de **12 meses**. Para mudar, adicione ao arquivo de
  ambiente `Retention__LeadMonths=<n>` (0 desativa) e reinicie o worker.

---

## 17. Solução de problemas rápida

| Sintoma | Verifique |
| ------- | --------- |
| API não sobe | `journalctl -u accesswifi-api -n 80` — geralmente `Jwt__Secret`/`Encryption__Key` ausente ou curto |
| 502 no Nginx | A API subiu? `systemctl status accesswifi-api` e `curl http://127.0.0.1:5000/settings` |
| Erro de banco | Connection string errada no `.env`, ou migrations não aplicadas (passo 10) |
| Login sempre 401 | Hash do admin não bate — regenere no passo 7.3 e atualize o `.env`, reinicie a API |
| Falha ao autorizar UniFi | Host/credenciais da controladora, ou a `Encryption__Key` mudou depois de cadastrar |

---

### Checklist final
- [ ] Firewall só com 22/80/443 · [ ] .NET 10 · [ ] Postgres com banco/usuário
- [ ] 4 segredos gerados e no `/etc/accesswifi/accesswifi.env` (chmod 600)
- [ ] Migrations aplicadas · [ ] API e worker `active (running)` · [ ] Nginx + TLS
- [ ] Login do super admin OK · [ ] Front apontando para a API
