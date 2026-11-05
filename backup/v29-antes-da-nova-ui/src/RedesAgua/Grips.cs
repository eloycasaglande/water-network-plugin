using System;
using System.Collections.Generic;
#if ACAD
using ZwGfx = Autodesk.AutoCAD.GraphicsInterface;
#else
using ZwGfx = ZwSoft.ZwCAD.GraphicsInterface;
#endif

[assembly: ExtensionApplication(typeof(RedesAgua.Inicializacao))]

namespace RedesAgua
{
    // Carregado junto com a DLL (NETLOAD): liga o grip "+" das pontas livres da rede.
    public class Inicializacao : IExtensionApplication
    {
        public void Initialize() { }   // grip "+" desativado por padrao (AGUA_GRIPS liga, experimental)
        public void Terminate() { Comandos.LigarGrips(false); }
    }

    // Estado entre o callback do grip (que nao pode abrir prompts) e o comando que o continua.
    static class GripLog
    {
        public static readonly string Arquivo =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RedesAgua_grips.log");

        public static void Escrever(string msg)
        {
            try { System.IO.File.AppendAllText(Arquivo, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n"); }
            catch (System.Exception) { }
        }
    }

    static class GripEstado
    {
        public static ObjectId Id;
        public static string Lado;
        public static Point3d? Destino;
        // ultimo resultado de GetGripPoints (legado): durante o arrasto o ZWCAD passa uma copia
        // sem ObjectId/Database, entao PontasLivres nao pode ser recalculado em MoveGripPointsAt.
        public static int Nativos;
        public static List<PontaGrip> Livres = new List<PontaGrip>();
    }

    // Grip "+" na extremidade livre de um tubo/peca.
    class PontaGrip : GripData
    {
        public readonly ObjectId Alvo;
        public readonly string Lado;   // "Saida" ou "Derivacao"

        public PontaGrip(Point3d p, ObjectId alvo, string lado)
        {
            GripPoint = p;
            Alvo = alvo;
            Lado = lado;
        }

        public override string GetTooltip()
        {
            return "Clique: adicionar peca | Arraste: puxar tubo";
        }

        public override bool ViewportDraw(ZwGfx.ViewportDraw vd, ObjectId entityId, DrawType type,
                                          Point3d? imageGripPoint, int gripSizeInPixels)
        {
            Point3d p = imageGripPoint.HasValue ? imageGripPoint.Value : GripPoint;
            Point2d ppu = vd.Viewport.GetNumPixelsInUnitSquare(p);
            double px = Math.Max(1.0, (gripSizeInPixels > 0 ? gripSizeInPixels : 5) * 1.6);
            double hx = px / Math.Max(ppu.X, 1e-9);
            double hy = px / Math.Max(ppu.Y, 1e-9);
            Matrix3d m = vd.Viewport.EyeToWorldTransform;
            Vector3d dx = new Vector3d(1, 0, 0).TransformBy(m).GetNormal() * hx;
            Vector3d dy = new Vector3d(0, 1, 0).TransformBy(m).GetNormal() * hy;
            vd.Geometry.WorldLine(p - dx, p + dx);
            vd.Geometry.WorldLine(p - dy, p + dy);
            return true;
        }

        public override bool WorldDraw(ZwGfx.WorldDraw wd, ObjectId entityId, DrawType type,
                                       Point3d? imageGripPoint, double dGripSize)
        {
            Point3d p = imageGripPoint.HasValue ? imageGripPoint.Value : GripPoint;
            double h = Math.Max(dGripSize, 1e-6) * 1.6;
            Vector3d dx = Vector3d.XAxis * h, dy = Vector3d.YAxis * h;
            wd.Geometry.WorldLine(p - dx, p + dx);
            wd.Geometry.WorldLine(p - dy, p + dy);
            return true;
        }

        // Clique (solta sem mover) = adicionar peca; arrasto segue e termina em MoveGripPointsAt.
        public override ReturnValue OnHotGrip(ObjectId entityId, Context context)
        {
            GripLog.Escrever("OnHotGrip lado=" + Lado);
            DateTime t0 = DateTime.UtcNow;
            System.Drawing.Point p0 = System.Windows.Forms.Cursor.Position;
            bool soltou = false;
            while ((DateTime.UtcNow - t0).TotalMilliseconds < 220)
            {
                if ((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) == 0)
                {
                    soltou = true;
                    break;
                }
                System.Drawing.Point p = System.Windows.Forms.Cursor.Position;
                if (Math.Abs(p.X - p0.X) + Math.Abs(p.Y - p0.Y) > 4) break;
                System.Threading.Thread.Sleep(10);
            }
            if (!soltou) return ReturnValue.Ok;   // arrasto: o ponto final chega em MoveGripPointsAt

            GripEstado.Id = Alvo;
            GripEstado.Lado = Lado;
            GripEstado.Destino = null;
            Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_AGUA_PECA_PONTA ", true, false, false);
            return ReturnValue.Failure;
        }
    }

    class PontaGripOverrule : GripOverrule
    {
        public override void GetGripPoints(Entity entity, GripDataCollection grips, double curViewUnitSize,
                                           int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
            int antes = grips.Count;
            try
            {
                var s = entity as Solid3d;
                if (s == null || s.ObjectId.IsNull) return;
                foreach (PontaGrip g in Comandos.PontasLivres(s))
                    grips.Add(g);
                GripLog.Escrever("GetGripPoints(GripData) nativos=" + antes + " total=" + grips.Count);
            }
            catch (System.Exception ex) { GripLog.Escrever("GetGripPoints erro: " + ex.Message); }
        }

        // ---- caminho legado (indices): usado pelo ZWCAD quando a versao com GripData nao e chamada ----
        public override void GetGripPoints(Entity entity, Point3dCollection gripPoints,
                                           IntegerCollection snapModes, IntegerCollection geometryIds)
        {
            base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
            int antes = gripPoints.Count;
            try
            {
                var s = entity as Solid3d;
                if (s == null || s.ObjectId.IsNull) return;
                GripEstado.Nativos = antes;
                GripEstado.Livres = new List<PontaGrip>();
                foreach (PontaGrip g in Comandos.PontasLivres(s))
                {
                    GripEstado.Livres.Add(g);
                    gripPoints.Add(g.GripPoint);
                    snapModes.Add(0);
                    geometryIds.Add(0);
                }
                GripLog.Escrever("GetGripPoints(legado) nativos=" + antes + " total=" + gripPoints.Count);
            }
            catch (System.Exception ex) { GripLog.Escrever("GetGripPoints(legado) erro: " + ex.Message); }
        }

        public override void MoveGripPointsAt(Entity entity, IntegerCollection indices, Vector3d offset)
        {
            var livres = GripEstado.Livres;
            int nativos = GripEstado.Nativos;
            foreach (int i in indices)
            {
                int k = i - nativos;
                if (k < 0 || k >= livres.Count) continue;
                // arrasto em curso (entidade temporaria): so nao move o solido
                if (entity == null || entity.ObjectId.IsNull) return;
                GripLog.Escrever("MoveGripPointsAt(legado) ponta=" + livres[k].Lado + " offset=" + offset.Length.ToString("0.###"));
                GripEstado.Id = livres[k].Alvo;
                GripEstado.Lado = livres[k].Lado;
                if (offset.Length < 1e-9)
                {
                    GripEstado.Destino = null;
                    Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_AGUA_PECA_PONTA ", true, false, false);
                }
                else
                {
                    GripEstado.Destino = livres[k].GripPoint + offset;
                    Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_AGUA_PUXAR ", true, false, false);
                }
                return;   // grip "+": nao move o solido
            }
            base.MoveGripPointsAt(entity, indices, offset);
        }

        // Arrasto de um "+": nao altera o solido; guarda o destino e o comando roda ao fim da operacao.
        public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset,
                                              MoveGripPointsFlags bitFlags)
        {
            PontaGrip pg = null;
            foreach (GripData g in grips)
            {
                pg = g as PontaGrip;
                if (pg != null) break;
            }
            if (pg == null)
            {
                base.MoveGripPointsAt(entity, grips, offset, bitFlags);
                return;
            }
            GripLog.Escrever("MoveGripPointsAt(GripData) offset=" + offset.Length.ToString("0.###"));
            if (offset.Length < 1e-9) return;
            GripEstado.Id = pg.Alvo;
            GripEstado.Lado = pg.Lado;
            GripEstado.Destino = pg.GripPoint + offset;
        }

        public override void OnGripStatusChanged(Entity entity, GripStatus status)
        {
            base.OnGripStatusChanged(entity, status);
            GripLog.Escrever("OnGripStatusChanged " + status);
            if (status == GripStatus.GripsDone && GripEstado.Destino.HasValue)
                Application.DocumentManager.MdiActiveDocument.SendStringToExecute("_AGUA_PUXAR ", true, false, false);
        }
    }

