using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

[assembly: CommandClass(typeof(RedesAgua.QuantitativoComandos))]

namespace RedesAgua
{
    class Reg
    {
        public string Run = "", Tipo = "", Fam = "", Mat = "";
        public int Seq, DN, DN2, PN = 10;
        public Extremidade EIn = Extremidade.Ponta, EOut = Extremidade.Ponta, EBr = Extremidade.Ponta;
        public double Len, Ang;
        public string Prev = "";
        public int DnSaida { get { return (Fam == "REDUCAO" || Fam == "LUVA_LR") ? DN2 : DN; } }
    }

    class Linha
    {
        public string Categoria, Descricao, Unidade, Obs = "";
        public int DN;
        public double Qtd;
        public int Ordem;
    }

    public class QuantitativoComandos
    {
        static readonly CultureInfo Br = new CultureInfo("pt-BR");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Le as pecas pela XData, agrupa, calcula juntas e parafusos das flanges,
        // mostra na linha de comando e grava um CSV (separador ;) ao lado do desenho.
        [CommandMethod("AGUA_QUANT")]
        public void Quantitativo()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            var so = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione a sessao da rede (Enter = todo o desenho): "
            };
            PromptSelectionResult sel = ed.GetSelection(so);
            if (sel.Status == PromptStatus.Cancel) return;

