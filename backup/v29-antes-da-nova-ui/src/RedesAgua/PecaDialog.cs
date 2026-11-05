using System;
using System.Drawing;
using System.Windows.Forms;

namespace RedesAgua
{
    // Selecao de peca. Devolve um PecaEscolha simples; se o dialogo falhar em tempo de
    // execucao, cai para um menu por palavras-chave na linha de comando.
    public class PecaDialog : Form
    {
        // Ordem alfabetica.
        static readonly string[] NomesFamilia =
        {
            "Bolsa-flange",
            "Cap / flange cego",
            "Carretel flangeado",
            "Curva (11,25° / 22,5° / 45° / 90°)",
            "Curva soldada (alumínio)",
            "Flange (anel)",
            "Joelho (11,25° / 22,5° / 45° / 90°)",
            "Junção Y 45°",
            "Junta mecânica (luva JM)",
            "Luva (bolsa-bolsa)",
            "Luva eletrofusão (PEAD)",
            "Luva termofusão (PEAD)",
            "Luva de redução (LR)",
            "Peça reta (extremidades à escolha)",
            "Ponta-flange",
            "Redução / aumento",
            "Registro borboleta",
            "Registro gaveta",
            "Tê (derivação)",
            "Válvula de retenção",
            "Válvula redutora de pressão (VRP)",
            "Ventosa"
        };
        // Familia interna e "preset" (variacao da familia) de cada item acima.
        static readonly string[] ChavesFamilia =
        {
            "RETA", "CAP", "RETA", "CURVA", "CURVA", "FLANGE", "JOELHO", "Y", "JM", "LUVA", "LUVA", "LUVA", "LUVA_LR", "RETA",
            "RETA", "REDUCAO", "BORBOLETA", "REGISTRO", "TE", "RETENCAO", "VRP", "VENTOSA"
        };
        static readonly string[] PresetsFamilia =
        {
            "BOLSAFLANGE", "", "CARRETEL", "", "SOLDA", "", "", "", "", "", "ELETRO", "TERMO", "", "RETA",
            "PONTAFLANGE", "REDUCAO", "", "", "", "", "", ""
        };
        static readonly string[] NomesExt = { "Bolsa", "Eletrofusão", "Flange", "Junta mecânica", "Ponta", "Soldada", "Termofusão" };
        static readonly string[] NomesDir = { "Baixo", "Cima", "Direita", "Esquerda" };
        static readonly string[] NomesAng = { "11.25", "22.5", "45", "90" };

        // Material da rede em desenho: em PEAD, curva/joelho/luva padrao sao de eletrofusao.
        public static string MaterialRede = "";
        readonly int dnAtual;
        ComboBox cbFam, cbDn, cbDn2, cbIn, cbOut, cbBr, cbDer, cbAng, cbDn3, cbBrOut;
        NumericUpDown nudL;
        CheckBox chkInv;
        Label lblDn, lblDn2, lblBr, lblDer, lblL, lblAng, lblDn3, lblBrOut;
        public PecaEscolha Resultado;

