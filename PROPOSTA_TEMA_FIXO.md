# Proposta — Portal já abre com o tema da Regional (sem passar pelo padrão)

> **Status (09/10/2026): substituída — não será implementada.** Desde 08/10 todas as lojas usam o mesmo
> endereço (a loja sai do ponto de acesso), então fixar o tema de uma empresa no endereço deixaria as outras
> com a cor errada. No lugar dela: o portal não mostra nada até o tema certo chegar (no máximo 4 s; depois
> abre no neutro), o tema veio de ~100 KB para ~1 KB (imagens em endereço próprio, guardadas no celular)
> e o celular guarda o tema por loja para quem volta. Empresa sem tema abre num padrão neutro.
>
> *Antes:* aguardando o "ok" nas decisões (seção 5).
> Já publicado à parte: o exemplo do telefone no portal virou **(93) 90000-0000** (front `4b16fec`).

## 1. Entendimento do pedido

O portal aparece primeiro com o tema **padrão** e, um instante depois, troca para o tema da **Regional**.
Como o endereço do portal é usado só pela Regional, o pedido é:

- sempre que o tema da Regional for **alterado**, gerar uma cópia **fixa** dele;
- o portal já abrir com essa cópia — o tema da loja passa a ser o "padrão" desse endereço;
- a cópia **só é refeita quando o tema muda** (nada a cada visita);
- **nenhum impacto** na velocidade do portal.

---

## 2. Como está hoje

| O quê | Onde |
| --- | --- |
| O portal começa com o tema neutro | `AccessWifi_V2_FRONT/src/theme/ThemeContext.tsx:139` — `useState(systemApplied)` |
| Só depois pede o tema ao back, pelo endereço | `ThemeContext.tsx:222` — `GET /settings?host=…` — e aplica em `:227` |
| Logo, favicon e banner ficam no banco **como texto** (data URL) e vêm dentro dessa resposta | `src/Models/DataBase/PortalSettings.cs:17-19` · `Controllers/SettingsController.cs:38` |
| O tema é salvo no admin (Configurações) | `Controllers/SettingsController.cs:79` (grava em `:130`) |
| A publicação do portal apaga a pasta do site inteira e copia a nova | `deploy/publicar-producao.sh:234` |

O que está na produção (consulta só de leitura, 01/10):

| Empresa | Logo | Favicon | Banner | Endereço do portal |
| --- | --- | --- | --- | --- |
| **Regional** (unidade Itaituba) | **121 KB** | 4 KB | 61 KB | `vps11702.panel.icontainer.online` |
| Empresa de teste (Matriz) | 38 KB | 1 KB | 119 KB | *(nenhum)* |

**Por que pisca:** a resposta do `/settings` da Regional tem uns **186 KB** (as três imagens em texto). O
portal desenha a tela padrão e só troca quando essa resposta inteira chega — num Wi-Fi fraco, isso é
bem visível. E o botão "Conectar" também depende dessa resposta (é ela que diz a unidade).

---

## 3. Como fica

**Quando alguém salva o tema da Regional** (e só nessa hora), a API gera:

```
/www/sites/vps11702.../tema/
  bloco.html              ← cores + endereço das imagens (uns 2 KB)
  logo-3f2a9c.png         ← a imagem de verdade, não mais texto (nome muda quando a imagem muda)
  banner-81d04e.jpg
  favicon-a19b77.png
```

**Quando um visitante abre o portal**, o próprio servidor (nginx) já entrega a página com o `bloco.html`
dentro dela. Não há pedido a mais: na primeira pintura da tela, cores, logo e banner já são os da Regional.
As imagens vêm como arquivos normais, que o celular guarda em cache — na segunda visita nem baixa de novo.

```
Hoje:   página → app → tela PADRÃO → /settings (186 KB) → tela da REGIONAL   (pisca)
Depois: página já com o tema → app → tela da REGIONAL                         (não pisca)
```

**Fora isso, nada muda:** o painel admin continua com o tema dele; outras empresas seguem como hoje.

---

## 4. Riscos e cuidados

- **Publicação não apaga o tema fixo:** a pasta `tema/` fica **ao lado** da pasta do site, não dentro —
  o `rm -rf` da publicação (`deploy/publicar-producao.sh:234`) não alcança.
- **Se a geração falhar** (ex.: disco cheio), o salvamento do tema **não falha** — só fica registrado
  no log, e o portal segue como hoje (padrão → tema), até o próximo salvamento.
- **Sem `bloco.html`** (antes da primeira geração), a página sai exatamente como hoje.
- **Painel admin** usa a mesma página: o bloco só se aplica fora de `/admin`.
- O logo da Regional tem **121 KB**, grande para um logo. Não mexo nele aqui, mas reduzir esse arquivo
  deixaria o portal ainda mais rápido (pode ser feito trocando a imagem no admin).

---

## 5. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Qual empresa tem o tema fixo nesse endereço | **Uma configuração no servidor dizendo "regional"** — simples, e o endereço é só dela | Uma opção na tela de Empresas (mais trabalho, para um caso único) |
| **D2** | Como o tema chega já pronto na página | **O nginx coloca o `bloco.html` dentro da página na hora de entregar (sem pedido a mais)** | Um arquivo `tema.js` separado (1 pedido a mais em toda visita) · ou reescrever o `index.html` a cada publicação |
| **D3** | Como guardar as imagens fixas | **Arquivos de imagem de verdade, com cache longo** (o nome muda quando a imagem muda) | Dentro da página como texto (a página fica ~190 KB mais pesada e nada fica em cache) |
| **D4** | Primeira geração (hoje não existe cópia fixa) | **Gerar uma vez na próxima publicação, se ainda não existir**; depois, só quando o tema for salvo | Esperar alguém salvar o tema no admin |
| **D5** | Aproveitar e tirar as imagens da resposta do `/settings` quando o portal já tiver o tema fixo em dia | **Sim** — cada visitante deixa de baixar ~186 KB à toa, e o botão "Conectar" fica liberado mais cedo. Se o tema fixo estiver desatualizado, o portal percebe (pela versão) e baixa completo como hoje | Não — deixa o `/settings` como está (o visitante continua baixando os 186 KB, só que sem piscar) |
| **D6** | Se esse endereço for aberto com `?unit=` de **outra** empresa | **Ignora o tema fixo e segue o fluxo de hoje** | Mostra o da Regional primeiro de qualquer jeito |

---

## 6. Próximo passo

Com o "ok" (ou as trocas) nas decisões, implemento e testo **localmente** (inclusive a página saindo do
nginx com o tema dentro), e depois publico com o seu sinal.
