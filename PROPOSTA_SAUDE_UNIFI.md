# Proposta — Saúde da UniFi por loja (console fora do ar registrado no painel)

> **Status (2026-10-30): aguardando o ok nas decisões (§6). Nada implementado, nada publicado.**

## 1. Entendimento do pedido

- Hoje o console da Itaituba saiu da nuvem da UniFi e os clientes ficaram sem liberação. Só soubemos o que era porque
  eu fui olhar na mão.
- O pedido: quando o sistema detectar certos erros, ele mesmo **pergunta à UniFi daquela loja o que está acontecendo**
  e **deixa isso registrado**, com o aviso de que o console está fora do ar.
- Entendi "portal" como o **painel** (onde vocês administram). A tela do visitante não muda (D1).

## 2. O que existe hoje e o que falta

| O que existe | Onde | O que falta |
|---|---|---|
| A liberação recusada é pega e vai para o log | `AuthorizeController.cs:106-112` | Não pergunta nada à UniFi e não registra no banco |
| O vigia manda Telegram e e-mail na primeira recusa | `UnifiMonitor.cs:168-181` | Só repete a mensagem do log. Qualquer liberação boa, de qualquer loja, encerra o incidente (limite descrito em `UnifiMonitor.cs:10-11`) |
| A mensagem de erro | `UnifiCloudClient.cs:431` | Hoje disse "recusou procurar o aparelho na rede (HTTP 404)", sem dizer que o console estava fora |
| Sem plano B quando o console está fora (é o que diz o comentário) | `UnifiCloudClient.cs:124-125` | O `404 device_offline` não é reconhecido: tenta a API oficial à toa antes de falhar |
| A leitura dos aparelhos a cada 5 min, com erro no painel | `UnitDeviceSync.cs:197-202`, `UnitsPanel.tsx:449` | **Não detecta console fora**: às 10:01:49 de hoje leu a Itaituba normalmente, sem erro, com o console desconectado |

**Lojas hoje em produção:** 3 no modo nuvem (`itaituba`, `lojao-coronel-leal`, `sao-bento-matriz`) e 3 no modo local
(`guara-castanhal`, `nacional-adm`, `personale-capanema`).

## 3. O que o caso de hoje ensinou (Itaituba, 30/10/2026)

| Hora | O que aconteceu |
|---|---|
| 09:06 | Última liberação boa |
| 09:52:18 | **Primeira recusa** (6 no total, a última às 09:54:46) |
| 09:56:05 | A nuvem registra a mudança para `disconnected`, **quase 4 minutos depois da primeira recusa** |
| 09:56:20 | O console ainda mandava avisos do Alarm Manager para o nosso servidor |
| 10:02:39 | Nuvem: `connected` |
| 10:03:58 | Nuvem: `disconnected` de novo, **depois de só 1 min 19 s** |
| 10:04:14 | Nuvem: `connected` outra vez. Às 10:04:34 a ligação até a loja respondeu 200 |
| 10:04:38 | Nuvem: `disconnected` de novo, **depois de só 24 s** |
| 10:04:54 | Chegou aviso do Alarm Manager com o console desconectado: a loja tem internet, e a ligação com a UniFi é que oscila |

Disso saem quatro regras do desenho:

1. **A primeira pergunta pode dar "está tudo bem".** A nuvem demorou quase 4 minutos para marcar a queda. Por isso o
   sistema continua perguntando depois do erro, e guarda as duas horas: a da nossa primeira recusa e a da nuvem.
2. **"Conectado" sozinho não encerra.** O console voltou e caiu duas vezes em menos de 2 minutos, e numa delas a ligação
   até a loja chegou a responder. É preciso ficar estável (D5).
3. **O estado sozinho não basta.** Além dele, o sistema testa a ligação até a loja, sem liberar ninguém.
4. **O último aviso recebido da loja ajuda a separar as causas.** Se os avisos continuam chegando, a loja ainda tem
   internet e o problema é a ligação com a UniFi. A ausência de aviso não prova nada, porque ele só chega quando um
   aparelho conecta. O painel mostra a hora sem tirar conclusão (`Unit.UnifiWebhookLastAt`).

