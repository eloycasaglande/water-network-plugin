using System;
using System.Collections.Generic;
using System.Globalization;

namespace RedesAgua
{
    public partial class Comandos
    {
        class ItemRede
        {
            public ObjectId Id;
            public Dictionary<string, string> C = new Dictionary<string, string>();
            public string Run { get { string s; return C.TryGetValue("run", out s) ? s : null; } }
            public int Seq { get { string s; int v; return C.TryGetValue("seq", out s) && int.TryParse(s, out v) ? v : -1; } }
            public string Prev { get { string s; return C.TryGetValue("prev", out s) ? s : null; } }
        }

        static Dictionary<string, string> LerCampos(Entity ent)
        {
            var campos = new Dictionary<string, string>();
            ResultBuffer rb = ent.GetXDataForApplication(Geometria.AppXData);
            if (rb == null) return campos;
            using (rb)
            {
                foreach (TypedValue v in rb.AsArray())
                {
                    string s = Convert.ToString(v.Value);
                    int k = s.IndexOf('=');
                    if (k > 0) campos[s.Substring(0, k)] = s.Substring(k + 1);
                }
            }
            return campos;
        }

        static string Fmt(Point3d p)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return p.X.ToString("R", inv) + ";" + p.Y.ToString("R", inv) + ";" + p.Z.ToString("R", inv);
        }

