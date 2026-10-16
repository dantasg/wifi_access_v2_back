# Proposta — Demonstração de campanha (todos os clientes para o e-mail da unidade)

> **Status (2026-10-16): implementada com as decisões do usuário**, que mudaram parte da proposta:
>
> - **D1:** um botão só, **"Enviar demonstração"**, na tela Campanhas (não um por campanha), com uma
>   **mensagem padrão**: "Oi, {primeiro_nome}! Obrigado por usar o nosso Wi-Fi. Temos novidades esperando por
>   você nesta semana. Um abraço da equipe {empresa}." O PDF e o e-mail saem como a campanha **"Todos os
>   clientes"**.
> - **D2:** na hora de enviar, **escolhe-se uma unidade**. Só ela recebe, no e-mail dela, o PDF com todos os
>   clientes que já se conectaram nela (`CustomerUnits`). Unidade sem e-mail ou sem clientes aparece
>   desabilitada.
> - **D3:** só o **super admin** (rota com `Authorize(Roles = superadmin)`; o botão só aparece para ele).
> - **D4/D5:** o e-mail sai **igual ao de uma campanha**: assunto e texto vêm de `CampaignEmailText`, o mesmo
>   do serviço. No sistema, fica no **Correio eletrônico** com o tipo **Demonstração** (`CampaignDemo`).
>   Como o botão não é de uma campanha, ela não aparece em "Execuções". O anexo do Correio é montado de novo
>   com os clientes de hoje.
> - **Não cria execução.** O envio é feito pela própria API, na hora (`CampaignDemoController`, `CampaignDemo`
>   em Models). Por isso não mexe no limite de uma mensagem por dia, no "já recebeu" nem no Dashboard, e o
>   serviço de campanhas não mudou (só passou a usar o `CampaignEmailText` compartilhado).
>
> Rotas: `GET /admin/campaigns/demo?company=` (unidades, e-mail, clientes, mensagem) e
> `POST /admin/campaigns/demo/{unidade}?company=`. Testado de ponta a ponta localmente (SMTP de teste: 62
> clientes no PDF da Matriz). FRONT_CHANGES PARTE 30.

## 1. Entendimento do pedido

Um **botão que simula uma campanha**, mas manda **todos os clientes cadastrados** (e não só os do dia, como
aniversariantes ou quem sumiu há 30 dias) para o **e-mail da unidade**. É para usar **na apresentação**:
apertar o botão e mostrar, na hora, o e-mail chegando com o PDF e os botões de WhatsApp de cada cliente.

## 2. Como está hoje

| Ponto | Onde | Situação |
| --- | --- | --- |
| Execução da campanha | `CampaignEngine.cs:84` (`ScheduleDueAsync`) | Só pelo horário da campanha. Uma por campanha por dia (D1, `CampaignEngine.cs:143`). |
| Quem entra | `CampaignAudience.cs:20` | Só quem combina com o tipo (aniversário da semana, 30 dias sem vir, etc.). |
| Um cliente, uma mensagem por dia | `CampaignEngine.cs:250` (D11) | Soma todas as campanhas da empresa no dia. |
| "Já recebeu" | `CampaignAudience.cs:69, 83, 96` | Sentimos sua falta, Cliente frequente e Filtrada olham quem já recebeu antes. |
| PDF e e-mail por unidade | `CampaignEngine.cs:476` e `:534` | O serviço monta o PDF da unidade e manda para o e-mail dela, com 3 tentativas. |
| Execução "de simulação" | `Campaign.cs:78` (`CampaignRun.Simulation`) | O campo existe (sobra da fase sem envio), sempre `false` hoje. O Dashboard já ignora (`DashboardBuilder.cs:332`). |

O PDF e o e-mail **já existem**. Faltam o botão, a rota e a execução "de demonstração", que pega todos os
clientes e não atrapalha as campanhas reais.

## 3. O que proponho

- **Botão "Enviar demonstração"** em cada campanha da lista (tela Campanhas). Usa o nome e a mensagem
  daquela campanha.
- Ao clicar, abre uma **confirmação** com cada unidade, o e-mail dela e **quantos clientes** vão no PDF.
  Unidade sem e-mail aparece com aviso e fica de fora.
- Confirmando, nasce uma **execução de demonstração**. Em poucos segundos, o serviço manda para cada
  unidade o **PDF com todos os clientes dela** (a unidade da última visita, como nas campanhas, D17), com a
  mensagem já personalizada para cada cliente e o link do WhatsApp.
  - A coluna de informação do PDF mostra a **última visita** de cada cliente, que vale para qualquer tipo.
- A demonstração **não atrapalha nada**:
  - não impede a execução real do dia;
  - não conta no limite de uma mensagem por cliente por dia;
  - não conta como "já recebeu" (Sentimos sua falta, Cliente frequente);
  - não entra nos números do Dashboard.
- Fica **registrada como "Demonstração"** em Execuções e no Correio eletrônico.

## 4. Decisões

| # | Ponto | Proposta | Alternativa |
| --- | --- | --- | --- |
| D1 | Onde fica o botão | Em cada campanha da lista, com a mensagem dela | Um botão só na tela, com mensagem padrão |
| D2 | Quem recebe | Cada unidade recebe todos os clientes dela, no e-mail dela | Escolher uma unidade na hora |
| D3 | Quem pode usar | Só o super admin (é para apresentação; evita mandar a base inteira por engano) | Qualquer admin da empresa |
| D4 | Como aparece | O e-mail sai igual ao de uma campanha real; "Demonstração" só no sistema (Execuções e Correio) | "Demonstração" também no assunto do e-mail |
| D5 | Registro | Em Execuções e no Correio, sem contar nos números | Sem registro nenhum, só o e-mail |
| D6 | Unidade sem e-mail | Avisa na confirmação e não manda | Bloquear o botão |

## 5. Testes

- Local, com um servidor de e-mail de teste: o PDF chega com todos os clientes de cada unidade.
- A campanha real do mesmo dia continua saindo normalmente, com os mesmos clientes de antes.
- O limite de uma mensagem por dia e as regras de "já recebeu" não mudam por causa da demonstração.
- O Dashboard não conta a demonstração. Só o super admin vê o botão.
