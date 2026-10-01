# 🟢 Produção — como está montada e como mexer nela

> Este documento descreve **a produção real** (no ar desde 21/09/2026). O
> [DEPLOY_VPS.md](DEPLOY_VPS.md) descreve uma instalação genérica, compilando no servidor e com
> nginx próprio. **Não é o que roda hoje.** Na dúvida, vale o que está aqui.

---

## 1. Visão geral

```
https://vps11702.panel.icontainer.online
 │
 ├── nginx do painel iContainer  (container Docker, rede "host", portas 80/443, HTTPS)
 │    ├── /authorize, /settings, /admin/login|refresh|logout|leads|settings|companies|units|users
 │    │        └──► API .NET  (systemd: accesswifi-api, 127.0.0.1:5000) ──► PostgreSQL 18
 │    └── todo o resto ──► portal (arquivos estáticos, SPA com fallback para index.html)
 │
 └── worker .NET (systemd: accesswifi-worker) — relatórios mensais e retenção de leads
```

- **Portal e API no mesmo endereço.** Sem CORS e sem `VITE_API_URL`: o front chama a própria origem.
- **UniFi da Itaituba pela nuvem** (modo `Cloud`), sem IP público na loja. Ver
  [PROPOSTA_UNIFI_CLOUD.md](PROPOSTA_UNIFI_CLOUD.md).
- **A unidade é identificada pelo endereço** (`Units.PortalHost`). Ver
  [PROPOSTA_IDENTIFICACAO_UNIDADE.md](PROPOSTA_IDENTIFICACAO_UNIDADE.md).

| | |
| --- | --- |
| Servidor | VPS Integrator, Ubuntu 26.04 LTS, 4 núcleos, 5,8 GB, 99 GB |
| IP | `216.22.13.216` |
| Domínio | `vps11702.panel.icontainer.online` (**do provedor**, não é nosso — ver §9) |
| Painel do provedor | `https://216.22.13.216:2090` (iContainer, estilo 1Panel) |

---

## 2. Onde fica cada coisa

| O quê | Onde |
| --- | --- |
| API | `/opt/accesswifi/api` (versão anterior em `api.anterior`) |
| Worker | `/opt/accesswifi/worker` (versão anterior em `worker.anterior`) |
| Qual commit está no ar | `/opt/accesswifi/VERSAO` |
| Segredos (banco, JWT, cifragem, admin) | `/etc/accesswifi/accesswifi.env` — `root:accesswifi`, `640` |
| Serviços | `/etc/systemd/system/accesswifi-{api,worker}.service` — cópia em [deploy/systemd/](deploy/systemd/) |
| Portal | `/etc/icontainer/apps/nginx/nginx/www/sites/vps11702.panel.icontainer.online/index` |
| Encaminhamento da API no nginx | `…/security/90-accesswifi-api.conf` — cópia em [deploy/nginx/](deploy/nginx/) |
| SSH só por chave | `/etc/ssh/sshd_config.d/00-accesswifi-hardening.conf` — cópia em [deploy/](deploy/) |
| Backups do banco | `/var/backups/accesswifi/antes-*.dump` (os 10 últimos) |
| Logs do nginx do site | `…/log/access.log` e `error.log` (em JSON) |

Os serviços rodam com o usuário de sistema **`accesswifi`**, que não tem login. Os binários são do
root, então o serviço só consegue ler, nunca alterar.

---

## 3. Acesso

**SSH — só por chave.** A senha foi desligada em 21/09 (havia ~3.700 tentativas de invasão por dia).

```bash
ssh -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216
```

> ⚠️ **Perder a chave `accesswifi_vps` = perder o SSH.** A saída de emergência é o console web do
> provedor (painel da Integrator). Guarde uma cópia da chave num lugar seguro.

**Banco (DBeaver).** Túnel SSH na aba "SSH" (host `216.22.13.216`, usuário `root`, chave pública
`accesswifi_vps`). Na aba "Principal": host `localhost`, porta `5432`, banco e usuário `accesswifi`.
Senha:

```bash
ssh -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216 "grep -oP 'Password=\K[^;]+' /etc/accesswifi/accesswifi.env"
```

⚠️ Esse é o usuário da aplicação: **pode tudo**. Faça backup antes de editar à mão (§6).