    public partial class Comandos
    {
        static PontaGripOverrule overruleGrips;

        internal static void LigarGrips(bool ligar)
        {
            try
            {
                if (ligar && overruleGrips == null)
                {
                    overruleGrips = new PontaGripOverrule();
                    overruleGrips.SetNoFilter();
                    Overrule.AddOverrule(RXObject.GetClass(typeof(Solid3d)), overruleGrips, false);
                    Overrule.Overruling = true;
                }
                else if (!ligar && overruleGrips != null)
                {
                    Overrule.RemoveOverrule(RXObject.GetClass(typeof(Solid3d)), overruleGrips);
                    overruleGrips.Dispose();
                    overruleGrips = null;
                }
            }
            catch (System.Exception ex) { GripLog.Escrever("LigarGrips erro: " + ex.Message); }
            GripLog.Escrever("LigarGrips(" + ligar + ") ativo=" + (overruleGrips != null));
        }

        [CommandMethod("AGUA_GRIPS")]
        public void GripsLigaDesliga()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            bool ligar = overruleGrips == null;
            LigarGrips(ligar);
            ed.WriteMessage("\n(Diagnostico em: " + GripLog.Arquivo + ")");
            ed.WriteMessage(ligar ? "\nGrip \"+\" das pontas: LIGADO." : "\nGrip \"+\" das pontas: DESLIGADO.");
        }

