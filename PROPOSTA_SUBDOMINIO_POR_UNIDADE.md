# Proposta — Um subdomínio por unidade, tudo automático

> **Status (06/10/2026): alternativa.** A recomendada agora é a
> [PROPOSTA_UNIDADE_PELO_AP.md](PROPOSTA_UNIDADE_PELO_AP.md): mesmo endereço para todas as lojas,
> sem certificado novo. Esta fica como referência, caso um dia se queira um endereço por loja.

## 1. Resposta curta

**A UniFi aceita subdomínio.** Para ela, subdomínio é só um nome (FQDN), e a Itaituba já funciona
assim: `vps11702.panel.icontainer.online` é um subdomínio de `icontainer.online`.

Não existe "um domínio por unidade". **Um domínio só serve todas as lojas, de todas as empresas**:
`centro.wifi.dominio.com.br`, `itaituba.wifi.dominio.com.br`, `outra.wifi.dominio.com.br`…

---

## 2. O que foi testado

| # | Teste | Como | Resultado |
| --- | --- | --- | --- |
| 1 | Regra do campo na UniFi | Pacote oficial da **UniFi Network 10.6.106** (a mais recente); li a validação do formulário do Hotspot | **External Portal Server** só aceita IPv4. **Domain** aceita qualquer nome: `itaituba.vps11702.panel.icontainer.online` ✔, `itaituba.wifi.lojasregional.com.br` ✔. Com caminho (`/itaituba`), porta (`:8443`) ou `?unit=` ✘. Com `--` ou `_` ✘ |
| 2 | O que a UniFi da Itaituba manda hoje | Log do nginx da produção (só leitura) | 1.523 redirecionamentos chegaram pelo nome, sempre `/guest/s/default/?ap=…&id=…&t=…&url=…&ssid=…`. A UniFi já redireciona por nome |
| 3 | DNS do provedor | Nomes inventados nos DNS do Google, Cloudflare e Quad9 | `qualquer-coisa.vps11702…` responde `216.22.13.216`. O provedor tem curinga de DNS |
| 4 | Certificado curinga no nome do provedor | Certificados públicos dos clientes do provedor (Certspotter) | **Nenhum** dos 299 é curinga. Todos são um por nome: o provedor não oferece curinga |
| 5 | Nosso servidor hoje | Pedido com nome inventado (só leitura) | Porta 80: conexão fechada. Porta 443: cai no nosso site, mas com o certificado do nome principal (aviso de "não seguro"). **Falta configurar** o nome no nginx e o certificado |
| 6 | Nosso código, local | API + portal com 2 lojas, cada uma com o seu nome | Mesmo caminho `/guest/s/default/`, cada uma com o seu tema (Lojas Regional amarelo, Dôce dourado). Nome sem cadastro: "Unidade não encontrada". **Nenhuma linha de código mudou** |
| 7 | nginx, local | nginx 1.30 (mesma linha da produção), um bloco para `*.vps11702.panel.icontainer.online` e certificados de teste | http vira https no mesmo nome; certificado certo para cada nome (verificado); a API traz o tema certo; nome sem certificado é recusado (não cai em outra loja); o nome principal (Itaituba) continua no site de hoje |

---

## 3. Por que precisa de certificado

O portal abre em `https`. O celular do cliente só aceita a página se o servidor mostrar um
certificado **com aquele nome exato escrito nele**. É como um documento: o de
`vps11702.panel.icontainer.online` não vale para `centro.vps11702.panel.icontainer.online`. Para o
celular são nomes diferentes. Sem o certificado certo aparece "conexão não segura", e a janela de
login do celular costuma ficar em branco.

Há dois jeitos de cobrir vários nomes:

| Jeito | Como funciona | O que exige |
| --- | --- | --- |
| **Um certificado por nome** | Cada loja nova pede o seu | Só que o nome aponte para o servidor |
| **Um certificado curinga** (`*.wifi.dominio.com.br`) | Um só cobre todas as lojas, inclusive as que ainda não existem | Provar que **mandamos no DNS** do domínio (criar um registro TXT) |

No nome do provedor só existe o primeiro jeito. O DNS é da Integrator e ninguém lá tem curinga
(teste 4). Por isso a primeira versão desta proposta emitia um certificado por loja, e cada loja nova
teria uma espera (§5).

**Prazo de validade não dá para evitar.** Pela regra mundial dos navegadores, todo certificado HTTPS
vence:
- hoje, no máximo 200 dias;
- a partir de 2027, 100 dias;
- a partir de 2029, 47 dias.

O site de hoje já é assim: o certificado vence em 19/12/2026 e o painel do provedor renova sozinho.
O que se controla é que **a renovação seja automática e vigiada**, sem ninguém lembrar de nada.

