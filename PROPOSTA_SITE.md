# Proposta: site institucional do AccessWifi

## 1. O pedido

Um site para apresentar o AccessWifi a clientes novos e, depois, receber tráfego pago (Google e Meta).

Respostas já dadas (27/10/2026):

- **Domínio:** por enquanto, o endereço do servidor. O domínio próprio entra antes dos anúncios.
- **Marca:** só AccessWifi.
- **Preços:** o site não mostra preços; eles vão no orçamento.
- **Contato:** WhatsApp e formulário. Cada pedido fica salvo no painel e chega por e-mail.

## 2. Onde o site fica

- No servidor atual, o portal do Wi-Fi atende o `/` e qualquer endereço que não exista (`AccessWifi_V2_FRONT/src/App.tsx:9` e `:19`). Isso não pode mudar, porque o Hotspot das lojas aponta para lá.
- Por isso, enquanto não houver domínio, o site fica em **`https://vps11702.panel.icontainer.online/site/`**:
  - uma pasta própria, entregue direto pelo nginx, sem passar pelo portal nem pela API;
  - o portal, o painel e o redirecionamento ficam exatamente como estão.
- Quando o domínio chegar (ex.: `accesswifi.com.br`):
  - o mesmo site passa para a raiz do domínio, com certificado próprio;
  - o `/site/` passa a mandar para lá;
  - no site, muda uma linha de configuração.

## 3. Conteúdo

O site tem uma página principal longa e duas páginas de apoio. O texto sai da apresentação dos clientes, que já foi aprovada (`GeradorApresentacoes/build-main.js`).

### Página principal, de cima para baixo

1. **Abertura:** "Wi-Fi que trabalha para as vendas" e "O Wi-Fi grátis da sua loja vira cadastro de clientes e campanhas automáticas". Botões **Falar no WhatsApp** e **Pedir demonstração**.
2. **O problema:**
   - o cliente usa o Wi-Fi e vai embora sem deixar nada;
   - aniversários e clientes que deixam de vir passam sem contato;
   - o marketing depende só de anúncio e do alcance das redes.
3. **Como funciona, em 4 passos:**
   1. o cliente conecta no Wi-Fi ou lê o QR Code;
   2. faz o cadastro e autoriza o uso dos dados (LGPD);
   3. a internet é liberada na hora;
   4. o cadastro aparece no painel e entra nas campanhas da loja.
4. **O portal com a marca da loja:** o print do portal no celular, com logo, cores e banner da loja.
5. **O painel:** Dashboard, Leads, Campanhas, relatórios por e-mail, Correio, Configurações e Usuários, com prints.
6. **Campanhas automáticas:**
   - aniversariantes da semana;
   - 1 ano de cadastro;
   - sentimos sua falta;
   - cliente frequente.
   Também mostra o que o gerente recebe: o PDF por loja com quem chamar.
7. **Várias lojas:**
   - a loja é reconhecida pelo ponto de acesso;
   - os números ficam separados e somados;
   - loja nova entra sem programação.
8. **Segurança e LGPD:**
   - autorização no cadastro;
   - senhas e chaves cifradas;
   - backup diário;
   - conferência a cada 5 minutos;
   - os dados são apagados depois de 24 meses sem visita.
9. **O que é preciso:** rede UniFi e uma configuração única do Wi-Fi de visitantes, feita pelo TI.
10. **Para quem:** varejo, farmácias, parques, restaurantes e cafés, e redes com várias lojas.
11. **Perguntas frequentes:**
    - Precisa de aplicativo?
    - Funciona no iPhone?
    - E se eu não tiver UniFi?
    - Quanto tempo leva para implantar?
    - De quem são os dados?
    - Como fica a LGPD?
12. **Contato:** o formulário e o botão do WhatsApp.

O **rodapé** traz a política de privacidade, o e-mail e o WhatsApp.

### Páginas de apoio