**Senha do painel admin** (usuário `root`):

```bash
ssh -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216 "cat /root/accesswifi-admin-senha.txt"
```

---

## 4. Publicar uma nova versão

**Um comando só**, no Git Bash, na raiz do repositório do back:

```bash
./deploy/publicar-producao.sh            # API + worker + portal
./deploy/publicar-producao.sh api        # só back end (inclui migrations)
./deploy/publicar-producao.sh portal     # só o front
./deploy/publicar-producao.sh rotinas    # só o backup diário e os avisos (§6) — não reinicia nada
```

Requisitos: o repositório do front ao lado do back (`../AccessWifi_V2_FRONT`), .NET 10 SDK,
Node e `dotnet-ef` instalados na sua máquina.

O script, em ordem:

1. **Recusa código não commitado**, nos dois repositórios. O servidor guarda qual commit está no ar.
2. Compila **na sua máquina**; o servidor só tem o runtime.
3. **Recusa o pacote** se ele tiver `appsettings.Development*` (segredo de dev) ou se o portal
   apontar para o ngrok/Vercel.
4. Faz **backup do banco antes** das migrations.
5. Aplica as migrations (script idempotente: roda só o que falta).
6. Troca os binários guardando a versão atual como `anterior` e reinicia.
7. **Se a API não responder em 20 s, volta sozinho para a versão anterior.**
8. Recoloca o encaminhamento do nginx, se o painel tiver apagado (ver §8).
9. Confere pela internet: portal, API e painel.

O serviço fica fora do ar por uns 5 segundos durante o reinício. Evite publicar no horário de
movimento das lojas.

---

## 5. Voltar atrás

```bash
./deploy/publicar-producao.sh reverter
```

Troca API, worker e portal pela cópia `anterior` do último deploy.

⚠️ **O `reverter` não desfaz migrations.** Se a versão nova mudou o banco e a antiga não funciona
com ele, restaure o backup feito antes do deploy (§6).

---

## 6. Banco: backup e restauração

**Backup manual para a sua máquina** (faça antes de qualquer edição à mão):

```bash
ssh -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216 "sudo -u postgres pg_dump accesswifi" > backup-producao.sql
```

**Backup diário (automático, desde 01/10/2026).** Todo dia às 03:15 o servidor:

1. faz o `pg_dump` do banco e confere que o arquivo abre;
2. junta o banco + `/etc/accesswifi/accesswifi.env` (que tem a `Encryption__Key`) + `VERSAO` num `.tar`;
3. criptografa com **AES-256 e a senha do backup** e manda para o **Telegram** (chat do robô de avisos);
4. guarda no servidor os 14 últimos (`/var/backups/accesswifi/diario-*.dump`, só o banco, só o root lê).

Se algo falhar, chega aviso no Telegram e no e-mail. Sem backup guardado fora há mais de 26 h, também.

> ⚠️ **A senha do backup não fica em lugar nenhum além do servidor e do seu gerenciador de senhas.**
> Sem ela, nenhum arquivo do Telegram abre — e se a VPS for perdida, ela vai junto.

**Outros backups no servidor:** os do script de publicação (`antes-*.dump`, os 10 últimos) e os feitos à
mão. Tudo em `/var/backups/accesswifi/`.

**Restaurar um backup do servidor** (sobrescreve o banco — pare a API antes):

```bash
systemctl stop accesswifi-api accesswifi-worker
sudo -u postgres pg_restore --clean --if-exists -d accesswifi /var/backups/accesswifi/diario-AAAAMMDD-HHMMSS.dump
systemctl start accesswifi-api accesswifi-worker
```

**Restaurar a partir do Telegram** (servidor perdido, ou para conferir num banco à parte):

```bash
# 1. Baixe o arquivo accesswifi-AAAAMMDD-HHMMSS.tar.gpg do chat do robô e leve para o servidor.
gpg -d accesswifi-AAAAMMDD-HHMMSS.tar.gpg > backup.tar     # pede a senha do backup
tar -xf backup.tar          # accesswifi.dump + accesswifi.env + VERSAO
# 2a. Conferir sem mexer em nada: restaura num banco à parte.
sudo -u postgres createdb accesswifi_conferencia
sudo -u postgres pg_restore -d accesswifi_conferencia accesswifi.dump
# 2b. Servidor novo: restaure em "accesswifi" e use o accesswifi.env do pacote em /etc/accesswifi/
#     (sem a Encryption__Key dele, a chave da UniFi e as senhas guardadas ficam ilegíveis).
```

