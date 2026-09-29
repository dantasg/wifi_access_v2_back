# Proposta — Módulo de campanhas

> **Status (2026-09-29): fases 1 a 3 implementadas e testadas localmente — nada publicado.** O envio
> real (fase 4) ainda não existe: tudo roda em simulação.
>
> **Testado localmente** com API e worker de verdade, num banco descartável com 20 mil clientes
> sintéticos:
>
> | Teste | Resultado |
> | --- | --- |
> | Mesmo telefone em 2 lojas pelo `/authorize` | 1 cliente, 2 unidades, nascimento como data |
> | Disparo no horário | As execuções nasceram às 12:52, conforme o horário cadastrado |
> | Seleção de 20.001 clientes | ~3,7 s, com as mensagens montadas |
> | Limite de 1 por dia (D11) | 4.345 ignorados = exatamente os que já tinham recebido de outra campanha no dia |
> | Pausar / retomar | Parou por 20 s; retomou dos pendentes, sem repetir |
> | Queda do serviço no meio | Congelou; ao voltar, continuou sozinho, sem duplicar ninguém |
> | Cancelar | 15.456 pendentes viraram cancelados em segundos |
> | Versões | v2 com "Horário: 12:52 → 10:30; Mensagem alterada", o usuário e a hora |
> | Relatório CSV | Uma linha por destinatário, abre certo no Excel |
>
> **Segunda rodada (só banco local):**
>
> | Teste | Resultado |
> | --- | --- |
> | Migration sobre cópia do banco local com dados + SQL idempotente de produção rodado 2× | Sem erro; empresas com fuso de Belém; cadastros intactos |
> | Carga inicial dos clientes | Data impossível/futura vazias; telefone curto fora; visitas e datas certas |
> | As 5 campanhas de sistema no Postgres | Alcance igual ao SQL escrito à mão, nas cinco |
> | 6 campanhas juntas sobre 20 mil clientes | 0 clientes com 2 mensagens no dia; 17.890 substituições; totais batem |
> | "Não repetir" (ausência, marco, N dias) | Números exatamente os esperados |
> | Semanal, mensal (dia 31), anual (29/02), disparo perdido | Próximos disparos certos; "Perdida" registrada |
> | Retenção e cascata | Sumidos há 25 meses saíram (com as mensagens); listas de 13 meses saíram, resumo ficou |
> | Admin da empresa, tema escuro, celular | Permissões certas; nada ilegível no escuro; nada estoura a largura |
>
> **Ajustes vindos dos testes:**
> - Uma execução **por campanha por dia**: mudar o horário para mais tarde no mesmo dia não dispara de novo.
> - O ciclo diário do worker agora roda o relatório e a retenção **separados**: um e-mail que falha não
>   impede mais o expurgo da LGPD.
>
>
> **Para publicar (quando você pedir):** `./deploy/publicar-producao.sh tudo`.
> - A migration `AddCampaigns` cria as tabelas e põe o fuso de Belém nas empresas.
> - Na subida, a API monta os clientes a partir dos cadastros existentes.
> - O `deploy/nginx/90-accesswifi-api.conf` já libera `/admin/campaigns`.
> - **A retenção nova (24 meses sem voltar, D4) passa a valer a partir daí.**
>
> Nenhuma campanha fica liberada para nenhuma empresa até o super admin marcar no cadastro dela.
>
> | # | Decisão final |
> | --- | --- |
> | D1 | Um destinatário por **número de telefone em cada execução**. Uma campanha diária para 500 pessoas gera **uma execução por dia**, não uma por pessoa. |
> | D2 | Tabela de Clientes **sem** o "não quero receber", e **invisível para o cliente**: nada muda na tela do Wi-Fi. |
> | D3 | Como recomendado (29/02 → 28/02), **mais** um validador no portal: a data de nascimento precisa existir e não pode ser no futuro. |
> | D4 | Apaga automaticamente **só quem ficar 24 meses sem voltar**. |
> | D5 | **Não entra** (sem "não quero receber" por enquanto). |
> | D6 | **Sem planos:** o super admin liga/desliga cada campanha por empresa. |
> | D10 | Dispara **sempre no horário cadastrado**, respeitando a repetição da campanha. |
> | D16 | Como recomendado, e reforçando: a campanha filtrada **roda o filtro a cada execução**. Quem se cadastrou hoje já pode entrar amanhã. |
> | D7–D9, D11–D15 | Como recomendado. |