- **`/site/obrigado`:** abre depois do envio do formulário. Ela permite medir a conversão nos anúncios.
- **Privacidade:** a página atual (`/privacidade.html`) ganha uma parte "Contato pelo site". Nessa parte, o AccessWifi é o responsável pelos dados, e não a loja.

## 4. Formulário de contato

### Campos que o visitante preenche

- nome;
- empresa;
- WhatsApp;
- e-mail (opcional);
- cidade e UF;
- quantas lojas;
- se já usa UniFi (sim, não ou não sei);
- mensagem (opcional);
- autorização de contato (obrigatória, com link para a política).

### Dados gravados sem aparecer no formulário

O sistema também grava a origem do visitante:

- `utm_source`, `utm_medium`, `utm_campaign`, `utm_term` e `utm_content`;
- `gclid` (Google) e `fbclid` (Meta);
- a página e o horário.

É isso que diz qual anúncio trouxe cada cliente.

### No sistema (C#, no padrão atual)

- **Banco:** tabela `SiteContacts`.
- **Rota pública** `POST /site-contact`:
  - limite por IP, por exemplo 5 por minuto, como a "unifi-webhook" do `Program.cs`;
  - campo escondido contra robôs;
  - validação no servidor.
- **Aviso por e-mail:** sai pelo SMTP global que já está configurado no painel.
- **Tela "Contatos do site" no painel**, só para super admin:
  - lista com filtros e a origem de cada contato;
  - situação: Novo, Em conversa, Virou cliente ou Descartado.
- **Retenção:** o contato é apagado 12 meses depois, se não virar cliente. Isso entra no `LeadRetentionService`, como as outras regras.
- **nginx:** ganha a linha da rota nova, como toda rota nova da API.
- **Ramo de trabalho:** a mudança é feita num ramo próprio, a partir do `main`, sem o WhatsApp que ainda não foi commitado. Assim, ela pode ser publicada sozinha.

## 5. Medição para os anúncios

- **Rastreio de terceiros:** Google Analytics 4, Google Ads e Pixel da Meta só funcionam depois do aceite no aviso de cookies (LGPD). Sem aceite, nada de terceiros é carregado. O site já sai preparado, e os códigos entram quando você criar as contas de anúncio.
- **Conversões:** o envio do formulário (página `/obrigado`) e o clique no WhatsApp.
- **Fora do Google por enquanto:** enquanto estiver no endereço provisório, o site fica com `noindex`, para o Google não guardar esse endereço. O Search Console e o mapa do site entram com o domínio.

## 6. Tecnologia

- **Astro + Tailwind v4:**
  - gera as páginas em HTML pronto, sem depender de JavaScript para mostrar o conteúdo;
  - o site abre rápido, e o Google Ads cobra menos pelo clique quando a página é rápida;
  - usa o mesmo Tailwind do painel.
- **Projeto próprio:** `AccessWifi_V2/AccessWifi_V2_SITE`, com git separado, como o front e o back.
- **Publicação:** um script `publicar-site.sh`, no modelo do `publicar-producao.sh` (só código commitado):
  - faz o build e copia para o servidor;
  - não reinicia nada e não mexe na API.
- **Imagens:** prints otimizados (WebP), com o selo "dados fictícios".

## 7. Identidade visual

- **Logo:** o símbolo de Wi-Fi que o sistema já usa (`AccessWifi_V2_FRONT/public/favicon.svg`) mais o nome "AccessWifi".
- **Cores:** o verde-azulado do painel (`#0f6a62` e `#14a697`, em `AccessWifi_V2_FRONT/src/index.css:23` e `:33`) sobre fundo claro, e o botão do WhatsApp no verde do WhatsApp.
- **Celular primeiro:** a maior parte do tráfego pago vem do celular.

