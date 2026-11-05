using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            "Junta de desmontagem (travada axialmente)",
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
            "RETA", "CAP", "RETA", "CURVA", "CURVA", "FLANGE", "JOELHO", "Y", "JD", "JM", "LUVA", "LUVA", "LUVA", "LUVA_LR", "RETA",
            "RETA", "REDUCAO", "BORBOLETA", "REGISTRO", "TE", "RETENCAO", "VRP", "VENTOSA"
        };
        static readonly string[] PresetsFamilia =
        {
            "BOLSAFLANGE", "", "CARRETEL", "", "SOLDA", "", "", "", "", "", "", "ELETRO", "TERMO", "", "RETA",
            "PONTAFLANGE", "REDUCAO", "", "", "", "", "", ""
        };
        // Texto de ajuda de cada item acima (mostrado sob o desenho).
        static readonly string[] DescFamilia =
        {
            "Peça reta com bolsa numa ponta e flange na outra.",
            "Fecha a extremidade da linha (flange cego).",
            "Peça curta com flange nas duas pontas.",
            "Mudança de direção com raio: 11,25°, 22,5°, 45° ou 90°.",
            "Curva de alumínio com extremidades soldadas.",
            "Apenas o anel do flange, para acoplar a outra peça.",
            "Mudança de direção em canto vivo, sem raio.",
            "Derivação a 45°; o DN da derivação é livre.",
            "Flange-flange com comprimento ajustável, travada por tirantes (mais tirantes quanto maior o DN).",
            "Luva de junta mecânica para emendar tubos de ponta.",
            "Luva para unir dois tubos de ponta.",
            "Luva de PEAD unida por eletrofusão.",
            "Luva de PEAD unida por termofusão.",
            "Luva que muda o DN de um lado para o outro.",
            "Peça reta com extremidades à escolha e comprimento em mm.",
            "Flange na entrada e ponta na saída.",
            "Muda o DN da linha. Marque \"Inverter\" para fazer aumento.",
            "Válvula de fechamento rápido (disco central e alavanca).",
            "Registro de gaveta com castelo e volante.",
            "Derivação a 90° (até o DN da linha), com redução opcional.",
            "Válvula que impede o retorno da água.",
            "Reduz e estabiliza a pressão a jusante.",
            "Expulsa e admite ar; instalada nos pontos altos."
        };
        static readonly string[] NomesExt = { "Bolsa", "Eletrofusão", "Flange", "Junta mecânica", "Ponta", "Soldada", "Termofusão" };
        static readonly string[] NomesDir = { "Baixo", "Cima", "Direita", "Esquerda" };
        static readonly string[] NomesAng = { "11.25", "22.5", "45", "90" };

        // Ultima escolha por item da lista (reaparece na proxima vez que o dialogo abrir).
        class Estado
        {
            public string Dn, Dn2, In, Out, Br, Dn3, BrOut, Der, Ang;
            public bool Inv;
            public decimal L;
        }
        static readonly Dictionary<int, Estado> Memoria = new Dictionary<int, Estado>();
        static int UltimaFamilia = -1;
        static bool EmEdicao;   // dialogo aberto por AGUA_EDITAR (peca ja inserida)

        // Material da rede em desenho: em PEAD, curva/joelho/luva padrao sao de eletrofusao.
        public static string MaterialRede = "";
        readonly int dnAtual;
        readonly ToolTip tt = new ToolTip { AutoPopDelay = 9000, InitialDelay = 350, ReshowDelay = 150 };
        ComboBox cbFam, cbDn, cbDn2, cbIn, cbOut, cbBr, cbDer, cbAng, cbDn3, cbBrOut;
        NumericUpDown nudL;
        CheckBox chkInv;
        Label lblDn, lblDn2, lblBr, lblDer, lblL, lblAng, lblDn3, lblBrOut, lblDesc;
        Tela pnl;
        public PecaEscolha Resultado;

        sealed class Tela : Panel
        {
            public Tela() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = true; }
        }

        PecaDialog(int dn)
        {
            dnAtual = dn;
            Text = (EmEdicao ? "Editar peça - DN" : "Peça - linha DN") + dn;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            const int wl = 130;   // largura dos rotulos da coluna da esquerda

            // Cabecalho: linha em uso (DN e material), sempre por inteiro.
            string mat = string.IsNullOrEmpty(MaterialRede) ? "" : "   ·   Material: " + MaterialRede;
            Controls.Add(new Label
            {
                Text = (EmEdicao ? "Editando a peça no DN" : "Linha atual: DN") + dn + mat,
                Left = 12, Top = 8, Width = 676, Height = 22,
                Font = new System.Drawing.Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });

            // Coluna da esquerda: peca, extremidades, geometria.
            GroupBox gP = Grupo("Peça", 10, 36, 350, 2);
            Linha(gP, "Peça", out cbFam, 0, wl, "Tipo de peça a inserir na linha.");
            cbFam.Items.AddRange(NomesFamilia);
            cbFam.DropDownWidth = 300;
            lblDn = Linha(gP, "DN da peça", out cbDn, 1, wl, "Diâmetro nominal da peça (a linha em uso está no topo da janela).");
            foreach (int d in Catalogo.DNs) cbDn.Items.Add(d.ToString());
            cbDn.SelectedItem = dn.ToString();

            GroupBox gE = Grupo("Extremidades", 10, gP.Bottom + 8, 350, 3);
            Linha(gE, "Entrada", out cbIn, 0, wl, "Extremidade do lado da peça anterior.");
            Linha(gE, "Saída", out cbOut, 1, wl, "Extremidade do lado do próximo trecho.");
            lblBr = Linha(gE, "Derivação", out cbBr, 2, wl, "Extremidade da derivação (tê e Y).");
            foreach (ComboBox c in new[] { cbIn, cbOut, cbBr })
            {
                c.Items.AddRange(NomesExt);
                c.SelectedItem = "Flange";
            }

            GroupBox gG = Grupo("Geometria", 10, gE.Bottom + 8, 350, 3);
            lblAng = Linha(gG, "Ângulo (graus)", out cbAng, 0, wl, "Deflexão da curva ou do joelho.");
            cbAng.Items.AddRange(NomesAng);
            cbAng.SelectedItem = "90";
            chkInv = new CheckBox { Text = "Inverter (aumento)", Left = 10 + wl + 4, Top = 22 + 28 + 2, Width = 190 };
            gG.Controls.Add(chkInv);
            tt.SetToolTip(chkInv, "Redução vira aumento: o DN cresce no sentido do fluxo.");
            lblL = new Label { Text = "Comprimento (mm)", Left = 10, Top = 22 + 56 + 4, Width = wl };
            nudL = new NumericUpDown
            {
                Left = 10 + wl + 4, Top = 22 + 56, Width = gG.Width - (10 + wl + 4) - 10,
                Minimum = 100, Maximum = 6000, Increment = 50, Value = 300
            };
            gG.Controls.Add(lblL);
            gG.Controls.Add(nudL);
            tt.SetToolTip(nudL, "Comprimento da peça reta ou da junta de desmontagem (face a face), em milímetros.");
            tt.SetToolTip(lblL, "Comprimento da peça reta, em milímetros.");

            // Coluna da direita: derivacao/saida, desenho e descricao.
            GroupBox gD = Grupo("Derivação / saída", 370, 36, 320, 4);
            lblDn2 = Linha(gD, "DN derivação / saída", out cbDn2, 0, 140, "DN da derivação (tê, Y) ou da saída (redução, LR).");
            foreach (int d in Catalogo.DNs) cbDn2.Items.Add(d.ToString());
            cbDn2.SelectedItem = dn.ToString();
            lblDer = Linha(gD, "Direção", out cbDer, 1, 140, "Cima/Baixo = acima/abaixo do plano; Direita/Esquerda = lateral em planta.");
            cbDer.Items.AddRange(NomesDir);
            cbDer.SelectedItem = "Cima";
            lblDn3 = Linha(gD, "Redução na derivação", out cbDn3, 2, 140, "DN final de uma redução instalada na derivação do tê.");
            lblBrOut = Linha(gD, "Extremidade da redução", out cbBrOut, 3, 140, "Extremidade de saída dessa redução.");
            cbBrOut.Items.AddRange(new[] { "Bolsa", "Flange", "Junta mecânica" });
            cbBrOut.SelectedItem = "Flange";

            pnl = new Tela { Left = 370, Top = gD.Bottom + 8, Width = 320, Height = 170, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            pnl.Paint += (s, e) =>
            {
                try { Desenhar(e.Graphics, pnl.ClientSize); } catch { }
            };
            Controls.Add(pnl);
            tt.SetToolTip(pnl, "Modelo 3D ilustrativo da peça. Arraste para girar, use a roda para ampliar.");
            LigarMouse();

            lblDesc = new Label { Left = 370, Top = pnl.Bottom + 4, Width = 320, Height = 34, ForeColor = SystemColors.GrayText };
            Controls.Add(lblDesc);

            // barra de botoes abaixo de tudo (Enter = OK, Esc = Cancelar)
            int barra = Math.Max(gG.Bottom, lblDesc.Bottom) + 10;
            Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Left = 10, Top = barra, Width = 680, Height = 2 });
            var ok = new Button { Text = "OK", Left = 490, Top = barra + 10, Width = 95, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Left = 595, Top = barra + 10, Width = 95, Height = 28, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(700, ok.Bottom + 12);

            // Abre na ultima peca usada (primeira vez: Te).
            cbFam.SelectedIndex = UltimaFamilia >= 0 ? UltimaFamilia : Array.IndexOf(ChavesFamilia, "TE");
            AtualizarOpcoesRedDeriv();
            AplicarPadroes();
            Atualizar();
            Restaurar();

            cbFam.SelectedIndexChanged += (s, e) => { AplicarPadroes(); Atualizar(); Restaurar(); };
            cbDn2.SelectedIndexChanged += (s, e) => { AtualizarOpcoesRedDeriv(); pnl.Invalidate(); };
            chkInv.CheckedChanged += (s, e) => Atualizar();
            foreach (ComboBox c in new[] { cbIn, cbOut, cbBr, cbDer, cbAng, cbDn3, cbBrOut })
                c.SelectedIndexChanged += (s, e) => pnl.Invalidate();
        }

        // Abre perto do cursor, sem sair da area de trabalho.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                int x = Cursor.Position.X - Width / 2, y = Cursor.Position.Y - Height / 2;
                Location = new Point(Math.Max(wa.Left, Math.Min(x, wa.Right - Width)),
                                     Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height)));
            }
            catch { }
        }

        GroupBox Grupo(string titulo, int x, int y, int w, int linhas)
        {
            var g = new GroupBox { Text = titulo, Left = x, Top = y, Width = w, Height = 24 + linhas * 28 + 8 };
            Controls.Add(g);
            return g;
        }

        Label Linha(GroupBox g, string texto, out ComboBox cb, int i, int largRotulo, string dica)
        {
            int top = 22 + i * 28;
            int cx = 10 + largRotulo + 4;
            var l = new Label { Text = texto, Left = 10, Top = top + 4, Width = largRotulo, AutoEllipsis = true };
            cb = new ComboBox { Left = cx, Top = top, Width = g.Width - cx - 10, DropDownStyle = ComboBoxStyle.DropDownList };
            g.Controls.Add(l);
            g.Controls.Add(cb);
            tt.SetToolTip(cb, dica);
            tt.SetToolTip(l, dica);
            return l;
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

            if (f == "JD")
            {
                int dnJd;
                if (int.TryParse(cbDn.SelectedItem as string, out dnJd))
                    nudL.Value = Math.Max(nudL.Minimum, Math.Min(nudL.Maximum, (decimal)Catalogo.JdComprimentoMm(dnJd)));
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
            bool y = fam == "Y", te = fam == "TE", red = fam == "REDUCAO", reta = fam == "RETA" || fam == "JD", luva = fam == "LUVA";
            bool jd = fam == "JD";
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
            // a reducao da derivacao e uma peca a parte: ao editar um te ela nao e refeita
            cbDn3.Enabled = te && !EmEdicao;
            lblDn3.Enabled = te && !EmEdicao;
            cbBrOut.Enabled = te && !EmEdicao;
            lblBrOut.Enabled = te && !EmEdicao;

            cbDer.Enabled = te || y || curva;
            lblDer.Enabled = te || y || curva;
            lblDer.Text = (te || y) ? "Direção da derivação" : "Direção da curva";
            cbAng.Enabled = curva;
            lblAng.Enabled = curva;
            chkInv.Enabled = red;
            nudL.Enabled = reta;
            lblL.Enabled = reta;

            cbIn.Enabled = !(luva || jm || lr || fl || solda || jd);
            cbOut.Enabled = !(luva || jm || lr || cap || vent || fl || solda || jd);
            if (lr || (luva && PresetAtual == ""))
            {
                cbIn.SelectedItem = "Bolsa";
                cbOut.SelectedItem = "Bolsa";
            }
            if (jd)
            {
                cbIn.SelectedItem = "Flange";
                cbOut.SelectedItem = "Flange";
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

            lblDesc.Text = DescFamilia[Math.Max(0, cbFam.SelectedIndex)];
            pnl.Invalidate();
        }

        // Reaplica a ultima escolha feita para o item atual da lista (so nos campos habilitados).
        void Restaurar()
        {
            Estado s;
            if (!Memoria.TryGetValue(cbFam.SelectedIndex, out s)) return;
            if (chkInv.Enabled && chkInv.Checked != s.Inv) { chkInv.Checked = s.Inv; Atualizar(); }   // antes do DN de saida
            Sel(cbDn, s.Dn);
            Sel(cbDn2, s.Dn2);
            Sel(cbIn, s.In);
            Sel(cbOut, s.Out);
            Sel(cbBr, s.Br);
            Sel(cbDn3, s.Dn3);
            Sel(cbBrOut, s.BrOut);
            Sel(cbDer, s.Der);
            Sel(cbAng, s.Ang);
            if (nudL.Enabled && s.L >= nudL.Minimum && s.L <= nudL.Maximum) nudL.Value = s.L;
            pnl.Invalidate();
        }

        static void Sel(ComboBox c, string valor)
        {
            if (c.Enabled && valor != null && c.Items.Contains(valor)) c.SelectedItem = valor;
        }

        void Memorizar()
        {
            UltimaFamilia = cbFam.SelectedIndex;
            Memoria[cbFam.SelectedIndex] = new Estado
            {
                Dn = cbDn.SelectedItem as string, Dn2 = cbDn2.SelectedItem as string,
                In = cbIn.SelectedItem as string, Out = cbOut.SelectedItem as string, Br = cbBr.SelectedItem as string,
                Dn3 = cbDn3.SelectedItem as string, BrOut = cbBrOut.SelectedItem as string,
                Der = cbDer.SelectedItem as string, Ang = cbAng.SelectedItem as string,
                Inv = chkInv.Checked, L = nudL.Value
            };
        }

        // ---------------------------------------------------------------- previa 3D

        double yaw = 0.6, pitch = 0.42, zoom = 1.0;   // vista inicial; o usuario gira com o mouse
        bool arrastando;
        Point ultimoPonto;
        Malha cacheMalha;
        string cacheChave;

        static string Txt(ComboBox c) { return c.SelectedItem as string ?? ""; }

        static int Num(ComboBox c, int padrao)
        {
            int v;
            return int.TryParse(c.SelectedItem as string, out v) ? v : padrao;
        }

        ParamPreview ParamAtual()
        {
            double ang;
            if (!double.TryParse(Txt(cbAng), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out ang)) ang = 90;
            string fam = FamiliaAtual;
            return new ParamPreview
            {
                Familia = fam,
                DN = Num(cbDn, dnAtual),
                DN2 = Num(cbDn2, dnAtual),
                DN3 = (fam == "TE" && !EmEdicao) ? Num(cbDn3, 0) : 0,
                EIn = Txt(cbIn), EOut = Txt(cbOut), EBr = Txt(cbBr), EBrOut = Txt(cbBrOut),
                Direcao = Txt(cbDer),
                Angulo = ang,
                Comprimento = (double)nudL.Value
            };
        }

        void Desenhar(Graphics g, Size sz)
        {
            if (sz.Width < 10 || sz.Height < 10) return;
            ParamPreview p = ParamAtual();
            string chave = p.Chave();
            if (cacheMalha == null || chave != cacheChave)
            {
                cacheMalha = Preview3D.Construir(p);
                cacheChave = chave;
            }
            using (Bitmap bmp = cacheMalha.Renderizar(sz.Width * 2, sz.Height * 2, yaw, pitch, zoom))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, sz.Width, sz.Height);
            }
            using (var f = new System.Drawing.Font(Font.FontFamily, 7.5f))
                g.DrawString("Arraste para girar · roda: zoom · duplo clique: restaurar", f, Brushes.Gray, 5, sz.Height - f.Height - 3);
        }

        void LigarMouse()
        {
            pnl.Cursor = Cursors.SizeAll;
            pnl.MouseEnter += (s, e) => pnl.Focus();   // a roda do mouse vai para o desenho
            pnl.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                arrastando = true;
                ultimoPonto = e.Location;
            };
            pnl.MouseUp += (s, e) => arrastando = false;
            pnl.MouseMove += (s, e) =>
            {
                if (!arrastando) return;
                yaw += (e.X - ultimoPonto.X) * 0.012;
                pitch = Math.Max(-1.5, Math.Min(1.5, pitch + (e.Y - ultimoPonto.Y) * 0.012));
                ultimoPonto = e.Location;
                pnl.Invalidate();
            };
            pnl.MouseWheel += (s, e) =>
            {
                zoom = Math.Max(0.4, Math.Min(4.0, zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12)));
                pnl.Invalidate();
            };
            pnl.DoubleClick += (s, e) => { yaw = 0.6; pitch = 0.42; zoom = 1.0; pnl.Invalidate(); };
        }

        // ---------------------------------------------------------------- resultado

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
                if (red != null && red != "Nenhuma" && !EmEdicao)
                {
                    r.DN3 = int.Parse(red);
                    r.EBrOut = Ext(cbBrOut.SelectedItem);
                }
            }
            if (MaterialRede == "PEAD" && r.Familia == "LUVA" && PresetAtual == "")
                r.EIn = r.EOut = Extremidade.Eletrofusao;
            if (!EmEdicao) Memorizar();
            Resultado = r;
        }

        // ---------------------------------------------------------------- edicao de peca existente

        static string NomeExt(Extremidade e)
        {
            switch (e)
            {
                case Extremidade.Bolsa: return "Bolsa";
                case Extremidade.Ponta: return "Ponta";
                case Extremidade.JuntaMecanica: return "Junta mecânica";
                case Extremidade.Eletrofusao: return "Eletrofusão";
                case Extremidade.Termofusao: return "Termofusão";
                case Extremidade.Solda: return "Soldada";
                case Extremidade.Flange: return "Flange";
                default: return null;
            }
        }

        // Item da lista que representa a peca ja inserida.
        static int IndiceDe(PecaEscolha e)
        {
            switch (e.Familia)
            {
                case "RETA": return 14;
                case "CURVA": return e.EIn == Extremidade.Solda ? 4 : 3;
                case "LUVA": return e.EIn == Extremidade.Eletrofusao ? 11 : (e.EIn == Extremidade.Termofusao ? 12 : 10);
            }
            int i = Array.IndexOf(ChavesFamilia, e.Familia);
            return i >= 0 ? i : Array.IndexOf(ChavesFamilia, "TE");
        }

        // Abre o dialogo preenchido com a peca existente (AGUA_EDITAR). Nao altera a memoria das escolhas.
        public static PecaEscolha PedirEdicao(Editor ed, int dnLinha, PecaEscolha atual)
        {
            int idx = IndiceDe(atual);
            Estado antigo;
            bool tinha = Memoria.TryGetValue(idx, out antigo);
            int ultima = UltimaFamilia;
            Memoria[idx] = new Estado
            {
                Dn = atual.DN.ToString(), Dn2 = atual.DN2.ToString(),
                In = NomeExt(atual.EIn), Out = NomeExt(atual.EOut), Br = NomeExt(atual.EBr),
                Der = atual.Derivacao,
                Ang = atual.Angulo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Inv = atual.Familia == "REDUCAO" && atual.DN2 > atual.DN,
                L = (decimal)atual.ComprimentoMm
            };
            UltimaFamilia = idx;
            EmEdicao = true;
            try { return Pedir(ed, dnLinha); }
            finally
            {
                EmEdicao = false;
                UltimaFamilia = ultima;
                if (tinha) Memoria[idx] = antigo; else Memoria.Remove(idx);
            }
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
                "\nPeca [Aumento/BolsaFlange/Borboleta/Cap/Carretel/Curva/CurvaSolda/Flange/Jd/Jm/Joelho/Y/LR/Luva/LuvaEletro/LuvaTermo/PecaReta/PontaFlange/Reducao/Registro/Retencao/Te/Ventosa/Vrp] <Te>: ",
                "Aumento BolsaFlange Borboleta Cap Carretel Curva CurvaSolda Flange Jd Jm Joelho Y LR Luva LuvaEletro LuvaTermo PecaReta PontaFlange Reducao Registro Retencao Te Ventosa Vrp")
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
                case "Jd":
                    e.Familia = "JD"; e.EIn = e.EOut = Extremidade.Flange; retaFixa = true;
                    break;
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

            if (e.Familia == "RETA" || e.Familia == "JD")
            {
                double comp0 = e.Familia == "JD" ? Catalogo.JdComprimentoMm(e.DN) : 300;
                var pl = new PromptDoubleOptions("\nComprimento (mm) <" + comp0 + ">: ")
                { DefaultValue = comp0, UseDefaultValue = true, AllowNone = true, AllowNegative = false, AllowZero = false };
                PromptDoubleResult rl = ed.GetDouble(pl);
                if (rl.Status != PromptStatus.OK) return null;
                e.ComprimentoMm = rl.Value;
            }
            return e;
        }
    }
}