**As rotinas** (backup e avisos) ficam em `deploy/ops/` e são instaladas pelo script de publicação
(alvos `tudo`, `api` e `rotinas`). Configuração (Telegram, senha do backup, e-mail dos avisos) — rode
**num terminal seu**, porque ele pergunta as senhas:

```bash
ssh -t -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216 python3 /opt/accesswifi/ops/accesswifi_ops.py configurar
```

Outros comandos, no servidor: `accesswifi_ops.py testar` (mensagem de teste nos dois canais),
`accesswifi_ops.py backup` (backup agora), `accesswifi_ops.py vigiar --simular` (mostra o que avisaria).
Segredos em `/etc/accesswifi/ops.env` (só o root lê).

**O que ter em mãos para configurar:**

1. **Token do robô do Telegram.** No Telegram, abra o **@BotFather** (o com selo azul de verificado) →
   **Iniciar** → mande `/newbot` → um nome (ex.: `AccessWifi Avisos`) → um usuário terminado em `bot`
   (ex.: `accesswifi_regional_avisos_bot`). A resposta "Done! Congratulations…" traz o token
   (`7123456789:AAH…`). Não mande o token para ninguém: quem o tem controla o robô. Durante o
   `configurar`, quando ele pedir, abra `t.me/<usuário do robô>` e toque em **Iniciar**.
2. **Senha do backup**, com 12 caracteres ou mais. Gere no gerenciador de senhas ou com
   `openssl rand -base64 24` (Git Bash) e **salve antes de usar**.
3. **E-mail que envia** (no Gmail, com "senha de app", que exige a verificação em duas etapas ligada) e
   **e-mail(s) que recebem** os avisos.

**Alterar depois** (trocar o SMTP, o e-mail que recebe, o robô ou a senha): rode o mesmo `configurar`.
Cada pergunta mostra o valor salvo; **Enter mantém**. Senhas e token aparecem como `[mantém o atual]`; o chat
do Telegram e a senha do backup perguntam "Manter…? [S/n]". No fim ele testa os dois canais de novo
(e oferece um backup — `n` se não precisar).

- **Trocou a senha do backup?** Só os backups **novos** usam a nova. Os que já estão no Telegram continuam
  abrindo **só com a antiga**: guarde as duas, anotando a partir de que data vale a nova.
- **Token vazou ou o robô foi apagado?** No @BotFather: `/revoke` (gera outro token para o mesmo robô) ou
  `/newbot`. Depois, `configurar` com o token novo.
- **A senha de app do Gmail foi revogada?** Os avisos por e-mail param (o Telegram segue). Gere outra no
  Google e rode o `configurar`.

> ⚠️ **Guarde a `Encryption__Key` fora do servidor** (`/root/accesswifi-encryption-key.txt`). Ela
> decifra a chave da UniFi e as senhas guardadas no banco. Backup sem ela restaura um banco com
> essas informações ilegíveis.

---

## 7. Diagnóstico

**Avisos automáticos (Telegram + e-mail).** A cada 5 minutos o servidor confere, por conta própria (fora
da API — se ela cair, o aviso sai mesmo assim):

| O quê | Avisa quando | Repete enquanto durar |
| --- | --- | --- |
| API | não responde por ~30 s seguidos | a cada 1 h |
| Worker | parado | a cada 6 h |
| Banco | não responde | a cada 1 h |
| Portal (cada endereço das unidades) | não abre pelo HTTPS | a cada 1 h |
| Certificado HTTPS | faltam menos de 15 dias para vencer | a cada 24 h |
| UniFi | recusou alguma liberação (cliente ficou sem internet) — com unidade e motivo | no máx. a cada 30 min |
| Disco | acima de 85% | a cada 24 h |
| Backup | nenhum guardado fora há mais de 26 h | a cada 24 h |

Quando volta ao normal, chega um "✅ voltou ao normal". Nos 5 minutos depois de uma publicação, API,
worker e portal não são conferidos (o reinício é proposital). **Não há monitor externo:** se a VPS
inteira cair, ninguém avisa.

