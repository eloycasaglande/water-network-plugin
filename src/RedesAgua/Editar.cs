using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RedesAgua
{
    public partial class Comandos
    {
        // Peca ja inserida -> PecaEscolha (para preencher o dialogo).
        static PecaEscolha EscolhaDe(Dictionary<string, string> c, Vector3d dir)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            int dn = int.Parse(c["dn"], inv);
            int dn2 = c.ContainsKey("dn2") ? int.Parse(c["dn2"], inv) : 0;
            var esc = new PecaEscolha
            {
                Familia = c["fam"],
                DN = dn,
                DN2 = dn2 > 0 ? dn2 : dn,
                EIn = (Extremidade)Enum.Parse(typeof(Extremidade), c["ein"]),
                EOut = (Extremidade)Enum.Parse(typeof(Extremidade), c["eout"]),
                EBr = c.ContainsKey("ebr") ? (Extremidade)Enum.Parse(typeof(Extremidade), c["ebr"]) : Extremidade.Flange,
                ComprimentoMm = c.ContainsKey("len") ? double.Parse(c["len"], inv) * 1000.0 : 300,
                PN = c.ContainsKey("pn") ? int.Parse(c["pn"], inv) : 10
            };
            if ((esc.Familia == "CURVA" || esc.Familia == "JOELHO") && c.ContainsKey("sd"))
            {
                esc.Angulo = c.ContainsKey("ang") ? double.Parse(c["ang"], inv) : 90;
                esc.Derivacao = MelhorDirecao(dir, LerPonto2(c["sd"]));
            }
            else if (c.ContainsKey("bd"))
                esc.Derivacao = MelhorDirecao(dir, LerPonto2(c["bd"]));
            return esc;
        }

        // Troca tipo, DN, extremidades, angulo ou direcao de uma peca ja inserida: abre o dialogo P
        // preenchido, refaz a peca na mesma entrada e desloca o que vem depois se o comprimento mudar.
        [CommandMethod("AGUA_EDITAR", CommandFlags.UsePickSet)]
        public void Editar()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            CultureInfo inv = CultureInfo.InvariantCulture;

            ObjectId idSel = ObjectId.Null;
            PromptSelectionResult impl = ed.SelectImplied();
            if (impl.Status == PromptStatus.OK && impl.Value.Count == 1) idSel = impl.Value.GetObjectIds()[0];
            else
            {
                var po = new PromptEntityOptions("\nSelecione a peca a editar: ");
                po.SetRejectMessage("\nSelecione um solido da rede.");
                po.AddAllowedClass(typeof(Solid3d), true);
                PromptEntityResult pr = ed.GetEntity(po);
                if (pr.Status != PromptStatus.OK) return;
                idSel = pr.ObjectId;
            }

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
            if (alvo == null)
            {
                ed.WriteMessage("\nO objeto selecionado nao e um elemento da rede.");
                return;
            }
            Dictionary<string, string> c = alvo.C;
            foreach (string k in new[] { "fam", "dn", "ein", "eout", "ip", "id", "sp", "sd" })
                if (!c.ContainsKey(k))
                {
                    ed.WriteMessage("\nElemento sem dados de conexao (criado em versao anterior); nao e possivel editar.");
                    return;
                }
            string fam = c["fam"];
            if (fam == "TUBO")
            {
                ed.WriteMessage("\nTubo nao tem dialogo de peca: use AGUA_ESTICAR para mudar o comprimento.");
                return;
            }

            Point3d ip = LerPonto(c["ip"]);
            Vector3d dir = LerPonto2(c["id"]);
            Point3d spAnt = LerPonto(c["sp"]);
            Vector3d sdAnt = LerPonto2(c["sd"]);
            string rmat = c.ContainsKey("rmat") && c["rmat"].Length > 0 ? c["rmat"] : c["mat"];
            int pn = c.ContainsKey("pn") ? int.Parse(c["pn"], inv) : 10;
            Extremidade ein = (Extremidade)Enum.Parse(typeof(Extremidade), c["ein"]);
            int dnLinha = int.Parse(c["dn"], inv);

            PecaEscolha atual = EscolhaDe(c, dir);
            PecaDialog.MaterialRede = rmat;
            PecaEscolha esc = PecaDialog.PedirEdicao(ed, dnLinha, atual);
            if (esc == null) return;
            esc.PN = pn;
            if ((fam == "REDUCAO" || fam == "JM") && esc.Familia == fam && esc.DN == atual.DN && esc.DN2 == atual.DN2)
                esc.ComprimentoForcadoMm = atual.ComprimentoMm;   // mantem o comprimento ja esticado

            Elemento novo;
            try
            {
                var de = new Porta { P = ip, D = dir, DN = dnLinha, E = ein, TemDirecao = true };
                novo = Pecas.CriarPeca(de, esc, rmat, dir);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nErro ao refazer a peca: " + ex.Message);
                return;
            }
            novo.PN = pn;

            Vector3d delta = novo.Saida.P - spAnt;

            // conjunto a deslocar: igual ao AGUA_ESTICAR
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
            string prefAlvo = alvo.Run + ":" + alvo.Seq + ":";
            ligados(prefAlvo, true);
            while (fila.Count > 0)
            {
                ItemRede m = fila.Dequeue();
                ligados(m.Run + ":" + m.Seq + ":", false);
            }

            // So da para deslocar o resto da rede se a saida (e a derivacao ligada) mantiver direcao e DN.
            if (mover.Count > 0)
            {
                if ((novo.Saida.D - sdAnt).Length > 1e-5)
                {
                    ed.WriteMessage("\nA nova peca sai em outra direcao e ha {0} elemento(s) depois dela; edicao cancelada.", mover.Count);
                    ed.WriteMessage("\n(Apague o trecho seguinte, edite e refaca com AGUA_CONTINUAR.)");
                    return;
                }
                if (itens.Any(it => it.Prev == prefAlvo + "B"))
                {
                    bool ok = novo.Derivacao != null && c.ContainsKey("bp") && c.ContainsKey("bd");
                    if (ok)
                    {
                        Vector3d relNovo = novo.Derivacao.P - novo.Entrada.P, relAnt = LerPonto(c["bp"]) - ip;
                        ok = (relNovo - relAnt).Length < 1e-5 && (novo.Derivacao.D - LerPonto2(c["bd"])).Length < 1e-5;
                    }
                    if (!ok)
                    {
                        ed.WriteMessage("\nHa um ramal ligado a derivacao e a nova peca a muda de posicao/direcao; edicao cancelada.");
                        return;
                    }
                }
                int sdnAnt;
                if (c.ContainsKey("sdn") && int.TryParse(c["sdn"], out sdnAnt) && sdnAnt != novo.DnSaida)
                    ed.WriteMessage("\nAtencao: a saida agora e DN{0}, mas o trecho seguinte continua em DN{1}.", novo.DnSaida, sdnAnt);
            }

            // aviso de compatibilidade com a peca anterior
            ItemRede ant = itens.FirstOrDefault(it => it.Run == alvo.Run && it.Seq == alvo.Seq - 1);
            string seAnt;
            if (ant != null && ant.C.TryGetValue("se", out seAnt))
            {
                Extremidade xa;
                if (Enum.TryParse(seAnt, out xa) && !Trecho.Compativel(xa, novo.EIn))
                    ed.WriteMessage("\nAtencao: ligacao {0} -> {1} nao e compativel (ponta entra em bolsa; flange com flange).", xa, novo.EIn);
            }

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                    var velho = (Solid3d)tr.GetObject(alvo.Id, OpenMode.ForWrite);
                    Solid3d s = novo.Solido;
                    s.SetPropertiesFrom(velho);                 // camada e cor atuais
                    if (novo.Familia != fam) s.ColorIndex = novo.Cor;   // outro tipo: cor padrao do tipo
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
                ed.WriteMessage("\nErro ao editar: " + ex.Message);
                return;
            }

            ed.WriteMessage("\nPeca editada ({0}). {1} elemento(s) posterior(es) deslocado(s) em {2:0.###} m.",
                novo.Familia, mover.Count, delta.Length);
            ed.WriteMessage("\n(Rotulos ja inseridos nao acompanham; refaca com AGUA_ROTULO se necessario.)");
        }
    }
}
