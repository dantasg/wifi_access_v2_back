# Proposta — Visitas e tempo na loja pelos avisos da UniFi

> **Status (2026-10-26): pronta localmente, no ramo `visitas-unifi`** (separado do WhatsApp, para poder publicar
> sozinha; ver §6 e §7). O usuário pediu para implementar já. As decisões abaixo são as que tomei para seguir
> (**D1 a D10**); qualquer uma pode ser trocada. **Nada publicado.**

## 1. Entendimento do pedido

- O cliente (Lojas Regional) quer que quem passou pelo portal **só passe de novo no maior prazo possível**.
- Hoje a visita só é registrada quando o cliente passa pelo portal (`AuthorizeController.cs`, `RegisterVisitAsync`).
  Se o prazo do portal aumentar sem mais nada, as voltas somem do sistema: Dashboard com menos visitas e a
  campanha "Sentimos sua falta" mandando mensagem para quem vem sempre.
- A solução é registrar a visita pelo **aviso da UniFi** (Alarm Manager → webhook), que chega toda vez que um
  aparelho conecta, mesmo sem passar pelo portal.
- O usuário também quer o **tempo médio que o cliente fica na loja**, que precisa do aviso de desconexão.

## 2. O que a análise dos avisos mostrou (Itaituba, 22 a 26/10/2026)

- Cada aviso traz o endereço do aparelho (`UNIFIclientMac`), a rede (`UNIFIwifiName`), o ponto de acesso, o nome
  do aparelho e a hora (`UNIFIutcTime`).
- O endereço bate com o que o portal grava no cadastro (`Leads.Mac`): 11 de 15 aparelhos da rede de clientes no
  primeiro dia. Clientes novos geram aviso segundos antes do cadastro.
- Chegam também os avisos da rede dos funcionários e dos cabos, que precisam ser ignorados.
- O mesmo aparelho costuma gerar 2 avisos em menos de 20 segundos.
- O alarme não deixa escolher a rede, só os aparelhos; com todos marcados, aparelhos novos também entram.

## 3. Decisões que tomei para seguir (confirmar ou trocar)

| # | Assunto | O que fiz | Alternativa |
|---|---|---|---|
| D1 | Endereço do aviso | **Um endereço secreto por unidade**, gerado no painel (Unidades → editar → "Visitas pela UniFi"), mostrado **uma vez só** e guardado só como "impressão" (hash), como os outros segredos. O endereço atual da Itaituba continua valendo, sem o TI mexer | Um endereço por empresa, com a loja descoberta pelo ponto de acesso |
| D2 | Quais avisos contam | **Só os da rede de clientes**: a rede do portal da empresa (Configurações → SSID, ex.: "PIX REGIONAL"). Cabo e outras redes são ignorados | Um campo de rede por unidade |
| D3 | O que é uma visita | **1 visita por cliente, por unidade, por dia** (fuso da empresa), venha do portal ou da UniFi. Se o cliente passar pelo portal e também gerar aviso no mesmo dia, conta uma vez só. Isso vale também para o portal sozinho: quem se cadastra duas vezes no mesmo dia conta uma | Continuar contando cada passagem pelo portal |
| D4 | Quem a UniFi reconhece | **Só aparelhos já cadastrados**: o endereço do aparelho é procurado nos cadastros das lojas da mesma empresa (a rede tem o mesmo nome nas lojas, então o celular usa o mesmo endereço). Aparelho desconhecido não vira visita; vira visita quando a pessoa se cadastrar no portal | Contar também aparelhos sem cadastro como "conexões" |
| D5 | O que a visita pela UniFi atualiza | **Tudo o que a visita do portal atualiza**: última visita, número de visitas, unidade do cliente e a "última conexão" do cadastro daquela loja. Assim as campanhas ("Sentimos sua falta", "Cliente frequente") enxergam as voltas | Só o Dashboard |
| D6 | Tempo na loja | **Do primeiro ao último sinal do aparelho no dia**, juntando as quedas curtas: conexões com intervalo de até **30 minutos** são a mesma estadia (o celular desliga o Wi-Fi para economizar bateria). Só conta o dia em que chegou pelo menos um aviso de **desconexão** | Medir só entre o 1º "conectou" e o último "desconectou" |
| D7 | Média do tempo | **Cada cliente conta uma vez, com o tempo da última visita dele no período** (a cada volta o tempo dele é atualizado), e a média é entre os clientes: "Em Itaituba os clientes ficam em média 10 minutos". Pedido do usuário em 26/10. **Estadias acima de 4 horas ficam fora** (quase sempre é funcionário ou aparelho da loja). No Dashboard, "Tempo médio na loja", com a comparação do período anterior, e por unidade (a última visita naquela unidade) | Média de todas as visitas (quem vem mais pesa mais) |
| D8 | Aparelhos da loja | **Lista de aparelhos ignorados por unidade** (a TV, celulares de funcionários): não viram visita nem entram no tempo. Na tela da unidade aparecem os aparelhos vistos nos últimos 7 dias, para marcar com um clique. Só o super admin, como o resto da configuração da UniFi | Detectar sozinho pelo tempo e frequência |
| D9 | O que fica guardado | **Os sinais do aparelho por dia** (tabela `WifiPresences`) ficam **90 dias**; o tempo da estadia fica na visita, sem prazo, como as visitas de hoje. A captura temporária continua gravando tudo até analisarmos o aviso de desconexão | Guardar os sinais por mais tempo |
| D10 | Prazo do portal | **Sem mudança no código**: o "Tempo de acesso" já aceita até 1 ano. Depois de publicar, aumentar primeiro numa loja (ex.: 90 dias), conferir na UniFi, e só então nas outras | — |

