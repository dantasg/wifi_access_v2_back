# Proposta — Reenviar o disparo de uma campanha

> **Status (2026-10-12): implementada com as decisões do usuário**, que mudaram parte da proposta:
>
> - O botão **Reenviar fica em cada unidade**, no quadro **E-mails para as unidades** do detalhe da
>   execução, ao lado de **Baixar PDF** (não na lista de execuções). Isso já resolve para qual unidade vai
>   (substitui D1: não há janela para escolher unidades; só a confirmação do clique).
> - **Qualquer pessoa com acesso a essa tela** pode reenviar (D6: o usuário de unidade só vê as unidades
>   dele, então só reenvia as dele).
> - **"Todo o resto vai normal, não muda nada do que é hoje"**: o e-mail sai **igual ao do disparo** (D5:
>   sem "Reenvio" no assunto), pelo serviço, com as mesmas tentativas (D2), com os mesmos clientes (D3), e
>   os números da execução não mudam (D4). Um reenvio por unidade de cada vez (D7).
> - O reenvio fica registrado como **Reenvio**: linha nova no quadro (etiqueta "Reenvio" e "Pedido por"),
>   tipo **Reenvio** no Correio eletrônico e "Reenviou o e-mail de uma unidade" no Histórico. A lista de
>   execuções não mudou.
>
> Rota: `POST /admin/campaigns/runs/{id}/deliveries/{id do envio}/resend`. Migration
> `CampaignDeliveryResend` (`IsResend`, `RequestedBy`; o índice "um envio por unidade" passou a valer só
> para os envios do disparo). FRONT_CHANGES PARTE 28.

## 1. Entendimento do pedido

Na tela **Campanhas → Execuções**, ao lado do botão **Abrir**, entra um botão **Reenviar**. Ele pega o
**e-mail cadastrado hoje** na unidade (igual ou diferente do e-mail usado no disparo) e manda de novo o PDF
daquela execução. Esse envio fica **registrado como "Reenvio"**.

Casos em que isso ajuda:

- a unidade estava **sem e-mail** no dia do disparo, e o e-mail foi cadastrado depois;
- o e-mail da unidade **mudou** (gerente novo);
- o gerente **apagou ou não achou** o e-mail.

---

## 2. Como está hoje

| Ponto | Onde | Situação |
| --- | --- | --- |
| Um e-mail por unidade em cada execução | `CampaignEngine.cs:473` (`CreateDeliveriesAsync`) | Guarda o e-mail da unidade **daquele momento**. Unidade sem e-mail: o envio já nasce como falha. |
| Envio do PDF | `CampaignEngine.cs:531` (`SendDeliveriesAsync`) | O worker monta o PDF, manda e tenta de novo se falhar (3 tentativas, de 5 em 5 min: `CampaignEngine.cs:24-27`). |
| Registro no Correio eletrônico | `CampaignEngine.cs:572` | Cada e-mail que sai vira uma linha em `SentEmails`, tipo **Campanha** (`SentEmail.cs:4`). |
| PDF de novo, para baixar | `CampaignsController.cs:494` (`DeliveryPdf`) | Já remonta o PDF de uma execução antiga com os mesmos clientes e a mesma mensagem (`CampaignDeliveryDocument.cs:17`). |
| Tela de execuções | `CampaignRuns.tsx:94-150` | Só o botão **Abrir**. |
| Detalhe da execução | `CampaignRuns.tsx:302` | Quadro **E-mails para as unidades**: unidade, e-mail, situação, clientes, enviado em, **Baixar PDF**. |
| Histórico da campanha | `CampaignKind.cs:111` (`CampaignEventAction`) | Registra criar, editar, pausar, retomar e cancelar, com quem fez. |

Ou seja: montar o PDF de novo e mandar e-mail **já existem**. Falta o botão, a rota que pede o reenvio
e o registro dele.

---

## 3. O que proponho

### Na tela

- Botão **Reenviar** ao lado de **Abrir**, só nas execuções **concluídas** que tiveram clientes
  (as de 0 clientes não têm o que mandar).
