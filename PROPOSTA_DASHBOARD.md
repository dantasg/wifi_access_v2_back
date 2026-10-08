# Proposta — Dashboard por empresa e por unidade

> **Status (08/10/2026): aprovada; em implementação.** As decisões D1, D3 e D6 foram aprovadas como
> recomendadas, e as demais seguem a recomendação. Primeiro foi publicada **só a gravação das conexões**
> (tabela `Visits`, migration `ConexoesDoPortal`), para o histórico já começar a contar. A tela vem
> depois. Os números de exemplo são contagens reais da Itaituba, tiradas da produção só com leitura e sem
> nenhum dado pessoal.

## 1. Entendimento do pedido

Uma tela de números para o cliente acompanhar o Wi-Fi, em duas visões:

- **Empresa:** todas as unidades da empresa somadas, com a comparação entre elas.
- **Unidade:** tudo de uma unidade.

Cada pessoa vê só o que já pode ver hoje (`AccessScope`):

| Perfil | Visão empresa | Visão unidade |
| --- | --- | --- |
| Super admin | Qualquer empresa (escolhe no seletor, como nas outras telas) | Qualquer unidade |
| Admin da empresa | A empresa dele | Qualquer unidade da empresa |
| Usuário de unidade | Só a soma das unidades dele | Só as unidades dele |

---

## 2. O que temos e o que falta

Hoje o banco guarda **o primeiro cadastro e a última visita**, mas não guarda **cada visita**:

| Tabela | O que guarda | Serve para |
| --- | --- | --- |
| `Leads` | Um por aparelho em cada unidade: data do 1º cadastro, última conexão e AP | Cadastros novos por dia, hora e dia da semana |
| `Customers` | Um por telefone na empresa: 1ª e última visita, nº de dias com visita, nascimento | Base de clientes, retorno, faixa etária, aniversariantes |
| `CustomerUnits` | 1ª e última visita do cliente em cada unidade | Clientes por unidade, clientes que vão em mais de uma loja |
| `CampaignRuns` | Enviados, falhas e simulados de cada execução | Números das campanhas |

**Falta:** o registro de cada conexão. Sem ele não dá para mostrar:
- quantas conexões houve num dia (só os cadastros novos);
- os horários de pico reais (só o horário de quem se cadastrou);
- o retorno por unidade (o nº de visitas é da empresa, não da loja);
- a conexão por ponto de acesso.

Proposta (D3): uma tabela nova, `Visits`, com uma linha por liberação feita pelo portal.
- **Colunas:** unidade, cliente, data e hora, se é a 1ª visita e o AP.
- **Quando grava:** no `/authorize`, depois da liberação, do mesmo jeito que o cliente é gravado hoje. Não atrasa o visitante; se falhar, só fica no log.
- **Histórico:** conta a partir do dia em que for publicada. Antes disso, o dashboard mostra só o que dá para tirar das tabelas acima.

### O que a Itaituba mostraria hoje (dados reais, 08/10/2026)

- **Cadastros:** 110 (15 em setembro, 95 em outubro até agora).
- **Clientes:** 106 distintos por telefone, e 11 deles voltaram (10%). Média de 1,25 dias com visita; o mais fiel veio 8 dias.
- **Dias de movimento:** sexta e sábado concentram 56% dos cadastros.
- **Horários de pico:** 9h e 15h.
- **Faixa etária:** 25 a 34 anos é a maior, com 40%.
- **Dados preenchidos:** 99% informaram nascimento válido; 56% informaram Instagram.
- **Campanhas:** 7 execuções, 2 mensagens enviadas.

---

## 3. A tela

```
┌───────────────────────────────────────────────────────────────────────────────┐
│ Dashboard     Empresa: [Lojas Regional ▼]  Unidade: [Todas ▼]  Período: [30 dias ▼] │
├──────────────┬──────────────┬──────────────┬──────────────┬───────────────────┤
│ Conexões     │ Clientes     │ Voltaram     │ Taxa de      │ Base de clientes  │
│ 1.240 ▲12%   │ novos 380    │ 210          │ retorno 36%  │ 2.950             │
├──────────────┴──────────────┴──────────────┴──────────────┴───────────────────┤
│ Conexões por dia (novos × voltaram)        │ Horário de pico  │ Dia da semana  │
│ ▁▂▃▅▇▆▃▂▁▂▃▅▇█▆▃ ...                       │ ▁▃▇▅▃▂▃▆▅▃▂▁     │ ▂▅▄▄▅██        │
├────────────────────────────────────────────┼──────────────────┴────────────────┤
│ Unidades (só na visão empresa)             │ Faixa etária     Frequência       │
│ Loja        Conexões Novos Retorno Ativos  │ 18-24 ███        1 visita  ██████ │
│ Itaituba       420     130   38%    610    │ 25-34 ███████    2         ██     │
│ Castanhal      310     ...                 │ 35-44 ████       3-4       █      │
│ ...                                        │ ...              5+        ▏      │
├────────────────────────────────────────────┴──────────────────────────────────┤
│ Aniversariantes do mês: 84  ·  Com Instagram: 56%  ·  Campanhas: 3 envios, 412 mensagens │
└───────────────────────────────────────────────────────────────────────────────┘
```

