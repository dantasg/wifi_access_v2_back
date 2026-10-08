# Proposta — Mesmo endereço para todas as lojas, unidade pelo ponto de acesso

> **Status (06/10/2026): proposta, testada só com leitura na produção. Nada mudou.**
> Alternativa à [PROPOSTA_SUBDOMINIO_POR_UNIDADE.md](PROPOSTA_SUBDOMINIO_POR_UNIDADE.md). Atende ao
> requisito de ser tudo automático, sem certificado novo por loja e sem espera para loja nova.

## 1. Resposta curta

**Funciona, com um ajuste:** a UniFi não manda o MAC da **controladora**. Ela manda o MAC do
**ponto de acesso (AP)** em que o celular está conectado. Só que cada AP pertence a uma controladora
e a uma unidade, e o sistema descobre isso sozinho, pela chave da UniFi que cada unidade já tem.

Assim, todas as lojas usam o mesmo endereço de hoje (`vps11702.panel.icontainer.online`), com o
certificado que já existe e que o painel do provedor já renova. Não há domínio novo, certificado novo
ou espera.

---

## 2. O que foi testado (produção, só leitura)

| # | Teste | Resultado |
| --- | --- | --- |
| 1 | O que a UniFi manda no endereço do portal (log do nginx) | `ap`, `id`, `t`, `url`, `ssid`. **Não há MAC da controladora** |
| 2 | Cadastros da Itaituba | 93 de 93 chegaram com `ap`, sempre `8c:30:66:4e:9b:58` |
| 3 | Aparelhos da Itaituba, pela chave já cadastrada | Controladora: **UCG Ultra** `58:d6:1f:5e:15:31`. Switch USW-16-PoE. AP **U7 Lite** `8c:30:66:4e:9b:58`, o mesmo que chega no portal |
| 4 | Alcance da chave | Enxerga **18 consoles, todos lojas da Regional**. Todos são UCG Ultra, online, com 1 site cada |
| 5 | **MAC repetido, com os dados reais** | **50 aparelhos, 50 MACs diferentes, nenhum repetido.** Os 17 APs têm 17 MACs diferentes e as 18 controladoras têm 18 MACs diferentes. O AP da Itaituba aparece em 1 console só |
| 6 | Fabricante dos MACs | As 6 faixas encontradas (`8c:30:66`, `1c:0b:8b`, `d8:b3:70`, `1c:6a:1b`, `9c:05:d6`, `58:d6:1f`) são da **Ubiquiti** no registro oficial do IEEE |
| 7 | Tempo para listar | **Uma chamada só traz os aparelhos das 18 lojas em 0,2 s.** Console a console, pela API da rede, leva de 1 a 10 s por loja. A sincronização usa a chamada única |
| 8 | Lojas sem AP | **CAMETA-04 não tem nenhum AP na UniFi**, só a controladora. Sem AP UniFi o portal não funciona lá, em qualquer solução |

Os testes 3 a 7 usaram uma ferramenta descartável: decifrou a chave no próprio servidor, fez só
consultas (GET), não imprimiu a chave e foi apagada em seguida.

---

## 3. Como funciona

```
Celular conecta no AP U7 Lite ──► UniFi redireciona para
https://vps11702.panel.icontainer.online/guest/s/default/?ap=8c:30:66:4e:9b:58&id=…   (igual para todas as lojas)
                                                     │
                       tabela "AP → unidade" (no banco, instantâneo) ──► Itaituba ──► tema + liberação na UniFi certa
                                                     ▲
       rotina em segundo plano: a cada 5 min, pergunta a cada controladora quais aparelhos ela tem
```

1. **Sincronização em segundo plano.** A cada 5 minutos, e na hora em que alguém salva a UniFi de
   uma unidade no painel, o sistema lista os aparelhos e grava "MAC → unidade". É uma chamada por
   chave: hoje uma só cobre as 18 lojas, em 0,2 s. Grava todos os aparelhos, não só os APs, porque o
   `ap` sempre será o MAC de um deles.
2. **Na visita.** O portal manda o `ap` que a UniFi já entrega. A API acha a unidade na tabela, sem
   chamar a UniFi, e a velocidade é a mesma de hoje.
3. **Ordem para achar a unidade:**
   1. `?unit=` (como hoje);
   2. o AP, na tabela;
   3. se o AP for desconhecido ou estiver em mais de uma unidade: procurar na hora, nas controladoras,
      o AP e o celular (`id`). O celular só aparece na controladora da loja em que ele está;
   4. o endereço, **só se ele for exclusivo de uma unidade**.

   Um endereço compartilhado por várias lojas nunca escolhe a loja sozinho: chutar seria mandar o
   cadastro e a liberação para a loja errada.

**Loja nova:**
1. Cadastrar a unidade e a chave da UniFi no painel. Isso já é necessário hoje, para liberar o
   visitante.