## 4. Como fica

```
Erro detectado (liberação recusada ou leitura falhou)  ──┐
                                                         ├─► fila ─► pergunta à UniFi da loja ─► registra no banco ─► painel
Ronda a cada 5 min, em cada console cadastrado (D2)    ──┘                    ▲                       │
                                                                              └── a cada 1 min ───────┘
                                                                          enquanto houver ocorrência aberta
```

**O portal não fica mais lento.** Na recusa, a liberação só deixa um recado na fila, sem esperar a resposta. A
pergunta à UniFi roda em segundo plano, fora do caminho do visitante. O redirecionamento não muda.

**A pergunta tem duas leituras, só leitura, no console da própria unidade e com a chave da própria unidade:**
`GET /v1/hosts/{console}` (estado e hora da mudança) e `GET /v1/connector/consoles/{console}/proxy/network/integration/v1/info`
(a ligação até a loja). As duas foram usadas hoje na Itaituba e responderam como esperado.

| O que a UniFi responde | Situação registrada | Texto no painel | Quem resolve |
|---|---|---|---|
| Estado `disconnected` ou ligação `device_offline` | **Console fora do ar** | "Console desconectado da nuvem da UniFi desde 09:56. Último aviso da loja: 09:56." | TI da loja: energia, internet, reiniciar o console, Acesso Remoto |
| Estado `connected`, ligação falhando | **Sistema de rede sem responder** | "O console está ligado, mas o Network não responde." | TI da loja |
| 401 | **Chave recusada** | "Chave de API inválida ou revogada." | Nós |
| 403, ou 404 sem `device_offline` | **Console fora do alcance da chave** | "A chave não alcança este console, ou o console está errado." | Nós |
| 429 | **Limite de chamadas** | Espera o tempo que a UniFi pedir | Sozinho |
| Sem resposta, ou 5xx da `api.ui.com` | **Nuvem da UniFi instável** | "A nuvem da UniFi não respondeu." Não culpa a loja | Ubiquiti |
| Recusa `not-guest` na liberação | **Rede sem Hotspot** | "A rede da loja não está como rede de visitantes." | TI da loja |
| Tudo normal e liberação boa depois | **Falha momentânea** | Fica no histórico, já encerrada | Ninguém |

**No banco:** uma tabela nova, `UnifiIncidents`, com unidade, situação, primeira recusa, hora informada pela nuvem,
última conferência, encerramento, número de liberações recusadas e o motivo técnico. **Sem nome, telefone nem MAC de
cliente.** Sem chave. Nenhuma tabela atual muda.

**No painel:**
- **Unidades:** coluna "UniFi" com a situação (verde, vermelho ou cinza) e o "desde quando".
- **Tela nova "Saúde da UniFi":** ocorrências abertas em cima e o histórico embaixo (loja, situação, início, fim,
  duração, recusas). Segue a empresa do seletor do topo, como as outras telas.
- **Tarja vermelha no Dashboard** enquanto houver console fora, com a loja e o "desde quando".

## 5. Limites conhecidos

- **O motivo da queda continua sem resposta automática.** A UniFi diz que o console saiu, não se foi energia, internet
  ou outra coisa. Hoje, por exemplo, sabemos o que aconteceu e não sabemos por quê.
- **A documentação oficial não lista os valores do estado** e avisa que esse bloco pode variar com a versão do sistema
  do console. Vi em campo `connected` e `disconnected`. Valor desconhecido vira "não confirmado", e quem decide é o
  teste da ligação.
- **Modo local:** a pergunta é só o teste de login que já existe (`UnifiLocalClient.cs:75-84`). Sem hora da queda e sem
  estado da nuvem (D8).
- **A fila fica na memória.** Se a API reiniciar, o recado se perde, mas a ronda pega de novo em até 5 min. As
  ocorrências abertas ficam no banco e não se perdem.