- Ao clicar, abre uma confirmação com **cada unidade da execução e o e-mail cadastrado hoje**, todas
  marcadas. Dá para desmarcar alguma. Unidade **sem e-mail** aparece avisando e não pode ser marcada.
- Depois de confirmar: "Reenvio pedido. Os e-mails saem em alguns segundos." O botão fica
  **Reenviando...** até sair.

### Rota nova

`POST /admin/campaigns/runs/{id da execução}/resend` com `{ "unitIds": [...] }`:

- cria um envio novo **do tipo Reenvio** para cada unidade escolhida, com o e-mail de **hoje**;
- quem manda é o worker, como no disparo, com as mesmas 3 tentativas;
- recusa se a execução não estiver concluída, se não tiver clientes, se a lista já foi apagada pela LGPD
  (12 meses) ou se já houver um reenvio dela esperando para sair (evita clique duplo).

### O e-mail

- Mesmo PDF da execução (os mesmos clientes e a mesma mensagem; logo e cores de hoje, como o "Baixar PDF").
- Assunto: **"Reenvio — Campanha Aniversário — Itaituba — 12/10/2026 (7 clientes)"**.
- O texto começa com: *"Este é um reenvio do PDF do disparo de 12/10/2026."* O resto é igual ao do disparo.

### Onde o reenvio fica registrado

| Lugar | Como aparece |
| --- | --- |
| Detalhe da execução → **E-mails para as unidades** | Linha nova com a etiqueta **Reenvio**, o e-mail usado, a situação, a hora e quem pediu. O envio original continua lá, como foi. |
| **Correio eletrônico** | Tipo **Reenvio** (novo filtro ao lado de "Relatório mensal" e "Campanha"). |
| **Histórico** da campanha | "Reenvio da execução de 12/10/2026 pedido por *usuário*". |
| Lista de **Execuções** | Abaixo da situação: **"Reenviado 1x"** (ou 2x, 3x…). |

### O que muda no banco

- `CampaignDeliveries`: duas colunas novas, `IsResend` (sim/não) e `RequestedBy` (quem pediu). Migration
  só de colunas novas, sem mexer nos dados.
- `SentEmails`: só um tipo novo (`CampaignResend`), sem migration.

---

## 4. O que **não** muda

- Os números da execução (clientes, processados, enviados, falhas) ficam como foram no dia. O reenvio é
  registrado à parte.
- O disparo automático das campanhas e o horário de cada uma.
- Os clientes não recebem nada: o e-mail vai só para a unidade, como hoje.

---

## 5. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | O botão reenvia para quais unidades? | **Abre a confirmação com todas as unidades da execução marcadas; dá para desmarcar.** | Sempre todas, sem escolher. |
| **D2** | Quem manda o e-mail? | **O worker, em até ~5 segundos, com as 3 tentativas de hoje.** | A própria tela manda na hora (resposta imediata, mas sem novas tentativas se o servidor de e-mail falhar). |
| **D3** | Quais clientes vão no PDF? | **Os mesmos do disparo, inclusive os que falharam** (ex.: unidade estava sem e-mail). | Só os que tinham sido enviados. |
| **D4** | O reenvio muda os números da execução? | **Não. Os números ficam como no dia; o reenvio aparece à parte (§3).** | Os clientes que falharam passam a contar como enviados. |
| **D5** | Assunto e texto do e-mail | **"Reenvio — …" no assunto e a primeira linha dizendo que é reenvio do disparo de DD/MM.** | E-mail igual ao original. |
| **D6** | Quem pode reenviar? | **Super admin e admin da empresa; o usuário de unidade, só as unidades dele.** | Só super admin e admin da empresa. |
| **D7** | Quantas vezes? | **Sem limite, mas um de cada vez: enquanto houver reenvio esperando para sair, o botão fica travado.** | Uma vez por execução. |

---

## 6. Próximo passo

Com o ok nas decisões (ou os ajustes), implemento no back e no front, com testes. Depois testo localmente,
com o e-mail indo para uma caixa de teste, e só então publico.
