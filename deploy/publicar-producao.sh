#!/usr/bin/env bash
# =============================================================================
# Publica o AccessWifi na VPS de produção (painel iContainer).
#
# Uso — no Git Bash, a partir da raiz do repositório do BACK:
#   ./deploy/publicar-producao.sh            API + worker + portal
#   ./deploy/publicar-producao.sh api        só API e worker (com migrations)
#   ./deploy/publicar-producao.sh portal     só o portal (o front)
#   ./deploy/publicar-producao.sh reverter   volta API, worker e portal para a versão anterior
#
# O que ele garante, na ordem:
#   1. Só publica código commitado (o servidor guarda qual commit está no ar).
#   2. Compila AQUI; o servidor só tem o runtime do .NET.
#   3. Recusa o pacote se tiver segredo de desenvolvimento ou URL do ngrok/Vercel.
#   4. Backup do banco ANTES das migrations.
#   5. Se a API não responder depois de subir, volta sozinho para a versão anterior.
#   6. Recoloca o encaminhamento do nginx se o painel tiver apagado.
#
# Detalhes da produção: PRODUCAO.md
# =============================================================================
set -Eeuo pipefail

# Nenhuma falha é silenciosa: se um comando quebrar, diz a linha e qual foi.
trap 'printf "\n\033[1;31m✗ Falhou na linha %s: %s\033[0m\n" "$LINENO" "$BASH_COMMAND" >&2' ERR

SERVIDOR="root@216.22.13.216"
CHAVE="${USERPROFILE:-$HOME}/.ssh/accesswifi_vps"
DOMINIO="vps11702.panel.icontainer.online"

RAIZ_BACK="$(cd "$(dirname "$0")/.." && pwd)"
RAIZ_FRONT="$(cd "$RAIZ_BACK/../AccessWifi_V2_FRONT" 2>/dev/null && pwd || true)"
ALVO="${1:-tudo}"

SSH=(ssh -i "$CHAVE" -o BatchMode=yes -o IdentitiesOnly=yes -o ConnectTimeout=15 "$SERVIDOR")

etapa() { printf '\n\033[1;34m==> %s\033[0m\n' "$1"; }
ok()    { printf '    \033[32m✓\033[0m %s\n' "$1"; }
falha() { trap - ERR; printf '\n\033[1;31m✗ %s\033[0m\n' "$1" >&2; exit 1; }

# Roda sem poluir a tela, mas se falhar mostra a saída inteira: erro de compilação não pode sumir.
quieto() {
  local log
  log="$(mktemp)"
  if ! "$@" >"$log" 2>&1; then
    sed 's/^/      /' "$log" >&2
    rm -f "$log"
    falha "Falhou: $*"
  fi
  rm -f "$log"
}

case "$ALVO" in
  tudo|api|portal|reverter) ;;
  *) falha "Alvo desconhecido: '$ALVO'. Use: tudo | api | portal | reverter" ;;
esac

[ -f "$CHAVE" ] || falha "Chave SSH não encontrada em $CHAVE"

# -----------------------------------------------------------------------------
# Reverter: troca o que está no ar pela cópia "anterior" guardada no último deploy.
# Não mexe no banco — migration desfeita exige restaurar o backup (ver PRODUCAO.md).
# -----------------------------------------------------------------------------
if [ "$ALVO" = reverter ]; then
  etapa "Revertendo para a versão anterior"
  "${SSH[@]}" 'bash -s' <<'REMOTO'
set -euo pipefail
SITE=/etc/icontainer/apps/nginx/nginx/www/sites/vps11702.panel.icontainer.online
for s in api worker; do
  [ -d /opt/accesswifi/$s.anterior ] || { echo "    sem versão anterior de $s — nada a reverter"; continue; }
  rm -rf /opt/accesswifi/$s.revertida
  mv /opt/accesswifi/$s /opt/accesswifi/$s.revertida
  mv /opt/accesswifi/$s.anterior /opt/accesswifi/$s
  echo "    $s revertida"
done
if [ -d "$SITE/index.anterior" ]; then
  rm -rf "$SITE/index.revertido"
  mv "$SITE/index" "$SITE/index.revertido"
  mv "$SITE/index.anterior" "$SITE/index"
  echo "    portal revertido"
fi
if [ -f /opt/accesswifi/VERSAO.anterior ]; then mv /opt/accesswifi/VERSAO.anterior /opt/accesswifi/VERSAO; fi
systemctl restart accesswifi-api accesswifi-worker
sleep 6
if systemctl is-active --quiet accesswifi-api; then echo "    API no ar"
else echo "    ATENÇÃO: a API não subiu — veja: journalctl -u accesswifi-api -n 50"; fi
REMOTO
  ok "Reversão concluída. No ar: $("${SSH[@]}" 'cat /opt/accesswifi/VERSAO 2>/dev/null || echo desconhecida')"
  exit 0
fi