## 8. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
|---|---|---|---|
| D1 | Endereço provisório | **`/site/` no servidor atual, fora do Google (`noindex`) até ter domínio** | Deixar só local até ter domínio |
| D2 | Tecnologia | **Astro + Tailwind: HTML pronto, rápido** | Vite + React, igual ao painel: abre mais devagar e é pior para o Google |
| D3 | Estrutura | **Uma página longa + `/obrigado`. Páginas por segmento (farmácia, varejo…) só quando os anúncios começarem** | Várias páginas desde já: Funcionalidades, Segmentos, Contato |
| D4 | Prints das telas | **Os da empresa teste GD Tecnologia: já prontos, com dados fictícios** | Refazer com uma "Empresa Exemplo" neutra, nas cores do AccessWifi |
| D5 | WhatsApp nas campanhas | **Não citar até estar publicado** | Citar como "em breve" |
| D6 | Quem somos | **Sem nome de pessoa, como na política de privacidade** | Seção com Genival Dantas, como na apresentação |
| D7 | Clientes atuais | **Sem logos nem nomes de clientes enquanto não houver autorização por escrito** | Pedir autorização a 1 ou 2 clientes para um depoimento |
| D8 | Exigência da UniFi | **Dizer claramente "funciona com redes UniFi" e perguntar no formulário se já usa** | Não citar e tratar na conversa |
| D9 | Campos do formulário | **Os 9 do §4** | Só nome e WhatsApp: mais pedidos, menos informação |
| D10 | Aviso de contato novo | **E-mail para genineto10@gmail.com** | Outro e-mail |
| D11 | Retenção dos contatos | **12 meses, se não virar cliente** | 24 meses, como os cadastros do portal |
| D12 | Rastreio dos anúncios | **Preparado agora, mas ligado só com o aviso de cookies e quando houver contas de anúncio** | Ligar já |
| D13 | Número do WhatsApp | **O mesmo do balão do portal (`VITE_WHATSAPP_NUMBER`, `AccessWifi_V2_FRONT/src/config.ts:20`)** | Outro número |

## 9. Fases e prazos

Os prazos seguem o ritmo real do histórico.

1. **Site com todo o conteúdo**, rodando local para você ver: cerca de 1 dia.
2. **Formulário, tela no painel e e-mail**, testados local: cerca de 1 dia.
3. **Publicar em `/site/`:** só com o seu ok, porque mexe no nginx da produção.
4. **Domínio, certificado, aviso de cookies, Pixel e Google, e páginas por segmento:** depende da compra do domínio e das contas de anúncio.

## 10. Decisões aprovadas

27/10/2026: "Faça as recomendações". Todas as recomendações de D1 a D13 valem, com uma exceção:

- **D5 mudou no mesmo dia.** O pedido foi "Coloque que temos integração com a META".
- O site ganhou a seção **"Integração com a Meta"** (`#whatsapp`), com a conversa de exemplo no celular e os pontos
  da PROPOSTA_WHATSAPP.md:
  - API oficial;
  - textos aprovados pela Meta;
  - situação de cada mensagem;
  - PDF ao gerente com quem não recebeu;
  - SAIR;
  - conta e número da empresa;
  - cobrança da Meta paga pela empresa.
- A integração também foi citada:
  - na abertura;
  - no menu;
  - nas campanhas;
  - nas perguntas, com uma nova sobre o risco de bloqueio;
  - na descrição da página;
  - na imagem de compartilhamento.
- Não se fala em "parceiro da Meta" nem se usa o logo da Meta.

## 11. Como ficou (fases 1 e 2, testadas local; nada publicado)

### Site (`AccessWifi_V2/AccessWifi_V2_SITE`, repositório próprio, ainda sem commit)

- Uma página longa:
  - abertura;
  - o problema;
  - 4 passos;
  - portal com a marca;
  - painel;
  - campanhas e o que o gerente recebe;
  - várias lojas;
  - segurança e LGPD;
  - o que é preciso;
  - para quem;
  - perguntas frequentes, com os dados estruturados de FAQ para o Google;
  - contato.
