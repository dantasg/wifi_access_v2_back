#!/usr/bin/env bash
# Instala/atualiza as rotinas de proteção (backup diário + vigia) no servidor, como root.
# Chamado pelo publicar-producao.sh (alvos tudo, api e rotinas). Pode rodar quantas vezes quiser.
set -euo pipefail
ORIGEM="$(cd "$(dirname "$0")" && pwd)"

# Os arquivos podem vir do Windows com CRLF: o systemd e o Python querem LF.
LIMPO="$(mktemp)"
trap 'rm -f "$LIMPO"' EXIT
tr -d '\r' < "$ORIGEM/accesswifi_ops.py" > "$LIMPO"
python3 - "$LIMPO" <<'PY'
import sys
compile(open(sys.argv[1], encoding='utf-8').read(), sys.argv[1], 'exec')
PY

install -d -m 755 /opt/accesswifi/ops
install -m 755 "$LIMPO" /opt/accesswifi/ops/accesswifi_ops.py
for UNIDADE in "$ORIGEM"/systemd/accesswifi-*.service "$ORIGEM"/systemd/accesswifi-*.timer; do
  tr -d '\r' < "$UNIDADE" > "/etc/systemd/system/$(basename "$UNIDADE")"
  chmod 644 "/etc/systemd/system/$(basename "$UNIDADE")"
done
systemctl daemon-reload
systemctl enable --now accesswifi-backup.timer accesswifi-vigia.timer >/dev/null 2>&1
# O vigia das liberações roda o tempo todo: reinicia para carregar a versão nova do script.
systemctl enable accesswifi-unifi.service >/dev/null 2>&1
systemctl restart accesswifi-unifi.service

echo "    ✓ rotinas de proteção: backup diário às 03:15, conferência a cada 5 min e vigia da UniFi ($(systemctl is-active accesswifi-unifi))"
if [ ! -s /etc/accesswifi/ops.env ]; then
  echo "    ! falta configurar o Telegram, o e-mail e a senha do backup — PRODUCAO.md §6"
fi