# -----------------------------------------------------------------------------
# 1. Só código commitado
# -----------------------------------------------------------------------------
etapa "Conferindo se o código está commitado"
repo_limpo() {
  local dir="$1" nome="$2"
  if [ -n "$(git -C "$dir" status --porcelain)" ]; then
    git -C "$dir" status --short >&2
    falha "Há alterações não commitadas em $nome. Commite antes de publicar — assim dá para saber exatamente o que está no ar."
  fi
  ok "$nome: $(git -C "$dir" log -1 --format='%h %s')"
}

VERSAO=""
if [ "$ALVO" != portal ]; then
  repo_limpo "$RAIZ_BACK" "back"
  VERSAO="back $(git -C "$RAIZ_BACK" log -1 --format=%h)"
fi
if [ "$ALVO" != api ]; then
  [ -n "$RAIZ_FRONT" ] || falha "Repositório do front não encontrado ao lado do back ($RAIZ_BACK/../AccessWifi_V2_FRONT)."
  repo_limpo "$RAIZ_FRONT" "front"
  VERSAO="$VERSAO${VERSAO:+ · }front $(git -C "$RAIZ_FRONT" log -1 --format=%h)"
fi

PACOTE="$(mktemp -d)"
trap 'rm -rf "$PACOTE"' EXIT

# -----------------------------------------------------------------------------
# 2. Compilar aqui
# -----------------------------------------------------------------------------
if [ "$ALVO" != portal ]; then
  etapa "Compilando API e worker (Linux x64)"
  quieto dotnet publish "$RAIZ_BACK/src/AccessWifi.Api/AccessWifi.Api.csproj" \
    -c Release -r linux-x64 --self-contained false -o "$PACOTE/api" -v q --nologo
  ok "API"
  quieto dotnet publish "$RAIZ_BACK/src/AccessWifiService/AccessWifiService.csproj" \
    -c Release -r linux-x64 --self-contained false -o "$PACOTE/worker" -v q --nologo
  ok "worker"

  etapa "Gerando o script SQL das migrations (idempotente)"
  quieto dotnet ef migrations script --idempotent \
    --project "$RAIZ_BACK/src/Models" --startup-project "$RAIZ_BACK/src/AccessWifi.Api" \
    -o "$PACOTE/migrate.sql"
  ok "$(grep -c 'INSERT INTO "__EFMigrationsHistory"' "$PACOTE/migrate.sql") migrations no script"
fi

if [ "$ALVO" != api ]; then
  etapa "Compilando o portal"
  # --mode vps: o Vite NÃO carrega o .env.production (que aponta para o ngrok). Sem VITE_API_URL,
  # o front chama a própria origem — portal e API moram no mesmo endereço.
  quieto bash -c 'cd "$1" && npx tsc -b && npx vite build --mode vps --outDir "$2" --emptyOutDir' \
    _ "$RAIZ_FRONT" "$PACOTE/portal"
  ok "portal"
fi

# -----------------------------------------------------------------------------
# 3. Travas de segurança no pacote
# -----------------------------------------------------------------------------
etapa "Conferindo o pacote"
if ls "$PACOTE"/api/appsettings.Development* "$PACOTE"/worker/appsettings.Development* >/dev/null 2>&1; then
  falha "O pacote contém appsettings.Development — segredo de desenvolvimento não vai para produção."
fi
ok "sem configuração de desenvolvimento"
if [ -d "$PACOTE/portal" ]; then
  if grep -rlqE "ngrok-free\.dev|vercel\.app" "$PACOTE/portal"; then
    falha "O portal aponta para ngrok/Vercel — ele precisa chamar a própria origem."
  fi
  ok "portal chama a própria origem"
fi
cp "$RAIZ_BACK/deploy/nginx/90-accesswifi-api.conf" "$PACOTE/"
printf '%s — publicado em %s\n' "$VERSAO" "$(date '+%d/%m/%Y %H:%M')" > "$PACOTE/VERSAO"

# -----------------------------------------------------------------------------
# 4. Enviar
# -----------------------------------------------------------------------------
etapa "Enviando para $SERVIDOR"
tar -C "$PACOTE" -czf - . | "${SSH[@]}" \
  'rm -rf /tmp/accesswifi-pub && mkdir -p /tmp/accesswifi-pub && tar -xzf - -C /tmp/accesswifi-pub'
ok "enviado"

# -----------------------------------------------------------------------------
# 5. Aplicar no servidor
# -----------------------------------------------------------------------------
etapa "Aplicando no servidor"
"${SSH[@]}" "ALVO=$ALVO bash -s" <<'REMOTO'
set -euo pipefail
P=/tmp/accesswifi-pub
SITE=/etc/icontainer/apps/nginx/nginx/www/sites/vps11702.panel.icontainer.online
ok()    { echo "    ✓ $1"; }
falha() { echo "    ✗ $1"; exit 1; }