            var regs = new List<Reg>();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                IEnumerable<ObjectId> ids;
                if (sel.Status == PromptStatus.OK)
                    ids = sel.Value.GetObjectIds();
                else
                {
                    var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
                    var todos = new List<ObjectId>();
                    foreach (ObjectId id in espaco) todos.Add(id);
                    ids = todos;
                }
                foreach (ObjectId id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    Reg r = Ler(ent);
                    if (r != null) regs.Add(r);
                }
                tr.Commit();
            }

            if (regs.Count == 0)
            {
                ed.WriteMessage("\nNenhuma peca da rede encontrada.");
                return;
            }

            List<Linha> linhas = Calcular(regs);
            Mostrar(ed, linhas);
            string caminho = GravarCsv(doc, linhas);
            if (caminho != null) ed.WriteMessage("\nArquivo gravado: " + caminho);
        }

        // ---------- leitura ----------

        internal static Reg Ler(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(Geometria.AppXData);
            if (rb == null) return null;
            using (rb)
            {
                TypedValue[] v = rb.AsArray();
                if (v.Length < 2) return null;

                if (Convert.ToString(v[1].Value) == "V2")
                {
                    var r = new Reg();
                    for (int i = 2; i < v.Length; i++)
                    {
                        string s = Convert.ToString(v[i].Value);
                        int k = s.IndexOf('=');
                        if (k < 0) continue;
                        string chave = s.Substring(0, k), val = s.Substring(k + 1);
                        switch (chave)
                        {
                            case "tipo": r.Tipo = val; break;
                            case "fam": r.Fam = val; break;
                            case "mat": r.Mat = val; break;
                            case "dn": r.DN = ParseInt(val); break;
                            case "dn2": r.DN2 = ParseInt(val); break;
                            case "pn": r.PN = ParseInt(val); break;
                            case "ein": r.EIn = ParseExt(val); break;
                            case "eout": r.EOut = ParseExt(val); break;
                            case "ebr": r.EBr = ParseExt(val); break;
                            case "len": r.Len = ParseDouble(val); break;
                            case "ang": r.Ang = ParseDouble(val); break;
                            case "run": r.Run = val; break;
                            case "seq": r.Seq = ParseInt(val); break;
                            case "prev": r.Prev = val; break;
                        }
                    }
                    return r;
                }

                // Formato antigo (v4): tipo, DN, material, comprimento, angulo
                if (v.Length >= 6)
                {
                    string tipo = Convert.ToString(v[1].Value);
                    return new Reg
                    {
                        Tipo = tipo, Fam = tipo, Run = "legado",
                        DN = Convert.ToInt32(v[2].Value), Mat = Convert.ToString(v[3].Value),
                        Len = Convert.ToDouble(v[4].Value), Ang = Convert.ToDouble(v[5].Value),
                        EIn = tipo == "CURVA" ? Extremidade.Bolsa : Extremidade.Ponta,
                        EOut = tipo == "CURVA" ? Extremidade.Bolsa : Extremidade.Ponta
                    };
                }
                return null;
            }
        }

        static int ParseInt(string s) { int x; return int.TryParse(s, NumberStyles.Integer, Inv, out x) ? x : 0; }
        static double ParseDouble(string s) { double x; return double.TryParse(s, NumberStyles.Float, Inv, out x) ? x : 0.0; }
        static Extremidade ParseExt(string s)
        {
            Extremidade e;
            return Enum.TryParse(s, out e) ? e : Extremidade.Ponta;
        }

        // ---------- calculo ----------

        static string Sigla(Extremidade e)
        {
            switch (e)
            {
                case Extremidade.Flange: return "Flange";
                case Extremidade.Bolsa: return "Bolsa";
                case Extremidade.JuntaMecanica: return "JM";
                case Extremidade.Fechada: return "Fechada";
                case Extremidade.Eletrofusao: return "Eletrofusão";
                case Extremidade.Termofusao: return "Termofusão";
                case Extremidade.Solda: return "Solda";
                default: return "Ponta";
            }
        }

        internal static string Nome(Reg r)
        {
            switch (r.Fam)
            {
                case "TUBO": return "Tubo " + r.Mat + " DN" + r.DN;
                case "CURVA":
                    if (r.EIn == Extremidade.Solda || r.EOut == Extremidade.Solda)
                        return "Curva soldada " + r.Ang.ToString("0.##", Br) + "° " + r.Mat + " DN" + r.DN;
                    return "Curva " + r.Ang.ToString("0.##", Br) + "° " + Sigla(r.EIn) + "/" + Sigla(r.EOut)
                           + " " + r.Mat + " DN" + r.DN;
                case "JOELHO":
                    return "Joelho " + r.Ang.ToString("0.##", Br) + "° " + Sigla(r.EIn) + "/" + Sigla(r.EOut)
                           + " " + r.Mat + " DN" + r.DN;
                case "BORBOLETA":
                    return "Registro borboleta " + r.Mat + " DN" + r.DN + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut);
                case "LUVA_LR":
                    return "Luva de redução (LR) DN" + Math.Max(r.DN, r.DN2) + "xDN" + Math.Min(r.DN, r.DN2)
                           + " bolsa-bolsa " + r.Mat;
                case "LUVA":
                    if (r.EIn == Extremidade.Eletrofusao) return "Luva de eletrofusão " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.Termofusao) return "Luva de termofusão " + r.Mat + " DN" + r.DN;
                    return "Luva bolsa-bolsa " + r.Mat + " DN" + r.DN;
                case "JM": return "Luva de junta mecânica (JM) " + r.Mat + " DN" + r.DN;
                case "CAP":
                    if (r.EIn == Extremidade.Flange) return "Flange cego " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.JuntaMecanica) return "Cap JM " + r.Mat + " DN" + r.DN;
                    return "Cap bolsa " + r.Mat + " DN" + r.DN;
                case "FLANGE":
                    return "Flange (anel) " + r.Mat + " DN" + r.DN;
                case "RETA":
                    if (r.EIn == Extremidade.Flange && r.EOut == Extremidade.Flange)
                        return "Carretel flangeado " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.Ponta && r.EOut == Extremidade.Flange)
                        return "Ponta-flange " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.Flange && r.EOut == Extremidade.Ponta)
                        return "Flange-ponta " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.Bolsa && r.EOut == Extremidade.Flange)
                        return "Bolsa-flange " + r.Mat + " DN" + r.DN;
                    if (r.EIn == Extremidade.Flange && r.EOut == Extremidade.Bolsa)
                        return "Flange-bolsa " + r.Mat + " DN" + r.DN;
                    return "Peça reta " + Sigla(r.EIn) + "/" + Sigla(r.EOut) + " " + r.Mat + " DN" + r.DN;
                case "REDUCAO":
                    return (r.DN2 > r.DN ? "Aumento DN" : "Redução DN") + Math.Max(r.DN, r.DN2) + "xDN" + Math.Min(r.DN, r.DN2) + " "
                           + Sigla(r.EIn) + "/" + Sigla(r.EOut) + " " + r.Mat;
                case "Y":
                    return "Junção Y 45° DN" + r.DN + "xDN" + r.DN2 + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut)
                           + "/" + Sigla(r.EBr) + " " + r.Mat;
                case "TE":
                    return "Tê DN" + r.DN + "xDN" + r.DN2 + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut)
                           + "/" + Sigla(r.EBr) + " " + r.Mat;
                case "RETENCAO":
                    return "Válvula de retenção " + r.Mat + " DN" + r.DN + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut);
                case "VRP":
                    return "Válvula redutora de pressão (VRP) " + r.Mat + " DN" + r.DN + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut);
                case "VENTOSA":
                    return "Ventosa " + r.Mat + " DN" + r.DN + " " + Sigla(r.EIn);
                case "REGISTRO":
                    return "Registro gaveta " + r.Mat + " DN" + r.DN + " " + Sigla(r.EIn) + "/" + Sigla(r.EOut);
                default:
                    return r.Fam + " DN" + r.DN;
            }
        }

        static bool SoldaCom(Extremidade a, Extremidade b, Extremidade tipo)
        {
            return (a == tipo && (b == tipo || b == Extremidade.Ponta)) || (a == Extremidade.Ponta && b == tipo);
        }

        static List<Linha> Calcular(List<Reg> regs)
        {
            var mapa = new Dictionary<string, Linha>();
            Action<int, string, string, int, double, string, string> add =
                (ordem, cat, desc, dn, qtd, un, obs) =>
                {
                    string chave = cat + "|" + desc + "|" + dn;
                    Linha l;
                    if (!mapa.TryGetValue(chave, out l))
                    {
                        l = new Linha { Ordem = ordem, Categoria = cat, Descricao = desc, DN = dn, Unidade = un, Obs = obs };
                        mapa[chave] = l;
                    }
                    l.Qtd += qtd;
                };

            // Tubos e pecas
            foreach (Reg r in regs)
            {
                if (r.Fam == "TUBO")
                    add(0, "Tubos", Nome(r), r.DN, r.Len, "m", "");
                else
                    add(1, "Pecas", Nome(r), r.DN, 1, "un", "");
            }

            // Juntas entre elementos consecutivos do mesmo trecho
            var livres = new Dictionary<int, int>();
            Action<int> livre = dn => { int n; livres.TryGetValue(dn, out n); livres[dn] = n + 1; };

            // Pares (A, porta de A, B): vizinhos na mesma sequencia + ligacoes de trechos retomados (prev=).
            var porChave = new Dictionary<string, Reg>();
            foreach (Reg r in regs)
                if (r.Run != "legado") porChave[r.Run + "|" + r.Seq] = r;

            var pares = new List<Tuple<Reg, char, Reg>>();
            var entradaUsada = new HashSet<Reg>();
            var saidaUsada = new HashSet<Reg>();
            var derivUsada = new HashSet<Reg>();
            foreach (Reg r in porChave.Values)
            {
                Reg prox;
                if (porChave.TryGetValue(r.Run + "|" + (r.Seq + 1), out prox))
                    pares.Add(Tuple.Create(r, 'S', prox));
                if (!string.IsNullOrEmpty(r.Prev))
                {
                    string[] p = r.Prev.Split(':');
                    Reg a;
                    if (p.Length == 3 && porChave.TryGetValue(p[0] + "|" + p[1], out a))
                        pares.Add(Tuple.Create(a, p[2].Length > 0 ? p[2][0] : 'S', r));
                }
            }

            {
                foreach (var par in pares)
                {
                    Reg a = par.Item1, b = par.Item3;
                    bool ramal = par.Item2 == 'B';
                    bool entrada = par.Item2 == 'E';   // trecho retomado pela entrada de A
                    Extremidade eA = ramal ? a.EBr : (entrada ? a.EIn : a.EOut);
                    int dn = ramal ? a.DN2 : (entrada ? a.DN : a.DnSaida);
                    entradaUsada.Add(b);
                    if (ramal) derivUsada.Add(a);
                    else if (entrada) entradaUsada.Add(a);
                    else saidaUsada.Add(a);

                    {
                        if (eA == Extremidade.Flange && b.EIn == Extremidade.Flange)
                        {
                            int n = Catalogo.ParafusosQtd(dn), m = Catalogo.ParafusosBitola(dn);
                            int c = Catalogo.ParafusoComprimentoMm(dn);
                            string pn = "PN" + a.PN;
                            add(2, "Juntas flangeadas", "Parafuso sextavado M" + m + " x " + c + " (" + pn + ")", dn, n, "un", "");
                            add(2, "Juntas flangeadas", "Porca sextavada M" + m, dn, n, "un", "");
                            add(2, "Juntas flangeadas", "Arruela lisa M" + m, dn, 2 * n, "un", "");
                            add(2, "Juntas flangeadas", "Junta de vedacao para flange DN" + dn + " " + pn, dn, 1, "un", "");
                        }
                        else if ((eA == Extremidade.JuntaMecanica && b.EIn == Extremidade.Ponta)
                              || (eA == Extremidade.Ponta && b.EIn == Extremidade.JuntaMecanica))
                        {
                            int n = Catalogo.JmParafusosQtd(dn), m = Catalogo.JmBitola(dn);
                            int c = Catalogo.JmParafusoComprimentoMm(dn);
                            add(3, "Juntas mecanicas", "Anel de borracha para junta mecanica DN" + dn, dn, 1, "un", "");
                            add(3, "Juntas mecanicas", "Contraflange (anel de aperto) JM DN" + dn, dn, 1, "un", "");
                            add(3, "Juntas mecanicas", "Parafuso T M" + m + " x " + c + " (JM)", dn, n, "un", "");
                            add(3, "Juntas mecanicas", "Porca sextavada M" + m + " (JM)", dn, n, "un", "");
                        }
                        else if ((eA == Extremidade.Bolsa && b.EIn == Extremidade.Ponta)
                              || (eA == Extremidade.Ponta && b.EIn == Extremidade.Bolsa))
                        {
                            add(3, "Juntas elasticas", "Anel de borracha para junta elastica DN" + dn, dn, 1, "un", "");
                        }
                        else if ((eA == Extremidade.Eletrofusao && b.EIn == Extremidade.Ponta)
                              || (eA == Extremidade.Ponta && b.EIn == Extremidade.Eletrofusao))
                            add(3, "Juntas soldadas", "Junta por eletrofusao DN" + dn, dn, 1, "un", "");
                        else if (SoldaCom(eA, b.EIn, Extremidade.Termofusao))
                            add(3, "Juntas soldadas", "Solda de topo (termofusao) DN" + dn, dn, 1, "un", "");
                        else if (SoldaCom(eA, b.EIn, Extremidade.Solda))
                            add(3, "Juntas soldadas", "Solda (aluminio) DN" + dn, dn, 1, "un", "");
                        else
                        {
                            add(4, "Verificar", "Ligacao incompativel " + Sigla(eA) + " -> " + Sigla(b.EIn)
                                + " (" + Nome(a) + " / " + Nome(b) + ")", dn, 1, "un", "revisar o projeto");
                        }
                    }
                }
            }

            // faces de flange sem par
            foreach (Reg r in regs)
            {
                bool legado = r.Run == "legado";
                if (r.EIn == Extremidade.Flange && (legado || !entradaUsada.Contains(r))) livre(r.DN);
                if (r.EOut == Extremidade.Flange && (legado || !saidaUsada.Contains(r))) livre(r.DnSaida);
                if ((r.Fam == "TE" || r.Fam == "Y") && r.EBr == Extremidade.Flange && !derivUsada.Contains(r)) livre(r.DN2);
            }

            foreach (var kv in livres.OrderBy(k => k.Key))
                add(4, "Verificar", "Flange sem par (extremidade livre)", kv.Key, kv.Value, "un",
                    "pode ser fim de linha ou derivacao ainda nao ligada");

            return mapa.Values.OrderBy(l => l.Ordem).ThenBy(l => l.Descricao).ToList();
        }

        // ---------- saida ----------

        static void Mostrar(Editor ed, List<Linha> linhas)
        {
            string atual = null;
            ed.WriteMessage("\n\n=== QUANTITATIVO ===");
            foreach (Linha l in linhas)
            {
                if (l.Categoria != atual)
                {
                    atual = l.Categoria;
                    ed.WriteMessage("\n-- " + atual.ToUpper() + " --");
                }
                string q = l.Unidade == "m" ? l.Qtd.ToString("F2", Br) : ((int)Math.Round(l.Qtd)).ToString(Br);
                ed.WriteMessage("\n  {0,-62} {1,9} {2}", l.Descricao, q, l.Unidade);
            }
        }

        static string GravarCsv(Document doc, List<Linha> linhas)
        {
            try
            {
                string pasta = null;
                string nome = "rede";
                if (!string.IsNullOrEmpty(doc.Name) && Path.IsPathRooted(doc.Name))
                {
                    pasta = Path.GetDirectoryName(doc.Name);
                    nome = Path.GetFileNameWithoutExtension(doc.Name);
                }
                if (string.IsNullOrEmpty(pasta))
                    pasta = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                string caminho = Path.Combine(pasta, "Quantitativo_" + nome + ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("Categoria;Descricao;DN (mm);Quantidade;Unidade;Observacao");
                foreach (Linha l in linhas)
                {
                    string q = l.Unidade == "m" ? l.Qtd.ToString("F2", Br) : ((int)Math.Round(l.Qtd)).ToString(Br);
                    sb.AppendLine(string.Join(";", new[]
                    {
                        l.Categoria, l.Descricao, l.DN.ToString(Br), q, l.Unidade, l.Obs
                    }));
                }
                File.WriteAllText(caminho, sb.ToString(), new UTF8Encoding(true));
                return caminho;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