- Página `/obrigado/`, cabeçalho fixo, botão flutuante do WhatsApp e imagem de compartilhamento (`og.png`).
- **Origem do visitante:** UTM, gclid, gbraid, wbraid e fbclid ficam no navegador, por até 90 dias. Um clique de
  anúncio substitui a origem anterior. Vão junto com o formulário.
- **Aviso de cookies e Google/Meta:** só aparecem quando houver algum código no `.env.production`. Hoje está vazio,
  então não há aviso nem rastreio de terceiros.
- `noindex` enquanto estiver em `/site/`.

### Back (ramo `site-contato`, ainda sem commit)

- Tabela `SiteContacts` (migration `SiteContacts`, só cria a tabela).
- **Rota pública `POST /site-contact`:**
  - limite de 5 por minuto por IP;
  - campo-isca contra robôs: o robô recebe "ok" e nada é gravado;
  - validação com a mensagem de cada campo;
  - CORS só para as origens de `SiteContact:AllowedOrigins` (vazio hoje).
- **Tela do super admin, `admin/site-contacts`:**
  - lista com busca e abas por situação, com as contagens;
  - mudar situação e anotações, guardando quem mudou e quando;
  - apagar.
- **Aviso por e-mail:** sai pelo serviço (`SiteContactNotifier`), a cada minuto, para `SiteContact:NotifyEmail`
  (genineto10@gmail.com no `appsettings.json` do serviço). Se falhar, tenta até 5 vezes, e o motivo aparece no
  painel.
- **Retenção:** `Retention:SiteContactMonths` = 12; quem virou cliente fica.
- **nginx:** `/site-contact`, `site-contacts` na lista do admin e `/site/` servindo `index/site/`, sem fallback
  para o portal.
- **Publicação:** alvo `site` no `publicar-producao.sh`. A publicação do portal passou a preservar a pasta `site/`.
- 23 testes novos (`SiteContactTests`). Os 457 do back passam.

### Front (ramo `site-contato`, ainda sem commit)

- Item "Contatos do site" no menu do super admin sem empresa escolhida (FRONT_CHANGES PARTE 38).
- Política de privacidade com a parte "11. Contato pelo site do AccessWifi" (`#contato-site`) e o parágrafo do site
  em "Cookies".

### Teste de ponta a ponta local (27/10/2026)

Banco `accesswifi_teste_site`, API na porta 5050 e SMTP falso. Tudo funcionou:

- Chegada por anúncio, com a origem guardada.
- Envio vazio barrado, com o aviso no formulário.
- Máscara do telefone.
- Envio levando para `/obrigado/`.
- Contato gravado com gclid e UTM.
- E-mail com o link do WhatsApp, a origem, a campanha e a palavra-chave.
- Painel com a origem "Anúncio Google" e a mudança para "Em conversa" com "Mudada por root".
- Erro da API ("Confira o e-mail.") mostrado no formulário.
- Robô recebendo "ok" sem gravar.
- 429 depois de 5 envios.

## 12. Para publicar (precisa do seu ok)

1. Commit dos ramos `site-contato` (back e front) e do repositório do site. Juntar no `main`, com o WhatsApp
   continuando sem commit, como foi feito com as visitas.
2. `publicar-producao.sh tudo` (API, serviço, portal e migration), a partir de cópias limpas, por causa do WhatsApp
   sem commit no `main`. Depois, `publicar-producao.sh site`.
3. **Conferir no servidor, antes, só leitura:** o `root` do nginx precisa estar no bloco `server`, não só no
   `location /`. É dele que a regra `/site/` herda a pasta.
4. Testar no ar: `/site/`, o envio do formulário, o e-mail e a tela do painel. Apagar o contato de teste.

**Antes dos anúncios:** domínio próprio, `PUBLIC_INDEX=true` e os códigos do Google e da Meta (ver o README do
site).
