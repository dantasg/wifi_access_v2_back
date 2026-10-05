#!/usr/bin/env bash
# Instala/atualiza as rotinas de proteção no servidor, como root: o programa AccessWifi.Ops (C#, pasta bin/,
# compilada pelo publicar-producao.sh) e os três serviços systemd — cada rotina num serviço próprio:
#   accesswifi-unifi   (sempre ligado)   avisa na hora quando a UniFi recusa uma liberação
#   accesswifi-vigia   (a cada 5 min)    confere API, worker, banco, portal, disco, certificado e backup
#   accesswifi-backup  (03:15)           backup criptografado do banco, enviado ao Telegram
# Chamado pelo publicar-producao.sh (alvos tudo, api e rotinas). Pode rodar quantas vezes quiser.
set -euo pipefail
ORIGEM="$(cd "$(dirname "$0")" && pwd)"
DESTINO=/opt/accesswifi/ops
NOVO="$DESTINO.novo"

# 1. O programa novo vai para uma pasta à parte e só substitui o atual se rodar.
rm -rf "$NOVO"
cp -r "$ORIGEM/bin" "$NOVO"
chown -R root:root "$NOVO"
chmod -R u=rwX,go=rX "$NOVO"
if ! VERSAO_OPS="$(/usr/bin/dotnet "$NOVO/AccessWifi.Ops.dll" versao 2>&1)"; then
  echo "    ✗ o programa novo das rotinas não roda — o atual foi mantido: $VERSAO_OPS"
  rm -rf "$NOVO"
  exit 1
fi
rm -rf "$DESTINO"
mv "$NOVO" "$DESTINO"

# 2. Atalho: "accesswifi-ops <comando>" (configurar, testar, backup, vigiar --simular…).
cat > /usr/local/bin/accesswifi-ops <<'ATALHO'
#!/usr/bin/env bash
exec /usr/bin/dotnet /opt/accesswifi/ops/AccessWifi.Ops.dll "$@"
ATALHO
chmod 755 /usr/local/bin/accesswifi-ops

# 3. Os serviços (podem vir do Windows com CRLF: o systemd quer LF).
for UNIDADE in "$ORIGEM"/systemd/accesswifi-*.service "$ORIGEM"/systemd/accesswifi-*.timer; do
  tr -d '\r' < "$UNIDADE" > "/etc/systemd/system/$(basename "$UNIDADE")"
  chmod 644 "/etc/systemd/system/$(basename "$UNIDADE")"
done
systemctl daemon-reload
systemctl enable --now accesswifi-backup.timer accesswifi-vigia.timer >/dev/null 2>&1
# O vigia das liberações roda o tempo todo: reinicia para carregar a versão nova do programa.
systemctl enable accesswifi-unifi.service >/dev/null 2>&1
systemctl restart accesswifi-unifi.service

echo "    ✓ rotinas de proteção (AccessWifi.Ops $VERSAO_OPS): backup diário às 03:15, conferência a cada 5 min e vigia da UniFi ($(systemctl is-active accesswifi-unifi))"
if [ ! -s /etc/accesswifi/ops.env ]; then
  echo "    ! falta configurar o Telegram, o e-mail e a senha do backup: accesswifi-ops configurar (PRODUCAO.md §6)"
fi