2. Os APs aparecem sozinhos em segundos.
3. O TI da loja copia na UniFi a **mesma** configuração de todas as lojas:
   - External Portal Server: `216.22.13.216`;
   - Domain: `vps11702.panel.icontainer.online`;
   - Pre-Authorization Access: o nome e o IP.

---

## 4. Riscos

| Risco | Chance | O que acontece | Proteção |
| --- | --- | --- | --- |
| **Dois aparelhos com o mesmo MAC** | Praticamente nula, e medida: 0 repetidos em 50 aparelhos das 18 lojas. O MAC é gravado na fábrica, de uma faixa que o IEEE dá só para a Ubiquiti, e a UniFi não deixa trocar o MAC de um AP | — | Se acontecer, o AP aparece em duas unidades e o sistema não escolhe: procura o celular (`id`) nas controladoras e avisa |
| **Controladora com MAC repetido** | Não afeta | O MAC da controladora não é usado. A controladora é identificada pelo ID da nuvem da Ubiquiti, que cada unidade já guarda | — |
| **Mesma controladora cadastrada em duas unidades** (erro de cadastro) | Baixa | Os mesmos APs nas duas | O painel recusa salvar e avisa |
| **Duas lojas no mesmo site da UniFi** (o TI juntou as lojas) | Baixa. Hoje cada loja tem a sua controladora | A controladora não diz qual AP é de qual loja | O painel deixa escolher à mão quais APs são de cada unidade |
| **AP novo ou trocado na garantia** (MAC novo) | Normal | Fica desconhecido até a próxima sincronização (≤ 5 min) | Procura na hora (~0,2 s, uma vez para aquele AP) e grava |
| **AP levado para outra loja** | Rara | Até a próxima sincronização, a tabela aponta a loja antiga | A sincronização corrige. Antes disso, a liberação na loja antiga falha, e o sistema procura o celular e acha a loja certa |
| **UniFi parar de mandar o `ap`** (atualização) | Baixa. O `ap` está no redirecionamento há anos e todo portal comercial usa | Sem AP, sobra o celular (`id`) | Procura o celular nas controladoras (~1 s) e avisa pelo Telegram/e-mail |
| **Nuvem da UniFi fora do ar** | Rara | A tabela continua valendo, então a loja é achada | A liberação já depende da nuvem hoje. Isso não muda |
| **Chave da UniFi revogada** | Rara | A sincronização para; a tabela continua valendo | Aviso. A liberação já depende da chave hoje |
| **`ap` digitado à mão no endereço** | Possível | A pessoa vê o tema de outra loja | A liberação exige o aparelho na rede daquela loja, como hoje |
| **Troca de provedor** | Decisão nossa | Cada loja troca o Domain na UniFi | Vale para qualquer opção com o nome do provedor. Um domínio próprio resolve depois |

---

## 5. Comparação

| | Subdomínio no nome do provedor | Domínio próprio com curinga | **Mesmo endereço, unidade pelo AP** |
| --- | --- | --- | --- |
| Certificado | Um por loja (espera na loja nova) | Um curinga, renovado por nós | **O de hoje, renovado pelo painel** |
| Custo | Zero | Domínio (~R$ 40/ano) | **Zero** |
| Configuração na UniFi | Nome diferente por loja | Nome diferente por loja | **Igual em todas** |
| Depende de | DNS do provedor | DNS na Cloudflare | **A chave da UniFi, que já é obrigatória** |
| Loja nova | Espera o certificado | Na hora | **Na hora** (APs em segundos) |

---

## 6. Decisões

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Como achar a unidade | Pelo AP; `?unit=` antes e o endereço por último | Subdomínio por loja |
| **D2** | Endereço | Todas as lojas em `vps11702.panel.icontainer.online` | Domínio próprio, um nome só |
| **D3** | Sincronização | Em segundo plano, a cada 5 min e ao salvar a UniFi da unidade | Só ao salvar |
| **D4** | AP desconhecido ou em duas unidades | Procurar o AP na hora (~0,2 s) e gravar. Se não achar, procurar o celular nas controladoras (1 a 10 s, caso raro). O endereço só vale se for exclusivo de uma unidade | Mostrar "portal não configurado" |
| **D5** | Painel | Mostrar os APs de cada unidade, quando sincronizou e um botão "sincronizar agora" | Sem tela |
| **D6** | Avisos | Telegram/e-mail se chegar visita de AP que nenhuma unidade tem, ou se um AP aparecer em duas | Só log |
| **D7** | Itaituba | Nada muda na loja nem no redirecionamento; passa a ser achada pelo AP | — |

## 7. Próximo passo

Com o ok, implemento e testo tudo local (com APs simulados). Depois publico. Nada muda para a
Itaituba: ela continua sendo achada também pelo endereço.