```bash
systemctl list-timers 'accesswifi-*'                       # quando roda o próximo backup/conferência
journalctl -u accesswifi-vigia -u accesswifi-backup --since today --no-pager   # o que fizeram hoje
```

```bash
systemctl status accesswifi-api accesswifi-worker          # estão no ar?
journalctl -u accesswifi-api -n 100 --no-pager             # log da API
journalctl -u accesswifi-api -p warning --since "-1 hour"  # só avisos e erros da última hora
cat /opt/accesswifi/VERSAO                                 # o que está publicado
```

Falhas de autorização na UniFi ficam no log da API com o motivo técnico. O visitante vê só
"Falha ao autorizar na UniFi.":

| No log | Causa |
| --- | --- |
| `Chave de API da nuvem UniFi inválida ou revogada` | Chave trocada ou apagada no unifi.ui.com |
| `A chave de API não alcança este console` | Chave de outro escopo/console |
| `não está configurada como rede de visitantes` | A SSID perdeu o Hotspot/Captive Portal |
| `Aparelho não encontrado na rede da unidade` | Aparelho saiu da rede antes de enviar o formulário |

Teste de conexão com a UniFi, sem autorizar ninguém: `POST /admin/units/{id}/unifi/test`.

**Velocidade da liberação.** Cada autorização grava o caminho e o tempo, sem dado pessoal:

```bash
journalctl -u accesswifi-api --since today -o cat | grep "Autorização UniFi"
# Autorização UniFi (nuvem) pelo caminho clássico em 812 ms.    ← normal: uma ida à loja
# Autorização UniFi (nuvem) pelo caminho oficial em 2140 ms.    ← plano B: veja o aviso logo antes
```

Se aparecer o caminho **oficial** com frequência, a linha anterior (`API clássica da UniFi não
autorizou (...)`) diz o motivo. Pode ser que a Ubiquiti tenha mudado ou desligado a API clássica.

---

## 8. Armadilhas conhecidas

**O painel pode apagar o nosso arquivo do nginx.** O encaminhamento da API mora em
`security/90-accesswifi-api.conf`, uma pasta que o painel também usa. Se alguém mexer nas
configurações de segurança/WAF do site pelo painel, ele pode regenerar a pasta. **Sintoma:** o
portal abre, mas nada funciona (a API devolve a página do portal em vez de JSON). **Correção:**
rodar o script de publicação, que recoloca o arquivo.

**UniFi: marcar "Domain".** No Hotspot da UniFi, o campo "External Portal Server" só aceita IPv4
enquanto a opção **Domain** estiver desmarcada. Marque, **salve a página**, e só então preencha
`vps11702.panel.icontainer.online`.

**UniFi: nunca usar o IP.** O certificado HTTPS é do nome, não do IP. Com "Secure Portal" ligado,
usar o IP faz todo visitante ver um aviso de "site não seguro".

**Painel mostra PostgreSQL até 17.x; o banco é 18.** Conectar funciona. O backup pelo painel pode
falhar (o `pg_dump` 17 recusa um servidor 18). Use o backup do §6.

**Allowlist da UniFi** (Pre-Authorization Access): precisa ter `vps11702.panel.icontainer.online`
e `216.22.13.216`.

---

## 9. Pendências

- **Domínio próprio.** O atual é do provedor: não dá para criar `itaituba.…` embaixo dele e ele não
  acompanha uma troca de provedor. Com domínio próprio, cada unidade ganha o seu endereço.
- **Dôce Cafeteria foi apagada em 01/10/2026** (empresa, unidade e tema; não tinha cadastros) para ser
  cadastrada de novo. Backup de antes: `/var/backups/accesswifi/doce-antes-de-apagar-20261001-134811.dump`.
- **Relatório mensal por e-mail não sai.** O SMTP do worker (tabela `Configuration`, chaves `SMTP_*`)
  está vazio e a Regional não tem e-mail de relatório. Os avisos (§7) usam uma configuração própria.
- **Trocar a chave da UniFi da Itaituba.** A atual passou por conversa. O painel ainda não tem tela
  para isso (PARTE 6 do `FRONT_CHANGES.md`); até lá, via `PUT /admin/units/{id}`.
- **Vercel.** Não serve mais a Itaituba. O plano gratuito não permite uso comercial.