if [ "$ALVO" != portal ]; then
  # Backup antes de qualquer migration. Guarda os 10 mais recentes.
  install -d -m 700 /var/backups/accesswifi
  B=/var/backups/accesswifi/antes-$(date +%Y%m%d-%H%M%S).dump
  sudo -u postgres pg_dump -Fc accesswifi > "$B"
  ls -1t /var/backups/accesswifi/antes-*.dump | tail -n +11 | xargs -r rm -f
  ok "backup do banco: $(basename "$B") ($(du -h "$B" | cut -f1))"

  export PGPASSWORD="$(grep -oP 'Password=\K[^;]+' /etc/accesswifi/accesswifi.env)"
  # O script idempotente avisa "already exists, skipping" a cada rodada — é esperado, não é erro.
  # Só warnings e erros aparecem.
  export PGOPTIONS='-c client_min_messages=warning'
  ANTES=$(psql -h localhost -U accesswifi -d accesswifi -tAc 'SELECT count(*) FROM "__EFMigrationsHistory"')
  psql -h localhost -U accesswifi -d accesswifi -v ON_ERROR_STOP=1 -q -f "$P/migrate.sql" >/dev/null
  DEPOIS=$(psql -h localhost -U accesswifi -d accesswifi -tAc 'SELECT count(*) FROM "__EFMigrationsHistory"')
  unset PGPASSWORD PGOPTIONS
  ok "migrations: $ANTES antes, $DEPOIS depois"

  # Troca os binários guardando a versão atual como "anterior" (para o reverter).
  for s in api worker; do
    rm -rf /opt/accesswifi/$s.anterior
    if [ -d /opt/accesswifi/$s ]; then mv /opt/accesswifi/$s /opt/accesswifi/$s.anterior; fi
    cp -r "$P/$s" /opt/accesswifi/$s
  done
  chown -R root:root /opt/accesswifi
  chmod -R a+rX /opt/accesswifi
  systemctl restart accesswifi-api accesswifi-worker

  # Espera a API responder (GET /settings sem parâmetro = 400, prova de que subiu).
  SUBIU=0
  for i in $(seq 1 20); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000/settings)" = 400 ]; then SUBIU=1; break; fi
    sleep 1
  done
  if [ $SUBIU = 0 ]; then
    echo "    ✗ a API não respondeu — voltando para a versão anterior"
    journalctl -u accesswifi-api -n 15 --no-pager -o cat | sed 's/^/      /'
    for s in api worker; do
      if [ -d /opt/accesswifi/$s.anterior ]; then
        rm -rf /opt/accesswifi/$s
        mv /opt/accesswifi/$s.anterior /opt/accesswifi/$s
      fi
    done
    systemctl restart accesswifi-api accesswifi-worker
    falha "versão nova recusada; a anterior voltou ao ar (o banco já recebeu as migrations — ver PRODUCAO.md §5)"
  fi
  ok "API no ar; worker: $(systemctl is-active accesswifi-worker)"
fi

if [ "$ALVO" != api ]; then
  rm -rf "$SITE/index.anterior"
  cp -a "$SITE/index" "$SITE/index.anterior"
  rm -rf "$SITE/index"/*
  cp -r "$P/portal/." "$SITE/index/"
  chown -R root:root "$SITE/index"
  chmod -R a+rX "$SITE/index"
  ok "portal publicado"
fi

# O painel pode regenerar a pasta security/ do site; se o nosso arquivo sumiu ou mudou, recoloca.
CONF="$SITE/security/90-accesswifi-api.conf"
if cmp -s "$P/90-accesswifi-api.conf" "$CONF"; then
  ok "encaminhamento da API no nginx em dia"
else
  cp "$P/90-accesswifi-api.conf" "$CONF"
  if ! docker exec ic-nginx-B0Yo nginx -t >/dev/null 2>&1; then
    rm -f "$CONF"
    falha "configuração do nginx inválida"
  fi
  docker exec ic-nginx-B0Yo nginx -s reload
  ok "encaminhamento da API no nginx (re)colocado"
fi

if [ -f /opt/accesswifi/VERSAO ]; then cp /opt/accesswifi/VERSAO /opt/accesswifi/VERSAO.anterior; fi
cp "$P/VERSAO" /opt/accesswifi/VERSAO
rm -rf "$P"
REMOTO

# -----------------------------------------------------------------------------
# 6. Conferência pela internet, como o visitante enxerga
# -----------------------------------------------------------------------------
etapa "Conferindo pela internet"
confere() {
  local nome="$1" url="$2" esperado="$3" codigo
  codigo="$(curl -s -o /dev/null -w '%{http_code}' --max-time 15 "$url" || true)"
  if [ "$codigo" = "$esperado" ]; then ok "$nome ($codigo)"
  else falha "$nome respondeu $codigo, esperado $esperado — $url"; fi
}
confere "portal"                "https://$DOMINIO/guest/s/default/" 200
confere "API"                   "https://$DOMINIO/settings"         400
confere "painel admin (página)" "https://$DOMINIO/admin"            200

printf '\n\033[1;32mPublicado: %s\033[0m\n' "$("${SSH[@]}" 'cat /opt/accesswifi/VERSAO')"