## 1. Entendimento do pedido

Um módulo para a empresa mandar mensagens aos clientes que se cadastraram no Wi-Fi.

| Item | O que você pediu |
| --- | --- |
| **Campanhas de sistema** | Nossas, prontas. Cada empresa só tem as que liberarmos, conforme o plano e o acordo. A empresa escreve a mensagem e escolhe o horário. Começam com **Aniversário** e **Aniversário de cadastro**, e você pediu sugestões de outras. |
| **Campanhas filtradas** | A empresa escolhe quem recebe (com ou sem filtro) e quando: uma data, ou repetindo todo dia, toda semana, todo mês ou todo ano. |
| **Canal** | WhatsApp ou Instagram. O **envio fica para depois**. |
| **Relatórios** | De execução de todas as campanhas ativas. |
| **Histórico** | Toda edição vira uma versão, com quem alterou e quando. |
| **Execução** | Percorrer os clientes selecionados **rápido**, **acompanhar** o andamento, **pausar e retomar de onde parou**. |

Esta proposta cobre tudo isso **até o ponto do envio**: a campanha roda de verdade (escolhe os clientes,
monta a mensagem de cada um e registra), mas ainda não manda nada. Quando o canal existir, é ligar.

---

## 2. O que já temos e o que falta

O cadastro do visitante (`Lead`) é a matéria-prima das campanhas.

| O que a campanha precisa | Hoje | Onde |
| --- | --- | --- |
| Data do **primeiro cadastro** | ✅ Existe e não muda | `Models/DataBase/Lead.cs:15` |
| Data da **última visita** | ✅ Atualizada a cada conexão | `Lead.cs:18`, `AuthorizeController.cs:81` |
| **Data de nascimento** como data | ⚠️ É **texto** (`"dd/mm/aaaa"`) e nada confere se a data existe (passa `31/02` e data no futuro) | `Lead.cs:22` |
| **Quantas vezes** o cliente veio | ❌ Não existe | — |
| **Um cliente = uma pessoa** | ⚠️ O cadastro é por **aparelho em cada loja**. A mesma pessoa em 2 lojas, ou com 2 celulares, vira 2 cadastros e receberia 2 mensagens | `AppDbContext.cs:102` |
| **"Não quero receber"** (descadastro) | ❌ Não existe | — |
| Agendador que roda a **qualquer hora** | ❌ O serviço de fundo acorda **uma vez por dia, às 8h** | `AccessWifiService/SrvWifiService.cs:10` |

### ⚠️ Conflito com a retenção da LGPD

Hoje o cliente é **apagado 12 meses depois do primeiro cadastro**
(`AccessWifiService/LeadRetentionService.cs:24` e `:38`). Com isso:

- o **Aniversário de cadastro** nunca encontraria ninguém: o cliente é apagado justamente no dia em que
  completa 1 ano;
- o **Aniversário** só alcançaria quem se cadastrou há menos de um ano.

A **D4** resolve isso.

Na produção hoje: **14 clientes**, 14 telefones diferentes, todos com nascimento no formato certo.
O primeiro cadastro é de 21/09. O servidor está no fuso de Brasília (-03).

---

## 3. Como o módulo se organiza

```
Campanha ─────► Versões (cada salvamento; quem e quando)
   │
   └──► Execuções (uma por disparo: "aniversariantes de 29/09")
            │
            └──► Destinatários (um por cliente, com o seu status)
```

| Peça | O que é |
| --- | --- |
| **Campanha** | Tipo (de sistema ou filtrada), canal, mensagem, agenda, filtros, ativa ou não. |
| **Versão** | Foto completa da campanha a cada salvamento, com o usuário e a hora. Não se altera nunca. |
| **Execução** | Um disparo: quando começou, qual versão usou, quantos clientes, andamento, quem pausou. |
| **Destinatário** | Um cliente dentro de uma execução: telefone, mensagem já montada, status. |

---

## 4. Campanhas de sistema