        // Desloca os pontos guardados no XData (sp, ip, bp).
        static ResultBuffer XDataDeslocado(Entity ent, Vector3d d)
        {
            ResultBuffer rb = ent.GetXDataForApplication(Geometria.AppXData);
            var lista = new List<TypedValue>();
            using (rb)
            {
                foreach (TypedValue v in rb.AsArray())
                {
                    string s = v.Value as string;
                    if (s != null && v.TypeCode == (short)DxfCode.ExtendedDataAsciiString &&
                        (s.StartsWith("sp=") || s.StartsWith("ip=") || s.StartsWith("bp=") || s.StartsWith("ctr=")))
                    {
                        int k = s.IndexOf('=') + 1;
                        Point3d p = LerPonto(s.Substring(k)) + d;
                        lista.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, s.Substring(0, k) + Fmt(p)));
                    }
                    else lista.Add(v);
                }
            }
            return new ResultBuffer(lista.ToArray());
        }


        // ---------- peca movida com MOVE: o XData guarda a posicao antiga ----------

        static Point3d CentroExtents(Entity e)
        {
            Extents3d x = e.GeometricExtents;
            return new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2,
                               (x.MinPoint.Z + x.MaxPoint.Z) / 2);
        }

        // Deslocamento (real - gravado) causado por MOVE. Usa o centro gravado ("ctr") ou, nos elementos
        // antigos, reconstroi a peca na posicao gravada e compara os centros.
        static Vector3d DesvioMovimento(Entity ent, Dictionary<string, string> c)
        {
            var zero = new Vector3d(0, 0, 0);
            try
            {
                Point3d atual = CentroExtents(ent);
                Point3d original;
                if (c.ContainsKey("ctr")) original = LerPonto(c["ctr"]);
                else
                {
                    Solid3d s = Reconstruir(c);
                    if (s == null) return zero;
                    original = CentroExtents(s);
                    s.Dispose();
                }
                Vector3d d = atual - original;
                return d.Length > 1e-6 ? d : zero;
            }
            catch (System.Exception) { return zero; }
        }

        static void AplicarDesvio(Dictionary<string, string> c, Vector3d d)
        {
            if (d.Length < 1e-9) return;
            foreach (string k in new[] { "sp", "ip", "bp", "ctr" })
                if (c.ContainsKey(k)) c[k] = Fmt(LerPonto(c[k]) + d);
        }

        // Refaz o solido de um elemento a partir do XData (posicao gravada). Null se nao for possivel.
        static Solid3d Reconstruir(Dictionary<string, string> c)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (!c.ContainsKey("ip") || !c.ContainsKey("id") || !c.ContainsKey("fam") || !c.ContainsKey("dn")) return null;
            string fam = c["fam"];
            Point3d ip = LerPonto(c["ip"]);
            Vector3d dir = LerPonto2(c["id"]);
            int dn = int.Parse(c["dn"], inv);
            int dn2 = c.ContainsKey("dn2") ? int.Parse(c["dn2"], inv) : 0;
            Extremidade ein = (Extremidade)Enum.Parse(typeof(Extremidade), c["ein"]);
            Extremidade eout = (Extremidade)Enum.Parse(typeof(Extremidade), c["eout"]);
            Extremidade ebr = c.ContainsKey("ebr") ? (Extremidade)Enum.Parse(typeof(Extremidade), c["ebr"]) : Extremidade.Flange;
            string mat = c.ContainsKey("rmat") && c["rmat"].Length > 0 ? c["rmat"] : c["mat"];
            double len = c.ContainsKey("len") ? double.Parse(c["len"], inv) : 0;
            var de = new Porta { P = ip, D = dir, DN = dn, E = ein, TemDirecao = true };

            if (fam == "TUBO") return Pecas.CriarTubo(de, len, mat).Solido;

            var esc = new PecaEscolha
            {
                Familia = fam, DN = dn, DN2 = dn2 > 0 ? dn2 : dn, EIn = ein, EOut = eout, EBr = ebr,
                ComprimentoMm = len * 1000.0, ComprimentoForcadoMm = (fam == "REDUCAO" || fam == "JM") ? len * 1000.0 : 0
            };
            if (fam == "CURVA" || fam == "JOELHO")
            {
                esc.Angulo = c.ContainsKey("ang") ? double.Parse(c["ang"], inv) : 90;
                Vector3d sd = LerPonto2(c["sd"]);
                // a direcao da curva e a que mais se aproxima do sentido de saida
                esc.Derivacao = MelhorDirecao(dir, sd);
            }
            else if (c.ContainsKey("bd"))
                esc.Derivacao = MelhorDirecao(dir, LerPonto2(c["bd"]));
            return Pecas.CriarPeca(de, esc, mat, dir).Solido;
        }

        static string MelhorDirecao(Vector3d rumo, Vector3d alvo)
        {
            Vector3d v = alvo - rumo * alvo.DotProduct(rumo);
            string melhor = "Cima";
            double dmax = -2;
            foreach (string n in new[] { "Cima", "Baixo", "Esquerda", "Direita" })
            {
                double d = v.Length < 1e-9 ? 0 : Pecas.VetorDirecao(rumo, n).DotProduct(v.GetNormal());
                if (d > dmax) { dmax = d; melhor = n; }
            }
            return melhor;
        }

        // Estica tubo, carretel (peca reta) ou reducao: a entrada fica parada e tudo que vem depois
        // (inclusive ramais ligados) e deslocado junto. Na reducao tambem ha a opcao de inverter.
        [CommandMethod("AGUA_ESTICAR", CommandFlags.UsePickSet)]
        public void Esticar()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            CultureInfo inv = CultureInfo.InvariantCulture;

            // peca ja selecionada antes de digitar o comando (PICKFIRST) ou escolhida agora
            ObjectId idSel = ObjectId.Null;
            PromptSelectionResult impl = ed.SelectImplied();
            if (impl.Status == PromptStatus.OK && impl.Value.Count == 1) idSel = impl.Value.GetObjectIds()[0];
            else
            {
                var po = new PromptEntityOptions("\nSelecione o tubo, carretel ou reducao a esticar: ");
                po.SetRejectMessage("\nSelecione um solido da rede.");
                po.AddAllowedClass(typeof(Solid3d), true);
                PromptEntityResult pr = ed.GetEntity(po);
                if (pr.Status != PromptStatus.OK) return;
                idSel = pr.ObjectId;
            }

            // le todos os elementos do espaco atual
            var itens = new List<ItemRede>();
            ItemRede alvo = null;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in espaco)
                {
                    if (id.IsErased) continue;
                    var s = tr.GetObject(id, OpenMode.ForRead) as Solid3d;
                    if (s == null) continue;
                    var it = new ItemRede { Id = id, C = LerCampos(s) };
                    if (it.Run == null || it.Seq < 0) continue;
                    itens.Add(it);
                    if (id == idSel)
                    {
                        alvo = it;
                        AplicarDesvio(it.C, DesvioMovimento(s, it.C));
                    }
                }
                tr.Commit();
            }
            if (alvo == null || !alvo.C.ContainsKey("ip") || !alvo.C.ContainsKey("id"))
            {
                ed.WriteMessage("\nElemento sem dados de conexao (criado em versao anterior); nao e possivel esticar.");
                return;
            }
            string fam = alvo.C.ContainsKey("fam") ? alvo.C["fam"] : "";
            if (fam != "TUBO" && fam != "RETA" && fam != "REDUCAO" && fam != "JM")
            {
                ed.WriteMessage("\nSo e possivel esticar tubo, carretel/peca reta, reducao e junta mecanica (selecionado: {0}).", fam);
                return;
            }

            Point3d ip = LerPonto(alvo.C["ip"]);
            Vector3d dir = LerPonto2(alvo.C["id"]);
            int dn = int.Parse(alvo.C["dn"], inv);
            int dn2 = alvo.C.ContainsKey("dn2") ? int.Parse(alvo.C["dn2"], inv) : 0;
            double lenAtual = double.Parse(alvo.C["len"], inv);   // m
            Extremidade ein = (Extremidade)Enum.Parse(typeof(Extremidade), alvo.C["ein"]);
            Extremidade eout = (Extremidade)Enum.Parse(typeof(Extremidade), alvo.C["eout"]);
            string rmat = alvo.C.ContainsKey("rmat") && alvo.C["rmat"].Length > 0 ? alvo.C["rmat"] : alvo.C["mat"];
            int pn = alvo.C.ContainsKey("pn") ? int.Parse(alvo.C["pn"], inv) : 10;

            // Arraste a ponta livre (saida) ate o novo comprimento; a entrada fica parada.
            // O comprimento e a projecao do ponto clicado sobre o eixo do elemento; "Comprimento" = digitar.
            bool inverter = false;
            bool tubo = fam == "TUBO";
            double novoLen = lenAtual;   // m
            string un = tubo ? "m" : "mm";
            double fator = tubo ? 1.0 : 1000.0;
            Vector3d eixo = dir.GetNormal();
            while (true)
            {
                var pp = new PromptPointOptions(string.Format(inv,
                    "\nArraste a ponta livre ate o novo comprimento (atual {0:0.###} {1}) [Comprimento{2}]: ",
                    lenAtual * fator, un, fam == "REDUCAO" ? "/Inverter" : ""),
                    fam == "REDUCAO" ? "Comprimento Inverter" : "Comprimento")
                { BasePoint = ip, UseBasePoint = true, UseDashedLine = true };
                PointMonitorEventHandler mon = (s, a) =>
                {
                    try
                    {
                        double l = (a.Context.RawPoint - ip).DotProduct(eixo);
                        a.AppendToolTipText(string.Format(inv, "{0:0.###} {1}", Math.Max(l, 0) * fator, un));
                    }
                    catch (System.Exception) { }
                };
                ed.PointMonitor += mon;
                PromptPointResult rp;
                try { rp = ed.GetPoint(pp); }
                finally { ed.PointMonitor -= mon; }

                if (rp.Status == PromptStatus.Keyword && rp.StringResult == "Inverter") { inverter = true; break; }
                if (rp.Status == PromptStatus.Keyword && rp.StringResult == "Comprimento")
                {
                    var pd = new PromptDoubleOptions(string.Format(inv, "\nNovo comprimento ({0}) <{1:0.###}>: ",
                        un, lenAtual * fator))
                    { DefaultValue = lenAtual * fator, UseDefaultValue = true, AllowNone = true, AllowNegative = false, AllowZero = false };
                    PromptDoubleResult rd = ed.GetDouble(pd);
                    if (rd.Status != PromptStatus.OK) return;
                    novoLen = rd.Value / fator;
                    break;
                }
                if (rp.Status != PromptStatus.OK) return;
                double proj = (rp.Value - ip).DotProduct(eixo);
                if (proj < 0.001) { ed.WriteMessage("\nPonto antes da entrada do elemento; arraste no sentido do fluxo."); continue; }
                novoLen = proj;
                break;
            }

            // reconstroi o elemento
            Elemento novo;
            try
            {
                var de = new Porta { P = ip, D = dir, DN = dn, E = ein, TemDirecao = true };
                if (fam == "TUBO")
                    novo = Pecas.CriarTubo(de, novoLen, rmat);
                else
                {
                    var esc = new PecaEscolha { Familia = fam, DN = dn, DN2 = dn2 > 0 ? dn2 : dn, EIn = ein, EOut = eout, PN = pn };
                    if (fam == "RETA") esc.ComprimentoMm = novoLen * 1000.0;
                    else if (inverter)
                    {
                        esc.DN = dn2; esc.DN2 = dn; esc.EIn = eout; esc.EOut = ein;
                    }
                    else esc.ComprimentoForcadoMm = novoLen * 1000.0;
                    novo = Pecas.CriarPeca(de, esc, rmat, dir);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao refazer o elemento: " + ex.Message);
                return;
            }
            novo.PN = pn;

            Vector3d delta = novo.Saida.P - LerPonto(alvo.C["sp"]);

            // conjunto a deslocar: elementos seguintes do mesmo trecho + trechos ligados a eles (saida/derivacao)
            var mover = new HashSet<ObjectId>();
            var runsMovidos = new HashSet<string>();
            var fila = new Queue<ItemRede>();
            foreach (ItemRede it in itens)
                if (it.Run == alvo.Run && it.Seq > alvo.Seq) { mover.Add(it.Id); fila.Enqueue(it); }

            Action<string, bool> ligados = (prefixo, ignorarEntrada) =>
            {
                foreach (ItemRede it in itens)
                {
                    string p = it.Prev;
                    if (p == null || !p.StartsWith(prefixo)) continue;
                    string suf = p.Substring(prefixo.Length);
                    if (ignorarEntrada && suf == "E") continue;
                    if (runsMovidos.Add(it.Run))
                        foreach (ItemRede it2 in itens)
                            if (it2.Run == it.Run && mover.Add(it2.Id)) fila.Enqueue(it2);
                }
            };
            ligados(alvo.Run + ":" + alvo.Seq + ":", true);
            while (fila.Count > 0)
            {
                ItemRede m = fila.Dequeue();
                ligados(m.Run + ":" + m.Seq + ":", false);
            }

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                    var velho = (Solid3d)tr.GetObject(alvo.Id, OpenMode.ForWrite);
                    Solid3d s = novo.Solido;
                    s.SetPropertiesFrom(velho);
                    s.XData = novo.XData(alvo.Run, alvo.Seq, alvo.Prev);
                    espaco.AppendEntity(s);
                    tr.AddNewlyCreatedDBObject(s, true);
                    velho.Erase();

                    if (delta.Length > 1e-9)
                    {
                        Matrix3d mat = Matrix3d.Displacement(delta);
                        foreach (ObjectId id in mover)
                        {
                            var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            ResultBuffer xd = XDataDeslocado(e, delta);
                            e.TransformBy(mat);
                            e.XData = xd;
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao esticar: " + ex.Message);
                return;
            }

            if (inverter)
                ed.WriteMessage("\nReducao invertida: agora DN{0} -> DN{1}. {2} elemento(s) posterior(es) deslocado(s).",
                    dn2, dn, mover.Count);
            else
                ed.WriteMessage("\nElemento esticado (deslocamento {0:0.###} m). {1} elemento(s) posterior(es) deslocado(s).",
                    delta.Length, mover.Count);
            ed.WriteMessage("\n(Rotulos ja inseridos nao acompanham; refaca com AGUA_ROTULO se necessario.)");
        }
    }
}
