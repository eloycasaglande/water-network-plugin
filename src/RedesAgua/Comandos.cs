using System;

[assembly: CommandClass(typeof(RedesAgua.Comandos))]

namespace RedesAgua
{
    public partial class Comandos
    {
        // Seta vista isometrica SW e estilo visual sombreado.
        [CommandMethod("AGUA_VISTA")]
        public void Vista()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            doc.SendStringToExecute("_-VIEW _SWISO ", true, false, false);
            doc.SendStringToExecute("_VSCURRENT _Realistic ", true, false, false);
            doc.SendStringToExecute("_ZOOM _E ", true, false, false);
        }

        // Rede continua. Distancias em metros; pecas dimensionadas em milimetros.
        //   ponto/distancia : novo trecho de tubo (mudou de direcao -> pergunta o angulo da curva)
        //   Curva           : angulo + direcao (Cima/Baixo/Esquerda/Direita/Ponto) + comprimento seguinte
        //   Peca (P)        : abre a selecao de peca (te, reducao, registro, carretel, luva)
        //   Desfazer / Fim
        // Convencao: curvas e pecas sao inseridas NO FIM do tubo desenhado; o comprimento
        // digitado e o do tubo (fica exato no quantitativo).
        [CommandMethod("AGUA_REDE")]
        public void Rede()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            string material = PedirMaterial(ed);
            if (material == null) return;

            var pdn = new PromptIntegerOptions("\nDN do tubo (mm) <100>: ")
            {
                DefaultValue = 100, UseDefaultValue = true, AllowNone = true,
                LowerLimit = 50, UpperLimit = 700
            };
            PromptIntegerResult rdn = ed.GetInteger(pdn);
            if (rdn.Status != PromptStatus.OK) return;
            int dn = Catalogo.DnPadrao(rdn.Value);
            if (dn != rdn.Value) ed.WriteMessage("\nDN {0} nao e padrao; usando DN{1}.", rdn.Value, dn);

            if (doc.Database.Insunits != UnitsValue.Meters)
                ed.WriteMessage("\nAviso: INSUNITS do desenho nao e metros; o plugin assume 1 unidade = 1 m.");

            PromptPointResult inicio = ed.GetPoint("\nPonto inicial: ");
            if (inicio.Status != PromptStatus.OK) return;