Todas seguem o mesmo molde: **todo dia, no horário que a empresa escolheu**, o sistema procura os
clientes que se encaixam **naquele dia** e cria a execução.

| Campanha | Quem recebe, no dia | Dado que usa | Situação |
| --- | --- | --- | --- |
| **Aniversário** | Quem faz aniversário hoje | Nascimento | Pedida por você |
| **Aniversário de cadastro** | Quem completa 1, 2, 3… anos de cadastro hoje | 1º cadastro | Pedida por você |
| **Boas-vindas** ⭐ | Quem se cadastrou pela primeira vez ontem ("obrigado pela visita") | 1º cadastro | Sugestão |
| **Sentimos sua falta** ⭐ | Quem não volta há X dias (a empresa escolhe: 30, 60, 90…). Recebe **uma vez por ausência**; se voltar e sumir de novo, recebe de novo | Última visita | Sugestão |
| **Cliente frequente** ⭐ | Quem atingiu a 5ª, 10ª… visita (a empresa escolhe o marco), para um mimo ou cupom | Nº de visitas | Sugestão |
| **Pós-visita** | Quem veio hoje, pedindo avaliação no Google | Visitas | Sugestão para uma fase seguinte |

**Habilitação:** o super admin liga, para cada empresa, quais campanhas de sistema ela pode usar
(**D6**). A empresa só configura as que estão ligadas: mensagem, horário e ativar/pausar. Ela não
cria outra do mesmo tipo.

---

## 5. Campanhas filtradas

**Quem recebe:** todos os clientes da empresa, ou só os que passarem nos filtros, combinados com "e":

| Filtro | Exemplo |
| --- | --- |
| Unidade(s) | Só Itaituba |
| Idade | De 18 a 30 anos |
| Mês de aniversário | Aniversariantes de dezembro |
| Data do 1º cadastro | Cadastrados entre 01/09 e 30/09 |
| Última visita | Vieram nos últimos 7 dias, ou não vêm há mais de 60 |
| Número de visitas | 3 ou mais |
| Tem Instagram | Sim/não |

A tela mostra, antes de salvar, **quantos clientes a campanha alcança hoje** ("alcança 1.234 clientes").

**Quando:** data e hora de início, e fim opcional.

| Repetição | Como funciona |
| --- | --- |
| Uma vez | Só na data e hora de início |
| Diária | Todo dia, no horário |
| Semanal | Nos dias da semana escolhidos (ex.: sextas) |
| Mensal | No dia do mês escolhido. Dia 29 a 31 em mês que não tem esse dia vai para o último dia do mês |
| Anual | No dia e mês escolhidos |

Os filtros são avaliados **a cada disparo**: quem se cadastrou depois de a campanha ser criada também
entra, se passar nos filtros.

---

## 6. A mensagem

Texto escrito pela empresa, com campos que o sistema preenche para cada cliente:

```
Oi, {primeiro_nome}! A {empresa} deseja um feliz aniversário 🎉
Passe na {unidade} esta semana e ganhe um café por nossa conta.
```

| Campo | Vira |
| --- | --- |
| `{primeiro_nome}` / `{nome}` | Ana / Ana Beatriz Souza |
| `{empresa}` | Lojas Regional |
| `{unidade}` | Itaituba (a última unidade que o cliente visitou) |
| `{idade}` | 28 (só nas campanhas de aniversário) |
| `{anos_de_cadastro}` | 2 (só no aniversário de cadastro) |

A tela mostra uma **prévia** com um cliente de exemplo.

---

## 7. ⚠️ Canais: o que muda por causa da Meta (WhatsApp e Instagram)

O envio fica para depois, mas duas regras da Meta **afetam o desenho desde já**:

- **WhatsApp oficial (API da Meta):** quando a empresa **inicia** a conversa, só pode mandar uma
  **mensagem-modelo aprovada pela Meta**. O texto livre que a empresa digita vira um modelo, com os
  campos acima como variáveis, e precisa passar pela aprovação. Existem APIs não oficiais que mandam
  texto livre, mas elas violam os termos do WhatsApp e o número corre risco de banimento.
