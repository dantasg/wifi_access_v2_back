#!/usr/bin/env bash
# =============================================================================
# Desfaz NA PRODUÇÃO a troca dos nomes do código para inglês (migration EnglishColumnNames), caso algo dê
# errado depois de publicar. Num passo só:
#   1. backup do banco;
#   2. para a API e o worker;
#   3. volta as 5 colunas para os nomes antigos (Down da migration, gerado pelo EF; os dados ficam);
#   4. volta API, worker e portal para a versão de antes (back 21a12e6 · front 5085992);
#   5. sobe de novo e confere se a API respondeu.
#
# Uso — no Git Bash, a partir da raiz do repositório do BACK:
#   ./deploy/rollback-english-names.sh
#
# Funciona também se o deploy já tiver voltado os programas sozinho (aí só acerta o banco). Se a cópia
# "anterior" no servidor não for a versão de antes da troca, para sem mexer em nada.
# =============================================================================
set -euo pipefail

SERVER="root@216.22.13.216"
KEY="${USERPROFILE:-$HOME}/.ssh/accesswifi_vps"
SSH=(ssh -i "$KEY" -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 "$SERVER")

[ -f "$KEY" ] || { echo "Chave SSH não encontrada em $KEY" >&2; exit 1; }

printf '\n\033[1;34m==> Desfazendo a troca dos nomes para inglês na produção\033[0m\n'
"${SSH[@]}" 'bash -s' <<'REMOTE'
set -euo pipefail
SITE=/etc/icontainer/apps/nginx/nginx/www/sites/vps11702.panel.icontainer.online
PREVIOUS_BACK="21a12e6"

echo "    no ar agora: $(cat /opt/accesswifi/VERSAO 2>/dev/null || echo desconhecida)"
SWAP_BINARIES=0
if [ -d /opt/accesswifi/api.anterior ]; then
  if grep -q "back $PREVIOUS_BACK" /opt/accesswifi/VERSAO.anterior 2>/dev/null; then
    SWAP_BINARIES=1
  else
    echo "    ✗ a cópia anterior não é a de antes da troca ($(cat /opt/accesswifi/VERSAO.anterior 2>/dev/null || echo sem registro))."
    echo "      Nada foi alterado. Veja PRODUCAO.md antes de reverter à mão."
    exit 1
  fi
elif ! grep -q "back $PREVIOUS_BACK" /opt/accesswifi/VERSAO 2>/dev/null; then
  echo "    ✗ sem cópia anterior e a versão no ar não é a de antes da troca. Nada foi alterado."
  exit 1
fi

# Avisa o vigia (rotinas de proteção) que o reinício é proposital: sem alerta por 5 minutos.
install -d -m 755 /run/accesswifi-ops && touch /run/accesswifi-ops/manutencao

BACKUP=/var/backups/accesswifi/antes-de-desfazer-ingles-$(date +%Y%m%d-%H%M%S).dump
sudo -u postgres pg_dump -Fc accesswifi > "$BACKUP"
echo "    ✓ backup do banco: $(basename "$BACKUP")"

systemctl stop accesswifi-api accesswifi-worker

# Down da migration EnglishColumnNames (dotnet ef migrations script EnglishColumnNames CorreioEletronico).
# Cada passo só roda se a migration estiver aplicada; tudo numa transação.
sudo -u postgres psql -d accesswifi -v ON_ERROR_STOP=1 -q <<'SQL'
START TRANSACTION;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    ALTER TABLE "Units" RENAME COLUMN "AreaCode" TO "Ddd";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    ALTER TABLE "PortalSettings" RENAME COLUMN "AreaCode" TO "Ddd";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    ALTER TABLE "Leads" RENAME COLUMN "Phone" TO "Telefone";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    ALTER TABLE "Leads" RENAME COLUMN "Name" TO "Nome";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    ALTER TABLE "Leads" RENAME COLUMN "BirthDate" TO "Nascimento";
    END IF;
END $EF$;
DO $EF$
BEGIN
    IF EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261009185541_EnglishColumnNames') THEN
    DELETE FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20261009185541_EnglishColumnNames';
    END IF;
END $EF$;
COMMIT;
SQL
echo "    ✓ banco: colunas com os nomes de antes"

if [ $SWAP_BINARIES = 1 ]; then
  for s in api worker; do
    rm -rf /opt/accesswifi/$s.revertida
    mv /opt/accesswifi/$s /opt/accesswifi/$s.revertida
    mv /opt/accesswifi/$s.anterior /opt/accesswifi/$s
    echo "    ✓ $s: versão de antes"
  done
  if [ -d "$SITE/index.anterior" ]; then
    rm -rf "$SITE/index.revertido"
    mv "$SITE/index" "$SITE/index.revertido"
    mv "$SITE/index.anterior" "$SITE/index"
    echo "    ✓ portal: versão de antes"
  fi
  if [ -f /opt/accesswifi/VERSAO.anterior ]; then mv /opt/accesswifi/VERSAO.anterior /opt/accesswifi/VERSAO; fi
fi

systemctl start accesswifi-api accesswifi-worker

# GET /settings sem parâmetro = 400: prova de que a API subiu e lê o banco.
for i in $(seq 1 20); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000/settings)" = 400 ]; then
    echo "    ✓ API no ar; worker: $(systemctl is-active accesswifi-worker)"
    echo "    no ar agora: $(cat /opt/accesswifi/VERSAO)"
    exit 0
  fi
  sleep 1
done
echo "    ✗ a API não respondeu — veja: journalctl -u accesswifi-api -n 50"
exit 1
REMOTE
