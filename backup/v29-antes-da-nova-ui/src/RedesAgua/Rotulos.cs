using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

[assembly: CommandClass(typeof(RedesAgua.RotuloComandos))]

namespace RedesAgua
{
    // Dados do rotulo mostrados/editados no dialogo.
    class RotuloDados
    {
        public string Texto = "";
        public bool Auto = true;
        public double AlturaMm = 70;   // usada quando Auto = false
    }

    public class RotuloComandos
    {
        const string AppRot = "REDESAGUA_ROT";
        const double FatorAuto = 0.6;      // altura = FatorAuto x DE da peca
        const double AlturaMinima = 0.04;  // m

        // Cria ou edita o rotulo (nome) de uma peca. Clique numa peca para criar;
        // clique num rotulo existente para editar texto e tamanho.
        [CommandMethod("AGUA_ROTULO")]
        public void Rotulo()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            var po = new PromptEntityOptions("\nSelecione a peca (ou um rotulo existente para editar): ");
            po.SetRejectMessage("\nSelecione uma peca da rede ou um rotulo.");
            po.AddAllowedClass(typeof(Solid3d), true);
            po.AddAllowedClass(typeof(MText), true);
            PromptEntityResult pr = ed.GetEntity(po);
            if (pr.Status != PromptStatus.OK) return;

            ObjectId pecaId = ObjectId.Null;
            ObjectId rotuloId = ObjectId.Null;
            ObjectId linhaId = ObjectId.Null;
            var dados = new RotuloDados();
            Reg reg = null;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(pr.ObjectId, OpenMode.ForRead);
                var campos = LerCampos(ent, AppRot);

                if (ent is MText && campos.Count > 0)
                {
                    // editar rotulo existente
                    var mt = (MText)ent;
                    rotuloId = mt.ObjectId;
                    dados.Texto = mt.Contents;
                    dados.Auto = !campos.ContainsKey("auto") || campos["auto"] == "1";
                    double h;
                    if (campos.ContainsKey("h") && double.TryParse(campos["h"], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out h))
                        dados.AlturaMm = h * 1000.0;
                    if (campos.ContainsKey("peca")) pecaId = IdDeHandle(doc.Database, campos["peca"]);
                    if (campos.ContainsKey("linha")) linhaId = IdDeHandle(doc.Database, campos["linha"]);
                    if (!pecaId.IsNull && !pecaId.IsErased)
                        reg = QuantitativoComandos.Ler((Entity)tr.GetObject(pecaId, OpenMode.ForRead));
                }
                else if (ent is Solid3d)
                {
                    reg = QuantitativoComandos.Ler(ent);
                    if (reg == null)
                    {
                        ed.WriteMessage("\nEste solido nao e uma peca da rede.");
                        return;
                    }
                    pecaId = ent.ObjectId;
                    dados.Texto = QuantitativoComandos.Nome(reg);
                }
                else
                {
                    ed.WriteMessage("\nSelecione uma peca da rede ou um rotulo criado por este comando.");
                    return;
                }
                tr.Commit();
            }

            if (!RotuloDialog.Pedir(ed, dados)) return;

            double altura = dados.Auto
                ? Math.Max(FatorAuto * (reg != null ? Catalogo.DiametroExternoMm(reg.DN) * 0.001 : 0.1), AlturaMinima)
                : dados.AlturaMm * 0.001;

            Vector3d vd = ed.GetCurrentView().ViewDirection;
            if (vd.Length < 1e-9) vd = Vector3d.ZAxis;
            vd = vd.GetNormal();

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                RegistrarApp(tr, doc.Database, AppRot);

                if (pecaId.IsNull || pecaId.IsErased)
                {
                    // peca nao existe mais: apenas atualiza o texto no lugar
                    if (!rotuloId.IsNull)
                    {
                        var mt = (MText)tr.GetObject(rotuloId, OpenMode.ForWrite);
                        mt.Contents = dados.Texto;
                        mt.TextHeight = altura;
                        mt.XData = XDataRotulo(rotuloId, null, dados, altura, null);
                    }
                    tr.Commit();
                    return;
                }

                // recria o rotulo (e a linha de chamada) a partir da peca
                foreach (ObjectId id in new[] { rotuloId, linhaId })
                {
                    if (id.IsNull || id.IsErased) continue;
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                }

                var peca = (Entity)tr.GetObject(pecaId, OpenMode.ForRead);
                var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

