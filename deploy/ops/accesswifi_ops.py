#!/usr/bin/env python3
"""
Rotinas de proteção do AccessWifi na VPS. Rodam fora da API (systemd timers): se a API cair, o aviso
sai mesmo assim. Só biblioteca padrão do Python.

  accesswifi_ops.py backup            backup do banco + configuração, criptografado, enviado ao Telegram
  accesswifi_ops.py vigiar            confere API, worker, banco, portal, UniFi, disco, certificado e backup
  accesswifi_ops.py avisar TITULO TEXTO
  accesswifi_ops.py configurar        passo guiado: robô do Telegram, senha do backup, e-mail dos avisos
  accesswifi_ops.py testar            manda uma mensagem de teste pelos canais configurados

  --simular (backup/vigiar)           faz tudo, mas não manda nada: só mostra o que mandaria

Segredos: /etc/accesswifi/ops.env (só o root lê). Estado: /var/lib/accesswifi-ops/estado.json.
Ver PRODUCAO.md §6 e §7.
"""

import datetime as dt
import getpass
import json
import os
import re
import shutil
import smtplib
import socket
import ssl
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from email.message import EmailMessage
from pathlib import Path

CONFIG = Path('/etc/accesswifi/ops.env')
CONFIG_APP = Path('/etc/accesswifi/accesswifi.env')
VERSAO_APP = Path('/opt/accesswifi/VERSAO')
ESTADO = Path('/var/lib/accesswifi-ops/estado.json')
MANUTENCAO = Path('/run/accesswifi-ops/manutencao')
PASTA_BACKUP = Path('/var/backups/accesswifi')
BANCO = 'accesswifi'
API_LOCAL = 'http://127.0.0.1:5000/settings'

BACKUPS_LOCAIS = 14            # quantos backups diários ficam no servidor
LIMITE_TELEGRAM = 49 * 1024 * 1024
DISCO_LIMITE = 85              # % de uso do disco que gera aviso
CERTIFICADO_DIAS = 15          # aviso quando faltar menos que isso
BACKUP_ATRASADO_H = 26         # sem backup bom há mais que isso → aviso
UNIFI_INTERVALO_MIN = 30       # no máximo um aviso de falha na UniFi a cada 30 min (acumula)
MANUTENCAO_MIN = 5             # depois de uma publicação, ignora API/worker/portal por 5 min

FUSO = dt.timezone(dt.timedelta(hours=-3))  # Belém (sem horário de verão)

SIMULAR = False


# ----------------------------------------------------------------------------- utilidades

def agora():
    return dt.datetime.now(FUSO)


def fmt(momento):
    return momento.astimezone(FUSO).strftime('%d/%m/%Y %H:%M')


def log(texto):
    print(texto, flush=True)


def tamanho(nbytes):
    return f'{nbytes / 1024:.0f} KB' if nbytes < 1024 * 1024 else f'{nbytes / 1024 / 1024:.1f} MB'


def ler_config():
    """KEY=VALOR por linha; o valor é tudo depois do primeiro '=' (senhas podem ter '=')."""
    valores = {}
    if CONFIG.exists():
        for linha in CONFIG.read_text(encoding='utf-8').splitlines():
            if linha.strip() and not linha.lstrip().startswith('#') and '=' in linha:
                chave, valor = linha.split('=', 1)
                valores[chave.strip()] = valor
    return valores


def gravar_config(valores):
    CONFIG.parent.mkdir(parents=True, exist_ok=True)
    texto = '# Rotinas de proteção do AccessWifi (accesswifi_ops.py configurar). Só o root lê.\n'
    texto += ''.join(f'{chave}={valor}\n' for chave, valor in valores.items() if valor != '')
    temporario = CONFIG.with_suffix('.tmp')
    descritor = os.open(temporario, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descritor, 'w', encoding='utf-8') as arquivo:
        arquivo.write(texto)
    os.replace(temporario, CONFIG)
    os.chmod(CONFIG, 0o600)


def ler_estado():
    try:
        return json.loads(ESTADO.read_text(encoding='utf-8'))
    except (FileNotFoundError, ValueError):
        return {}


