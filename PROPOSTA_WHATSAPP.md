# Proposta — WhatsApp automático nas campanhas (API oficial da Meta), um número por empresa

> **Status (2026-10-23): implementação em andamento, por partes (§12). Partes 1 a 5 e 7 prontas localmente;
> falta a 6 (publicação e piloto).**
> - Parte 1: banco, credenciais cifradas, tela WhatsApp, teste de conexão e interruptor.
> - Parte 2 (§17): textos das campanhas viram modelos da Meta sozinhos, WhatsApp de atendimento das unidades e
>   situação de cada texto na tela.
> - Parte 3 (§18): envio na execução da campanha, espera dos avisos, PDF e e-mail com quem recebeu e quem não,
>   histórico por cliente e "Enviar teste".
> - Parte 4 (§19): avisos da Meta (entregue, lida, falhou), respostas e "SAIR", situação dos textos, alertas por
>   e-mail ao super admin, nginx.
> - Parte 5 (§20): texto da autorização com o nome da empresa no portal, "Saiu do WhatsApp" nos Leads e respostas
>   no detalhe da execução.
> - Parte 7 (§21): menu WhatsApp com Conversas, Registro, Conta e Configuração.
> - 452 testes; testes de ponta a ponta locais com uma Meta simulada e um SMTP falso.
> - **Nada publicado:** a produção só muda quando o usuário mandar.
> - **sai** o caminho "Tech Provider" com o botão "Conectar WhatsApp";
> - **entram** as credenciais de cada empresa, guardadas cifradas, e o interruptor de WhatsApp por empresa.
>
> O Tech Provider continua possível no futuro, sem refazer nada.

## 1. Entendimento do pedido

- **Um número de WhatsApp por empresa.** As credenciais desse número ficam na empresa, **cifradas** no banco.
- **Um interruptor por empresa** (o "bitWhatsApp"):
  - **Ligado:** no horário da campanha, cada cliente recebe a mensagem **sozinho, pelo WhatsApp**. O e-mail
    com o PDF **continua indo para o gestor**, agora dizendo **quem recebeu e quem não recebeu**, para o gestor
    falar só com quem ficou de fora.
  - **Desligado:** fica **como hoje**: só o e-mail com o PDF para o gestor.
- **O histórico mostra tudo isso:**
  - em cada execução: se o WhatsApp estava ligado e quantos receberam ou falharam;
  - em cada cliente: a situação no WhatsApp e o motivo da falha;
  - na empresa: quem ligou ou desligou o interruptor, e quando.

## 2. Como fica a execução de uma campanha

1. **No horário:** escolhe os clientes **como hoje**, inclusive a regra de uma mensagem por cliente por dia
   (D11, `CampaignEngine.cs:250`).
2. **Com o WhatsApp desligado:** monta o PDF por unidade e manda o e-mail ao gestor, **sem nenhuma mudança**
   (`CampaignEngine.cs:476` e `:534`).
3. **Com o WhatsApp ligado:**
   1. Manda o **modelo aprovado** da campanha para cada cliente, com o nome dele, pelo número da empresa.
   2. **Espera até 10 minutos** pelos avisos da Meta: entregue ou falhou (**D4**). Se todos responderem
      antes, segue na hora.
   3. Manda o **e-mail ao gestor**. O PDF vem em duas partes (**D5**):
      - **"Não receberam pelo WhatsApp — fale com eles"**, com o motivo e o link do WhatsApp;
      - **"Receberam pelo WhatsApp"**, só para conferir.
   4. Os avisos que chegam depois (lida, falhou mais tarde) **atualizam o histórico**.
4. **Se o WhatsApp não puder ser usado na hora** (texto ainda não aprovado, credencial inválida, limite da Meta
   esgotado):
   - ninguém deixa de ser atendido: quem não recebeu vai para a parte "Não receberam", com o motivo;
   - o super admin recebe um alerta por e-mail (**D9**).

## 3. O que cada empresa precisa ter na Meta

Cada empresa usa a **própria conta** na Meta, como "desenvolvedor direto" (**D1**). A implantação fazemos junto
com o administrador dela, em cerca de meio dia por empresa.

- **Portfólio empresarial** da empresa na Meta, com a **forma de pagamento** cadastrada. A empresa paga a Meta
  direto.