                Extents3d ext = peca.GeometricExtents;
                Point3d centro = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) / 2);
                double diag = ext.MinPoint.DistanceTo(ext.MaxPoint);

                // eixos do plano da vista: right = direita da tela, up = cima da tela
                Vector3d up0 = Vector3d.ZAxis - vd * Vector3d.ZAxis.DotProduct(vd);
                if (up0.Length < 1e-6) up0 = Vector3d.YAxis;
                up0 = up0.GetNormal();
                Vector3d right = up0.CrossProduct(vd).GetNormal();
                Vector3d up = vd.CrossProduct(right).GetNormal();

                Point3d local = centro + up * (diag * 0.35 + altura * 1.5) + right * (diag * 0.3);

                var linha = new Line(centro, local);
                linha.ColorIndex = 7;
                espaco.AppendEntity(linha);
                tr.AddNewlyCreatedDBObject(linha, true);

                var mtx = new MText
                {
                    Contents = dados.Texto,
                    TextHeight = altura,
                    Location = local,
                    Normal = vd,
                    Direction = right,
                    Attachment = AttachmentPoint.BottomLeft,
                    ColorIndex = 7
                };
                espaco.AppendEntity(mtx);
                tr.AddNewlyCreatedDBObject(mtx, true);

                mtx.XData = XDataRotulo(mtx.ObjectId, pecaId, dados, altura, linha.ObjectId);
                linha.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppRot),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "linha-de=" + mtx.Handle.ToString()));
                tr.Commit();
            }
            ed.WriteMessage("\nRotulo \"{0}\" criado (altura {1:F3} m).", dados.Texto, altura);
        }

        // ---------- utilitarios ----------

        static ResultBuffer XDataRotulo(ObjectId mtextId, ObjectId? pecaId, RotuloDados d, double altura, ObjectId? linhaId)
        {
            var inv = CultureInfo.InvariantCulture;
            var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppRot));
            Action<string> add = s => rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, s));
            add("auto=" + (d.Auto ? "1" : "0"));
            add("h=" + altura.ToString("R", inv));
            if (pecaId.HasValue && !pecaId.Value.IsNull) add("peca=" + HandleDe(pecaId.Value));
            if (linhaId.HasValue && !linhaId.Value.IsNull) add("linha=" + HandleDe(linhaId.Value));
            return rb;
        }

        static string HandleDe(ObjectId id)
        {
            return id.Handle.ToString();
        }

        static ObjectId IdDeHandle(Database db, string handleHex)
        {
            try
            {
                long h = Convert.ToInt64(handleHex, 16);
                ObjectId id;
                return db.TryGetObjectId(new Handle(h), out id) ? id : ObjectId.Null;
            }
            catch (System.Exception)
            {
                return ObjectId.Null;
            }
        }

        static Dictionary<string, string> LerCampos(Entity ent, string app)
        {
            var d = new Dictionary<string, string>();
            ResultBuffer rb = ent.GetXDataForApplication(app);
            if (rb == null) return d;
            using (rb)
            {
                foreach (TypedValue v in rb.AsArray())
                {
                    string s = Convert.ToString(v.Value);
                    int k = s.IndexOf('=');
                    if (k > 0) d[s.Substring(0, k)] = s.Substring(k + 1);
                }
            }
            return d;
        }

        static void RegistrarApp(Transaction tr, Database db, string nome)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(nome)) return;
            rat.UpgradeOpen();
            var ra = new RegAppTableRecord { Name = nome };
            rat.Add(ra);
            tr.AddNewlyCreatedDBObject(ra, true);
        }
    }

    class RotuloDialog : Form
    {
        readonly TextBox txt;
        readonly RadioButton rbAuto, rbManual;
        readonly NumericUpDown nudAltura;

        RotuloDialog(RotuloDados d)
        {
            Text = "Rótulo da peça";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(400, 190);

            Controls.Add(new Label { Text = "Texto", Left = 14, Top = 16, Width = 80 });
            txt = new TextBox { Left = 100, Top = 12, Width = 285, Text = d.Texto };
            Controls.Add(txt);

            Controls.Add(new Label { Text = "Tamanho", Left = 14, Top = 54, Width = 80 });
            rbAuto = new RadioButton
            {
                Text = "Automático (proporcional ao DN)", Left = 100, Top = 50, Width = 285, Checked = d.Auto
            };
            rbManual = new RadioButton { Text = "Altura (mm):", Left = 100, Top = 78, Width = 110, Checked = !d.Auto };
            nudAltura = new NumericUpDown
            {
                Left = 215, Top = 76, Width = 100, Minimum = 5, Maximum = 5000, Increment = 10,
                Value = (decimal)Math.Min(5000, Math.Max(5, d.AlturaMm))
            };
            Controls.Add(rbAuto);
            Controls.Add(rbManual);
            Controls.Add(nudAltura);

            var ok = new Button { Text = "OK", Left = 215, Top = 140, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Left = 305, Top = 140, Width = 80, DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static bool Pedir(Editor ed, RotuloDados d)
        {
            try
            {
                using (var f = new RotuloDialog(d))
                {
                    DialogResult r = CadApp.ShowModalDialog(f);
                    if (r != DialogResult.OK) return false;
                    d.Texto = f.txt.Text;
                    d.Auto = f.rbAuto.Checked;
                    d.AlturaMm = (double)f.nudAltura.Value;
                    return !string.IsNullOrWhiteSpace(d.Texto);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n(Dialogo indisponivel: {0}. Usando linha de comando.)", ex.Message);
                var ps = new PromptStringOptions("\nTexto do rotulo <" + d.Texto + ">: ")
                { AllowSpaces = true, DefaultValue = d.Texto, UseDefaultValue = true };
                PromptResult rs = ed.GetString(ps);
                if (rs.Status != PromptStatus.OK) return false;
                d.Texto = rs.StringResult;

                var pa = new PromptDoubleOptions("\nAltura do texto em mm (0 = automatico) <0>: ")
                { DefaultValue = 0, UseDefaultValue = true, AllowNegative = false, AllowNone = true };
                PromptDoubleResult ra = ed.GetDouble(pa);
                if (ra.Status != PromptStatus.OK) return false;
                d.Auto = ra.Value <= 0;
                if (!d.Auto) d.AlturaMm = ra.Value;
                return !string.IsNullOrWhiteSpace(d.Texto);
            }
        }
    }
}