## 4. Como fica

- **Rota:** `POST|GET /webhooks/unifi/{código}` (a mesma de hoje). O código acha a unidade. O código antigo da
  captura, com `?unit=itaituba`, continua aceito enquanto a Itaituba não trocar.
- **A cada aviso de conexão na rede de clientes:**
  1. aparelho ignorado? para aí;
  2. atualiza os sinais do aparelho no dia (primeiro sinal, último sinal, quedas, conexões e desconexões);
  3. procura o cadastro pelo endereço do aparelho; achou o cliente e ainda não há visita dele hoje nesta
     unidade: registra a visita (origem "UniFi");
  4. atualiza o tempo da estadia na visita do dia.
- **No portal:** a visita do dia não duplica, e o cliente que acabou de se cadastrar ganha os sinais que chegaram
  antes do cadastro.
- **Dashboard:** "Tempo médio na loja" nos números do período e na tabela por unidade.
- **Unidades (super admin):** bloco "Visitas pela UniFi" com o endereço, a última vez que chegou aviso, quantos
  avisos hoje, os aparelhos vistos e os ignorados.

## 5. Limites conhecidos

- Só conta quem conecta no Wi-Fi.
- Celular que troca de endereço com frequência aparece como aparelho novo até passar pelo portal.
- A UniFi demora alguns minutos para perceber a saída: o tempo fica um pouco maior que o real, igual para todos.
- O conteúdo do aviso de desconexão ainda não foi visto. O código aceita o aviso mesmo sem o nome da rede, quando
  o aparelho já foi visto na rede de clientes no dia. Depois da captura, conferir e ajustar se preciso.

## 6. O que foi feito (26/10/2026, local)

- **Banco** (migration `20261026132409_UnifiVisits`):
  - `Units`: `UnifiWebhookHash`, `UnifiWebhookCreatedAt`, `UnifiWebhookLastAt`;
  - `Visits`: `Source` (as visitas antigas ficam "Portal") e `StaySeconds`;
  - tabelas novas `WifiPresences` (sinais por aparelho, unidade e dia) e `UnitIgnoredDevices` (aparelhos da loja).
- **Avisos:** `UnifiWebhookController` (a mesma rota da captura) acha a unidade pelo código e chama
  `Models/Visits/UnifiVisitTracker.cs`. O aviso é lido por `UnifiNotice.cs`. A captura temporária continua gravando
  tudo. O limite subiu de 120 para 300 avisos por minuto por IP (conexões e desconexões da loja inteira).
- **Portal** (`AuthorizeController.RegisterVisitAsync`): uma visita por cliente, unidade e dia; os sinais do
  aparelho que chegaram antes do cadastro passam a ser do cliente.
- **Painel (super admin):** `UnitUnifiVisitsController`, em `admin/units/{id}/unifi-visits`: situação, gerar ou
  apagar o endereço, aparelhos dos últimos dias, marcar e desmarcar aparelho da loja. Na tela: Unidades → Editar →
  bloco "Visitas pela UniFi".
- **Dashboard:** "Tempo médio na loja" (cartão e coluna por unidade), com cada cliente uma vez e a última visita valendo. Só aparece onde a UniFi já mediu alguém. "Voltaram" e "Taxa de retorno" contam também as voltas vistas pela UniFi, porque elas viram visita; isso está coberto por teste.
- **Retenção:** sinais apagados depois de 90 dias (`Retention:WifiPresenceDays`).
- **Testes:**
  - 13 testes novos (`UnifiVisitsTests` e retenção) e 1 ajustado (`PortalVisitsTests`, por causa da D3); 433 passam;
  - ponta a ponta local, numa cópia de teste do banco sem chaves da UniFi, com os 15 pontos certos: endereço mostrado
    uma vez, avisos simulados, rede dos funcionários e cabo de fora, visita só pela UniFi, 25 minutos com a queda de 7
    juntada, TV marcada como da loja, código antigo com `?unit=` aceito e o Dashboard com o tempo médio.

## 7. Para publicar (só com o ok do usuário)

1. **Back** (`./deploy/publicar-producao.sh api`, dentro de `AccessWifi_V2_BACK_visitas`, depois do commit): aplica a
   migration. **A Itaituba começa a gerar visitas pelos avisos na hora**, porque o endereço da captura com
   `?unit=itaituba` continua valendo.
2. **Front:** o alvo `portal` do script procura o front em `../AccessWifi_V2_FRONT`, que tem o WhatsApp sem commit.
   Para publicar o front deste ramo, é preciso apontar o script para `AccessWifi_V2_FRONT_visitas`. Combinar como
   fazer antes.
3. **TI da Itaituba:** criar o segundo alarme, de **desconexão**, com o mesmo endereço. Sem ele não há tempo na loja.
4. Conferir em produção o primeiro aviso de desconexão. Se ele não trouxer o endereço do aparelho, ajustar
   `UnifiNotice.cs`.
5. Depois de alguns dias: aumentar o "Tempo de acesso" do portal da Regional (D10), apagar a captura temporária
   (tabela e gravação) e gerar o endereço próprio da Itaituba no painel, tirando o código da captura do env.