Na **visão unidade** a tabela de unidades dá lugar a "Conexões por ponto de acesso" e "Clientes que também
vão a outras lojas da empresa".

### Definições

| Número | Como conta |
| --- | --- |
| Conexões | Liberações feitas pelo portal no período. A liberação vale pelo tempo de acesso da empresa (hoje 24 h na Lojas Regional), então quem volta dentro desse prazo não conta de novo |
| Clientes novos | 1ª visita na **empresa** (visão empresa) ou na **unidade** (visão unidade) dentro do período |
| Voltaram | Clientes com visita no período que já tinham vindo antes |
| Taxa de retorno | Voltaram ÷ clientes com visita no período |
| Base de clientes | Clientes da empresa ou da unidade até o fim do período (a retenção apaga quem ficou 24 meses sem voltar) |
| Ativos | Clientes com visita nos últimos 30 dias |
| Variação (▲▼) | Comparação com o período anterior, de mesmo tamanho |

Os dias e as horas seguem o fuso da empresa (`Companies.TimeZone`), como o relatório mensal.

---

## 4. Decisões para aprovação

| # | Decisão | Recomendação | Alternativa |
| --- | --- | --- | --- |
| **D1** | Onde fica | Item **Dashboard** no menu, sendo a **primeira tela depois do login**, para todos os perfis | Item comum, com a tela de Leads continuando como a primeira |
| **D2** | Visões | Empresa (soma + tabela por unidade) e unidade, na **mesma tela**, trocando pelo seletor de unidade | Duas telas separadas |
| **D3** | Registro de cada conexão | **Tabela `Visits` nova**, gravada no `/authorize` depois da liberação. Conta a partir da publicação | Só o que dá para tirar das tabelas atuais (sem conexões por dia, sem pico real, sem retorno por loja) |
| **D4** | Período | Padrão: **últimos 30 dias**, com 7 dias, mês atual, mês anterior e datas livres. Sempre comparado com o período anterior | Só mês fechado, como o relatório |
| **D5** | Privacidade | **Só números agregados**. Nome e telefone continuam só na tela de Leads | Lista de "clientes mais fiéis" no dashboard |
| **D6** | Gráficos | **SVG próprio** (barras e linha), sem biblioteca nova. Só o painel carrega; o portal do visitante não muda | Biblioteca pronta (ex.: Recharts, +~100 KB) |
| **D7** | Desempenho | Contas **na hora**, com índices em `Visits (IDUnit, At)`. 53 lojas × ~400 conexões/mês ≈ 250 mil linhas/ano, o que o Postgres soma em milissegundos. Resumo diário pré-calculado só se ficar lento | Resumo diário desde o início |
| **D8** | Retenção | `Visits` segue a regra dos cadastros. Quando um cliente é apagado após 24 meses, as visitas dele ficam **sem o vínculo com o cliente**, e os totais antigos não mudam | Apagar as visitas junto |
| **D9** | Atualização | Ao abrir a tela e com um botão "Atualizar". Sem tempo real | Atualizar sozinho a cada minuto |
| **D10** | Exportar | **Depois.** Primeiro a tela; depois o relatório mensal por e-mail pode usar os mesmos números | PDF do dashboard já nesta etapa |

---

## 5. O que muda em cada lugar

- **Banco:** tabela `Visits` (migration) e índice.
- **API:**
  - gravação no `AuthorizeController`, depois da liberação;
  - `GET /admin/dashboard?company=&unit=&from=&to=`, que devolve todos os números da tela numa chamada só, já filtrados pelo `AccessScope`.
- **nginx:** `dashboard` entra na lista de rotas da API em `deploy/nginx/90-accesswifi-api.conf`. O script de publicação já recoloca esse arquivo.
- **Front:**
  - tela `DashboardPanel`, com seletores, cartões e gráficos em SVG;
  - item no menu;
  - modo demonstração com números de exemplo.
- **Testes:** contas de cada número, escopo por perfil (o usuário de unidade não vê outra loja), fuso da empresa e período anterior.

## 6. Próximo passo

Com as decisões aprovadas (ou ajustadas), monto antes um **protótipo da tela** com os números de exemplo
para a equipe ver. Depois implemento, testo local e publico.

Se a D3 for aprovada, vale publicar **só o registro das conexões** antes do resto, porque o histórico
começa a contar no dia em que ele entra no ar.
