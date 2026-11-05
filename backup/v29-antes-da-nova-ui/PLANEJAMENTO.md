# Plugin ZWCAD – Redes de Água Municipais (estilo Plant 3D, focado em saneamento)

Status: planejamento inicial (nenhum código escrito ainda).

## Objetivo
Plugin para ZWCAD para projetos de tubulações de água municipais: redes de DN50 até adutoras,
luvas, juntas mecânicas, flanges, peças especiais, registros, conexões etc.

## Decisão técnica
- Linguagem: **C# / .NET API do ZWCAD** (namespaces `ZwSoft.ZwCAD.*`, similar à API do AutoCAD). Requer ZWCAD Professional.
- ZRX (C++) só se precisar de entidades customizadas muito pesadas. LISP/COM só para protótipos.
- O ZWCAD não tem framework de plantas (specs/catálogos) → construir próprio.

## Arquitetura proposta
1. Catálogo/spec em JSON ou SQLite (material, DN, PN, norma, dimensões, código).
   Materiais: ferro dúctil, PVC/DEFoFo, PEAD, aço. Norma de partida: ABNT NBR 7675.
2. Peças inteligentes: blocos 3D/sólidos paramétricos + XData (DN, PN, material, código) + pontos de conexão.
3. Roteamento: desenhar eixo → insere tubos, luvas e conexões automaticamente (curvas, derivações, reduções).
4. Validação de compatibilidade DN/PN/junta entre peças conectadas.
5. Saídas: quantitativo/lista de materiais, tabelas, perfil longitudinal, exportação Excel.
6. UI: paleta de catálogo, comandos próprios, propriedades das peças.

## Fases
| Fase | Entrega |
|---|---|
| 1. MVP | Catálogo + inserção de peças 2D/3D + tubo entre pontos + quantitativo |
| 2 | Conexões automáticas, validação DN/PN, derivações |
| 3 | Perfil longitudinal, cotas e anotações automáticas |
| 4 | Integração cadastro/GIS, relatórios, orçamento |

## Respostas do usuário até agora
1. Versão do ZWCAD: vai atualizar para **25.23.0.19981** (confirmar se é Professional).

2. Foco: **2D (planta com simbologia) e 3D, ambos**.
3. **Claude escreve o código; o usuário valida** no ZWCAD.
4. Materiais: **PVC, DEFoFo, PEAD, PVCO**. Registro gaveta de ferro fundido, flangeado ou de bolsa.
   Redes de DN50 até adutoras de DN600-700.
5. Catálogos de fabricantes: **ainda não tem** (começar com dimensões típicas das normas; catálogos entram depois).