- **Um app da Meta** da própria empresa, com o produto WhatsApp.
  - A Meta diz que, para quem usa a API para si mesmo, **não é preciso a análise do app** ("if you are using the
    API for yourself as a Direct Developer, you do not need Advanced access or app review").
- **Um número só para a API.** Sem a "coexistência", que exige ser Tech Provider, o número não pode estar no
  aplicativo WhatsApp ao mesmo tempo. Por isso, o melhor é um **número novo** para cada empresa.
- **Token de usuário do sistema.** É o que a Meta indica para desenvolvedor direto: "use um token de acesso de
  usuário do sistema", que é de longa duração.
  - O usuário do sistema precisa de acesso à conta do WhatsApp **e** à conta de mensagens.
- **Avisos (webhook)** apontando para o nosso endereço da empresa, com um código de verificação que o admin
  escolhe e digita nos dois lugares, no painel e no app da Meta (§5.2).
- Para passar de **250 clientes por dia**, a empresa precisa **verificar o próprio negócio** na Meta (§10).

**O que guardamos de cada empresa:**

| Dado | Como fica no banco |
| --- | --- |
| Id do número (Phone number ID) | aberto |
| Id da conta do WhatsApp (WABA ID) | aberto |
| Número exibido e nome | aberto |
| Token de usuário do sistema | **cifrado**, só de ida (o painel mostra "configurado", nunca o valor) |
| Chave secreta do app (App Secret), que confere a assinatura dos avisos | **cifrada**, só de ida |
| Código de verificação dos avisos (Verify Token), digitado pelo admin | **cifrado**, só de ida |

**Regra dos segredos (pedido do usuário, 21/10/2026):** o token, a chave secreta do app e o código de
verificação **nunca vão para o front**. Ficam cifrados no banco, só o servidor decifra na hora de usar, e a
tela só recebe `hasAccessToken`, `hasAppSecret` e `hasVerifyToken`. É o mesmo padrão da senha do SMTP
(`SystemSettingsDtos.cs:5`) e da chave da UniFi (`UnitDtos.cs:5`). Para trocar, digita-se um novo; em branco,
mantém o atual.

A cifra é a mesma da chave da UniFi e da senha do SMTP: AES-256-GCM, prefixo `enc:v1:`
(`src/Models/Security/AesGcmEncryptor.cs`, uso em `UnitsController.cs:462`).

## 4. Banco (uma migration: `CompanyWhatsApp`)

- **Companies:**
  - `WhatsAppEnabled` (bool, padrão desligado). É o "bitWhatsApp" (nome em **D2**).
  - `WhatsAppChangedAt` e `WhatsAppChangedBy`: quem ligou ou desligou, e quando.
- **Companies.WhatsApp** (dados embutidos na empresa, no mesmo padrão de `Unit.Unifi` em `CompanyUnifi.cs`):
  - `PhoneNumberId`, `BusinessAccountId`, `DisplayPhoneNumber`, `DisplayName`;
  - `AccessToken`, `AppSecret` e `VerifyToken`, cifrados;
  - `QualityRating`, `MessagingLimit`, `CheckedAt` e `LastError` (resultado do último teste ou aviso).
- **CampaignRuns:**
  - `WhatsAppEnabled`: como estava o interruptor quando a execução começou;
  - `WhatsAppSentCount` e `WhatsAppFailedCount`;
  - `WhatsAppWaitUntil`: até quando esperar os avisos antes do e-mail.
- **CampaignRecipients:** `WhatsAppStatus` (`NotSent`, `Skipped`, `Accepted`, `Sent`, `Delivered`, `Read` ou
  `Failed`) e `WhatsAppError` (o motivo em português), para a tela e o PDF. O registro completo de cada mensagem
  fica em `WhatsAppMessages` (§15).
- **WhatsAppTemplates** (tabela nova): o modelo da Meta de cada versão de campanha, por empresa.
  - Campos: `IDCompany`, `IDCampaignVersion`, `Name`, `Language`, `Status` (`Pending`, `Submitted`,
    `Approved`, `Rejected`, `Paused`, `Disabled`), `RejectedReason`, `MetaTemplateId` e as datas.
- **Customers:** `WhatsAppOptOutAt`, quando respondeu "SAIR" (**D7**).
- **Registro de tudo do WhatsApp:** `WhatsAppMessages`, `WhatsAppMessageEvents`, `WhatsAppInboundMessages`,
  `WhatsAppWebhookRequests` e `WhatsAppAccountEvents` (detalhes em **§15**).

## 5. API (C#, Controllers)

### 5.1 `CompanyWhatsAppController`: `admin/whatsapp?company=`, admin da empresa e super admin (**D3**); usuário de unidade não acessa

- `GET`: a situação, **sem nenhum segredo**. Mostra `hasAccessToken`, `hasAppSecret` e `hasVerifyToken`, como o
  `hasPassword` do SMTP, e o endereço dos avisos (que não é segredo), para colar no app da Meta.
- `PUT`: grava as credenciais. Os segredos são só de ida: em branco, mantém o que está.
- `POST test`: confere o token e o número na Meta e devolve o número, o nome, a qualidade e o limite atual.
- `PUT enabled`: liga ou desliga o envio automático.
  - Só liga se o último teste passou.
  - Grava quem fez e quando.
- `POST test-message`: manda o modelo aprovado de uma campanha para **um número informado**, para testar com o
  nosso próprio celular.

### 5.2 `WhatsAppWebhookController`: `webhooks/whatsapp/{empresa}`, sem login

- `GET`: a verificação da Meta. Confere o `hub.verify_token` da empresa e devolve o `hub.challenge`.
- `POST`: confere a **assinatura** (`X-Hub-Signature-256`, HMAC-SHA256 com a App Secret **daquela empresa**).
  Recusa se não bater, e ignora o que já foi recebido.
- **O que trata:**
  - situação das mensagens: enviada, entregue, lida e falhou com o código;
  - aprovação ou recusa de modelo;
  - a falha `131050` (o cliente desligou "Ofertas e anúncios" no próprio WhatsApp): vira só o motivo da falha;
  - a resposta **"SAIR"**, que marca o cliente como "saiu do automático" (D7);
  - qualidade e limite do número;
  - respostas dos clientes (**D10**).
- **nginx:** a rota nova entra no `deploy/nginx/90-accesswifi-api.conf:7`, junto de `authorize|settings`.

### 5.3 Outros

- **Campanhas:** ao salvar uma versão, se a empresa tem WhatsApp configurado, o modelo fica "na fila". Quem
  manda para a Meta é o serviço, para a tela não esperar (**D8**). A tela mostra a situação do modelo.
- **Leads:** mostra quem saiu das promoções.
- **Respostas:** ficam em Conversas e no Registro do menu WhatsApp (D10; §21). A lista separada
  `GET admin/whatsapp/replies` da parte 5 saiu na parte 7.

## 6. Serviço de campanhas (worker)

- **`WhatsAppClient`** (em `src/Models/WhatsApp`, compartilhado com a API):
  - envia modelo, cria modelo, consulta o número e o limite;
  - a versão da Graph API fica nas Configurações do sistema;
  - trata os erros que importam:
    - `130429` (velocidade): tenta de novo, devagar;
    - `131049` (limite de marketing por pessoa) e `131050` (desligou as promoções): vão para "não recebeu";
    - token inválido: para o WhatsApp da empresa e alerta;
    - limite do dia esgotado: o resto vai para "não recebeu".
- **Modelos:** manda os modelos "na fila" para a Meta.
  - Os campos `{primeiro_nome}`, `{empresa}` etc. (`CampaignMessage.cs:17`) viram as variáveis do modelo, com
    exemplos.
  - Categoria marketing, português, **sem botão**. O texto termina com "Para não receber mais, responda SAIR."
    (D8), que o sistema acrescenta sozinho.
  - Confere a situação de tempos em tempos, caso algum aviso se perca.
- **Execução:** ganha uma etapa entre escolher os clientes e mandar os e-mails.
  - Envia pelo WhatsApp, respeitando:
    - pausar e cancelar pela tela;
    - quem saiu;
    - o telefone com 55 (`CampaignContact.cs:16`).
  - Depois espera os avisos até `WhatsAppWaitUntil` e então segue para o e-mail.
- **Alertas** ao super admin: token inválido, modelo recusado, qualidade baixa e limite esgotado.

## 7. PDF e e-mail ao gestor

- **WhatsApp desligado:** igual a hoje.
- **WhatsApp ligado:**
  - **PDF** (`CampaignPdf.cs`):
    - o topo mostra o resumo, por exemplo "12 receberam pelo WhatsApp · 3 não receberam";
    - primeiro vem a parte **"Não receberam — fale com eles"**, com o motivo de cada um ("número sem
      WhatsApp", "desligou as promoções", "limite do dia") e o link do WhatsApp com a mensagem pronta;
    - depois, **"Receberam pelo WhatsApp"**.
  - **Assunto:** "Campanha Aniversário — Matriz — 20/10/2026 (15 clientes: 12 pelo WhatsApp)".
  - **Texto do e-mail** (`CampaignEmailText.cs`): "12 clientes já receberam a mensagem pelo WhatsApp. 3 não
    receberam e estão no PDF para você entrar em contato."
- **Correio eletrônico:** o PDF remontado (`EmailsController`) sai igual ao enviado, com as duas partes.

## 8. Painel (React)

- **Configurações → WhatsApp** (da empresa escolhida no topo). O admin da empresa e o super admin editam e ligam;
  o usuário de unidade não vê (D3).
  - Credenciais, com os segredos só de ida.
  - **"Testar conexão"**: mostra o número, o nome, a qualidade e o limite.
  - Endereço dos avisos, com botão de copiar. O código de verificação é digitado pelo admin, como os outros
    segredos, e nunca volta para a tela.
  - **Interruptor "Envio automático pelo WhatsApp"**: só liga depois do teste. Mostra quem ligou e quando.
  - **"Enviar teste"** para um número informado.
- **Campanhas:** em cada campanha, um selo com a situação do modelo: em análise, aprovado ou recusado, com o
  motivo.
- **Execuções** (`CampaignRuns.tsx`):
  - na lista: o canal ("E-mail" ou "WhatsApp + e-mail") e os números ("WhatsApp: 12 enviados · 3 falharam");
  - no detalhe, por cliente: a situação no WhatsApp (entregue, lida, falhou com o motivo, ou "não enviado —
    WhatsApp desligado"), e um filtro por ela.
- **Leads:** marca de quem saiu das promoções.
- **Respostas** (D10):
  - no detalhe da execução, a resposta aparece junto do cliente;
  - uma lista "Respostas" mostra as mais recentes, com o link do WhatsApp para a loja responder pelo celular dela.

## 9. Portal

- Novo texto da autorização (`GuestPortal.tsx:183`): **"Autorizo a {empresa} a usar meus dados para contato e
  comunicações, conforme a LGPD."**
  - A Meta exige que o texto diga o nome da empresa.
  - Não precisa citar o WhatsApp.
- **Os já cadastrados também recebem** (D6), mesmo com o texto antigo, que não diz o nome da empresa.
  - O risco: a Meta pede o nome da empresa na autorização. Se muitos clientes bloquearem ou denunciarem, a
    qualidade e o limite do número caem.
  - O alerta de qualidade (§6) avisa se isso acontecer.
- **Nada muda no tempo nem no redirecionamento.**

## 10. Regras da Meta que valem aqui (páginas oficiais, conferidas em 19 e 20/10/2026)

- **Preço:**
  - **R$ 0,3217 por mensagem de marketing entregue** (calculadora oficial). O que não é entregue não é cobrado.
  - Quem paga é a empresa.
  - As tarifas só mudam em 1º de janeiro, abril, julho e outubro.
- **Textos:** campanha iniciada pela empresa só sai com **modelo aprovado**. A aprovação leva até 24 h.
- **Limite:**
  - começa em **250 clientes diferentes por 24 h**;
  - vai a **2.000** com a empresa verificada na Meta (ou depois de 2.000 mensagens de alta qualidade em 30 dias);
  - depois sobe sozinho.
  - Velocidade: 80 mensagens por segundo, muito acima do nosso volume.
- **Autorização:** precisa dizer o nome da empresa.
- **Saída:** a Meta pede que os pedidos para sair sejam respeitados. Ela sugere um botão no modelo; nós usamos a
  resposta "SAIR" (D8).
- **Avisos:**
  - precisam de **HTTPS com certificado válido** (o nosso servidor já tem);
  - chegam assinados;
  - se repetem por até 7 dias;
  - não existe API para buscar avisos antigos.
- **Respostas da empresa** (sem modelo, dentro de 24 h): a página de preços da Meta diz que, desde outubro de
  2026, são 1.000 grátis por mês por número e as seguintes são cobradas. A página do whatsappbusiness.com ainda
  diz que não cobra: **as fontes divergem.**

## 11. Decisões (respondidas em 20/10/2026)

| # | Ponto | Decisão |
| --- | --- | --- |
| D1 | Conta na Meta | **Cada empresa com a própria conta (desenvolvedor direto), credenciais cifradas no AccessWifi, e a empresa paga a Meta** |
| D2 | Nome do interruptor no código | **`WhatsAppEnabled`** (na variável, `bWhatsAppEnabled`) |
| D3 | Quem configura e liga | **O admin da empresa** (e o super admin). O usuário de unidade não vê |
| D4 | Quando sai o e-mail ao gestor | **Até 10 minutos depois dos envios, ou antes se todos já tiverem resposta da Meta** |
| D5 | PDF com WhatsApp ligado | **Duas partes: "Não receberam — fale com eles" primeiro, "Receberam" depois** |
| D6 | Clientes já cadastrados | **Todos recebem**, inclusive os que aceitaram o texto antigo. O texto novo vale para os próximos cadastros (§9) |
| D7 | Saída das promoções | **Opção B: quem responde "SAIR" sai só do WhatsApp automático e continua no PDF do gestor, com o motivo** (§11.1) |
| D8 | Modelos | **Cada versão da campanha vira modelo da Meta sozinha, sem botão; o texto termina com "Para não receber mais, responda SAIR."** (§11.2) |
| D9 | WhatsApp indisponível na hora | **Todos vão para o PDF com o motivo, e o super admin recebe alerta** |
| D10 | Respostas dos clientes | **O modelo termina com o número de atendimento da loja; as respostas ficam registradas no painel, só para leitura, e não são apagadas** (§11.3) |

### 11.1 D7 — quem pede para sair (decidido: opção B, pelo "SAIR")

- **Como sai:** o cliente responde **"SAIR"** à mensagem.
  - O sistema aceita maiúscula ou minúscula, com ou sem ponto, e espaços ("sair", "Sair.", " SAIR ").
  - Não há botão (D8).
- **O que muda para ele:**
  - o WhatsApp automático **não manda mais nada**;
  - ele **continua no PDF do gestor**, na parte "Não receberam", com o motivo "pediu para sair (SAIR) em
    dd/mm", e o gestor decide se fala com ele pelo celular da loja.
- **Sem resposta automática de confirmação.** Responder seria uma mensagem da empresa, que pode ter custo
  (§10). Fica sem custo nenhum.
- **"Ofertas e anúncios" desligado no WhatsApp:** o cliente também pode desligar as promoções da empresa nas
  configurações do próprio WhatsApp. Isso não depende de nós.
  - A Meta não entrega e **não cobra** (falha `131050`).
  - Ele aparece no PDF com o motivo "desligou as promoções no WhatsApp".

### 11.2 D8 — como os textos viram "modelos" da Meta

- **A regra da Meta:** quando a empresa puxa a conversa, como numa campanha, a mensagem precisa ser um
  **modelo** que a Meta aprovou antes. Texto solto só é permitido para responder um cliente, até 24 h depois
  da última mensagem dele.
- **O que o sistema faz sozinho:** cada vez que uma campanha é criada ou tem o texto editado, o serviço envia
  o texto para a Meta como modelo.
  - Os campos viram "variáveis". Por exemplo, "Feliz aniversário, {primeiro_nome}! A {empresa} preparou um
    presente para você" vira "Feliz aniversário, {{1}}! A {{2}} preparou um presente para você", com um exemplo
    de cada campo ("Maria", "Lojão dos Plásticos"), como a Meta pede.
  - Vai como categoria **marketing**, em português, **sem botão**. No fim do texto, o sistema acrescenta
    "Para não receber mais, responda SAIR."
  - **O botão não teria custo a mais:** a Meta cobra por mensagem entregue, pela categoria, e o toque do
    cliente no botão é uma mensagem dele, que a Meta não cobra. Ficamos com o "SAIR" por escolha, e dá para
    trocar depois sem refazer nada.
- **Aprovação:** a Meta analisa em até 24 h. A tela da campanha mostra **"Em análise"**, **"Aprovado"** ou
  **"Recusado"** com o motivo. Se for recusado, basta corrigir o texto e salvar: vai uma versão nova.
- **Texto editado e ainda em análise na hora da campanha:**
  - a campanha sai com o **último texto aprovado**, e o histórico mostra qual versão saiu;
  - se nunca houve texto aprovado, vale a D9: todos vão para o PDF.
- **Por que sozinho:** a outra opção era alguém cadastrar cada texto à mão no WhatsApp Manager da Meta, em
  cada empresa, a cada edição, e depois ligar o nome aqui. Sozinho, ninguém precisa entrar na Meta para editar
  uma campanha.

### 11.3 D10 — respostas dos clientes: custo e tempo

- **Ler a resposta não custa nada.** A Meta diz: "A Meta não cobra pelas mensagens de usuários para
  empresas". Ela cobra só o que a empresa **envia**.
- **Não é preciso ficar observando.** A resposta chega sozinha: a Meta avisa o nosso servidor na hora em que
  o cliente responde, seja 1 minuto ou 3 dias depois.
  - Se o servidor estiver fora do ar, a Meta tenta de novo por até 7 dias.
  - A resposta só se perde se o servidor ficar mais de 7 dias fora.
- **Por quanto tempo guardamos:** seguem **a mesma regra do cliente** (decisão de 20/10). Não expiram por tempo;
  só são apagadas junto com o cliente, quando a regra da LGPD apaga quem ficou 24 meses sem voltar
  (`AccessWifiService/LeadRetentionService.cs:12`).
- **Responder tem regras:**
  - O número da campanha fica só na API, então ninguém responde por ele no celular.
  - Por isso, a mensagem termina com o **número de atendimento da loja**, e o painel mostra a resposta com um
    **link para a loja responder pelo celular dela**. Assim a conversa sai do número normal da loja e não tem
    custo da Meta.
  - Responder pelo número da API seria a "caixa de entrada" (fora deste plano). Nesse caso, valem estas regras:
    - texto livre só até 24 h depois da última mensagem do cliente;
    - a Meta diz que são 1.000 respostas grátis por mês por número e cobra as seguintes, mas a página do
      WhatsApp ainda diz que não cobra: **as fontes divergem**;
    - depois de 24 h, só com modelo aprovado, cobrado como marketing.

## 12. Ordem de implementação e prazo

Pelo nosso ritmo real: as campanhas por e-mail, o Dashboard e a UniFi pela nuvem saíram em cerca de 1 dia cada.
Cada passo é testado localmente e pode ser publicado sozinho, com o interruptor desligado em todas as
empresas.

| Passo | O que entra | Trabalho |
| --- | --- | --- |
| 1 | Banco, credenciais cifradas, tela WhatsApp, "Testar conexão" e interruptor com registro | 1 dia |
| 2 | Modelos: versão da campanha → modelo da Meta, situação na tela | 1 dia |
| 3 | Envio na execução, espera dos avisos, PDF e e-mail com o resultado, histórico (execuções e clientes) | 1,5 a 2 dias |
| 4 | Avisos da Meta (webhook), saída, nginx e alertas | 1 dia |
| 5 | Portal (texto novo), Leads (saída) e lista de respostas | meio dia |
| 6 | Testes de ponta a ponta locais, publicação e piloto com uma empresa, enviando primeiro só para os nossos números | 1 dia |
| 7 | Registro completo (§15): tabelas e gravação, menu "WhatsApp" (Conversas, Registro e Conta) | 1 dia |
| **Total** | | **7 a 8 dias úteis** |

**Fora do nosso controle:**
- a empresa criar a conta, o app e o número na Meta: 1 a 3 dias úteis;
- a aprovação dos modelos: até 24 h;
- o limite inicial de 250 clientes por dia, até a empresa verificar o negócio.

## 13. Testes

- **Automáticos**, com a API da Meta simulada:
  - **Interruptor desligado:** tudo igual a hoje (o mesmo PDF e o mesmo e-mail).
  - **Interruptor ligado:**
    - enviados e falhas;
    - quem saiu;
    - modelo não aprovado e token inválido;
    - limite esgotado;
    - pausar e cancelar no meio;
    - uma mensagem por dia.
  - **Avisos:**
    - assinatura válida e inválida;
    - repetidos;
    - fora de ordem ("lida" antes de "entregue");
    - verificação da Meta.
  - **PDF:** as duas partes e o resumo.
  - **Segurança:** a API nunca devolve segredo, e os segredos ficam com `enc:v1:` no banco.
- **De ponta a ponta, local:** Meta simulada, SMTP de teste e navegador, como no botão de demonstração.
- **Piloto em produção:** uma empresa, enviando primeiro só para os nossos números. Só depois liga para os
  clientes.

## 14. Próximo passo

1. Todas as decisões estão respondidas.
2. Escolher a empresa do piloto e o número novo dela.
3. Com o "ok", começo pelo passo 1 (§12).

## 15. Registro de tudo do WhatsApp (pedido de 21/10/2026)

**Pedido:** registrar **absolutamente tudo** do WhatsApp, do envio à resposta do cliente. Esta seção substitui as
tabelas `WhatsAppWebhookEvents` e `WhatsAppReplies` do §4 e põe a situação de cada mensagem numa tabela própria.
Assim o registro não depende da lista de clientes da campanha, que a retenção apaga depois de 12 meses
(`LeadRetentionService.cs:16`).

### 15.1 O que fica registrado

| O quê | Onde | O que guarda |
| --- | --- | --- |
| **Cada mensagem enviada** | `WhatsAppMessages` | Ver lista abaixo |
| **Cada mudança de situação** (enviada, entregue, lida, falhou) | `WhatsAppMessageEvents` | Situação, hora informada pela Meta, erro, preço e categoria, e o aviso da Meta como chegou |
| **Cada mensagem de texto do cliente** (respostas e "SAIR") | `WhatsAppInboundMessages` | Ver lista abaixo |
| **Cada aviso que chega da Meta**, do jeito que chegou | `WhatsAppWebhookRequests` | Corpo do aviso, se a assinatura confere, quando foi tratado e o erro, se houve. **Apagado depois de 90 dias** (D12) |
| **Tudo da conta da empresa** | `WhatsAppAccountEvents` | Credenciais trocadas (sem os segredos), WhatsApp ligado ou desligado, testes de conexão, modelo enviado, aprovado ou recusado, qualidade, limite e nome do número. Sempre com quem fez, quando e o que a Meta respondeu |
| **Saída das promoções** | `Customers.WhatsAppOptOutAt` | A data, mais o vínculo com a mensagem "SAIR" que causou a saída |

**`WhatsAppMessages`, cada mensagem enviada:**
- **De onde veio:** empresa, cliente, campanha, execução e destinatário da campanha. Ou então "teste", com o
  usuário que mandou.
- **Para quem e por qual número:** o telefone do cliente e o número da empresa que enviou.
- **O texto:** o modelo usado (nome, idioma e versão), os valores de cada campo e **o texto exatamente como o
  cliente leu**.
- **A conversa com a Meta:**
  - o que foi pedido (sem o token);
  - o que a Meta respondeu;
  - o código HTTP, o tempo da chamada e as tentativas.
- **O id da mensagem na Meta** (`wamid`), que liga tudo o que vem depois.
- **A situação atual:** na fila, aceita, enviada, entregue, lida ou falhou, com o código e o motivo do erro.
- **O preço:** categoria, se foi cobrada e o tipo de cobrança, que vêm nos avisos da Meta.
- **Os horários:** criada, aceita, enviada, entregue, lida e falhou.

**`WhatsAppInboundMessages`, cada mensagem de texto do cliente:**
- **Só de clientes cadastrados** (D13). Mensagem de um número que não é cliente da empresa não é registrada.
- **Só texto** (D11). Foto, áudio, vídeo, documento, figurinha, localização e reação não são registrados.
- **Quem mandou:** o cliente, o telefone e o nome do perfil no WhatsApp (a Meta envia junto).
- **O texto**, exatamente como chegou.
- **A qual campanha pertence** (D14): à da **última mensagem de campanha enviada a esse cliente antes da
  resposta**. Todas as respostas seguintes ficam com essa campanha **até chegar a mensagem da próxima campanha**.
  - Exemplo: o aniversário sai em 10/03 e a Maria responde em 10/03 e em 15/03; as duas ficam com
    "Aniversário de 10/03". Em 20/04 sai "Sentimos sua falta". A resposta dela em 21/04 fica com a campanha
    nova.
- **Se era um "SAIR".**
- **O aviso da Meta como chegou.**
- **O id da Meta.** Serve para não gravar a mesma mensagem duas vezes, já que a Meta repete avisos por até
  7 dias.

**Segredos:** o token, a chave secreta do app e o código de verificação **nunca** entram em registro nenhum.

### 15.2 Por quanto tempo fica

Segue a decisão D10: **a mesma regra do cliente**.
- **Mensagens enviadas, situações e respostas:** não expiram por tempo. Só são apagadas junto com o cliente,
  quando a regra da LGPD apaga quem ficou 24 meses sem voltar.
- **Registro da conta** (`WhatsAppAccountEvents`): fica para sempre, porque não tem dado pessoal.
- **A lista de clientes da campanha**, que já é apagada depois de 12 meses: pode ir embora sem levar o
  registro do WhatsApp junto. A mensagem só perde o vínculo com o destinatário e continua com a campanha e o
  cliente.

### 15.3 No painel: menu "WhatsApp" (D15)

- **Conversas:**
  - uma lista de clientes, com a última mensagem de cada um;
  - ao abrir um cliente, **o histórico completo, separado por campanha**: cada mensagem enviada, com a
    situação, e embaixo as respostas que vieram até a campanha seguinte.
  - Um link do WhatsApp para a loja responder pelo celular dela (D10).
- **Registro:** tudo, enviado e recebido, com filtros por período, campanha, unidade, situação, direção e
  telefone.
- **Detalhe de uma mensagem:**
  - cada mudança de situação, com a hora;
  - o motivo do erro;
  - se foi cobrada;
  - o que foi pedido e o que a Meta respondeu.
- **Conta:** o histórico da empresa (quem ligou ou desligou, testes, modelos, qualidade e limite).
- **Quem vê:** o admin da empresa e o super admin, como na D3. O usuário de unidade vê só os clientes e as
  campanhas das unidades dele, como no resto do sistema.

### 15.4 Regras da Meta que importam aqui (páginas oficiais, 21/10/2026)

- Cada mensagem enviada pode ter **até três avisos de situação**: enviada, entregue e lida. Os avisos de
  situação **não trazem o texto** da mensagem, por isso guardamos o texto no envio.
- A mensagem do cliente chega com o **nome do perfil** e, quando ele responde citando uma mensagem, com o
  **id da mensagem citada** (`context`).
- A Meta repete os avisos por até 7 dias e **não tem como buscar avisos antigos**: o que não guardarmos na
  hora se perde.

### 15.5 Decisões (respondidas em 21/10/2026)

| # | Ponto | Decisão |
| --- | --- | --- |
| D11 | Fotos, áudios, vídeos e documentos do cliente | **Não registrar: só texto** |
| D12 | Cópia do aviso da Meta "do jeito que chegou" | **Opção A: a cópia do pacote inteiro fica 90 dias e depois é apagada; os pedaços de cada cliente seguem a regra do cliente** (§15.7) |
| D13 | Mensagem de um número que não é cliente cadastrado | **Não registrar** |
| D14 | A qual campanha a resposta pertence | **À última campanha enviada ao cliente, até chegar a próxima** (exemplo em §15.1) |
| D15 | Onde ver | **Menu "WhatsApp", com Conversas, Registro e Conta (§15.3); o detalhe da execução também mostra a situação e as respostas de cada cliente** |

### 15.6 O que muda no prazo

O registro completo e o menu "WhatsApp" acrescentam cerca de **1 dia** aos 6 a 7 dias do §12. Sem mídia (D11),
sai o download dos arquivos:
- tabelas e gravação: meio dia;
- telas de Conversas, Registro e Conta: meio dia.

Total: **7 a 8 dias úteis**.

### 15.7 D12 — a cópia do aviso da Meta (decidido: opção A)

**O que é o aviso.** Quando algo acontece no WhatsApp, a Meta chama o nosso servidor e manda um **pacote de
dados**. Pode ser uma mensagem entregue, uma mensagem lida ou uma resposta do cliente.

Um mesmo pacote pode trazer **várias coisas de uma vez, de clientes diferentes**. Por exemplo, num pacote só:
- "a mensagem da Maria foi entregue";
- "a do João foi lida";
- "a Ana respondeu: Obrigada!".

**O que o sistema já faz com ele.** Abre o pacote e guarda cada pedaço no lugar certo:
- a entrega na mensagem da Maria;
- a leitura na do João;
- a resposta na conversa da Ana.

**Isso já é o registro completo**, e segue a regra do cliente (D10).

**A dúvida:** guardar, **além disso**, uma cópia do pacote inteiro, do jeito que chegou?
- **Para que serve:** é a prova do que a Meta mandou. Se um dia o sistema ler algo errado, dá para conferir e
  reprocessar.
- **O problema:** o pacote mistura clientes. Quando a regra da LGPD apagar a Maria, não dá para tirar só o
  pedaço dela de dentro dele.

**Opções:**
- **A (recomendada): guardar a cópia por 90 dias e depois apagar.** Os pedaços de cada cliente continuam
  guardados pela regra do cliente. Dá tempo de achar e corrigir qualquer erro.
- **B: guardar a cópia para sempre.** Ficariam guardados dados de clientes que a LGPD já mandou apagar.
- **C: não guardar a cópia.** Ficam só os pedaços. Se o sistema ler algo errado, não há como conferir depois.

## 16. Fontes (páginas oficiais da Meta e do WhatsApp)

- Tokens (desenvolvedor direto usa token de usuário do sistema):
  https://developers.facebook.com/documentation/business-messaging/whatsapp/access-tokens
- Análise do app (desenvolvedor direto não precisa):
  https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/app-review
- Coexistência (exige Tech Provider ou Solution Partner):
  https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/onboarding-business-app-users
- Preços: https://developers.facebook.com/documentation/business-messaging/whatsapp/pricing
- Calculadora (Brasil, marketing):
  https://whatsappbusiness.com/pt-br/products/platform-pricing/?country=Brasil&currency=Real%20brasileiro%20(BRL)&category=Marketing
- Modelos:
  - https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/overview
  - https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/template-review
  - https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/components
- Limite de marketing por pessoa:
  https://developers.facebook.com/documentation/business-messaging/whatsapp/templates/marketing-templates/per-user-limits
- Limites de envio: https://developers.facebook.com/documentation/business-messaging/whatsapp/messaging-limits
- Velocidade: https://developers.facebook.com/documentation/business-messaging/whatsapp/throughput
- Avisos:
  - https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/overview
  - https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/create-webhook-endpoint
- Autorização: https://developers.facebook.com/documentation/business-messaging/whatsapp/getting-opt-in
- Nome do número: https://developers.facebook.com/documentation/business-messaging/whatsapp/display-names
- Avisos de mensagens (estrutura, até três avisos por mensagem enviada):
  https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/messages
- Mensagem de texto recebida (campo `context`, a mensagem citada):
  https://developers.facebook.com/documentation/business-messaging/whatsapp/webhooks/reference/messages/text

## 17. Parte 2 — textos aprovados pela Meta (pronta localmente em 22/10/2026)

### 17.1 O que foi feito

- **Tabela `WhatsAppTemplates`:** um modelo da Meta por texto de campanha. Guarda o texto exato que foi para a
  Meta, o campo de cada variável, a situação, o motivo, o id na Meta, as tentativas e as datas.
  - Versões da campanha com o mesmo texto usam o mesmo modelo. Mudou só o horário: não vai nada para a Meta.
- **Montagem do texto** (`WhatsAppTemplateBuilder`), seguindo as regras da Meta (template-review e components):
  - `{primeiro_nome}`, `{nome}`, `{unidade}`, `{idade}` e `{anos_de_cadastro}` viram `{{1}}`, `{{2}}`… com um exemplo
    real (nome e WhatsApp de uma unidade da empresa);
  - `{empresa}` já vai escrito, porque é igual para todos os clientes;
  - no fim do texto: **"Fale com a loja pelo WhatsApp (número) sempre que precisar."** (D10);
  - no rodapé: **"Para não receber mais, responda SAIR."** (D8);
  - categoria marketing, idioma `pt_BR`, sem botão.
- **Serviço** (`WhatsAppTemplateService`, no AccessWifiService, a cada minuto), só nas empresas com a conexão
  testada:
  - cria o modelo do texto atual de cada campanha ativa ou pausada e manda para a Meta;
  - falha de token ou rede: tenta de novo em 5 min, 15 min, 1 h e depois a cada 6 h;
  - a Meta recusa o conteúdo na criação: fica "Ajuste o texto", com o motivo dela;
  - o nome já existe na Meta: adota o que está lá;
  - confere a situação dos que estão em análise a cada 15 min e dos aprovados a cada 6 h, caso o aviso da Meta
    se perca (os avisos entram na parte 4);
  - tudo vai para o registro da conta (`WhatsAppAccountEvents`), com o pedido e a resposta da Meta, sem o token.
- **Telas:**
  - WhatsApp → **3. WhatsApp de atendimento das unidades** e **4. Textos aprovados pela Meta** (o texto como o
    cliente vê, a situação e o motivo);
  - Campanhas → em cada campanha, **"Texto no WhatsApp"** com a situação e, enquanto o texto novo está em análise
    ou foi recusado, "Enquanto isso, sai o texto aprovado da vN".

### 17.2 Decisões que tomei para seguir (confirmar ou trocar)

| # | Ponto | O que fiz | Outra opção |
| --- | --- | --- | --- |
| D16 | Onde fica o "número de atendimento da loja" (D10) | **Campo novo em cada unidade**, editado pelo admin da empresa na tela WhatsApp. Na parte 3, **cliente de unidade sem número não recebe a mensagem automática**: vai para o PDF do gestor com o motivo | Um número só por empresa |
| D17 | Texto que começa com um campo (ex.: "{primeiro_nome}, hoje faz…") | A Meta recusa modelo que começa com variável. **O sistema põe "Olá, " na frente sozinho** ("Olá, Maria, hoje faz…"). Duas das quatro campanhas do Lojão começam assim | Não mandar e pedir para o admin reescrever |
| D18 | Onde vai o "Para não receber mais, responda SAIR." | **No rodapé do WhatsApp** (letra menor, no fim da mensagem), porque o fim do texto já tem o WhatsApp da loja | No fim do texto, junto do WhatsApp da loja |

### 17.3 O que fica para as próximas partes

- O texto da campanha pode ter até 1000 caracteres, mas com o final automático o WhatsApp aceita 1024 no total.
  Texto longo aparece como "Ajuste o texto" com quantos caracteres tirar. Na parte 3, o formulário da campanha
  pode avisar antes de salvar.
- Os modelos antigos ficam na Meta (limite de 250 por conta sem verificação). Se precisar, a limpeza dos que não
  são usados há meses entra depois.

## 18. Parte 3 — envio nas campanhas (pronta localmente em 22/10/2026)

### 18.1 Como ficou a execução com o WhatsApp ligado

1. **Na criação da execução**, guarda como estava o interruptor da empresa.
2. **Escolhe os clientes como hoje.** Cada um entra "na fila" do WhatsApp.
3. **Escolhe o texto:**
   - o da versão da execução, se aprovado;
   - senão, o último aprovado de uma versão anterior (§11.2). A tela mostra qual versão saiu.
4. **Manda para cada cliente** (`CampaignWhatsAppSender`, no AccessWifiService):
   - em blocos de 50, conferindo pausa e cancelamento entre eles;
   - grava cada mensagem em `WhatsAppMessages`: o texto exato, o id da Meta, o pedido e a resposta, sem o token.
5. **Não manda**, e o cliente vai para "Não receberam" com o motivo:
   - pediu para sair (SAIR, D7);
   - a unidade não tem WhatsApp de atendimento (D16);
   - telefone inválido;
   - falta um dado do texto (ex.: idade);
   - limite do dia da Meta: conta os clientes das últimas 24 horas contra o limite lido no teste de conexão.
6. **Recusas da Meta** (página oficial de códigos de erro):
   - **só daquele cliente** (número sem WhatsApp, bloqueou a empresa, desligou as promoções): "Não receberam", com
     o motivo;
   - **passageiras** (velocidade, Meta fora do ar): tenta de novo em 1 minuto; passada 1 hora, desiste;
   - **da conta ou do texto** (token, pagamento, conta restrita, texto pausado ou desativado): o WhatsApp para
     nesta execução, e todos que faltavam vão para o PDF com o motivo (D9).
7. **Espera os avisos até 10 minutos** (D4) e manda o e-mail ao gestor. Sem nada para esperar, manda na hora.
   - Os avisos da Meta entram na parte 4. Até lá, a espera é sempre de 10 minutos, e quem a Meta aceitou aparece
     como "Enviada".

### 18.2 PDF e e-mail ao gestor (D5)

- **Resumo no topo:** "4 receberam pelo WhatsApp · 3 não receberam".
- **Primeiro, "Não receberam pelo WhatsApp — fale com eles"**, com o motivo embaixo do nome e o link do WhatsApp
  com a mensagem pronta.
- **Depois, "Receberam pelo WhatsApp"**, com a situação (Enviada, Entregue, Lida).
- **Todos receberam:** só o resumo e a lista, sem a mensagem e sem os passos.
- **Assunto:** "Campanha Sentimos sua falta — Matriz — 22/10/2026 (7 clientes: 4 pelo WhatsApp)" (ou "nenhum pelo
  WhatsApp").
- **Texto do e-mail:** quantos receberam, quantos não, e a mensagem só quando há com quem falar.
- **WhatsApp desligado:** o PDF e o e-mail ficam exatamente como antes.

### 18.3 Telas

- **Execuções:** coluna "WhatsApp" ("4 receberam · 9 não").
- **Detalhe da execução:**
  - bloco "WhatsApp automático", com o texto que saiu, Receberam, Entregues, Lidas, Não receberam e Na fila;
  - até quando o e-mail espera os avisos, e o motivo se o WhatsApp parou;
  - na lista de clientes, a coluna WhatsApp, com a situação e o motivo, e o filtro "Receberam" ou "Não receberam".
- **WhatsApp → Textos aprovados → "Enviar teste"** (§5.1): manda um texto aprovado para o celular informado, com
  "Maria" e o WhatsApp de uma unidade nos campos. Fica registrado como teste, com quem mandou. A Meta cobra como
  marketing.

### 18.4 Para as próximas partes

- **Parte 4, avisos da Meta (webhook):**
  - entregue, lida e falhou atualizam a mensagem e o cliente;
  - a espera dos 10 minutos encurta quando todos responderem;
  - "SAIR" grava a saída no cliente;
  - alertas ao super admin (D9: token inválido, texto pausado, limite).
- **O limite de 24 horas é uma conta nossa, por segurança.** A Meta não diz na documentação o que acontece quando o
  limite estoura. Por isso o sistema para antes e manda o resto para o PDF.

## 19. Parte 4 — avisos da Meta (pronta localmente em 22/10/2026)

### 19.1 O endereço dos avisos

- **Endereço:** `https://<servidor>/webhooks/whatsapp/<empresa>`. A tela WhatsApp mostra o endereço com o botão
  "Copiar endereço" e o passo a passo no app da Meta (WhatsApp → Configuração → Webhooks).
- **Campos que a empresa assina:** `messages`, `message_template_status_update` e `phone_number_quality_update`.
- **Verificação (GET):**
  - confere o `hub.verify_token` com o código de verificação da empresa, que fica cifrado;
  - código certo: devolve o `hub.challenge`; errado: 403.
- **Avisos (POST):**
  - confere a assinatura `X-Hub-Signature-256`, um HMAC-SHA256 do corpo com a chave secreta do app da empresa;
  - sem assinatura válida, responde 401 e não trata nada.
- **Cópia de cada aviso:** fica guardada como chegou por 90 dias (D12) e depois é apagada pela rotina de retenção.
- **Falha ao tratar:** responde 500 para a Meta mandar de novo; tratar o mesmo aviso duas vezes não muda nada.
- **"Testar conexão" liga a conta ao app** (`POST /{conta}/subscribed_apps`): a Meta exige isso para mandar os
  avisos da conta.
- **nginx:** `location ~ ^/webhooks/whatsapp/` com `client_max_body_size 4m`, porque a Meta manda até 3 MB por aviso.

### 19.2 O que cada aviso faz

- **Situação da mensagem** (enviada, entregue, lida, falhou):
  - atualiza a mensagem e o cliente da campanha;
  - grava cada mudança em `WhatsAppMessageEvents`, com a cobrança (categoria e se foi cobrada);
  - nunca volta atrás ("lida" antes de "entregue" fica lida) e guarda a hora mais cedo que a Meta informou;
  - "falhou" só conta antes da entrega, com o motivo em português (ex.: "desligou as promoções", 131050).
- **E-mail ao gestor sem esperar:** quando todos os clientes já têm a palavra final da Meta, o e-mail sai na hora,
  sem esperar os 10 minutos (D4). No teste, saiu 6 segundos depois do disparo.
- **Resposta de texto do cliente** (`WhatsAppInboundMessages`):
  - só texto (D11) e só de cliente cadastrado (D13);
  - acha o cliente pela mensagem que mandamos a ele ou pelo telefone, com e sem o 9 do celular;
  - fica com a campanha da última mensagem enviada a ele (D14).
- **"SAIR"** (maiúscula ou minúscula, com ponto ou espaços):
  - grava `Customers.WhatsAppOptOutAt` e a mensagem que causou a saída;
  - o cliente sai do envio automático e vai para o PDF com o motivo (D7).
- **Situação do texto** (aprovado, recusado, pausado, desativado):
  - atualiza o texto na hora, sem esperar a conferência de 15 minutos;
  - recusado, pausado ou desativado vira alerta.
- **Limite do número:** atualiza o limite do dia (`max_daily_conversations_per_business`).

### 19.3 Alertas ao super admin (D9)

- **Para quem:** os e-mails de **Configurações do sistema → Alertas do WhatsApp**. Saem pelo e-mail de envio do
  sistema.
- **O mesmo alerta não se repete em 12 horas.** Sem e-mail configurado, o alerta fica só no registro.
- **Quando sai:**
  - token vencido ou sem acesso: a conexão cai, e as campanhas vão só por e-mail até alguém testar de novo;
  - texto recusado, pausado ou desativado;
  - campanha que não pôde usar o WhatsApp;
  - qualidade do número média ou baixa.
- **Conferência do número de 6 em 6 horas:** o serviço lê na Meta a qualidade, o limite e se o token ainda vale.

## 20. Parte 5 — portal, Leads e respostas (pronta localmente em 22/10/2026)

- **Portal (§9):** a autorização passou a dizer o nome da empresa:
  - texto: "Autorizo a {empresa} a usar meus dados para contato e comunicações, conforme a Lei Geral de Proteção de
    Dados (LGPD)";
  - o nome vem no mesmo `/settings` que o portal já chamava, na mesma consulta ao banco, então não há chamada nem
    espera a mais;
  - tempo e redirecionamento não mudaram;
  - quem abre o portal com o tema guardado de antes vê o texto antigo por um instante, até a resposta chegar.
- **Leads:** quem respondeu "SAIR" ganha a marca **"Saiu do WhatsApp"** ao lado do nome. Passando o mouse, aparece a
  data e a explicação: não recebe mais a mensagem automática e continua no PDF do gestor. A marca é encontrada
  pelo telefone do cadastro.
- **Detalhe da execução:** na coluna WhatsApp, "Respondeu: “…”" ou "Pediu para sair: “SAIR”", com quantas
  respostas a mais o cliente mandou àquela execução.
- **Tela WhatsApp → Respostas dos clientes** (D10): **substituída na parte 7 (§21)** pela aba Conversas e pelo
  filtro "Só respostas" do Registro, que mostram o mesmo e mais. O endpoint `GET /admin/whatsapp/replies` saiu.
- **API:**
  - `LeadDto.whatsAppOptOutAt`;
  - `CampaignRecipientDto.lastReply` e `replyCount`;
  - `SettingsDto.companyName` (só na leitura do portal).
- **Ainda não existe "voltar a receber"** para quem respondeu SAIR. Se um cliente pedir para voltar, hoje é preciso
  mexer no banco. Dá para pôr um botão nos Leads depois, se fizer falta.

## 21. Parte 7 — menu WhatsApp: Conversas, Registro e Conta (pronta localmente em 23/10/2026)

O registro de tudo (§15) agora aparece no painel, só para leitura. O menu **WhatsApp** abre uma página com abas:

| Aba | Quem vê | O que mostra |
|---|---|---|
| **Conversas** | admin da empresa, super admin e usuário de unidade | os clientes com mensagens, da conversa mais recente para a mais antiga |
| **Registro** | os mesmos | tudo, enviado e recebido, com filtros |
| **Conta** | admin da empresa e super admin (D3) | o histórico da conta da empresa |
| **Configuração** | admin da empresa e super admin (D3) | a tela das partes 1 a 4 (credenciais, avisos da Meta, unidades, textos, interruptor) |

- **A página abre em Conversas.** A configuração continua igual, só foi para a última aba.
- **Usuário de unidade:** passou a ver o menu WhatsApp, só com Conversas e Registro, e só dos clientes das unidades
  dele. A unidade do cliente é a da **última visita**, como no resto do sistema.

### 21.1 Conversas

- **Lista:** nome, telefone, unidade, a última mensagem (de qualquer lado) com a hora, a situação da última
  enviada, quantas foram enviadas e quantas respostas vieram, e o selo "Saiu do WhatsApp". Busca por nome ou
  telefone, de 30 em 30.
- **Ao abrir um cliente:** o histórico completo, **separado por campanha** (D14):
  - cada mensagem enviada, com a campanha, o dia da execução, a versão do texto, o texto como o cliente leu e a
    situação com as horas (enviada, entregue, lida ou não entregue, com o motivo);
  - embaixo, as respostas que vieram até a mensagem seguinte, com "Pediu para sair" nos "SAIR";
  - os envios do "Enviar teste" aparecem como "Teste enviado por {usuário}";
  - o link do WhatsApp do cliente, para a loja responder pelo celular dela (D10).

### 21.2 Registro

- **Tudo, do mais novo para o mais antigo**, em páginas de 50: "→ Enviada" ou "← Resposta", hora, cliente, telefone,
  unidade, campanha, texto e situação.
- **Filtros:** período (sem período, os últimos 30 dias, no fuso da empresa), direção (tudo, só enviadas, só
  respostas), situação (Aceita, Enviada, Entregue, Lida, Não entregue, ou "Pediu para sair" nas respostas),
  campanha, unidade e telefone.
- **Detalhe de uma enviada** (ao clicar na linha):
  - cada aviso da Meta, com a hora e o motivo do erro;
  - a cobrança (categoria, tipo, se foi cobrada);
  - o número de origem, o texto da Meta usado e os valores preenchidos;
  - "Ver o que foi pedido à Meta" e "Ver o que a Meta respondeu". O pedido é guardado sem o token (§15.1), então
    nada secreto aparece.

### 21.3 Conta

O histórico da empresa, do mais novo para o mais antigo, em páginas: credenciais salvas, testes de conexão,
interruptor ligado ou desligado, WhatsApp das unidades, textos mandados à Meta e a situação de cada um, mudanças
de qualidade e limite do número e a conferência do endereço dos avisos. Cada linha diz quem fez e quando e, nas idas à
Meta, mostra o pedido e a resposta.

### 21.4 API (só leitura)

- `GET /admin/whatsapp/conversations?company=&search=&page=&pageSize=`
- `GET /admin/whatsapp/conversations/{customerId}?company=`
- `GET /admin/whatsapp/log?company=&from=&to=&direction=&status=&campaign=&unit=&phone=&page=&pageSize=`
- `GET /admin/whatsapp/messages/{id}?company=`
- `GET /admin/whatsapp/account?company=&page=&pageSize=` (403 para usuário de unidade)
- O super admin escolhe a empresa pelo seletor do topo (`?company=`), como nas outras telas.
- `GET /admin/whatsapp/replies` (parte 5) saiu: o mesmo está em Conversas e em Registro → "Só respostas".

### 21.5 Testes

- Teste novo: Conversas com a busca, o histórico separado por campanha com a resposta embaixo, os filtros do
  Registro, o detalhe com as situações na ordem, a Conta, e o usuário de unidade que só vê os clientes das unidades
  dele e não abre a Conta (403).
- Ponta a ponta local, com a Meta simulada: admin com as quatro abas; histórico com a campanha e as respostas;
  "Só respostas" com o SAIR marcado; filtro "Lida"; detalhe com situações e cobrança; pedido à Meta sem o token;
  Conta com "Envio automático ligado"; usuário da Filial só com Conversas e Registro e sem ver os clientes da
  Matriz.
