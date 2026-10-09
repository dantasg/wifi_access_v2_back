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
 │    ├── /authorize, /settings, /admin/login|refresh|logout|leads|settings|system-settings|companies|units|users|campaigns
 │    │        └──► API .NET  (systemd: accesswifi-api, 127.0.0.1:5000) ──► PostgreSQL 18
 │    └── todo o resto ──► portal (arquivos estáticos, SPA com fallback para index.html)
 │
 └── worker .NET (systemd: accesswifi-worker) — campanhas (PDF por e-mail para cada unidade),
                                                   relatório mensal por unidade e retenção de leads
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
4. No servidor, **testa o gerador do PDF de campanha** (`AccessWifiService.dll --testar-pdf`, biblioteca
   nativa do QuestPDF). Se não rodar, para ali: nada é trocado, nem o banco.
5. Faz **backup do banco antes** das migrations.
6. Aplica as migrations (script idempotente: roda só o que falta).
7. Troca os binários guardando a versão atual como `anterior` e reinicia.
8. **Se a API não responder em 20 s, volta sozinho para a versão anterior.**
9. Recoloca o encaminhamento do nginx, se o painel tiver apagado (ver §8).
10. Confere pela internet: portal, API e painel.

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

**As rotinas** (backup e avisos) são o programa **`AccessWifi.Ops`** (C#, `src/AccessWifi.Ops`), separado
da API e do worker de propósito: se eles caírem, os avisos saem mesmo assim. **Cada rotina roda num serviço
systemd próprio** — se uma travar, as outras seguem:

| Serviço | Quando | Comando |
| --- | --- | --- |
| `accesswifi-unifi` | sempre ligado (reinicia sozinho em 10 s) | `seguir-unifi` — avisa na hora quando a UniFi recusa uma liberação |
| `accesswifi-vigia` (timer) | a cada 5 min | `vigiar` — API, worker, banco, portal, certificado, disco, backup e o vigia acima |
| `accesswifi-backup` (timer) | 03:15 | `backup` — backup criptografado, enviado ao Telegram |

São instaladas pelo script de publicação (alvos `tudo`, `api` e `rotinas`; o `rotinas` não reinicia nada). No
servidor, o atalho é **`accesswifi-ops <comando>`**. Configuração (Telegram, senha do backup, e-mail dos avisos)
— rode **num terminal seu**, porque ele pergunta as senhas sem mostrá-las:

```bash
ssh -t -i "C:\Users\Genival Dantas\.ssh\accesswifi_vps" root@216.22.13.216 accesswifi-ops configurar
```

Outros comandos, no servidor: `accesswifi-ops testar` (mensagem de teste nos dois canais), `accesswifi-ops backup`
(backup agora), `accesswifi-ops vigiar --simular` e `accesswifi-ops backup --simular` (fazem tudo, mas só mostram
o que mandariam). Segredos em `/etc/accesswifi/ops.env` (só o root lê). O e-mail usa **STARTTLS, porta 587**
(a 465 não é suportada).

**E-mail do sistema (relatório mensal e PDFs das campanhas)** é outra conta, global, configurada no **painel**:
super admin, sem empresa escolhida → **Configurações** (Configurações do sistema). Servidor, porta, usuário, senha,
remetente e STARTTLS ficam nas chaves `SMTP_*` da tabela `Configuration`; a senha vai cifrada com a
`Encryption__Key` e **nunca volta para a tela** (só aparece "guardada"; vazio mantém). O botão **Enviar e-mail de
teste** usa o que está salvo. Sem servidor configurado, o relatório não sai e as campanhas ficam como falha ("O SMTP
não está configurado"), com o motivo no histórico. Para usar a mesma conta dos avisos sem digitar de novo:
`accesswifi-ops smtp-no-banco` (o `configurar` também oferece, no fim).

**Manutenção do `AccessWifi.Ops`:**

| Arquivo | O que faz |
| --- | --- |
| `Program.cs` | Lê o comando e chama a rotina |
| `BackupJob.cs` | Backup (pg_dump → tar → gpg AES-256 → Telegram; 14 cópias locais) |
| `HealthCheckJob.cs` + `CheckTracker.cs` | Conferência de 5 em 5 min; o `CheckTracker` é a regra "avisa, lembra, avisa que voltou" |
| `UnifiWatcher.cs` + `UnifiMonitor.cs` | Vigia da UniFi; o `UnifiMonitor` é só a regra (testável), o `UnifiWatcher` lê o `journalctl -f` |
| `SetupWizard.cs` | `configurar` e `testar` |
| `Notifier.cs`, `TelegramClient.cs`, `AlertEmail.cs` | Envio dos avisos (Telegram + e-mail, nunca derruba quem chamou) |
| `OpsSettings.cs`, `OpsState.cs`, `EnvFile.cs` | `ops.env` e `estado.json` (com trava, porque backup e conferência podem rodar juntos) |
| `AppDatabase.cs` | Banco da aplicação com a configuração da API (`/etc/accesswifi/accesswifi.env`) |
| `deploy/ops/systemd/*` e `deploy/ops/instalar.sh` | Os três serviços e a instalação (o programa novo só substitui o atual se rodar) |

As rotinas só rodam no servidor (Linux: `systemctl`, `journalctl`, `pg_dump`, `gpg`). A lógica tem testes
automáticos — **`dotnet test tests/AccessWifi.Ops.Tests`** — inclusive a sequência real de recusas de 05/10/2026 e a
leitura dos arquivos que já estão no servidor. Para testar uma mudança no servidor sem trocar o que está rodando:
`dotnet publish src/AccessWifi.Ops -c Release -r linux-x64 --self-contained false -o pub`, copie `pub/` para
`/tmp/teste` e rode `dotnet /tmp/teste/AccessWifi.Ops.dll vigiar --simular` (ou `backup --simular`).

*(Histórico: de 01/10 a 05/10/2026 as rotinas foram um script Python, `deploy/ops/accesswifi_ops.py`; a versão em
C# lê os mesmos `ops.env` e `estado.json`, então a troca não exigiu configurar de novo.)*

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

**Avisos automáticos (Telegram + e-mail).** O servidor confere por conta própria (fora da API — se ela
cair, o aviso sai mesmo assim). As **recusas da UniFi são avisadas na hora** por um serviço que acompanha o
log da API (`accesswifi-unifi`); o resto, a cada 5 minutos:

| O quê | Avisa quando | Repete enquanto durar |
| --- | --- | --- |
| API | não responde por ~30 s seguidos | a cada 1 h |
| Worker | parado | a cada 6 h |
| Banco | não responde | a cada 1 h |
| Portal (cada endereço das unidades) | não abre pelo HTTPS | a cada 1 h |
| Certificado HTTPS | faltam menos de 15 dias para vencer | a cada 24 h |
| UniFi | **na 1ª recusa, em segundos** (cliente ficou sem internet) — com unidade e motivo | resumo a cada 10 min; "✅ voltou" na próxima liberação boa |
| Vigia da UniFi | o serviço `accesswifi-unifi` parou (as recusas deixariam de ser avisadas) | a cada 6 h |
| Pontos de acesso | portal aberto por AP que nenhuma loja tem; AP em duas unidades; APs de uma unidade não lidos | a cada 6 h (por AP/unidade) |
| Disco | acima de 85% | a cada 24 h |
| Backup | nenhum guardado fora há mais de 26 h | a cada 24 h |

Quando volta ao normal, chega um "✅ voltou ao normal". Nos 5 minutos depois de uma publicação, API,
worker e portal não são conferidos (o reinício é proposital). **Não há monitor externo:** se a VPS
inteira cair, ninguém avisa.

```bash
systemctl list-timers 'accesswifi-*'                       # quando roda o próximo backup/conferência
systemctl status accesswifi-unifi                           # vigia das liberações da UniFi (sempre ligado)
journalctl -u accesswifi-vigia -u accesswifi-backup -u accesswifi-unifi --since today --no-pager   # o que fizeram hoje
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

**Unidade pelo ponto de acesso** (`PROPOSTA_UNIDADE_PELO_AP.md`). Todas as lojas usam o mesmo endereço; a
loja sai do MAC do AP. A API lê os APs das unidades no modo nuvem a cada 5 min (painel → Unidades → coluna
"Pontos de acesso"; o botão "Ler pontos de acesso" lê na hora).

```bash
journalctl -u accesswifi-api --since today -o cat | grep -E "ponto de acesso|Aparelhos da unidade|Pontos de acesso"
```

| No log | O que fazer |
| --- | --- |
| `Portal aberto por um ponto de acesso desconhecido {MAC} no endereço ...` | Loja ainda não cadastrada (unidade no modo nuvem, com console e chave) ou console fora do alcance da chave |
| `Ponto de acesso {MAC} em mais de uma unidade` | Duas unidades com o mesmo console: corrigir o console de uma delas |
| `Aparelhos da unidade {slug} não lidos na nuvem da UniFi: ...` | Motivo no fim da linha (chave, console errado). Os APs já lidos continuam valendo |

**Loja nova da rede:** painel → Unidades → Nova unidade, modo **Nuvem**, com o Console ID (o `hostId`, trecho
da URL do unifi.ui.com) e a chave. Não precisa endereço do portal. Na UniFi da loja: o mesmo endereço
(`vps11702.panel.icontainer.online`, "Domain") e a mesma allowlist do §8. Ao salvar, a coluna "Pontos de
acesso" já mostra os aparelhos; se ficar em 0, o console está errado.

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

- **Domínio próprio.** O atual é do provedor e não acompanha uma troca de provedor. Desde 08/10/2026 não
  é mais preciso um endereço por loja (a loja sai do ponto de acesso, §7).
- **Relatório mensal e campanhas por e-mail não saem ainda.** Falta a conta de envio (painel → Configurações
  do sistema, §6) e o e-mail da unidade Itaituba (painel → Unidades → Editar). Os avisos (§7) usam uma
  configuração própria.
- **Trocar a chave da UniFi da Itaituba.** A atual passou por conversa. O painel ainda não tem tela
  para isso (PARTE 6 do `FRONT_CHANGES.md`); até lá, via `PUT /admin/units/{id}`.
- **Vercel.** Não serve mais a Itaituba. O plano gratuito não permite uso comercial.