- **Instagram:** a API **não deixa a empresa iniciar conversa** com quem nunca mandou mensagem para ela.
  Só dá para responder dentro de uma janela depois que o cliente escreve. Para campanha, que é mensagem
  iniciada pela empresa, **o Instagram provavelmente não serve**.

Por isso o desenho guarda a mensagem como **texto com campos**, pronto para virar modelo da Meta, e
deixa o envio atrás de uma "porta" que cada canal implementa depois. Antes de construir o envio,
confirmamos isso com a Meta (**D15**).

---

## 8. A execução: rápida, acompanhável, pausável

1. **Seleção num comando só no banco.** No disparo, um único comando copia os clientes escolhidos para
   a lista de destinatários da execução, já com a mensagem montada. Milhares de clientes levam menos de
   um segundo, porque os campos de filtro (nascimento, datas, visitas) ficam em colunas próprias e
   indexadas (**D2**).
2. **Cada destinatário tem o seu status**: pendente, enviado, simulado, falhou ou ignorado. É isso que
   permite **retomar de onde parou**: o processamento sempre pega os pendentes.
3. **Processa em lotes** (ex.: 200 por vez) e confere entre um lote e outro se alguém pediu pausa. O
   ritmo tem limite configurável (mensagens por minuto), coisa que o WhatsApp vai exigir.
4. **Pausar e retomar:**
   - pausar a **execução** para no fim do lote atual; retomar continua dos pendentes;
   - pausar a **campanha** impede novas execuções;
   - **cancelar** marca os pendentes como "cancelado".
   - Tudo fica registrado com quem fez e quando.
5. **Sobrevive a queda.** Se o servidor reiniciar no meio, a execução continua sozinha dos pendentes.
6. **Nunca duplica:** uma execução por campanha por disparo, e um destinatário por cliente em cada
   execução, garantidos pelo banco, não só pelo código.
7. **Acompanhamento ao vivo:** barra de progresso com total, enviados, falhas e ignorados, atualizando
   a cada poucos segundos.

**Nesta fase, a execução roda em modo simulação (D14):** faz tudo isso, mas marca cada destinatário
como **"simulado"** em vez de enviar. Assim dá para testar a campanha e ver quem receberia, com as
mensagens montadas, antes de existir o WhatsApp.

---

## 9. Histórico de versões e auditoria

- **Cada salvamento cria uma versão** numerada, com o usuário e a hora. As versões antigas nunca mudam.
- A tela de histórico mostra as versões e **o que mudou** em cada uma (ex.: "mensagem alterada",
  "horário 09:00 → 10:00").
- **Cada execução guarda a versão que usou.** Editar a campanha com uma execução em andamento não muda
  aquela execução (**D13**).
- Ativar, pausar, retomar e cancelar também ficam registrados, com quem fez e quando.

---

## 10. Relatórios

| Relatório | Mostra |
| --- | --- |
| **Painel das campanhas** | Todas as ativas: próximo disparo, resultado do último, total no mês |
| **Execuções de uma campanha** | Cada disparo: data, versão, total, enviados, falhas, ignorados, duração, quem pausou |
| **Destinatários de uma execução** | Cliente, telefone, mensagem montada, status e motivo, com exportação para CSV |

---