        PecaDialog(int dn)
        {
            dnAtual = dn;
            Text = "Peça - linha DN" + dn;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;

            int y = 14;
            Linha("Peça", out cbFam, ref y);
            cbFam.Items.AddRange(NomesFamilia);
            cbFam.SelectedIndex = Array.IndexOf(ChavesFamilia, "TE");

            lblDn = Linha("DN da peça (linha: " + dn + ")", out cbDn, ref y);
            foreach (int d in Catalogo.DNs) cbDn.Items.Add(d.ToString());
            cbDn.SelectedItem = dn.ToString();

            lblDn2 = Linha("DN derivação / saída", out cbDn2, ref y);
            foreach (int d in Catalogo.DNs) cbDn2.Items.Add(d.ToString());
            cbDn2.SelectedItem = dn.ToString();

            Linha("Extremidade entrada", out cbIn, ref y);
            Linha("Extremidade saída", out cbOut, ref y);
            lblBr = Linha("Extremidade derivação", out cbBr, ref y);
            foreach (ComboBox c in new[] { cbIn, cbOut, cbBr })
            {
                c.Items.AddRange(NomesExt);
                c.SelectedItem = "Flange";
            }

            lblDn3 = Linha("Redução na derivação (DN)", out cbDn3, ref y);
            lblBrOut = Linha("Extremidade da redução", out cbBrOut, ref y);
            cbBrOut.Items.AddRange(new[] { "Bolsa", "Flange", "Junta mecânica" });
            cbBrOut.SelectedItem = "Flange";

            lblDer = Linha("Direção", out cbDer, ref y);
            cbDer.Items.AddRange(NomesDir);
            cbDer.SelectedItem = "Cima";

            lblAng = Linha("Ângulo (graus)", out cbAng, ref y);
            cbAng.Items.AddRange(NomesAng);
            cbAng.SelectedItem = "90";

            chkInv = new CheckBox { Text = "Inverter (aumento)", Left = 180, Top = y, Width = 180 };
            Controls.Add(chkInv);
            y += 30;

            lblL = new Label { Text = "Comprimento (mm)", Left = 14, Top = y + 4, Width = 160 };
            nudL = new NumericUpDown
            {
                Left = 180, Top = y, Width = 180, Minimum = 100, Maximum = 6000, Increment = 50, Value = 300
            };
            Controls.Add(lblL);
            Controls.Add(nudL);
            y += 36;

            var ok = new Button { Text = "OK", Left = 180, Top = y + 6, Width = 85, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Left = 275, Top = y + 6, Width = 85, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(380, y + 46);

            AtualizarOpcoesRedDeriv();
            cbFam.SelectedIndexChanged += (s, e) => { AplicarPadroes(); Atualizar(); };
            cbDn2.SelectedIndexChanged += (s, e) => AtualizarOpcoesRedDeriv();
            chkInv.CheckedChanged += (s, e) => Atualizar();
            AplicarPadroes();
            Atualizar();
        }

        string FamiliaAtual { get { return ChavesFamilia[Math.Max(0, cbFam.SelectedIndex)]; } }
        string PresetAtual
        {
            get
            {
                if (FamiliaAtual == "REDUCAO") return chkInv != null && chkInv.Checked ? "AUMENTO" : "REDUCAO";
                return PresetsFamilia[Math.Max(0, cbFam.SelectedIndex)];
            }
        }

        // DN de saida/derivacao: aumento so oferece DN maiores; reducao, menores; te, ate o DN da linha; Y, qualquer DN.
        void AtualizarDn2()
        {
            string fam = FamiliaAtual, preset = PresetAtual;
            string anterior = cbDn2.SelectedItem as string;
            cbDn2.Items.Clear();
            foreach (int d in Catalogo.DNs)
            {
                bool ok = true;
                if (fam == "TE") ok = d <= dnAtual;   // Y: derivacao com DN independente (qualquer DN)
                else if (fam == "REDUCAO") ok = preset == "AUMENTO" ? d > dnAtual : d < dnAtual;
                if (ok) cbDn2.Items.Add(d.ToString());
            }
            if (cbDn2.Items.Count == 0) cbDn2.Items.Add(dnAtual.ToString());

            if (anterior != null && cbDn2.Items.Contains(anterior))
                cbDn2.SelectedItem = anterior;
            else if (fam == "REDUCAO" && preset == "AUMENTO")
                cbDn2.SelectedIndex = 0;
            else if (fam == "REDUCAO")
                cbDn2.SelectedIndex = cbDn2.Items.Count - 1;
            else if (cbDn2.Items.Contains(dnAtual.ToString()))
                cbDn2.SelectedItem = dnAtual.ToString();
            else
                cbDn2.SelectedIndex = cbDn2.Items.Count - 1;
        }

        Label Linha(string texto, out ComboBox cb, ref int y)
        {
            var l = new Label { Text = texto, Left = 14, Top = y + 4, Width = 160 };
            cb = new ComboBox { Left = 180, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            Controls.Add(l);
            Controls.Add(cb);
            y += 34;
            return l;
        }

        // Reducao na derivacao: "Nenhuma" ou qualquer DN menor que o da derivacao.
        void AtualizarOpcoesRedDeriv()
        {
            if (cbDn3 == null || cbDn2.SelectedItem == null) return;
            int dnDeriv = int.Parse((string)cbDn2.SelectedItem);
            string anterior = cbDn3.SelectedItem as string;
            cbDn3.Items.Clear();
            cbDn3.Items.Add("Nenhuma");
            foreach (int d in Catalogo.DNs)
                if (d < dnDeriv) cbDn3.Items.Add(d.ToString());
            cbDn3.SelectedItem = (anterior != null && cbDn3.Items.Contains(anterior)) ? anterior : "Nenhuma";
        }

        // Extremidades sugeridas ao trocar de peca.
        void AplicarPadroes()
        {
            string f = FamiliaAtual;
            string padrao = (f == "CURVA" || f == "JOELHO" || f == "LUVA" || f == "LUVA_LR" || f == "BORBOLETA") ? "Bolsa" : "Flange";
            if (MaterialRede == "PEAD" && (f == "CURVA" || f == "JOELHO")) padrao = "Eletrofusão";
            cbIn.SelectedItem = padrao;
            cbOut.SelectedItem = padrao;
            cbBr.SelectedItem = padrao;
            if (f == "CAP" || f == "VENTOSA") cbIn.SelectedItem = "Flange";
            if (f == "FLANGE")
            {
                cbIn.SelectedItem = "Bolsa";
                cbOut.SelectedItem = "Flange";
            }

            switch (PresetAtual)
            {
                case "PONTAFLANGE":   // flange na entrada (liga na peca anterior), ponta na saida
                    cbIn.SelectedItem = "Flange";
                    cbOut.SelectedItem = "Ponta";
                    break;
                case "BOLSAFLANGE":
                    cbIn.SelectedItem = "Bolsa";
                    cbOut.SelectedItem = "Flange";
                    break;
                case "CARRETEL":
                    cbIn.SelectedItem = "Flange";
                    cbOut.SelectedItem = "Flange";
                    break;
                case "SOLDA":
                    cbIn.SelectedItem = "Soldada";
                    cbOut.SelectedItem = "Soldada";
                    break;
                case "ELETRO":
                    cbIn.SelectedItem = "Eletrofusão";
                    cbOut.SelectedItem = "Eletrofusão";
                    break;
                case "TERMO":
                    cbIn.SelectedItem = "Termofusão";
                    cbOut.SelectedItem = "Termofusão";
                    break;
            }
        }

        void Atualizar()
        {
            string fam = FamiliaAtual;
            bool y = fam == "Y", te = fam == "TE", red = fam == "REDUCAO", reta = fam == "RETA", luva = fam == "LUVA";
            bool jm = fam == "JM", cap = fam == "CAP", lr = fam == "LUVA_LR";
            bool curva = fam == "CURVA" || fam == "JOELHO";
            bool vent = fam == "VENTOSA", fl = fam == "FLANGE";
            bool solda = fam == "CURVA" && PresetAtual == "SOLDA";
            AtualizarDn2();

            // Te e reducao partem do DN da linha; as demais pecas tem DN proprio selecionavel.
            cbDn.Enabled = !(te || y || red);
            lblDn.Enabled = cbDn.Enabled;
            if (!cbDn.Enabled) cbDn.SelectedItem = dnAtual.ToString();

            cbDn2.Enabled = te || y || red || lr;
            lblDn2.Enabled = cbDn2.Enabled;
            lblDn2.Text = (te || y) ? "DN da derivação"
                : (PresetAtual == "AUMENTO" ? "DN maior (saída)" : (PresetAtual == "REDUCAO" ? "DN menor (saída)" : "DN de saída"));

            cbBr.Enabled = te || y;
            lblBr.Enabled = te || y;
            cbDn3.Enabled = te;
            lblDn3.Enabled = te;
            cbBrOut.Enabled = te;
            lblBrOut.Enabled = te;

            cbDer.Enabled = te || y || curva;
            lblDer.Enabled = te || y || curva;
            lblDer.Text = (te || y) ? "Direção da derivação" : "Direção da curva";
            cbAng.Enabled = curva;
            lblAng.Enabled = curva;
            chkInv.Enabled = red;
            nudL.Enabled = reta;
            lblL.Enabled = reta;

            cbIn.Enabled = !(luva || jm || lr || fl || solda);
            cbOut.Enabled = !(luva || jm || lr || cap || vent || fl || solda);
            if (lr || (luva && PresetAtual == ""))
            {
                cbIn.SelectedItem = "Bolsa";
                cbOut.SelectedItem = "Bolsa";
            }
            if (jm)
            {
                cbIn.SelectedItem = "Junta mecânica";
                cbOut.SelectedItem = "Junta mecânica";
            }
            if ((cap || vent) && (string)cbIn.SelectedItem == "Ponta")
                cbIn.SelectedItem = "Flange";
            if (fl)
            {
                cbIn.SelectedItem = "Bolsa";
                cbOut.SelectedItem = "Flange";
            }
            if (te && cbDn2.SelectedItem != null && int.Parse((string)cbDn2.SelectedItem) > dnAtual)
                cbDn2.SelectedItem = dnAtual.ToString();
        }

        static Extremidade Ext(object item)
        {
            switch ((string)item)
            {
                case "Bolsa": return Extremidade.Bolsa;
                case "Ponta": return Extremidade.Ponta;
                case "Junta mecânica": return Extremidade.JuntaMecanica;
                case "Eletrofusão": return Extremidade.Eletrofusao;
                case "Termofusão": return Extremidade.Termofusao;
                case "Soldada": return Extremidade.Solda;
                default: return Extremidade.Flange;
            }
        }

        void Coletar()
        {
            var r = new PecaEscolha
            {
                Familia = FamiliaAtual,
                DN = int.Parse((string)cbDn.SelectedItem),
                DN2 = int.Parse((string)cbDn2.SelectedItem),
                EIn = Ext(cbIn.SelectedItem),
                EOut = Ext(cbOut.SelectedItem),
                EBr = Ext(cbBr.SelectedItem),
                ComprimentoMm = (double)nudL.Value,
                Derivacao = (string)cbDer.SelectedItem,
                Angulo = double.Parse((string)cbAng.SelectedItem, System.Globalization.CultureInfo.InvariantCulture),
                PN = 10
            };
            if (r.Familia == "TE")
            {
                if (r.DN2 > dnAtual) r.DN2 = dnAtual;
                string red = (string)cbDn3.SelectedItem;
                if (red != null && red != "Nenhuma")
                {
                    r.DN3 = int.Parse(red);
                    r.EBrOut = Ext(cbBrOut.SelectedItem);
                }
            }
            if (MaterialRede == "PEAD" && r.Familia == "LUVA" && PresetAtual == "")
                r.EIn = r.EOut = Extremidade.Eletrofusao;
            Resultado = r;
        }

        public static PecaEscolha Pedir(Editor ed, int dn)
        {
            try
            {
                using (var f = new PecaDialog(dn))
                {
                    DialogResult dr = CadApp.ShowModalDialog(f);
                    if (dr != DialogResult.OK) return null;
                    f.Coletar();
                    return f.Resultado;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n(Dialogo indisponivel: {0}. Usando menu na linha de comando.)", ex.Message);
                return PedirPorTexto(ed, dn);
            }
        }

        static Extremidade ExtDeTexto(string s)
        {
            switch (s)
            {
                case "Bolsa": return Extremidade.Bolsa;
                case "Ponta": return Extremidade.Ponta;
                case "Jm": return Extremidade.JuntaMecanica;
                case "Eletro": return Extremidade.Eletrofusao;
                case "Termo": return Extremidade.Termofusao;
                case "Solda": return Extremidade.Solda;
                default: return Extremidade.Flange;
            }
        }

        // Menu alternativo na linha de comando (ordem alfabetica).
        static PecaEscolha PedirPorTexto(Editor ed, int dn)
        {
            var kf = new PromptKeywordOptions(
                "\nPeca [Aumento/BolsaFlange/Borboleta/Cap/Carretel/Curva/CurvaSolda/Flange/Jm/Joelho/Y/LR/Luva/LuvaEletro/LuvaTermo/PecaReta/PontaFlange/Reducao/Registro/Retencao/Te/Ventosa/Vrp] <Te>: ",
                "Aumento BolsaFlange Borboleta Cap Carretel Curva CurvaSolda Flange Jm Joelho Y LR Luva LuvaEletro LuvaTermo PecaReta PontaFlange Reducao Registro Retencao Te Ventosa Vrp")
            { AllowNone = true };
            kf.Keywords.Default = "Te";
            PromptResult rf = ed.GetKeywords(kf);
            if (rf.Status != PromptStatus.OK && rf.Status != PromptStatus.None) return null;
            string f = rf.Status == PromptStatus.None ? "Te" : rf.StringResult;

            var e = new PecaEscolha { DN = dn, DN2 = dn };
            bool aumento = f == "Aumento";
            bool retaFixa = false;   // extremidades ja definidas pelo nome da peca
            switch (f)
            {
                case "Aumento": e.Familia = "REDUCAO"; break;
                case "BolsaFlange":
                    e.Familia = "RETA"; e.EIn = Extremidade.Bolsa; e.EOut = Extremidade.Flange; retaFixa = true;
                    break;
                case "Borboleta": e.Familia = "BORBOLETA"; break;
                case "Cap": e.Familia = "CAP"; break;
                case "Carretel":
                    e.Familia = "RETA"; e.EIn = Extremidade.Flange; e.EOut = Extremidade.Flange; retaFixa = true;
                    break;
                case "PontaFlange":
                    e.Familia = "RETA"; e.EIn = Extremidade.Flange; e.EOut = Extremidade.Ponta; retaFixa = true;
                    break;
                case "PecaReta": e.Familia = "RETA"; break;
                case "Curva": e.Familia = "CURVA"; break;
                case "Flange": e.Familia = "FLANGE"; break;
                case "Joelho": e.Familia = "JOELHO"; break;
                case "Y": e.Familia = "Y"; break;
                case "Jm": e.Familia = "JM"; break;
                case "LR": e.Familia = "LUVA_LR"; break;
                case "Luva": e.Familia = "LUVA"; break;
                case "LuvaEletro":
                    e.Familia = "LUVA"; e.EIn = e.EOut = Extremidade.Eletrofusao; retaFixa = true;
                    break;
                case "LuvaTermo":
                    e.Familia = "LUVA"; e.EIn = e.EOut = Extremidade.Termofusao; retaFixa = true;
                    break;
                case "CurvaSolda":
                    e.Familia = "CURVA"; e.EIn = e.EOut = Extremidade.Solda; retaFixa = true;
                    break;
                case "Reducao": e.Familia = "REDUCAO"; break;
                case "Registro": e.Familia = "REGISTRO"; break;
                case "Retencao": e.Familia = "RETENCAO"; break;
                case "Ventosa": e.Familia = "VENTOSA"; break;
                case "Vrp": e.Familia = "VRP"; break;
                default: e.Familia = "TE"; break;
            }

            if (e.Familia != "TE" && e.Familia != "Y" && e.Familia != "REDUCAO")
            {
                var pd = new PromptIntegerOptions("\nDN da peca <" + dn + ">: ")
                { DefaultValue = dn, UseDefaultValue = true, AllowNone = true, LowerLimit = 50, UpperLimit = 700 };
                PromptIntegerResult rd0 = ed.GetInteger(pd);
                if (rd0.Status != PromptStatus.OK) return null;
                e.DN = Catalogo.DnPadrao(rd0.Value);
                e.DN2 = e.DN;
            }

            if (e.Familia == "TE" || e.Familia == "Y" || e.Familia == "REDUCAO" || e.Familia == "LUVA_LR")
            {
                var pi = new PromptIntegerOptions((e.Familia == "TE" || e.Familia == "Y")
                    ? "\nDN da derivacao <" + dn + ">: " : "\nDN de saida <" + dn + ">: ")
                { DefaultValue = dn, UseDefaultValue = true, AllowNone = true, LowerLimit = 50, UpperLimit = 700 };
                PromptIntegerResult ri = ed.GetInteger(pi);
                if (ri.Status != PromptStatus.OK) return null;
                e.DN2 = Catalogo.DnPadrao(ri.Value);
                if (aumento && e.DN2 <= dn)
                {
                    ed.WriteMessage("\nAumento precisa de DN maior que DN{0}.", dn);
                    return null;
                }
                if (f == "Reducao" && e.DN2 >= dn)
                {
                    ed.WriteMessage("\nReducao precisa de DN menor que DN{0}.", dn);
                    return null;
                }
            }

            if (retaFixa)
            {
                // extremidades ja definidas (carretel, ponta-flange, bolsa-flange)
            }
            else if (e.Familia == "JM")
            {
                e.EIn = e.EOut = Extremidade.JuntaMecanica;
            }
            else if (e.Familia == "LUVA" || e.Familia == "LUVA_LR")
            {
                e.EIn = e.EOut = Extremidade.Bolsa;
            }
            else if (e.Familia == "FLANGE")
            {
                var kj = new PromptKeywordOptions("\nEntrada [Bolsa/Jm] <Bolsa>: ", "Bolsa Jm") { AllowNone = true };
                kj.Keywords.Default = "Bolsa";
                PromptResult rj = ed.GetKeywords(kj);
                if (rj.Status != PromptStatus.OK && rj.Status != PromptStatus.None) return null;
                e.EIn = (rj.Status == PromptStatus.OK && rj.StringResult == "Jm") ? Extremidade.JuntaMecanica : Extremidade.Bolsa;
                e.EOut = Extremidade.Flange;
            }
            else if (e.Familia == "RETA")
            {
                var ki = new PromptKeywordOptions("\nEntrada [Flange/Bolsa/Ponta/Jm] <Flange>: ", "Flange Bolsa Ponta Jm")
                { AllowNone = true };
                ki.Keywords.Default = "Flange";
                PromptResult ri = ed.GetKeywords(ki);
                if (ri.Status != PromptStatus.OK && ri.Status != PromptStatus.None) return null;
                e.EIn = ExtDeTexto(ri.Status == PromptStatus.OK ? ri.StringResult : "Flange");

                var ko = new PromptKeywordOptions("\nSaida [Flange/Bolsa/Ponta/Jm] <Ponta>: ", "Flange Bolsa Ponta Jm")
                { AllowNone = true };
                ko.Keywords.Default = "Ponta";
                PromptResult ro = ed.GetKeywords(ko);
                if (ro.Status != PromptStatus.OK && ro.Status != PromptStatus.None) return null;
                e.EOut = ExtDeTexto(ro.Status == PromptStatus.OK ? ro.StringResult : "Ponta");
            }
            else
            {
                string padrao = (e.Familia == "CURVA" || e.Familia == "JOELHO") ? "Bolsa" : "Flange";
                var ke = new PromptKeywordOptions("\nExtremidades [Flange/Bolsa/Jm/Eletro/Termo] <" + padrao + ">: ", "Flange Bolsa Jm Eletro Termo")
                { AllowNone = true };
                ke.Keywords.Default = padrao;
                PromptResult re = ed.GetKeywords(ke);
                if (re.Status != PromptStatus.OK && re.Status != PromptStatus.None) return null;
                Extremidade x = ExtDeTexto(re.Status == PromptStatus.OK ? re.StringResult : padrao);
                e.EIn = e.EOut = e.EBr = x;
            }

            if (e.Familia == "TE" || e.Familia == "Y" || e.Familia == "CURVA" || e.Familia == "JOELHO")
            {
                var kd = new PromptKeywordOptions("\nDirecao [Cima/Baixo/Esquerda/Direita] <Cima>: ",
                    "Cima Baixo Esquerda Direita") { AllowNone = true };
                kd.Keywords.Default = "Cima";
                PromptResult rd = ed.GetKeywords(kd);
                if (rd.Status != PromptStatus.OK && rd.Status != PromptStatus.None) return null;
                e.Derivacao = rd.Status == PromptStatus.None ? "Cima" : rd.StringResult;
            }

            if (e.Familia == "CURVA" || e.Familia == "JOELHO")
            {
                var pa = new PromptDoubleOptions("\nAngulo [90 / 45 / 22.5 / 11.25] <90>: ")
                { DefaultValue = 90, UseDefaultValue = true, AllowNone = true, AllowNegative = false, AllowZero = false };
                PromptDoubleResult ra = ed.GetDouble(pa);
                if (ra.Status != PromptStatus.OK) return null;
                e.Angulo = Catalogo.AnguloPadraoMaisProximo(ra.Value);
            }

            if (e.Familia == "TE")
            {
                var p3 = new PromptIntegerOptions("\nReducao na derivacao: DN final (0 = nenhuma) <0>: ")
                { DefaultValue = 0, UseDefaultValue = true, AllowNone = true, LowerLimit = 0, UpperLimit = 700 };
                PromptIntegerResult r3 = ed.GetInteger(p3);
                if (r3.Status != PromptStatus.OK) return null;
                if (r3.Value > 0)
                {
                    e.DN3 = Catalogo.DnPadrao(r3.Value);
                    var kb = new PromptKeywordOptions("\nExtremidade da reducao [Flange/Bolsa/Jm] <Flange>: ", "Flange Bolsa Jm")
                    { AllowNone = true };
                    kb.Keywords.Default = "Flange";
                    PromptResult rb = ed.GetKeywords(kb);
                    if (rb.Status != PromptStatus.OK && rb.Status != PromptStatus.None) return null;
                    e.EBrOut = ExtDeTexto(rb.Status == PromptStatus.OK ? rb.StringResult : "Flange");
                }
            }

            if (e.Familia == "RETA")
            {
                var pl = new PromptDoubleOptions("\nComprimento (mm) <300>: ")
                { DefaultValue = 300, UseDefaultValue = true, AllowNone = true, AllowNegative = false, AllowZero = false };
                PromptDoubleResult rl = ed.GetDouble(pl);
                if (rl.Status != PromptStatus.OK) return null;
                e.ComprimentoMm = rl.Value;
            }
            return e;
        }
    }
}