## Ambiente verificado (2026-10-13)
- ZWCAD 2025 em `C:\Program Files\ZWSOFT\ZWCAD 2025`, versão 25.23.0.19981 (confirmada).
- DLLs gerenciados presentes: ZwManaged.dll, ZwDatabaseMgd.dll, ZwDatabaseMgdBrep.dll.
- Edição **Professional confirmada** pelo usuário (25.23_2026.09.07, #19981, x64).
- .NET SDK 8.0.425 instalado via winget em 2026-10-13. .NET Framework 4.8.x presente.
- ZWCAD hospeda o CLR .NET Framework v4.0 (`zwcad.exe.config`, sem host .NET Core) → **alvo do build: net48**, x64.

## Código
- `src/RedesAgua/` (net48, x64). Compila sem erros. Comando de teste `AGUA_TUBO`: cilindro 3D entre dois pontos com XData (app REDESAGUA, DN).
- Saída: `dist/v2/RedesAgua.dll` (compilar com `dotnet build src/RedesAgua -c Release -o dist/vN`; o ZWCAD trava a DLL carregada, então cada versão vai numa pasta nova e exige reiniciar o ZWCAD). Assume unidades do desenho em metros.
- v2: arquitetura com modelo de trecho em memória (`Rede.cs`), dimensões provisórias (`Catalogo.cs`), geometria (`Geometria.cs`).
  Comandos: `AGUA_REDE` (rede contínua, curvas automáticas em 90/45/22,5/11,25 graus, Desfazer/Fim) e `AGUA_VISTA` (SW iso + estilo Realistic).
  Convenção: a distância digitada vai até o vértice (PI); a curva consome a tangente T = R·tan(θ/2) do tubo anterior e do seguinte.
- v2 validada pelo usuário (2026-10-13): "funcionando bem".
- v3 (`dist/v3`): palavra-chave `Curva` pergunta o ângulo (90/45/22,5/11,25) e depois a direção; clique com mudança de direção também pergunta o ângulo; sem curva o trecho segue reto.
  Unidades: **desenho em metros** (comprimentos de tubo); **peças em milímetros** no `Catalogo.cs` (`MmParaDesenho = 0.001`). Avisa se INSUNITS não for metros.
- v3 validada ("funcionando bem").
- v4 (`dist/v4`): curva com direção **Cima/Baixo/Esquerda/Direita/Ponto** (relativa ao rumo, Z = cima) + comprimento do trecho seguinte; comando `AGUA_QUANT` (seleção ou desenho todo; agrupa tubos por DN em metros e curvas por DN/ângulo; grava `Quantitativo_<dwg>.csv` com `;`). XData: tipo, DN, material, comprimento (m), ângulo.
  **Nota:** com `[assembly: CommandClass]` presente, toda classe com `[CommandMethod]` precisa do seu próprio atributo de assembly.
- v4 ainda não validada.

## v5 (`dist/v5`) — modelo com portas e peças (compila; NÃO validada no ZWCAD)
- Imagens de referência do usuário em `imagens/` (registro gaveta flangeado e de bolsa, ponta-flange, carretel flangeado, tê flangeado e de bolsa).
- Modelo: `Trecho` = lista de `Elemento` (TUBO, CURVA, PECA) com portas (ponto, sentido, DN, extremidade Ponta/Bolsa/Flange).
- **Convenção nova:** curvas e peças entram **no fim do tubo**; o comprimento digitado é o do tubo (metragem exata). Curva = bolsa-bolsa.
- Material da rede perguntado no início: DEFoFo / PVCO / PVC / PEAD (peças de DEFoFo/PVCO = "FoFo"). DE por ISO 2531 (DN100 = 118 mm).
- `P` (Peça) abre diálogo: Tê (DN derivação, direção), Redução/Aumento (DN saída), Registro gaveta (cabeçote), Carretel/peça reta (comprimento; flange-flange = carretel, ponta-flange etc.), Luva. Extremidades escolhíveis. Fallback por palavras-chave se o diálogo falhar.
- `AGUA_QUANT`: tubos (m), peças, juntas flangeadas (parafuso Mxx×comp, porca, 2 arruelas, junta de vedação por par de flanges), anel de borracha por junta elástica, alertas de ligação incompatível e flanges sem par. CSV `;`.
- Dados provisórios (Catalogo.cs): flanges PN10 EN 1092-2 (DN400 = 16×M24, DN450/500 = 20×M24, DN600/700 = 20/24×M27); comprimento face-a-face do registro (EN 558 série 14).
- Riscos a validar: booleanas (furos de flange), orientação do cone da redução (há sonda de correção), diálogo WinForms via `Application.ShowModalDialog`.
- Fica para depois: PN16, PEAD/PVC com DE próprio, junta de desmontagem, bolsa-flange como peça de transição de tubo, flange cego, cap, cruzeta, colar de tomada, ventosa, hidrante, descarga, retenção, borboleta DN≥300, rede ramificada (começar de porta livre), 2D com simbologia, volante do registro.

## v6 (`dist/v6`) — junta mecânica e cap (compila; não validada)
- Nova extremidade **Junta mecânica (JM)**: contraflange (anel de aperto) + flange da bolsa + bolsa + parafusos T/porcas visíveis. Liga com Ponta.
  Disponível em qualquer peça e como família própria "Junta mecânica (luva JM)" (JM nas duas pontas).
- Nova família **Cap / flange cego**: entrada Bolsa, JM ou Flange; saída "Fechada" (nada se liga depois, avisa se tentar).
- Quantitativo: por junta JM = 1 anel de borracha + 1 contraflange + N parafusos T + N porcas (N por DN, provisório AWWA C111).
- Peça após peça: tecla P de novo; ex. registro flangeado + ponta-flange = "Carretel / ponta-flange" com entrada Flange e saída Ponta.

## v7 (`dist/v7`) — DN da peça, cap de luva, retomar rede (compila; não validada)
- Diálogo de peça: **DN da peça** selecionável para registro, carretel/peça reta, luva, JM e cap (tê/redução partem do DN da linha). Avisa se diferir do DN da linha.
- Cap bolsa ("cap de luva", ref. `imagens/cap.webp`): tampa cilíndrica de fundo plano com borda chanfrada e anel na boca.
- `AGUA_CONTINUAR`: seleciona tubo/peça e continua da saída (ou da derivação do tê). Lê a porta gravada no XData (`sp/sd/sdn/se`, `bp/bd/bdn/be`, `rmat`);
  o novo trecho grava `prev=run:seq:S|B` e o quantitativo liga os dois trechos (parafusos/juntas corretos, sem falso "flange sem par").
  Só funciona em elementos criados a partir da v7 (os da v5/v6 não têm a porta gravada).

## v8 (`dist/v8`) — mais peças em P, em ordem alfabética (compila; não validada)
- Lista de P (alfabética): Cap/flange cego, Carretel/ponta-flange/peça reta, Curva (11,25/22,5/45/90), Joelho (idem, raio curto),
  Junta mecânica (luva JM), Luva (bolsa-bolsa), Luva de redução (LR), Redução/aumento, Registro borboleta, Registro gaveta, Tê.
- Curva e joelho: ângulo, direção (Cima/Baixo/Esquerda/Direita), extremidades escolhíveis (bolsa, flange, JM, ponta).
- Registro borboleta: corpo curto, pescoço, caixa de engrenagem e volante. Luva LR: bolsa no DN e bolsa no DN2 com cone.
- **Tê com redução na derivação**: campos "Redução na derivação (DN)" e "Extremidade da redução" (flange/bolsa/JM); cria a redução
  já ligada à derivação (trecho próprio com `prev=...:B`); continuar o ramal com `AGUA_CONTINUAR`.

## v9 (`dist/v9`) — lista de P separada por item (compila; não validada)
- Lista em ordem alfabética, cada variação como item próprio: Aumento, Bolsa-flange, Cap/flange cego, Carretel flangeado, Curva, Joelho,
  Junta mecânica (luva JM), Luva (bolsa-bolsa), Luva de redução (LR), Peça reta (extremidades à escolha), Ponta-flange, Redução,
  Registro borboleta, Registro gaveta, Tê.
- Aumento só oferece DN maiores; Redução só DN menores. Ponta-flange = flange na entrada, ponta na saída (extremidades editáveis).
- Quantitativo diferencia "Aumento" de "Redução".

## v10 (`dist/v10`) — rótulos das peças (compila; não validada)
- `AGUA_ROTULO`: clique numa peça → diálogo (texto padrão = nome da peça, tamanho automático = 0,6 × DE com mínimo 40 mm, ou altura manual em mm).
  Cria MText no plano da vista atual (aparece de frente na vista em que foi criado) + linha de chamada. XData `REDESAGUA_ROT` liga rótulo, linha e peça.
  Clicar num rótulo existente edita texto/tamanho (recria rótulo e linha).
- Nomes de peça com acentos (Tê, Redução, Peça reta...).
- Ideias futuras: tag numerada (RG-01, TE-02) no quantitativo; rotular várias peças de uma vez; duplo clique abrir o rótulo.

## v11 (`dist/v11`) — AGUA_CONTINUAR pela entrada (compila; não validada)
- `AGUA_CONTINUAR` pergunta [Entrada/Saída/Derivação]. Entrada: porta de entrada do elemento com sentido invertido (rede cresce para trás).
  XData novos `ip/id/idn/ie`; para v7–v10 deriva a entrada de peças retas por `saída − sentido × comprimento` (curvas antigas não).
- Quantitativo: vínculo `prev=...:E` marca a entrada do elemento como usada.
- README.md criado (como instalar, rodar, comandos, convenções).

## Requisitos adicionais do usuário (2026-10-13), a fazer
- Peças além das curvas: flange, junta mecânica (JM), luva, redução, tê/derivação, registro gaveta (flangeado ou de bolsa),
  **carretel flangeado**, **ponta flange**, etc. Escolhidas por palavra-chave no vértice durante a rede.
- Quantitativo completo: tubos (metragem), peças e **parafusos/porcas das flanges** (bitola e quantidade), juntas/anéis.
- **Desenho das peças bonito** (flanges com anéis e furos, volante do registro, curvas suaves, estilo Plant 3D).
- Dados de flange provisórios a usar: furação EN 1092-2 PN10 (DN50: 4×M16; DN80/100: 8×M16; DN150/200: 8×M20; DN250/300: 12×M20; DN400: 12×M24; DN500/600: 20×M24/M27; DN700: 24×M27) — conferir com ABNT NBR 7675 e catálogo.

## Pendente (retomar daqui)
- Criar o projeto C# (net48, x64) e o primeiro comando para validar com NETLOAD.

## Próximo passo após respostas
Montar estrutura do projeto C# (class library referenciando os DLLs do ZWCAD, `NETLOAD`)
e o primeiro comando funcional (ex.: inserir flange DN100 PN10 com atributos).

## v12
- Registro gaveta redesenhado: corpo abaulado com transições cônicas, castelo com flange e parafusos, parte alta cônica, pescoço com caixa de gaxeta, haste e cabeçote quadrado de manobra.

## v13
- Registro gaveta refeito conforme as fotos 03/04: corpo fundido alto com sapata, cúpula com flange aparafusado, cone, pescoço com gaxeta, haste e cabeçote cônico; cor azul.

## v14
- Registro gaveta vermelho (barrilete, coluna, flange do castelo, cone, gaxeta, cabeçote), sem caixas.
- Novas peças: válvula de retenção, VRP, ventosa (fim de linha).
- Todas as peças e tubos ocos (furo interno; bolsas com interior).

## v15
- Suporte ao AutoCAD 2025: projeto `src\RedesAgua.AutoCAD` (net8.0-windows, pacote NuGet AutoCAD.NET 25.0.1) compila os mesmos .cs com o símbolo ACAD.
- `Usings.cs` escolhe os namespaces (ZwSoft.ZwCAD ou Autodesk.AutoCAD); alias `CadApp` para Application.
- Saída: `dist\v15` (ZWCAD) e `dist\v15-autocad` (AutoCAD). Compila; não testado em AutoCAD real.

## v16
- Registro borboleta refeito conforme a foto (imagens/registro borboleta.webp): corpo abaulado, pescoço com colar, manipulo em T; padrão bolsa. Obs.: a foto é de registro de esfera.

## v17

- Nova peça `FLANGE` ("Flange (fim de linha)", tecla `Flange` no menu de texto): entrada bolsa ou JM, saída flange, furo passante. Saída: `dist\v17` (ZWCAD). Aguarda teste.

## v18

- `FLANGE` passa a ser só o anel da flange (disco com furos, furo central = DE do tubo). Saída: `dist18`. Aguarda teste.

## v19

- Nova peça `Y` (Junção Y 45°): derivação a 45° com DN próprio (redução opcional), direção e extremidades à escolha. Saída: `dist19`. Aguarda teste.

## v20

- `AGUA_ESTICAR` (tubo, carretel, redução; inverter redução); diálogo: Aumento+Redução unificados com caixa Inverter; `AGUA_CONTINUAR`/`AGUA_ESTICAR` leem a posição real do sólido (peça movida com MOVE) via XData `ctr` ou reconstrução. Saída: `dist\v20`. Aguarda teste.

## v21

- `AGUA_ESTICAR` tambem estica a junta mecanica.
- Grip "+" nas pontas livres (GripOverrule em `Grips.cs`): clique = `AGUA_PECA_PONTA`, arrasto = `AGUA_PUXAR`; `Continuar` refatorado em `PrepararTrecho`. Saida: `dist\v21` (ZWCAD) e `dist\v21-autocad`. Aguarda teste: separacao clique x arrasto depende do comportamento real do grip no ZWCAD.

## v22

- Grip "+": a v21 apareceu como grip padrao que movia a peca (callback GripData nao usado pelo ZWCAD). v22: trata tambem o caminho legado (indices) e escreve diagnostico em `%TEMP%\RedesAgua_grips.log`. Aguarda teste.

## v23

- Grip "+" (caminho legado do ZWCAD): o log mostrou que o arrasto final funciona, mas durante o arrasto o ZWCAD passa uma copia temporaria da entidade (NullReference). Agora as pontas livres sao guardadas em GetGripPoints e usadas em MoveGripPointsAt; durante o arrasto o solido nao se move.

## v24

- Ctrl+Z no prompt de AGUA_REDE/CONTINUAR/pontos seguintes = opcao Desfazer (PreTranslateMessage + SendKeys "D{ENTER}"): apaga o ultimo tubo/peca e o proximo ponto volta ao snap anterior. Aguarda teste no ZWCAD (se Ctrl+Z nao for interceptado, digitar D).

## v25

- `AGUA_COR` (novo): muda a cor de tubos/pecas (selecao previa ou na hora; opcao Trecho pinta o trecho inteiro; cores nomeadas, Numero ACI ou PorCamada). Esticar preserva a cor.
- Registro gaveta/retencao/VRP: castelo (cilindro + flange) e sapata limitados a largura livre entre os flanges das pontas (`LarguraMaxima`); pescoco e cabecote reduzidos proporcionalmente nos DN grandes.
- `AGUA_REDE`/`AGUA_CONTINUAR`: nova opcao `Reta` = segue na direcao atual so digitando o comprimento (sem curva).
- `AGUA_ESTICAR`: aceita peca pre-selecionada (selecione > digite AGUA_ESTICAR > arraste a ponta livre; mostra o comprimento no tooltip); `Comprimento` digita o valor; `Inverter` na reducao. So a saida arrasta (entrada fixa). Saida: `dist\v25`. Aguarda teste.

## v26

- Registro de gaveta redesenhado conforme as fotos 05-08: corpo fundido alto (caixa + topo arredondado ao longo do fluxo), tampa aparafusada, pescoco com gaxeta, haste e cabecote conico com quadrado. Dimensoes proporcionais ao DN e ao trecho livre entre as pontas (serve para bolsa e flange); vao livre minimo em bolsa. Sem sapata. Saida: `dist\v26`. Aguarda teste.

## v27

- Juncao Y: o DN da derivacao agora e independente (qualquer DN, inclusive maior que o da linha); antes era limitado ao DN da linha. Saida: `dist27`. Aguarda teste (conferir a geometria quando a derivacao for maior que o corpo).

## v28

- Novas extremidades: `Eletrofusao` (manga PEAD com terminais), `Termofusao` (ponta lisa com cordao, solda de topo) e `Solda` (so cordao, rede em aluminio). Compatibilidade: eletrofusao/termofusao/solda ligam em Ponta (termo e solda tambem entre si).
- Dialogo P: "Luva eletrofusao (PEAD)", "Luva termofusao (PEAD)" (soquete) e "Curva soldada (aluminio)" (mesmo DN, extremidades soldadas, cor laranja); combos de extremidade de curva/pecas aceitam as novas.
- Materiais da rede: novos FoFo e Aluminio (tubo cinza). Na rede, a curva usa termofusao em PEAD e solda em Aluminio por padrao.
- Quantitativo: novas juntas (eletrofusao, solda de topo/termofusao, solda aluminio) e nomes de curva soldada / luvas.
- Saida: `dist28`. Aguarda teste.

## v29

- PEAD: luvas e joelhos sao de eletrofusao (manga com diametro maior que o tubo, como bolsa; raio 1,2 x). Na rede, a opcao Curva em PEAD gera joelho de eletrofusao; no dialogo P, curva/joelho/luva em PEAD vem com eletrofusao por padrao. Saida: `dist29`. Aguarda teste.