            var trecho = new Trecho(inicio.Value, dn, material);
            Executar(doc, ed, trecho);
        }

        // Retoma uma rede existente: selecione um tubo/peca e continue da sua saida
        // (ou da derivacao, se for um te).
        [CommandMethod("AGUA_CONTINUAR")]
        public void Continuar()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            var po = new PromptEntityOptions("\nSelecione o tubo ou peca onde continuar a rede: ");
            po.SetRejectMessage("\nSelecione um solido da rede.");
            po.AddAllowedClass(typeof(Solid3d), true);
            PromptEntityResult pr = ed.GetEntity(po);
            if (pr.Status != PromptStatus.OK) return;

            Trecho trecho = PrepararTrecho(doc, ed, pr.ObjectId, null);
            if (trecho != null) Executar(doc, ed, trecho);
        }

        // Le a porta do elemento (corrigida se a peca foi movida) e monta o trecho para continuar.
        // ladoFixo: null = pergunta; "Entrada" / "Saida" / "Derivacao" = sem pergunta (usado pelo grip "+").
        static Trecho PrepararTrecho(Document doc, Editor ed, ObjectId id, string ladoFixo)
        {
            var campos = new System.Collections.Generic.Dictionary<string, string>();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                ResultBuffer rb = ent.GetXDataForApplication(Geometria.AppXData);
                if (rb != null)
                {
                    using (rb)
                    {
                        foreach (TypedValue v in rb.AsArray())
                        {
                            string s = Convert.ToString(v.Value);
                            int k = s.IndexOf('=');
                            if (k > 0) campos[s.Substring(0, k)] = s.Substring(k + 1);
                        }
                    }
                }
                // peca movida com MOVE: usa a posicao real do solido, nao a gravada
                AplicarDesvio(campos, DesvioMovimento(ent, campos));
                tr.Commit();
            }

            if (!campos.ContainsKey("sp") || !campos.ContainsKey("run") || !campos.ContainsKey("seq"))
            {
                ed.WriteMessage("\nElemento sem dados de conexao (criado em versao anterior). Nao e possivel continuar dele.");
                return null;
            }

            // Lado de onde continuar: Entrada (cresce para tras), Saida ou Derivacao (te).
            bool temDeriv = campos.ContainsKey("bp");
            string lado = "Saida";
            if (ladoFixo != null) lado = ladoFixo;
            else
            {
                var kp = new PromptKeywordOptions(
                    temDeriv ? "\nContinuar a partir de [Entrada/Saida/Derivacao] <Saida>: "
                             : "\nContinuar a partir de [Entrada/Saida] <Saida>: ",
                    temDeriv ? "Entrada Saida Derivacao" : "Entrada Saida")
                { AllowNone = true };
                kp.Keywords.Default = "Saida";
                PromptResult rk = ed.GetKeywords(kp);
                if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return null;
                if (rk.Status == PromptStatus.OK) lado = rk.StringResult;
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Porta porta;
            string sufixo;
            try
            {
                if (lado == "Entrada")
                {
                    sufixo = "E";
                    Point3d p;
                    Vector3d d;
                    int dnE;
                    Extremidade eE;
                    if (campos.ContainsKey("ip"))
                    {
                        p = LerPonto(campos["ip"]);
                        d = LerPonto2(campos["id"]);
                        dnE = int.Parse(campos["idn"], inv);
                        eE = (Extremidade)Enum.Parse(typeof(Extremidade), campos["ie"]);
                    }
                    else
                    {
                        // elementos da v7-v10: a entrada de pecas retas = saida - sentido x comprimento
                        string fam;
                        campos.TryGetValue("fam", out fam);
                        if (fam == "CURVA" || fam == "JOELHO" || !campos.ContainsKey("len"))
                        {
                            ed.WriteMessage("\nEsta curva foi criada em versao anterior; nao e possivel continuar pela entrada.");
                            return null;
                        }
                        d = LerPonto2(campos["sd"]);
                        p = LerPonto(campos["sp"]) - d * double.Parse(campos["len"], inv);
                        dnE = int.Parse(campos["dn"], inv);
                        eE = (Extremidade)Enum.Parse(typeof(Extremidade), campos["ein"]);
                    }
                    // a rede nova cresce para tras: sentido oposto ao do fluxo na entrada
                    porta = new Porta { P = p, D = d.Negate(), DN = dnE, E = eE, TemDirecao = true };
                }
                else
                {
                    bool deriv = lado == "Derivacao";
                    sufixo = deriv ? "B" : "S";
                    string pre = deriv ? "b" : "s";
                    porta = new Porta
                    {
                        P = LerPonto(campos[pre + "p"]),
                        D = LerPonto2(campos[pre + "d"]),
                        DN = int.Parse(campos[pre + "dn"], inv),
                        E = (Extremidade)Enum.Parse(typeof(Extremidade), campos[deriv ? "be" : "se"]),
                        TemDirecao = true
                    };
                }
            }
            catch (System.Exception)
            {
                ed.WriteMessage("\nNao foi possivel ler a porta do elemento selecionado.");
                return null;
            }

            if (porta.E == Extremidade.Fechada)
            {
                ed.WriteMessage("\nEsta extremidade esta fechada (cap / flange cego).");
                return null;
            }

            string material;
            if (!campos.TryGetValue("rmat", out material) || string.IsNullOrEmpty(material)) material = "DEFoFo";
            var trecho = new Trecho(porta.P, porta.DN, material) { Atual = porta };
            trecho.Prev = campos["run"] + ":" + campos["seq"] + ":" + sufixo;
            ed.WriteMessage("\nContinuando a rede DN{0} ({1}) a partir da {2}.", porta.DN, material,
                lado == "Entrada" ? "entrada (sentido oposto ao fluxo)" : (lado == "Derivacao" ? "derivacao" : "saida"));
            return trecho;
        }

        static Point3d LerPonto(string s)
        {
            double[] v = Triplo(s);
            return new Point3d(v[0], v[1], v[2]);
        }

        static Vector3d LerPonto2(string s)
        {
            double[] v = Triplo(s);
            return new Vector3d(v[0], v[1], v[2]);
        }

        static double[] Triplo(string s)
        {
            string[] p = s.Split(';');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new[] { double.Parse(p[0], inv), double.Parse(p[1], inv), double.Parse(p[2], inv) };
        }

        // Ctrl+Z durante o prompt: vira a opcao "Desfazer" (remove o ultimo tubo/peca e volta ao ponto anterior).
        static void TeclaCtrlZ(object sender, PreTranslateMessageEventArgs e)
        {
            try
            {
                const int WM_KEYDOWN = 0x0100;
                if (e.Message.message != WM_KEYDOWN || e.Message.wParam.ToInt64() != 'Z') return;
                if ((System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Control) == 0) return;
                e.Handled = true;
                System.Windows.Forms.SendKeys.Send("D{ENTER}");
            }
            catch (System.Exception) { }
        }

        static void Executar(Document doc, Editor ed, Trecho trecho)
        {
            Application.PreTranslateMessage += TeclaCtrlZ;
            try { ExecutarLoop(doc, ed, trecho); }
            finally { Application.PreTranslateMessage -= TeclaCtrlZ; }
        }

        static void ExecutarLoop(Document doc, Editor ed, Trecho trecho)
        {
            while (true)
            {
                var opt = new PromptPointOptions(
                    "\nProximo ponto ou distancia [Reta/Curva/Peca/Desfazer/Fim] <Fim>: ", "Reta Curva Peca Desfazer Fim")
                {
                    BasePoint = trecho.Atual.P,
                    UseBasePoint = true,
                    UseDashedLine = true,
                    AllowNone = true
                };
                PromptPointResult r = ed.GetPoint(opt);

                if (r.Status == PromptStatus.None || r.Status == PromptStatus.Cancel) break;

                if (r.Status == PromptStatus.Keyword)
                {
                    if (r.StringResult == "Fim") break;
                    if (r.StringResult == "Desfazer") { Desfazer(doc, ed, trecho); continue; }
                    if (r.StringResult == "Reta") { ComandoReta(doc, ed, trecho); continue; }
                    if (r.StringResult == "Curva") { ComandoCurva(doc, ed, trecho); continue; }
                    if (r.StringResult == "Peca") { ComandoPeca(doc, ed, trecho); continue; }
                    continue;
                }
                if (r.Status != PromptStatus.OK) break;

                ComandoPonto(doc, ed, trecho, r.Value);
            }

            ed.WriteMessage("\nRede finalizada: {0} elemento(s), DN{1}. Use AGUA_QUANT para o quantitativo.",
                trecho.Elementos.Count, trecho.Atual.DN);
        }

        // ----- passos do fluxo -----

        static void ComandoPonto(Document doc, Editor ed, Trecho t, Point3d pedido)
        {
            Vector3d v = pedido - t.Atual.P;
            double comp = v.Length;
            if (comp < 1e-6) { ed.WriteMessage("\nPonto coincidente com o anterior."); return; }

            Porta de = t.Atual;
            if (!de.TemDirecao)
            {
                de = Clonar(de, v.GetNormal());
                AdicionarElemento(doc, ed, t, Pecas.CriarTubo(de, comp, t.Material));
                return;
            }

            double ang = de.D.GetAngleTo(v.GetNormal()) * 180.0 / Math.PI;
            if (ang < 1.0)
            {
                AdicionarElemento(doc, ed, t, Pecas.CriarTubo(de, comp, t.Material));
                return;
            }
            if (ang > 120.0) { ed.WriteMessage("\nDeflexao acima de 90 graus nao e permitida."); return; }

            double? a = PedirAngulo(ed, Catalogo.AnguloPadraoMaisProximo(ang));
            if (a == null) return;
            AdicionarCurvaETubo(doc, ed, t, a.Value, v.GetNormal(), comp);
        }

        // Segue em frente na direcao atual da rede (sem curva): pede so o comprimento do tubo.
        static void ComandoReta(Document doc, Editor ed, Trecho t)
        {
            if (!t.Atual.TemDirecao)
            {
                ed.WriteMessage("\nSem direcao definida: clique um ponto para o primeiro trecho.");
                return;
            }
            var od = new PromptDistanceOptions("\nComprimento do tubo na direcao atual (m): ")
            { BasePoint = t.Atual.P, UseBasePoint = true, AllowNegative = false, AllowZero = false };
            PromptDoubleResult rd = ed.GetDistance(od);
            if (rd.Status != PromptStatus.OK) return;
            AdicionarElemento(doc, ed, t, Pecas.CriarTubo(t.Atual, rd.Value, t.Material));
        }

        static void ComandoCurva(Document doc, Editor ed, Trecho t)
        {
            if (!t.Atual.TemDirecao)
            {
                ed.WriteMessage("\nDesenhe primeiro um trecho reto antes da curva.");
                return;
            }
            double? ang = PedirAngulo(ed, 90.0);
            if (ang == null) return;

            var kd = new PromptKeywordOptions(
                "\nDirecao da curva [Cima/Baixo/Esquerda/Direita/Ponto] <Direita>: ",
                "Cima Baixo Esquerda Direita Ponto")
            { AllowNone = true };
            kd.Keywords.Default = "Direita";
            PromptResult rk = ed.GetKeywords(kd);
            if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return;
            string dir = rk.Status == PromptStatus.None ? "Direita" : rk.StringResult;

            Point3d ultimo = t.Atual.P;
            if (dir == "Ponto")
            {
                var od = new PromptPointOptions(
                    string.Format("\nDirecao apos a curva de {0:G} graus (clique) ou distancia: ", ang.Value))
                { BasePoint = ultimo, UseBasePoint = true, UseDashedLine = true };
                PromptPointResult rd = ed.GetPoint(od);
                if (rd.Status != PromptStatus.OK) return;
                Vector3d v = rd.Value - ultimo;
                if (v.Length < 1e-6) return;
                AdicionarCurvaETubo(doc, ed, t, ang.Value, v.GetNormal(), v.Length);
                return;
            }

            var oc = new PromptDistanceOptions("\nComprimento do tubo apos a curva (m): ")
            { BasePoint = ultimo, UseBasePoint = true, AllowNegative = false, AllowZero = false };
            PromptDoubleResult rc = ed.GetDistance(oc);
            if (rc.Status != PromptStatus.OK) return;
            AdicionarCurvaETubo(doc, ed, t, ang.Value, Pecas.VetorDirecao(t.Atual.D, dir), rc.Value);
        }

        static void AdicionarCurvaETubo(Document doc, Editor ed, Trecho t, double ang, Vector3d alvo, double comp)
        {
            Elemento curva;
            // PEAD: joelho de eletrofusao (diametro maior, como bolsa); aluminio: curva soldada; demais: bolsa-bolsa
            bool pead = t.Material == "PEAD";
            Extremidade ce = pead ? Extremidade.Eletrofusao
                           : (t.Material == "Aluminio" ? Extremidade.Solda : Extremidade.Bolsa);
            try { curva = Pecas.CriarCurva(t.Atual, ang, alvo, t.Material, ce, ce, pead); }
            catch (System.Exception ex) { ed.WriteMessage("\nErro ao criar a curva: " + ex.Message); return; }
            if (!AdicionarElemento(doc, ed, t, curva)) return;
            AdicionarElemento(doc, ed, t, Pecas.CriarTubo(t.Atual, comp, t.Material));
        }

        static void ComandoPeca(Document doc, Editor ed, Trecho t)
        {
            PecaDialog.MaterialRede = t.Material;
            PecaEscolha esc = PecaDialog.Pedir(ed, t.Atual.DN);
            if (esc == null) return;

            Porta de = t.Atual;
            if (!de.TemDirecao)
            {
                var od = new PromptPointOptions("\nDirecao da peca (clique um ponto): ")
                { BasePoint = de.P, UseBasePoint = true, UseDashedLine = true };
                PromptPointResult rd = ed.GetPoint(od);
                if (rd.Status != PromptStatus.OK) return;
                Vector3d v = rd.Value - de.P;
                if (v.Length < 1e-6) return;
                de = Clonar(de, v.GetNormal());
            }

            Elemento peca;
            try { peca = Pecas.CriarPeca(de, esc, t.Material, de.D); }
            catch (System.Exception ex) { ed.WriteMessage("\nErro ao criar a peca: " + ex.Message); return; }

            if (!AdicionarElemento(doc, ed, t, peca)) return;

            // Te com reducao na derivacao: cria a reducao ja ligada a derivacao (trecho proprio,
            // de onde se pode continuar com AGUA_CONTINUAR).
            if (esc.Familia == "TE" && esc.DN3 > 0 && peca.Derivacao != null && esc.DN3 < peca.Derivacao.DN)
            {
                try
                {
                    var red = new PecaEscolha
                    {
                        Familia = "REDUCAO",
                        DN = peca.Derivacao.DN,
                        DN2 = esc.DN3,
                        EIn = peca.Derivacao.E == Extremidade.Flange ? Extremidade.Flange : Extremidade.Ponta,
                        EOut = esc.EBrOut,
                        PN = esc.PN
                    };
                    Elemento r = Pecas.CriarPeca(peca.Derivacao, red, t.Material, peca.Derivacao.D);
                    string runRamal = Guid.NewGuid().ToString("N").Substring(0, 8);
                    string prev = t.RunId + ":" + (t.Elementos.Count - 1) + ":B";
                    InserirAvulso(doc, ed, r, runRamal, 0, prev);
                    ed.WriteMessage("\nReducao DN{0}xDN{1} criada na derivacao. Para continuar o ramal use AGUA_CONTINUAR.",
                        peca.Derivacao.DN, esc.DN3);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nErro ao criar a reducao da derivacao: " + ex.Message);
                }
            }
        }

        // Insere um elemento que nao entra na cadeia do trecho atual (ex.: reducao do ramal de um te).
        static void InserirAvulso(Document doc, Editor ed, Elemento e, string runId, int seq, string prev)
        {
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                RegistrarApp(tr, doc.Database);
                var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                Solid3d s = e.Solido;
                s.ColorIndex = e.Cor;
                s.XData = e.XData(runId, seq, prev);
                espaco.AppendEntity(s);
                tr.AddNewlyCreatedDBObject(s, true);
                e.Ids.Add(s.ObjectId);
                tr.Commit();
            }
        }

        static void Desfazer(Document doc, Editor ed, Trecho t)
        {
            Elemento e = t.RemoverUltimo();
            if (e == null) { ed.WriteMessage("\nNada para desfazer."); return; }
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in e.Ids)
                {
                    if (id.IsErased) continue;
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    ent.Erase();
                }
                tr.Commit();
            }
        }

        // ----- utilitarios -----

        static Porta Clonar(Porta p, Vector3d dir)
        {
            return new Porta { P = p.P, D = dir, DN = p.DN, E = p.E, TemDirecao = true };
        }

        static bool AdicionarElemento(Document doc, Editor ed, Trecho t, Elemento e)
        {
            bool temAnterior = t.Elementos.Count > 0 || t.Prev != null;
            if (temAnterior && !Trecho.Compativel(t.Atual.E, e.EIn))
                ed.WriteMessage("\nAtencao: ligacao {0} -> {1} nao e compativel (ponta entra em bolsa; flange com flange).",
                    t.Atual.E, e.EIn);
            if (temAnterior && t.Atual.DN != e.DN)
                ed.WriteMessage("\nAtencao: DN da peca ({0}) difere do DN da linha ({1}).", e.DN, t.Atual.DN);

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    RegistrarApp(tr, doc.Database);
                    var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                    Solid3d s = e.Solido;
                    s.ColorIndex = e.Cor;
                    s.XData = e.XData(t.RunId, t.Elementos.Count, t.Elementos.Count == 0 ? t.Prev : null);
                    espaco.AppendEntity(s);
                    tr.AddNewlyCreatedDBObject(s, true);
                    e.Ids.Add(s.ObjectId);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao inserir no desenho: " + ex.Message);
                return false;
            }
            t.Adicionar(e);
            return true;
        }

        static string PedirMaterial(Editor ed)
        {
            var km = new PromptKeywordOptions(
                "\nMaterial da rede [DEFoFo/FoFo/PVCO/PVC/PEAD/Aluminio] <DEFoFo>: ", "DEFoFo FoFo PVCO PVC PEAD Aluminio")
            { AllowNone = true };
            km.Keywords.Default = "DEFoFo";
            PromptResult r = ed.GetKeywords(km);
            if (r.Status == PromptStatus.None) return "DEFoFo";
            if (r.Status != PromptStatus.OK) return null;
            return r.StringResult;
        }

        static double? PedirAngulo(Editor ed, double padrao)
        {
            var po = new PromptDoubleOptions(
                "\nAngulo da curva [90 / 45 / 22.5 / 11.25] <" + padrao.ToString("G") + ">: ")
            {
                DefaultValue = padrao, UseDefaultValue = true, AllowNone = true,
                AllowNegative = false, AllowZero = false
            };
            PromptDoubleResult r = ed.GetDouble(po);
            if (r.Status != PromptStatus.OK) return null;
            double valor = Catalogo.AnguloPadraoMaisProximo(r.Value);
            if (Math.Abs(valor - r.Value) > 0.01)
                ed.WriteMessage("\nAngulo {0:G} nao e padrao; usando {1:G} graus.", r.Value, valor);
            return valor;
        }

        static void RegistrarApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(Geometria.AppXData)) return;
            rat.UpgradeOpen();
            var ra = new RegAppTableRecord { Name = Geometria.AppXData };
            rat.Add(ra);
            tr.AddNewlyCreatedDBObject(ra, true);
        }
    }
}
