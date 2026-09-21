# Wi-Fi de visitantes instantâneo — o que o TI da loja precisa fazer

> Texto para enviar ao TI de cada unidade. Hoje a liberação do Wi-Fi leva ~1 segundo; com isto,
> cai para ~0,1–0,3 segundo.

---

## Por que hoje leva ~1 segundo

Quando o cliente toca em "Conectar", o nosso servidor precisa mandar um comando para o gateway da
loja liberar o celular. Como a loja não tem IP público, esse comando dá uma volta: vai para a nuvem
da Ubiquiti (unifi.ui.com), que repassa ao gateway por um túnel dela. **Essa volta custa de 0,5 a 1
segundo**, e não temos como encurtá-la.

## O que muda

O gateway da loja abre uma **VPN WireGuard diretamente com o nosso servidor**. O comando de liberação
passa a ir **direto**, sem a volta pela Ubiquiti, e leva **~0,1 segundo**.

```
Hoje:    nosso servidor ──► nuvem da Ubiquiti ──► gateway da loja      (~1 s)
Depois:  nosso servidor ────────── VPN ─────────► gateway da loja      (~0,1 s)
```

## O que NÃO muda (segurança)

- **Nenhuma porta é aberta na loja.** Quem inicia a conexão é o gateway, de dentro para fora. Por
  isso funciona mesmo sem IP público.
- **A internet da loja não passa pelo nosso servidor.** O UniFi só manda tráfego pela VPN se for
  criada uma "Traffic Route". **Não crie nenhuma.** A VPN serve só para o nosso servidor conversar
  com o gateway.
- **O nosso servidor enxerga só o gateway**, não os computadores nem as câmeras da rede interna.
- **Dá para desligar a qualquer momento**: é só pausar ou apagar a VPN no painel. O portal continua
  funcionando pelo caminho de hoje, só volta a ser um pouco mais lento.

---

## O que você vai fazer (~15 minutos)

> Nós configuramos o nosso lado primeiro e te enviamos os dados. Não faça nada antes de recebê-los.

### 1. Criar a VPN no gateway

No UniFi Network: **Settings → VPN → VPN Client → Create New → WireGuard**. Escolha a configuração
**manual** e preencha com o que vamos te enviar:

| Campo | Valor |
| --- | --- |
| Nome | `AccessWifi` |
| Servidor (endpoint) | `216.22.13.216`, porta `51820` |
| Chave pública do servidor | *(vamos enviar)* |
| Endereço do túnel desta loja | *(vamos enviar, algo como `10.66.0.2/32`)* |
| Chave privada | **deixe o próprio UniFi gerar** |

Aplique e aguarde o status passar de **Connecting** para **Connected**.

⚠️ **Não crie "Traffic Route" para essa VPN.**

### 2. Nos mandar a chave pública do gateway

Depois de aplicar, o painel mostra a **chave pública** que ele gerou. Mande para nós. Ela **não é
segredo**, pode ir por mensagem normal. A chave privada **nunca sai do gateway**, e é por isso que
pedimos para ele gerar.

### 3. Criar uma chave de API local

Em **UniFi Network → Settings → Control Plane → Integrations**, crie uma chave de API. É a mesma tela
em que você gerou as primeiras chaves, no início da implantação. Essa chave **é** secreta: mande num
arquivo `.txt` anexado, não no texto da mensagem.

### 4. Testamos juntos

Com o seu celular na rede de visitantes, conferimos a liberação. Se o status da VPN ficar em
**Connecting** e não passar disso, ou se o teste falhar, pode ser preciso liberar no firewall o
endereço do nosso servidor no túnel (`10.66.0.1`) para acessar o gateway na porta `443`. Se for o
caso, a gente te passa o passo exato.

---

## Resumo do que precisamos de você

1. A VPN WireGuard criada no gateway (passo 1), **sem Traffic Route**
2. A **chave pública** que o gateway gerou (passo 2)
3. Uma **chave de API local** num `.txt` (passo 3)
4. 10 minutos para o teste junto (passo 4)