        // Pontas livres (saida e derivacao ainda sem ligacao), na posicao real do solido.
        internal static List<PontaGrip> PontasLivres(Solid3d s)
        {
            var res = new List<PontaGrip>();
            Dictionary<string, string> c = LerCampos(s);
            string run, seqS;
            int seq;
            if (!c.TryGetValue("run", out run) || !c.TryGetValue("seq", out seqS) || !int.TryParse(seqS, out seq))
                return res;

            bool temSaida = c.ContainsKey("sp") && !(c.ContainsKey("se") && c["se"] == "Fechada");
            bool temDeriv = c.ContainsKey("bp") && !(c.ContainsKey("be") && c["be"] == "Fechada");
            if (!temSaida && !temDeriv) return res;

            // quem ja esta ligado: proximo da mesma sequencia ou trecho que aponta (prev) para esta porta
            bool saidaLigada = false, derivLigada = false;
            string chaveS = run + ":" + seq + ":S", chaveB = run + ":" + seq + ":B";
            using (Transaction tr = s.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(s.OwnerId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || id == s.ObjectId) continue;
                    var o = tr.GetObject(id, OpenMode.ForRead) as Solid3d;
                    if (o == null) continue;
                    Dictionary<string, string> d = LerCampos(o);
                    string r2;
                    if (!d.TryGetValue("run", out r2)) continue;
                    string prev;
                    if (d.TryGetValue("prev", out prev))
                    {
                        if (prev == chaveS) saidaLigada = true;
                        else if (prev == chaveB) derivLigada = true;
                    }
                    string sq2;
                    int seq2;
                    if (r2 == run && d.TryGetValue("seq", out sq2) && int.TryParse(sq2, out seq2) && seq2 == seq + 1)
                        saidaLigada = true;
                }
            }

            Vector3d desvio = DesvioMovimento(s, c);
            if (temSaida && !saidaLigada)
                res.Add(new PontaGrip(LerPonto(c["sp"]) + desvio, s.ObjectId, "Saida"));
            if (temDeriv && !derivLigada)
                res.Add(new PontaGrip(LerPonto(c["bp"]) + desvio, s.ObjectId, "Derivacao"));
            return res;
        }

        // Arrasto no "+": puxa o tubo ate o ponto solto (reto ou com curva) e segue pedindo pontos.
        [CommandMethod("AGUA_PUXAR")]
        public void Puxar()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            if (GripEstado.Id.IsNull || !GripEstado.Destino.HasValue) return;
            ObjectId id = GripEstado.Id;
            string lado = GripEstado.Lado;
            Point3d destino = GripEstado.Destino.Value;
            GripEstado.Destino = null;

            Trecho t = PrepararTrecho(doc, ed, id, lado);
            if (t == null) return;
            ComandoPonto(doc, ed, t, destino);
            Executar(doc, ed, t);
        }

        // Clique no "+": abre a janela de peca na ponta e segue com a rede.
        [CommandMethod("AGUA_PECA_PONTA")]
        public void PecaNaPonta()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            if (GripEstado.Id.IsNull) return;

            Trecho t = PrepararTrecho(doc, ed, GripEstado.Id, GripEstado.Lado);
            if (t == null) return;
            ComandoPeca(doc, ed, t);
            Executar(doc, ed, t);
        }
    }
}