## 11. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Quem é "o cliente" | **O telefone, dentro da empresa.** A mesma pessoa em duas lojas ou com dois celulares recebe **uma** mensagem | Cada cadastro (aparelho + loja): a mesma pessoa pode receber em dobro |
| **D2** | Base de clientes | **Uma tabela "Clientes" por empresa + telefone**, atualizada a cada conexão: nome, nascimento **já como data**, 1ª e última visita, nº de visitas, "não quer receber". A seleção fica simples e rápida. Carrego os 14 de hoje na criação | Calcular tudo a partir dos cadastros em cada disparo: sem tabela nova, mas mais lento e mais difícil de manter |
| **D3** | Nascimento inválido | **Fica fora das campanhas de aniversário**, sem adivinhar. Quem nasceu em 29/02 recebe em 28/02 nos anos não bissextos | Tentar corrigir datas estranhas |
| **D4** | Retenção da LGPD | **Passa a contar da última visita, com prazo de 24 meses.** Quem continua voltando não é apagado, e o aniversário de cadastro volta a funcionar. Quem pedir para sair pode ser apagado na hora | Manter 12 meses do 1º cadastro: o aniversário de cadastro nunca acontece e o aniversário alcança pouca gente |
| **D5** | "Não quero receber" | **Criar já o campo no cliente e respeitar em toda execução** (conta como "ignorado"). O jeito de o cliente pedir (ex.: responder SAIR) vem com o envio | Deixar para a fase do envio |
| **D6** | Habilitação por empresa | **O super admin liga/desliga cada campanha de sistema e as "campanhas filtradas" por empresa.** Um plano vira depois um pacote dessas chaves | Criar já o cadastro de planos |
| **D7** | Quem gerencia | **O admin da empresa cria, edita e pausa as campanhas da própria empresa; o super admin vê e faz tudo** | Só o super admin |
| **D8** | Fuso horário | **Por empresa, padrão horário de Belém (-03, sem horário de verão).** O horário configurado é o da loja | O fuso do servidor para todas |
| **D9** | Novas campanhas de sistema | **Entram no catálogo agora: Boas-vindas, Sentimos sua falta e Cliente frequente.** Pós-visita numa fase seguinte | Só as duas de aniversário |
| **D10** | Precisão do agendador | **Confere a cada minuto: o disparo começa até 1 minuto depois do horário.** Se o servidor estava fora no horário, dispara quando voltar, **no mesmo dia**; no dia seguinte, não dispara mais | Uma vez por hora |
| **D11** | Excesso de mensagens | **No máximo 1 mensagem por cliente por dia, somando todas as campanhas da empresa.** Se dois disparos caírem no mesmo dia, vai o de maior prioridade (aniversário primeiro) e o outro conta como "ignorado: limite do dia". Nas filtradas que repetem, dá para escolher "não mandar de novo para quem recebeu desta campanha nos últimos N dias" | Sem limite |
| **D12** | Repetições das filtradas | **Uma vez, diária, semanal (dias escolhidos), mensal (dia do mês) e anual; início obrigatório e fim opcional** | Só diária/semanal/mensal/anual, sem "uma vez" |
| **D13** | Editar com execução em andamento | **A execução termina com a versão com que começou;** a versão nova vale do próximo disparo em diante | A execução passa a usar a versão nova no meio |
| **D14** | O que roda nesta fase | **Tudo, em modo simulação:** seleciona, monta a mensagem e registra, sem enviar. Quando o canal existir, é só ligar | Não executar nada até existir envio |
| **D15** | Instagram | **Fica como opção, mas desligado para campanhas até confirmarmos com a Meta;** o WhatsApp é o canal principal | Construir para os dois desde já |
| **D16** | Quanto tempo guardar os detalhes | **A lista de destinatários de cada execução fica 12 meses; os números (resumo) de cada execução e as versões ficam para sempre** | Guardar tudo para sempre |

---

## 12. Entrega em fases (cada uma publicável sozinha)

| Fase | O que entra | Você já consegue |
| --- | --- | --- |
| **1. Base** | Tabela de Clientes (D1–D5) e carga dos atuais, retenção nova, habilitação por empresa (D6) | Ver os clientes consolidados e ligar campanhas por empresa |
| **2. Cadastro das campanhas** | Telas de campanha de sistema e filtrada, mensagem com prévia, prévia de alcance, versões e histórico | Criar e editar campanhas, com o histórico de quem mudou o quê |
| **3. Execução** | Agendador de minuto a minuto, execução em simulação, acompanhamento ao vivo, pausar, retomar e cancelar, relatórios | Ver cada campanha "rodar" e quem receberia, com os números |
| **4. Envio (depois)** | WhatsApp oficial (modelos da Meta, limite por minuto, "SAIR") e Instagram se for viável | Mandar de verdade |

**Publicação:** as fases 1 a 3 criam tabelas novas. O script de publicação faz backup do banco antes,
como sempre. A **retenção nova (D4) muda o que é apagado**, então ela só entra depois do seu "ok"
explícito.

---

## 13. Próximo passo

Me responda **"ok"** para eu seguir com as recomendações, ou diga qual decisão (D1–D16) quer mudar.
Começo pela **Fase 1**.