- **Custo de chamadas:** a ronda são 3 consoles a cada 5 min. O limite oficial é de 10.000 chamadas por minuto
  ([Site Manager API](https://developer.ui.com/site-manager/v1.0.0/gettingstarted)).

## 6. Decisões para aprovação

| # | Assunto | Recomendação | Alternativa |
|---|---|---|---|
| D1 | Onde aparece | **Só no painel.** A tela do visitante continua igual | Mostrar também um aviso ao visitante |
| D2 | Quando perguntar à UniFi | **Nos erros e numa ronda a cada 5 min** nos consoles cadastrados: o painel fica sabendo antes do primeiro cliente reclamar. O que vier só da ronda aparece como "sem cliente afetado" | Só nos erros, como você pediu ao pé da letra. Mais simples, mas a queda só aparece quando um cliente tenta |
| D3 | Quais erros disparam | **Toda recusa da UniFi na liberação e toda leitura de aparelhos que falhar**, menos "aparelho não encontrado" e "MAC inválido", que são do celular do cliente e não da loja | Lista menor, só as recusas |
| D4 | O que a automação pode fazer | **Só ler, só o console da própria unidade, com a chave da própria unidade.** Nunca lista os consoles da conta e nunca manda comando | Tentar reiniciar o console sozinha (não recomendo: a chave abre a conta inteira) |
| D5 | Quando encerrar | **Estado `connected`, ligação respondendo e 10 minutos seguidos assim**, ou uma liberação boa naquela loja. Se cair de novo nesse prazo, continua a mesma ocorrência | Encerrar na primeira leitura boa (hoje teria encerrado às 10:03 e reaberto em seguida) |
| D6 | Telegram e e-mail | **Continuam, agora com o diagnóstico:** um aviso ao abrir ("Console da Itaituba fora do ar") e um ao encerrar, com a duração | Só registrar no painel |
| D7 | Quem vê | **Super admin vê tudo, com o motivo técnico. Admin da empresa vê as lojas dele, em texto simples:** é o TI dele que resolve na maioria dos casos | Só super admin |
| D8 | Lojas no modo local (3 de 6) | **Registrar também:** na recusa, roda o teste de login e repete a cada 5 min até voltar. Sem ronda, para não fazer login na controladora do cliente o dia inteiro | Só nuvem, por enquanto |
| D9 | Reconhecer o console fora na liberação | **Sim:** ao ver `device_offline`, para de tentar e grava no log "console fora do ar". O visitante recebe a mesma resposta de hoje, só mais rápido | Não mexer na liberação |
| D10 | Quanto tempo guardar | **12 meses**, como os contatos do site | 90 dias |
| D11 | Avisar o e-mail da unidade | **Não, por enquanto:** primeiro ver em uso se os avisos são confiáveis | Avisar o gerente da loja também |
| D12 | Como publicar | **Separado do WhatsApp**, em cópia própria a partir do `main`, com teste local contra uma nuvem falsa. **Só publico com a sua ordem** | Junto com o WhatsApp |

## 7. Implementação

- **API (`src/AccessWifi.Api/Features/Units/`):** fila, serviço que pergunta à UniFi e serviço em segundo plano, no
  mesmo padrão do `UnitDeviceSyncWorker`. Em C#, como o resto.
- **`UnifiCloudClient`:** método só de leitura para o estado do console, e o `device_offline` reconhecido (D9).
- **`AuthorizeController` e `UnitDeviceSync`:** uma linha cada, para deixar o recado na fila.
- **`UnifiMonitor`:** avisos de abertura e encerramento, lendo as linhas novas do log (D6).
- **`LeadRetentionService`:** apaga o histórico depois do prazo (D10).
- **Rota:** `admin/units/unifi-incidents`, que já passa pela regra atual do nginx, sem mexer nele.
- **Painel:** tela nova, coluna em Unidades e tarja no Dashboard.
- **Testes:** cada linha da tabela do §4, o caso de hoje (demora da nuvem e queda depois da volta), a garantia de que a
  recusa não espera a pergunta, e que uma unidade nunca vê a ocorrência de outra empresa.
- **Migration:** só a tabela nova.

**Prazo:** cerca de 1 dia de trabalho até estar pronto e testado localmente, pelo ritmo das últimas features. A
publicação depende da sua ordem.

## 8. Próximo passo

Responder "ok" (segue tudo como recomendado) ou dizer quais decisões trocar. Aí implemento e testo localmente, sem
publicar.