def gravar_estado(estado):
    if SIMULAR:
        return
    ESTADO.parent.mkdir(parents=True, exist_ok=True)
    temporario = ESTADO.with_suffix('.tmp')
    temporario.write_text(json.dumps(estado, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(temporario, ESTADO)


def rodar(comando, **kwargs):
    return subprocess.run(comando, capture_output=True, text=True, timeout=kwargs.pop('timeout', 120), **kwargs)


def psql(sql):
    """Consulta só de leitura no banco da aplicação (usuário postgres, sem senha)."""
    resultado = rodar(['runuser', '-u', 'postgres', '--', 'psql', '-d', BANCO, '-At', '-c', sql], timeout=30)
    if resultado.returncode != 0:
        raise RuntimeError(resultado.stderr.strip() or 'psql falhou')
    return [linha for linha in resultado.stdout.splitlines() if linha.strip()]


# ----------------------------------------------------------------------------- avisos

def _telegram(metodo, campos, arquivo=None, token=None, timeout=60):
    """Chama a API do Telegram. O token nunca aparece na linha de comando (fica só neste processo)."""
    token = token or ler_config().get('TELEGRAM_TOKEN', '')
    url = f'https://api.telegram.org/bot{token}/{metodo}'
    if arquivo is None:
        corpo = urllib.parse.urlencode(campos).encode()
        cabecalhos = {'Content-Type': 'application/x-www-form-urlencoded'}
    else:
        limite = uuid.uuid4().hex
        partes = []
        for chave, valor in campos.items():
            partes.append(f'--{limite}\r\nContent-Disposition: form-data; name="{chave}"\r\n\r\n{valor}\r\n'.encode())
        nome = Path(arquivo).name
        partes.append(
            f'--{limite}\r\nContent-Disposition: form-data; name="document"; filename="{nome}"\r\n'
            'Content-Type: application/octet-stream\r\n\r\n'.encode())
        partes.append(Path(arquivo).read_bytes())
        partes.append(f'\r\n--{limite}--\r\n'.encode())
        corpo = b''.join(partes)
        cabecalhos = {'Content-Type': f'multipart/form-data; boundary={limite}'}
    pedido = urllib.request.Request(url, data=corpo, headers=cabecalhos, method='POST')
    try:
        with urllib.request.urlopen(pedido, timeout=timeout) as resposta:
            dados = json.loads(resposta.read().decode())
    except urllib.error.HTTPError as erro:
        dados = json.loads(erro.read().decode() or '{}')
    if not dados.get('ok'):
        raise RuntimeError(f'Telegram recusou: {dados.get("description", "sem detalhe")}')
    return dados.get('result')


def _com_tentativas(funcao, tentativas=3, espera=5):
    ultimo = None
    for tentativa in range(tentativas):
        try:
            return funcao()
        except Exception as erro:  # rede instável: tenta de novo
            ultimo = erro
            if tentativa < tentativas - 1:
                time.sleep(espera)
    raise ultimo


def enviar_telegram(texto, arquivo=None):
    config = ler_config()
    if not config.get('TELEGRAM_TOKEN') or not config.get('TELEGRAM_CHAT_ID'):
        raise RuntimeError('Telegram não configurado (rode: accesswifi_ops.py configurar)')
    chat = config['TELEGRAM_CHAT_ID']
    if arquivo is None:
        _com_tentativas(lambda: _telegram('sendMessage', {'chat_id': chat, 'text': texto[:4000]}))
    else:
        _com_tentativas(lambda: _telegram('sendDocument', {'chat_id': chat, 'caption': texto[:1000]}, arquivo,
                                          timeout=300))


def enviar_email(assunto, texto, config=None):
    config = config or ler_config()
    destinos = [e.strip() for e in config.get('AVISO_EMAILS', '').split(',') if e.strip()]
    if not config.get('SMTP_HOST') or not destinos:
        raise RuntimeError('e-mail não configurado (rode: accesswifi_ops.py configurar)')
    mensagem = EmailMessage()
    remetente = config.get('SMTP_REMETENTE') or config.get('SMTP_USUARIO', '')
    mensagem['From'] = f'{config.get("SMTP_NOME", "AccessWifi")} <{remetente}>'
    mensagem['To'] = ', '.join(destinos)
    mensagem['Subject'] = f'[AccessWifi] {assunto}'
    mensagem.set_content(texto)
    porta = int(config.get('SMTP_PORTA', '587') or 587)

    def mandar():
        if porta == 465:
            conexao = smtplib.SMTP_SSL(config['SMTP_HOST'], porta, timeout=30, context=ssl.create_default_context())
        else:
            conexao = smtplib.SMTP(config['SMTP_HOST'], porta, timeout=30)
            conexao.starttls(context=ssl.create_default_context())
        with conexao:
            conexao.login(config.get('SMTP_USUARIO', ''), config.get('SMTP_SENHA', ''))
            conexao.send_message(mensagem)

    _com_tentativas(mandar)


def avisar(titulo, texto=''):
    """Telegram e e-mail. Nunca derruba quem chamou: o que falhar fica no log (journal)."""
    corpo = f'{titulo}\n\n{texto}'.strip() + f'\n\n— AccessWifi · {fmt(agora())}'
    if SIMULAR:
        log(f'[simulação] avisaria:\n{corpo}\n')
        return
    for canal, enviar in (('Telegram', lambda: enviar_telegram(corpo)),
                          ('e-mail', lambda: enviar_email(titulo, corpo))):
        try:
            enviar()
            log(f'aviso enviado por {canal}: {titulo}')
        except Exception as erro:
            log(f'AVISO NÃO ENVIADO por {canal} ({erro}): {titulo}')


# ----------------------------------------------------------------------------- backup

def backup():
    """Banco + configuração do servidor num .tar criptografado (AES-256, senha do backup) → Telegram.
    Fica também uma cópia do banco (sem criptografia, só o root lê) no servidor, para restaurar rápido."""
    config = ler_config()
    estado = ler_estado()
    momento = agora()
    carimbo = momento.strftime('%Y%m%d-%H%M%S')
    PASTA_BACKUP.mkdir(parents=True, exist_ok=True)
    os.chmod(PASTA_BACKUP, 0o700)
    os.umask(0o077)
    despejo = PASTA_BACKUP / f'diario-{carimbo}.dump'
    temporaria = Path(tempfile.mkdtemp(prefix='accesswifi-backup-'))
    try:
        with open(despejo, 'wb') as saida:
            resultado = subprocess.run(['runuser', '-u', 'postgres', '--', 'pg_dump', '-Fc', BANCO],
                                       stdout=saida, stderr=subprocess.PIPE, timeout=1800)
        if resultado.returncode != 0:
            raise RuntimeError(f'pg_dump falhou: {resultado.stderr.decode(errors="replace").strip()[:500]}')

        listagem = rodar(['pg_restore', '--list', str(despejo)])
        tabelas = sum(1 for linha in listagem.stdout.splitlines() if 'TABLE DATA' in linha)
        if listagem.returncode != 0 or tabelas == 0:
            raise RuntimeError('o arquivo gerado pelo pg_dump não abre (pg_restore --list falhou)')

        for antigo in sorted(PASTA_BACKUP.glob('diario-*.dump'))[:-BACKUPS_LOCAIS]:
            antigo.unlink()

        if not config.get('BACKUP_SENHA') and not SIMULAR:
            log('backup local feito, mas a senha do backup não está configurada: nada foi enviado ao Telegram')
            raise RuntimeError('backup só local: falta configurar (rode: accesswifi_ops.py configurar)')

        pacote = temporaria / f'accesswifi-{carimbo}.tar'
        with tarfile.open(pacote, 'w') as tar:
            tar.add(despejo, arcname='accesswifi.dump')
            if CONFIG_APP.exists():
                tar.add(CONFIG_APP, arcname='accesswifi.env')  # tem a Encryption__Key (ver PRODUCAO.md §6)
            if VERSAO_APP.exists():
                tar.add(VERSAO_APP, arcname='VERSAO')
        cifrado = Path(f'{pacote}.gpg')
        gnupg = temporaria / 'gnupg'
        gnupg.mkdir(mode=0o700)
        senha = config.get('BACKUP_SENHA') or 'simulacao-sem-senha'
        resultado = subprocess.run(
            ['gpg', '--homedir', str(gnupg), '--batch', '--yes', '--pinentry-mode', 'loopback',
             '--passphrase-fd', '0', '--symmetric', '--cipher-algo', 'AES256', '--output', str(cifrado), str(pacote)],
            input=(senha + '\n').encode(), capture_output=True, timeout=600)
        if resultado.returncode != 0:
            raise RuntimeError(f'criptografia falhou: {resultado.stderr.decode(errors="replace").strip()[:300]}')
        if cifrado.stat().st_size > LIMITE_TELEGRAM:
            raise RuntimeError(f'backup com {tamanho(cifrado.stat().st_size)}: maior que o limite do Telegram (50 MB)')

        legenda = (f'💾 Backup AccessWifi — {fmt(momento)}\n'
                   f'Banco: {tamanho(despejo.stat().st_size)}, {tabelas} tabelas com dados. '
                   f'Criptografado: abre só com a senha do backup (PRODUCAO.md §6).')
        if SIMULAR:
            log(f'[simulação] enviaria ao Telegram: {cifrado.name} ({tamanho(cifrado.stat().st_size)})\n{legenda}')
        else:
            enviar_telegram(legenda, arquivo=cifrado)
            estado['backup_ok_em'] = momento.isoformat()
            estado.pop('backup_falhou', None)
            gravar_estado(estado)
        log(f'backup ok: {despejo.name} ({tamanho(despejo.stat().st_size)}, {tabelas} tabelas)')
        return 0
    except Exception as erro:
        log(f'BACKUP FALHOU: {erro}')
        if not SIMULAR:
            estado['backup_falhou'] = momento.isoformat()
            gravar_estado(estado)
        avisar('🔴 Backup do banco falhou', f'{erro}\n\nO backup de hoje NÃO foi guardado fora do servidor.')
        return 1
    finally:
        shutil.rmtree(temporaria, ignore_errors=True)


# ----------------------------------------------------------------------------- vigia

def _api_no_ar():
    """Qualquer resposta HTTP < 500 prova que a API está de pé (sem parâmetro, o /settings responde 400)."""
    for tentativa in range(3):
        try:
            with urllib.request.urlopen(API_LOCAL, timeout=5) as resposta:
                return resposta.status < 500, f'HTTP {resposta.status}'
        except urllib.error.HTTPError as erro:
            if erro.code < 500:
                return True, f'HTTP {erro.code}'
            motivo = f'HTTP {erro.code}'
        except Exception as erro:
            motivo = str(getattr(erro, 'reason', erro))
        if tentativa < 2:
            time.sleep(10)  # um reinício (publicação) leva poucos segundos
    return False, f'a API não responde em 127.0.0.1:5000 ({motivo})'


def _servico_ativo(nome):
    situacao = rodar(['systemctl', 'is-active', nome], timeout=15).stdout.strip()
    return situacao == 'active', f'{nome}: {situacao or "desconhecido"}'


def _banco_no_ar():
    try:
        return psql('select 1') == ['1'], 'o banco responde'
    except Exception as erro:
        return False, f'o banco não responde: {str(erro)[:200]}'


def _portal(host):
    """Abre o portal pelo próprio servidor (nginx + certificado), com o nome de verdade (SNI).
    Devolve (portal ok, detalhe, dias até o certificado vencer ou None)."""
    contexto = ssl.create_default_context()
    try:
        with socket.create_connection(('127.0.0.1', 443), timeout=10) as conexao:
            with contexto.wrap_socket(conexao, server_hostname=host) as tls:
                certificado = tls.getpeercert()
                vence = dt.datetime.fromtimestamp(ssl.cert_time_to_seconds(certificado['notAfter']), dt.timezone.utc)
                dias = (vence - dt.datetime.now(dt.timezone.utc)).days
                tls.sendall(f'GET / HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n'.encode())
                primeira = tls.recv(64).decode(errors='replace').split('\r\n')[0]
        codigo = primeira.split(' ')[1] if len(primeira.split(' ')) > 1 else '?'
        return codigo == '200', f'https://{host} respondeu {primeira or "nada"}', (dias, vence)
    except ssl.SSLCertVerificationError as erro:
        return False, f'certificado de {host} inválido: {erro.verify_message}', None
    except Exception as erro:
        return False, f'https://{host} não abre: {erro}', None


def _falhas_unifi(desde):
    """Conta no log da API as liberações que a UniFi recusou desde o momento dado."""
    saida = rodar(['journalctl', '-u', 'accesswifi-api', '--since', f'@{int(desde)}', '--no-pager', '-o', 'cat'],
                  timeout=60).stdout
    por_unidade, motivos = {}, {}
    for linha in saida.splitlines():
        achou = re.search(r'Falha ao autorizar guest na UniFi da unidade (\S+?)\.?$', linha.strip())
        if achou:
            por_unidade[achou.group(1)] = por_unidade.get(achou.group(1), 0) + 1
        elif 'UnifiException:' in linha:
            motivo = linha.split('UnifiException:', 1)[1].strip()
            motivos[motivo] = motivos.get(motivo, 0) + 1
    return por_unidade, motivos


def _checar(estado, chave, peca, ok, detalhe, repetir_h):
    """Avisa quando uma peça quebra, lembra a cada `repetir_h` horas enquanto continuar e avisa quando volta."""
    checagens = estado.setdefault('checagens', {})
    anterior = checagens.get(chave, {})
    momento = agora()
    if ok:
        if anterior.get('falhando'):
            desde = dt.datetime.fromisoformat(anterior['desde'])
            avisar(f'✅ {peca}: voltou ao normal', f'Ficou com problema de {fmt(desde)} até {fmt(momento)}.')
        checagens[chave] = {'falhando': False}
        return
    if not anterior.get('falhando'):
        avisar(f'🔴 {peca}: problema', detalhe)
        checagens[chave] = {'falhando': True, 'desde': momento.isoformat(), 'ultimo_aviso': momento.isoformat()}
    elif momento - dt.datetime.fromisoformat(anterior['ultimo_aviso']) >= dt.timedelta(hours=repetir_h):
        desde = dt.datetime.fromisoformat(anterior['desde'])
        avisar(f'🔴 {peca}: continua com problema', f'{detalhe}\nDesde {fmt(desde)}.')
        anterior['ultimo_aviso'] = momento.isoformat()
        checagens[chave] = anterior


def vigiar():
    estado = ler_estado()
    momento = agora()
    estado.setdefault('instalado_em', momento.isoformat())

    em_manutencao = MANUTENCAO.exists() and time.time() - MANUTENCAO.stat().st_mtime < MANUTENCAO_MIN * 60
    if em_manutencao:
        log('publicação recente: API, worker e portal não são conferidos agora')
    else:
        ok, detalhe = _api_no_ar()
        _checar(estado, 'api', 'API (portal e painel)', ok,
                f'{detalhe}. Ninguém consegue se cadastrar no Wi-Fi.', repetir_h=1)
        ok, detalhe = _servico_ativo('accesswifi-worker')
        _checar(estado, 'worker', 'Worker (campanhas, relatórios e retenção)', ok,
                f'{detalhe}. O portal segue funcionando; campanhas e relatórios param.', repetir_h=6)

    ok, detalhe = _banco_no_ar()
    _checar(estado, 'banco', 'Banco de dados', ok, detalhe, repetir_h=1)

    try:
        hosts = psql('select distinct lower("PortalHost") from "Units" where "Active" and "PortalHost" <> \'\'') if ok else []
    except Exception:
        hosts = []
    for host in hosts:
        portal_ok, detalhe, certificado = _portal(host)
        if not em_manutencao:
            _checar(estado, f'portal:{host}', f'Portal {host}', portal_ok, detalhe, repetir_h=1)
        if certificado:
            dias, vence = certificado
            _checar(estado, f'certificado:{host}', f'Certificado HTTPS de {host}', dias >= CERTIFICADO_DIAS,
                    f'O certificado de {host} vence em {dias} dia(s), em {fmt(vence)}. '
                    'Sem ele, os celulares mostram "site não seguro".', repetir_h=24)

    # UniFi: cada falha é um cliente que ficou sem internet. Acumula e avisa no máximo a cada 30 min.
    desde = estado.get('unifi_lido_ate', time.time() - 300)
    ate = time.time()
    por_unidade, motivos = _falhas_unifi(desde)
    pendente = estado.setdefault('unifi_pendente', {'unidades': {}, 'motivos': {}})
    for unidade, quantidade in por_unidade.items():
        pendente['unidades'][unidade] = pendente['unidades'].get(unidade, 0) + quantidade
    for motivo, quantidade in motivos.items():
        pendente['motivos'][motivo] = pendente['motivos'].get(motivo, 0) + quantidade
    estado['unifi_lido_ate'] = ate
    ultimo = estado.get('unifi_ultimo_aviso', 0)
    if pendente['unidades'] and time.time() - ultimo >= UNIFI_INTERVALO_MIN * 60:
        total = sum(pendente['unidades'].values())
        linhas = [f'• {unidade}: {quantidade}' for unidade, quantidade in sorted(pendente['unidades'].items())]
        linhas += ['', 'Motivo(s) no log:'] + [f'• {motivo} ({quantidade}x)'
                                               for motivo, quantidade in pendente['motivos'].items()]
        avisar(f'🟠 {total} liberação(ões) recusada(s) pela UniFi',
               'Clientes preencheram o portal e NÃO ganharam internet:\n' + '\n'.join(linhas) +
               '\n\nVer PRODUCAO.md §7 (tabela de causas).')
        estado['unifi_pendente'] = {'unidades': {}, 'motivos': {}}
        estado['unifi_ultimo_aviso'] = time.time()

    uso = shutil.disk_usage('/')
    porcento = round(uso.used * 100 / uso.total)
    _checar(estado, 'disco', 'Disco do servidor', porcento < DISCO_LIMITE,
            f'Disco em {porcento}% ({tamanho(uso.free)} livres).', repetir_h=24)

    referencia = dt.datetime.fromisoformat(estado.get('backup_ok_em') or estado['instalado_em'])
    horas = (momento - referencia).total_seconds() / 3600
    _checar(estado, 'backup', 'Backup diário', horas < BACKUP_ATRASADO_H,
            f'Nenhum backup guardado fora do servidor há mais de {BACKUP_ATRASADO_H} h. Último: '
            f'{fmt(referencia) if estado.get("backup_ok_em") else "nenhum ainda"}.', repetir_h=24)

    gravar_estado(estado)
    log('vigia: conferência concluída')
    return 0


# ----------------------------------------------------------------------------- configuração (interativa)

def _perguntar(texto, atual='', segredo=False):
    sugestao = ' [mantém o atual]' if atual and segredo else (f' [{atual}]' if atual else '')
    resposta = (getpass.getpass if segredo else input)(f'{texto}{sugestao}: ').strip()
    return resposta or atual


def configurar():
    if os.geteuid() != 0 or not sys.stdin.isatty():
        print('Rode como root, num terminal (ssh -t ...).')
        return 1
    config = ler_config()
    print('\n=== Rotinas de proteção do AccessWifi ===')
    print('Os valores ficam em /etc/accesswifi/ops.env (só o root lê). Enter mantém o que já está.\n')

    print('1) Robô do Telegram')
    while True:
        token = _perguntar('   Token do @BotFather', config.get('TELEGRAM_TOKEN', ''), segredo=True)
        try:
            robo = _telegram('getMe', {}, token=token, timeout=20)
            print(f'   ✓ robô @{robo["username"]}')
            break
        except Exception as erro:
            print(f'   ✗ token não aceito ({erro}). Tente de novo.')
    config['TELEGRAM_TOKEN'] = token
    if config.get('TELEGRAM_CHAT_ID') and input('   Manter o chat já configurado? [S/n]: ').strip().lower() != 'n':
        pass
    else:
        print(f'   Agora, no Telegram, abra o @{robo["username"]} e mande /start (esperando até 3 minutos)...')
        chat = None
        limite = time.time() + 180
        while chat is None and time.time() < limite:
            for atualizacao in _telegram('getUpdates', {'timeout': 20}, token=token, timeout=30) or []:
                mensagem = atualizacao.get('message') or {}
                if (mensagem.get('chat') or {}).get('type') == 'private':
                    chat = mensagem['chat']
        if chat is None:
            print('   ✗ não chegou nenhuma mensagem. Rode o configurar de novo.')
            return 1
        print(f'   ✓ chat de {chat.get("first_name", "")} {chat.get("last_name", "")}'.rstrip())
        config['TELEGRAM_CHAT_ID'] = str(chat['id'])

    print('\n2) Senha do backup (o arquivo no Telegram só abre com ela)')
    if config.get('BACKUP_SENHA') and input('   Manter a senha atual? [S/n]: ').strip().lower() != 'n':
        pass
    else:
        while True:
            senha = getpass.getpass('   Nova senha (mínimo 12 caracteres): ')
            if len(senha) < 12:
                print('   ✗ curta demais.')
            elif getpass.getpass('   Repita: ') != senha:
                print('   ✗ as duas não batem.')
            else:
                break
        config['BACKUP_SENHA'] = senha
        print('   ⚠ Guarde essa senha num gerenciador de senhas. Sem ela, NENHUM backup abre.')

    print('\n3) E-mail dos avisos')
    config['AVISO_EMAILS'] = _perguntar('   Quem recebe (separe por vírgula)', config.get('AVISO_EMAILS', ''))
    config['SMTP_HOST'] = _perguntar('   Servidor de envio', config.get('SMTP_HOST', '') or 'smtp.gmail.com')
    config['SMTP_PORTA'] = _perguntar('   Porta', config.get('SMTP_PORTA', '') or '587')
    config['SMTP_USUARIO'] = _perguntar('   Usuário (o e-mail que envia)', config.get('SMTP_USUARIO', ''))
    senha_smtp = _perguntar('   Senha (no Gmail, a "senha de app")', config.get('SMTP_SENHA', ''), segredo=True)
    config['SMTP_SENHA'] = senha_smtp.replace(' ', '') if 'gmail' in config['SMTP_HOST'] else senha_smtp
    config['SMTP_REMETENTE'] = _perguntar('   Remetente', config.get('SMTP_REMETENTE', '') or config['SMTP_USUARIO'])
    config['SMTP_NOME'] = _perguntar('   Nome do remetente', config.get('SMTP_NOME', '') or 'AccessWifi Avisos')

    gravar_config(config)
    print('\n✓ configuração salva. Testando os dois canais...')
    testar()
    if input('\nFazer o primeiro backup agora? [S/n]: ').strip().lower() != 'n':
        return backup()
    return 0


def testar():
    titulo = '🧪 Teste dos avisos do AccessWifi'
    texto = (f'Se você recebeu isto, os avisos do servidor estão funcionando.\n\n'
             f'— AccessWifi · {fmt(agora())}')
    resultado = 0
    for canal, enviar in (('Telegram', lambda: enviar_telegram(f'{titulo}\n\n{texto}')),
                          ('e-mail', lambda: enviar_email(titulo, texto))):
        try:
            enviar()
            print(f'   ✓ {canal}: enviado')
        except Exception as erro:
            print(f'   ✗ {canal}: {erro}')
            resultado = 1
    return resultado


# ----------------------------------------------------------------------------- entrada

def main(argumentos):
    global SIMULAR
    SIMULAR = '--simular' in argumentos
    argumentos = [a for a in argumentos if a != '--simular']
    comando = argumentos[0] if argumentos else ''
    if comando == 'backup':
        return backup()
    if comando == 'vigiar':
        return vigiar()
    if comando == 'avisar' and len(argumentos) >= 2:
        avisar(argumentos[1], ' '.join(argumentos[2:]))
        return 0
    if comando == 'configurar':
        return configurar()
    if comando == 'testar':
        return testar()
    print(__doc__)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