---

## 4. A solução recomendada: domínio próprio com certificado curinga

```
*.wifi.dominio.com.br  ─► DNS na Cloudflare (grátis) ─► 216.22.13.216 ─► nginx (um certificado curinga)
                                                                         └─ o nome diz a unidade (já existe)
```

| | Nome do provedor (um certificado por loja) | **Domínio próprio (curinga)** |
| --- | --- | --- |
| Loja nova | Emite um certificado no cadastro: normalmente 1 minuto; em semana cheia, horas | **Funciona na hora.** Nada é emitido |
| Limite do Let's Encrypt | Dividido com todos os clientes do provedor | Só nosso, e a renovação não entra no limite |
| Renovação | Automática, uma por loja | Automática, **uma só para todas** |
| Se trocar de provedor | Cada loja troca o nome na UniFi | Muda só o IP no DNS; as lojas não mexem em nada |
| Custo | Zero | O domínio (cerca de R$ 40/ano no registro.br); Cloudflare grátis |

**O que fica automático:**

- **DNS.** Um registro curinga `*.wifi` aponta todas as lojas para o servidor, inclusive as futuras.
- **Certificado.** Uma rotina em C# no `AccessWifi.Ops` emite o curinga pela API da Cloudflare e
  renova 30 dias antes de vencer. Se falhar, tenta todo dia e avisa pelo Telegram e pelo e-mail, com 30 dias de folga.
- **nginx.** Um arquivo nosso atende `*.wifi.dominio.com.br` com esse certificado: mesmo portal,
  mesma API.
- **Loja nova.** Só se cadastra o endereço no painel (Unidades). O TI da loja coloca na UniFi o IP
  `216.22.13.216`, o Domain com o nome e a liberação prévia (Pre-Authorization Access).

**Os únicos prazos que sobram, e como ficam automáticos:**

| Prazo | Como fica |
| --- | --- |
| Certificado curinga (90 dias) | Renovação automática pela rotina, vigiada pelos avisos |
| Registro do domínio | Registrar por vários anos (o registro.br permite até 10) e ligar a renovação automática |

**A Itaituba** pode continuar no nome de hoje, sem mexer na loja. Ou migra quando for conveniente,
com os dois nomes valendo durante a troca.

---

## 5. Alternativas descartadas

- **Um certificado por loja no nome do provedor.** Funciona e é grátis, mas loja nova espera a
  emissão e divide o limite semanal com os outros clientes do provedor (de 20 a 49 emissões por
  semana nas últimas semanas). Não atende "nada com espera".
- **Caminho no endereço (`…/itaituba`) ou `?unit=`.** A UniFi recusa (teste 1).
- **Endereço único, descobrindo a loja pelo MAC do ponto de acesso.** Exige manter a lista de APs de
  cada loja, e um AP novo derruba o portal até sincronizar.
- **Cloudflare como proxy (nuvem laranja).** O visitante teria que alcançar a Cloudflare antes do
  login, o que libera meia internet antes do cadastro. Também acrescenta um salto no caminho.
  O DNS fica na Cloudflare, mas sem proxy.

---

## 6. Decisões

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Endereço das lojas | Domínio próprio, `<slug>.wifi.dominio.com.br` | Nome do provedor, com um certificado por loja |
| **D2** | DNS | Cloudflare (grátis), só DNS, sem proxy | — |
| **D3** | Certificado | Curinga do Let's Encrypt, emitido e renovado pela rotina em C# | — |
| **D4** | Itaituba | Fica no nome de hoje; migra depois, se quiser | Migrar junto |
| **D5** | Validação do endereço no painel | A mesma regra da UniFi (sem `--`, `_`, caminho ou porta) | Manter a atual |
| **D6** | Avisos | O vigia confere o certificado curinga e o endereço de cada loja | Só o endereço principal |
| **D7** | Teste real antes de valer | Na produção, só com `teste.wifi.dominio.com.br`, sem tocar na Itaituba | Ir direto |

## 7. O que depende de vocês

Comprar e criar contas eu não faço, então estes passos são seus:

1. Escolher e registrar o domínio no registro.br (por vários anos, com renovação automática).
2. Criar a conta grátis na Cloudflare, adicionar o domínio e trocar os servidores DNS no registro.br.
3. Criar na Cloudflare uma chave de API que só edita o DNS desse domínio, e digitá-la direto no
   servidor. Ela não deve passar pela conversa.

O resto eu faço: rotina, nginx, painel, avisos e o passo a passo do TI. Tudo local primeiro. Depois,
o teste do D7 na produção, que precisa de um ok à parte porque mexe no nginx da produção.
