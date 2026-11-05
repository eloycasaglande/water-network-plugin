# RedesAgua — plugin ZWCAD para redes de água

Plugin em C# (.NET Framework 4.8) para o **ZWCAD 2025 Professional** que desenha redes de água
municipais em 3D (DN50 até adutoras DN700), com tubos, curvas, peças especiais e quantitativo
automático (metragem, peças, parafusos, porcas, juntas).

> **Estado:** em desenvolvimento. A versão atual é a `v36` (pasta `dist\v35`). O código compila,
> mas as versões mais novas ainda estão sendo validadas no ZWCAD. As dimensões das peças são
> **provisórias** (veja [Dados e normas](#dados-e-normas)).

## Requisitos

| Item | Versão |
|---|---|
| ZWCAD | 2025 **Professional** (testado com 25.23, build 19981, x64) |
| AutoCAD | 2025 (x64), usando a DLL da pasta `dist\vNN-autocad` (veja abaixo) |
| Windows | 10 ou 11, 64 bits |
| .NET SDK | 8.0 (somente para compilar) |
| .NET Framework | 4.8 (já vem com o Windows 11) |

A edição Standard do ZWCAD **não** carrega plugins .NET.

## Qual DLL usar

O mesmo código gera **duas DLLs**, porque o ZWCAD e o AutoCAD têm APIs .NET diferentes:

| CAD | Pasta da DLL | Plataforma |
|---|---|---|
| ZWCAD 2025 Professional | `dist\vNN\RedesAgua.dll` | .NET Framework 4.8 |
| AutoCAD 2025 | `dist\vNN-autocad\RedesAgua.dll` | .NET 8 |

No AutoCAD, o carregamento é o mesmo: comando `NETLOAD` e escolher a DLL da pasta `-autocad`.
Se o AutoCAD perguntar sobre carregar de uma pasta não confiável (`SECURELOAD`), confirme ou
adicione a pasta em `TRUSTEDPATHS`. Os comandos `AGUA_...` são os mesmos nos dois programas.
**Não carregue a DLL do ZWCAD no AutoCAD, nem o contrário.**

> A versão do AutoCAD compila, mas ainda **não foi testada** num AutoCAD real.

### Instalador para o AutoCAD 2025

`entrega\InstalarRedesAgua_AutoCAD2025.exe` copia o plugin para
`%APPDATA%\Autodesk\ApplicationPlugins\RedesAgua.bundle`, e o AutoCAD 2025 o carrega sozinho
ao abrir (sem `NETLOAD`). Para remover: `InstalarRedesAgua_AutoCAD2025.exe -desinstalar`.
Para gerar de novo: `dotnet build src\Instalador -c Release -o entrega` (usa a DLL de `dist\v16-autocad`).

## Como instalar e rodar

### Opção 1: usar a DLL já compilada (a mais simples)

1. Abra o ZWCAD e crie ou abra um desenho.
2. Na linha de comando, digite `NETLOAD` e tecle Enter.
3. Selecione a DLL da versão mais recente, por exemplo:
   `...\plugin encanamentos\dist\v35\RedesAgua.dll`
4. Se o ZWCAD pedir confirmação de segurança, escolha carregar.
5. Os comandos `AGUA_...` já estão disponíveis (veja a tabela abaixo).

O carregamento vale **só para a sessão atual**: ao fechar o ZWCAD, é preciso repetir o `NETLOAD`.

> **Importante:** o ZWCAD trava a DLL enquanto ela está carregada, e não troca uma DLL já carregada
> por outra. Para testar uma versão nova, **feche e abra o ZWCAD** antes do `NETLOAD`.

### Opção 2: compilar do código-fonte

1. Instale o [.NET SDK 8](https://dotnet.microsoft.com/download) (ou rode no PowerShell:
   `winget install Microsoft.DotNet.SDK.8`).
2. Na pasta do projeto, rode:

   ```powershell
   dotnet build src\RedesAgua -c Release -o dist\vNN
   ```

   Troque `vNN` por um nome novo de pasta (por exemplo `v11`), porque a DLL anterior pode estar
   travada pelo ZWCAD.
3. Se o ZWCAD estiver instalado em outra pasta, informe o caminho:

   ```powershell
   dotnet build src\RedesAgua -c Release -o dist\vNN -p:ZwcadDir="D:\Programas\ZWCAD 2025"
   ```
4. Carregue `dist\vNN\RedesAgua.dll` com `NETLOAD`, como na opção 1.

Para o **AutoCAD 2025** (não precisa ter o AutoCAD instalado para compilar; as referências vêm do
pacote NuGet `AutoCAD.NET`):

```powershell
dotnet build src\RedesAgua.AutoCAD -c Release -o dist\vNN-autocad
```

### Carregar automaticamente (opcional, não testado)

Para não repetir o `NETLOAD` a cada sessão, pode-se colocar a linha abaixo num arquivo LISP
carregado na inicialização do ZWCAD (por exemplo `acaddoc.lsp` na pasta de suporte):

```lisp
(command "_.NETLOAD" "C:/caminho/para/plugin encanamentos/dist/v36/RedesAgua.dll")
```

## Versão web (Chrome)

Pasta `web/` — um único `index.html`, sem instalação, que funciona **como no ZWCAD**: área de desenho 3D e
**linha de comando** embaixo. **Todas as medidas são em centímetros (cm)** (DN e comprimento de peças em mm, como no catálogo).
Comandos: `REDE`, `CONTINUAR`, `QUANT`, `IMPRIMIR`, `VISTA`, `PLANTA`, `FRENTE`, `ZOOM`, `DESFAZER`, `SALVAR`, `ABRIR`,
`EXPORTAR`, `IMPORTAR` (também na barra superior; o botão **?** explica cada um).

- `REDE`: material, DN, ponto inicial e o mesmo laço do plugin: *Próximo ponto ou distância [Reta/Curva/Peça/Desfazer/Fim]*.
- `Peça`: a mesma janela de peças do plugin (23 itens, extremidades, DN, ângulo, redução na derivação) com **pré-visualização 3D** (arraste para girar, roda para zoom). As peças usam a mesma geometria do plugin.
- Passar o mouse sobre uma peça destaca e mostra nome, DN, extremidades e comprimento.
- `SALVAR`/`ABRIR` guardam a rede **neste navegador** (também há salvamento automático); `EXPORTAR`/`IMPORTAR` usam arquivo `.json`.
- `CONTINUAR`, `IMPRIMIR` (imprime a tela como está, sem a grade, pela impressão nativa do computador; retrato/paisagem automático) e `IMPRIMIRPB` (versão para impressora preto e branco: tons de cinza bem separados por tipo de peça e contorno preto) e `QUANT` (tela e CSV).

Publicada em https://eloycasaglande.github.io/water-network-plugin/web/ (GitHub Pages).
O plugin do ZWCAD/AutoCAD continua como está.

## Comandos

| Comando | O que faz |
|---|---|
| `AGUA_VISTA` | Coloca a vista isométrica SW com estilo visual sombreado. |
| `AGUA_REDE` | Desenha uma rede contínua (tubos, curvas e peças). |
| `AGUA_CONTINUAR` | Retoma uma rede existente a partir da **entrada**, da **saída** ou da **derivação** (tê) de uma peça. |
| `AGUA_ROTULO` | Cria ou edita o rótulo (nome) de uma peça, no plano da vista atual. |
| `AGUA_QUANT` | Gera o quantitativo (na linha de comando e em arquivo CSV). |
| `AGUA_IMPRIMIR` | Imprime só a região escolhida por janela (2 cantos), com retrato/paisagem automático e escala ajustada à folha. |

## Como desenhar uma rede

1. Rode `AGUA_REDE`.
2. Escolha o **material** (DEFoFo, PVCO, PVC ou PEAD) e o **DN**.
3. Clique o **ponto inicial**.
4. A cada passo, clique um ponto ou **digite uma distância em metros** (como no comando LINHA) e use as opções:

| Opção | Efeito |
|---|---|
| ponto ou distância | Novo trecho de tubo reto. Se a direção mudar, pergunta o ângulo da curva. |
| `Curva` | Escolhe o ângulo (90, 45, 22,5 ou 11,25), a direção (Cima, Baixo, Esquerda, Direita ou Ponto) e o comprimento do tubo seguinte. |
| `Peca` (ou `P`) | Abre a janela de seleção de peça (veja abaixo). |
| `Desfazer` | Remove o último elemento. |
| `Fim` (ou Enter) | Termina a rede. |

### Convenções importantes

- **Unidades:** o desenho deve estar em **metros** (`INSUNITS` = metros). As peças são dimensionadas
  em milímetros no catálogo e convertidas automaticamente.
- **Comprimento do tubo:** o que você digita é o comprimento **do tubo**. Curvas e peças entram
  **no fim do tubo**, sem consumir a metragem digitada. Assim o quantitativo fica exato.
- **Direção:** Cima/Baixo usam o eixo Z. Esquerda/Direita são relativas ao rumo atual do tubo.
  Em tubo vertical não existe "frente": Direita/Esquerda viram +X/−X e Cima/Baixo viram +Y/−Y.
- **Compatibilidade:** ponta entra em bolsa ou em junta mecânica; flange só combina com flange.
  O programa avisa quando uma ligação é incompatível (por exemplo, flange direto em tubo liso).
- **Peça sobre peça:** aperte `P` quantas vezes quiser. Não precisa de tubo entre elas.

## Peças disponíveis (tecla P, em ordem alfabética)

| Peça | Observação |
|---|---|
| Bolsa-flange | Bolsa na entrada, flange na saída. |
| Cap / flange cego | Fecha o fim da rede. Cap de bolsa, de junta mecânica ou flange cego. |
| Carretel flangeado | Flange nas duas pontas, comprimento livre. |
| Curva | 11,25°, 22,5°, 45° ou 90°, em qualquer direção. |
| Joelho | Mesmos ângulos, raio mais curto. |
| Flange (anel) | Só o anel da flange (disco com furos PN10), para o fim da tubulação. |
| Junção Y 45° | Corpo reto + derivação a 45° a favor do fluxo; DN da derivação menor = com redução; direção (cima/baixo/esquerda/direita); flange ou bolsa. |
| _Comando_ `AGUA_ESTICAR` | Seleciona tubo, carretel/peça reta, redução ou junta mecânica e muda o comprimento (a entrada fica parada; o que vem depois, inclusive ramais, acompanha). Na redução também há a opção **Inverter**. |
| _Comando_ `AGUA_EDITAR` | Seleciona uma peça já inserida (ou deixe-a selecionada antes de digitar o comando) e abre a janela P preenchida: dá para trocar tipo, DN, extremidades, ângulo e direção. A peça é refeita na mesma entrada e o que vem depois é deslocado se o comprimento mudar. Recusa se a saída mudar de direção e houver rede depois dela. Para tubo, use `AGUA_ESTICAR`. |
| Grip **+** na ponta (experimental, desligado por padrao; `AGUA_GRIPS` liga) | Selecione um tubo/peça: aparece um **+** na extremidade livre (saída e derivação). **Clique** = abre a janela de peça; **arraste** = puxa o tubo até o ponto solto (reto, ou pergunta o ângulo da curva) e segue pedindo pontos. `AGUA_GRIPS` liga/desliga. |
| Junta mecânica (luva JM) | Luva com contraflange (anel de aperto), anel de borracha e parafusos T. |
| Luva (bolsa-bolsa) | |
| Luva de redução (LR) | Bolsa-bolsa com DN diferente em cada ponta. |
| Peça reta | Extremidades à escolha. |
| Ponta-flange | Flange na entrada, ponta na saída (editável na janela). |
| Redução / aumento | Uma só peça: marque **Inverter (aumento)** na janela para aumentar o DN. Comprimento ajustável com `AGUA_ESTICAR`. |
| Junta de desmontagem | Flange-flange travada axialmente, com luva, anel de aperto e tirantes roscados (mesma quantidade e bitola dos parafusos da flange: 4 em DN50 até 24 em DN700). Comprimento editável na janela e com `AGUA_ESTICAR` (padrão 150 + 0,4 x DN mm). |
| Registro borboleta | Corpo abaulado com manipulo em T (como na foto); bolsa ou flange. |
| Registro gaveta | Flangeado ou de bolsa, vermelho, com cabeçote de manobra. |
| Tê | DN da derivação, direção e **redução na derivação** (flange, bolsa ou JM) opcionais. |
| Válvula de retenção | Portinhola, flangeada ou de bolsa (verde). |
| Válvula redutora de pressão (VRP) | Com câmara do diafragma e piloto (ciano). |
| Ventosa | Fim de linha, entrada flangeada ou de bolsa (amarela). Use sobre a derivação de um tê (`AGUA_CONTINUAR` → Derivação → `P`). |

Em cada peça é possível escolher o **DN** e o **tipo de extremidade** (flange, bolsa, junta
mecânica ou ponta), quando faz sentido.

## Quantitativo (`AGUA_QUANT`)

1. Rode `AGUA_QUANT`.
2. Selecione a sessão da rede, ou tecle Enter para considerar o desenho todo.
3. O resultado aparece na linha de comando e é gravado em
   `Quantitativo_<nome do desenho>.csv`, na pasta do desenho (separador `;`, abre direto no Excel).

O quantitativo traz:

- **Tubos**: metragem por material e DN.
- **Peças**: quantidade por tipo, DN e extremidades.
- **Juntas flangeadas** (por par de flanges): parafusos (bitola e comprimento, ex.: `M16 x 70`),
  porcas, 2 arruelas e a junta de vedação.
- **Juntas mecânicas**: anel de borracha, contraflange, parafusos T e porcas.
- **Juntas elásticas**: anel de borracha por junta bolsa-ponta.
- **Verificar**: ligações incompatíveis e flanges sem par (fim de linha ou derivação não ligada).

## Rótulos (`AGUA_ROTULO`)

- Clique numa peça: abre a janela com o **texto** (já vem com o nome da peça) e o **tamanho**
  (automático, proporcional ao diâmetro, ou altura manual em mm).
- O rótulo é criado no plano da vista atual, com linha de chamada até a peça.
- Clique num rótulo existente para editar o texto e o tamanho.

## Retomar uma rede (`AGUA_CONTINUAR`)

Clique num tubo ou peça e escolha de que lado continuar, com o mesmo DN e material:

- **Entrada**: a rede cresce para trás, no sentido oposto ao do fluxo. Útil para corrigir ou
  acrescentar algo que antecede a peça (se já houver algo ligado ali, apague primeiro com `ERASE`).
- **Saída** (padrão): continua no sentido do fluxo.
- **Derivação**: só aparece em tê; começa um ramal.

O quantitativo reconhece a ligação entre o trecho antigo e o novo.

Só funciona em elementos criados a partir da `v7`. Curvas e joelhos criados antes da `v11` não
permitem continuar pela entrada.

## Dados e normas

As dimensões estão em `src\RedesAgua\Catalogo.cs` e são **provisórias**, até haver catálogos reais
de fabricantes:

- **Diâmetro externo (DE):** tabela ISO 2531 / NBR 7675 (ferro dúctil e DEFoFo).
- **Flanges:** PN10 conforme EN 1092-2 / ISO 7005-2 (compatível com a NBR 7675 PN10).
- **Junta mecânica:** quantidade e bitola dos parafusos tipo AWWA C111 (aproximado).
- **Registro:** comprimento face a face tipo EN 558, série 14.

Ainda não há PN16, nem DE próprio de PVC PBA e PEAD. **Confira com o catálogo do fabricante antes
de usar em projeto executivo.**

## Estrutura do projeto

```
plugin encanamentos\
├── README.md              este arquivo
├── PLANEJAMENTO.md        planejamento e histórico das versões
├── imagens\               fotos de referência das peças
├── dist\vNN\              DLLs compiladas de cada versão
└── src\RedesAgua\
    ├── RedesAgua.csproj   projeto do ZWCAD (.NET Framework 4.8, x64)
    ├── Usings.cs          namespaces da API do CAD (ZWCAD ou AutoCAD, símbolo ACAD)
    ├── Catalogo.cs        dimensões e tabelas (mm)
    ├── Geometria.cs       primitivas 3D (cilindro, cone, arco, caixa)
    ├── Elementos.cs       modelo (portas, elementos) e construção das peças
    ├── Rede.cs            trecho: lista ordenada de elementos
    ├── Comandos.cs        AGUA_VISTA, AGUA_REDE, AGUA_CONTINUAR
    ├── PecaDialog.cs      janela de seleção de peça (e menu alternativo)
    ├── Quantitativo.cs    AGUA_QUANT
    ├── Imprimir.cs        AGUA_IMPRIMIR
    └── Rotulos.cs         AGUA_ROTULO
src\RedesAgua.AutoCAD\       projeto do AutoCAD 2025 (.NET 8): compila os mesmos .cs de src\RedesAgua
```

### Como funciona por dentro

- Cada trecho é uma **lista de elementos** (tubo, curva, peça). Cada elemento tem **portas** de entrada e
  saída (ponto, sentido, DN e tipo de extremidade). A saída do último elemento é onde a rede continua.
- Cada elemento vira um **sólido 3D** no desenho, com **XData** (aplicação `REDESAGUA`) contendo tipo,
  DN, material, extremidades, comprimento, ângulo e a posição das portas. É esse dado que o
  quantitativo, o `AGUA_CONTINUAR` e o rótulo lêem.
- O quantitativo une elementos consecutivos pela sequência (`run`/`seq`) e pelos vínculos de trechos
  retomados (`prev`), e calcula os parafusos por par de flanges.

## Limitações conhecidas

- As versões 5 a 10 foram compiladas mas ainda não totalmente validadas no ZWCAD. Se algo sair torto
  (furos de flange, cone da redução, parafusos), reporte com uma imagem do desenho.
- Redes ramificadas são feitas por partes (`AGUA_CONTINUAR` a partir da derivação), não no mesmo comando.
- O rótulo não acompanha a peça se ela for movida.
- Ainda não há simbologia 2D de planta, perfil longitudinal, volante do registro gaveta, nem peças
  como hidrante, descarga e colar de tomada.
- Tubos e peças são **ocos** (parede ~10% do raio); a espessura é provisória.
- PEAD e PVC usam, por enquanto, as mesmas dimensões de ferro dúctil.
