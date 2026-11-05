using System;

[assembly: CommandClass(typeof(RedesAgua.ImprimirComandos))]

namespace RedesAgua
{
    public class ImprimirComandos
    {
        // Imprime so a regiao escolhida por janela: pede os dois cantos, escolhe retrato ou
        // paisagem conforme a proporcao da janela, ajusta a escala para caber na folha,
        // centraliza e abre a caixa de plotagem ja preenchida (impressora/folha/estilo ficam
        // por conta do usuario).
        [CommandMethod("AGUA_IMPRIMIR")]
        public void Imprimir()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            PromptPointResult p1 = ed.GetPoint("\nPrimeiro canto da area a imprimir: ");
            if (p1.Status != PromptStatus.OK) return;
            var opCanto = new PromptCornerOptions("\nCanto oposto: ", p1.Value) { UseDashedLine = true };
            PromptPointResult p2 = ed.GetCorner(opCanto);
            if (p2.Status != PromptStatus.OK) return;

            // Pontos digitados estao no UCS; a janela de plotagem usa coordenadas da vista (DCS).
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d a = DeWcsParaDcs(ed, p1.Value.TransformBy(ucs));
            Point3d b = DeWcsParaDcs(ed, p2.Value.TransformBy(ucs));
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X);
            double y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            if (x1 - x0 < 1e-9 || y1 - y0 < 1e-9)
            {
                ed.WriteMessage("\nJanela invalida (largura ou altura zero).");
                return;
            }
            bool paisagem = (x1 - x0) >= (y1 - y0);

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    LayoutManager lm = LayoutManager.Current;
                    var layout = (Layout)tr.GetObject(lm.GetLayoutId(lm.CurrentLayout), OpenMode.ForWrite);

                    var ps = new PlotSettings(layout.ModelType);
                    ps.CopyFrom(layout);
                    PlotSettingsValidator v = PlotSettingsValidator.Current;
                    v.RefreshLists(ps);

                    v.SetPlotType(ps, PlotType.Window);
                    v.SetPlotWindowArea(ps, new Extents2d(x0, y0, x1, y1));

                    // Rotacao 0 mantem a folha como o driver a define; se ela for mais alta que larga
                    // (retrato), paisagem = 90 graus, e vice-versa.
                    v.SetPlotRotation(ps, PlotRotation.Degrees000);
                    bool folhaRetrato = ps.PlotPaperSize.X <= ps.PlotPaperSize.Y;
                    bool giro = paisagem == folhaRetrato;
                    v.SetPlotRotation(ps, giro ? PlotRotation.Degrees090 : PlotRotation.Degrees000);

                    v.SetUseStandardScale(ps, true);
                    v.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                    v.SetPlotCentered(ps, true);

                    layout.CopyFrom(ps);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nNao foi possivel preparar a plotagem: {0}", ex.Message);
                return;
            }

            ed.WriteMessage("\nJanela definida, orientacao {0}, ajustada a folha. Abrindo a plotagem...",
                paisagem ? "paisagem" : "retrato");
            doc.SendStringToExecute("_PLOT ", true, false, false);
        }

        static Point3d DeWcsParaDcs(Editor ed, Point3d wcs)
        {
            ViewTableRecord vw = ed.GetCurrentView();
            Matrix3d dcsParaWcs =
                Matrix3d.Rotation(-vw.ViewTwist, vw.ViewDirection, vw.Target) *
                Matrix3d.Displacement(vw.Target - Point3d.Origin) *
                Matrix3d.PlaneToWorld(vw.ViewDirection);
            return wcs.TransformBy(dcsParaWcs.Inverse());
        }
    }
}
