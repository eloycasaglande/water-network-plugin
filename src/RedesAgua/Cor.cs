using System;
using System.Collections.Generic;

namespace RedesAgua
{
    public partial class Comandos
    {
        // Muda a cor de tubos e pecas ja desenhados. Aceita selecao previa (PICKFIRST).
        // Opcao "Trecho": pinta todos os elementos dos trechos (tubulacoes) dos objetos selecionados.
        [CommandMethod("AGUA_COR", CommandFlags.UsePickSet)]
        public void Cor()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            ObjectId[] ids = null;
            PromptSelectionResult impl = ed.SelectImplied();
            bool trecho = false;
            if (impl.Status == PromptStatus.OK && impl.Value.Count > 0) ids = impl.Value.GetObjectIds();
            else
            {
                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelecione tubos/pecas para mudar a cor [Trecho = pinta o trecho inteiro]: "
                };
                pso.Keywords.Add("Trecho");
                pso.KeywordInput += (s, a) => { if (a.Input == "Trecho") trecho = true; };
                PromptSelectionResult ps = ed.GetSelection(pso,
                    new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") }));
                if (ps.Status != PromptStatus.OK) return;
                ids = ps.Value.GetObjectIds();
            }

            var kc = new PromptKeywordOptions(
                "\nCor [Vermelho/Amarelo/Verde/Ciano/Azul/Magenta/Branco/Cinza/Laranja/Numero/PorCamada] <Azul>: ",
                "Vermelho Amarelo Verde Ciano Azul Magenta Branco Cinza Laranja Numero PorCamada")
            { AllowNone = true };
            kc.Keywords.Default = "Azul";
            PromptResult rc = ed.GetKeywords(kc);
            if (rc.Status != PromptStatus.OK && rc.Status != PromptStatus.None) return;
            string nome = rc.Status == PromptStatus.None ? "Azul" : rc.StringResult;

            short aci;
            switch (nome)
            {
                case "Vermelho": aci = 1; break;
                case "Amarelo": aci = 2; break;
                case "Verde": aci = 3; break;
                case "Ciano": aci = 4; break;
                case "Azul": aci = 5; break;
                case "Magenta": aci = 6; break;
                case "Branco": aci = 7; break;
                case "Cinza": aci = 8; break;
                case "Laranja": aci = 30; break;
                case "PorCamada": aci = 256; break;
                default:
                    var pi = new PromptIntegerOptions("\nIndice de cor (1-255): ")
                    { LowerLimit = 1, UpperLimit = 255, DefaultValue = 5, UseDefaultValue = true };
                    PromptIntegerResult ri = ed.GetInteger(pi);
                    if (ri.Status != PromptStatus.OK) return;
                    aci = (short)ri.Value;
                    break;
            }

            if (!trecho)
            {
                var ka = new PromptKeywordOptions("\nAplicar em [Selecionados/Trecho inteiro] <Selecionados>: ",
                    "Selecionados Trecho")
                { AllowNone = true };
                ka.Keywords.Default = "Selecionados";
                PromptResult rka = ed.GetKeywords(ka);
                if (rka.Status != PromptStatus.OK && rka.Status != PromptStatus.None) return;
                trecho = rka.Status == PromptStatus.OK && rka.StringResult == "Trecho";
            }

            int n = 0;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var alvo = new HashSet<ObjectId>(ids);
                if (trecho)
                {
                    var runs = new HashSet<string>();
                    foreach (ObjectId id in ids)
                    {
                        var e0 = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        string run;
                        if (e0 != null && LerCampos(e0).TryGetValue("run", out run)) runs.Add(run);
                    }
                    var espaco = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in espaco)
                    {
                        if (id.IsErased) continue;
                        var s = tr.GetObject(id, OpenMode.ForRead) as Solid3d;
                        string run;
                        if (s != null && LerCampos(s).TryGetValue("run", out run) && runs.Contains(run)) alvo.Add(id);
                    }
                }
                foreach (ObjectId id in alvo)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Solid3d;
                    if (ent == null) continue;
                    ent.ColorIndex = aci;
                    n++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\n{0} elemento(s) com a cor alterada.", n);
        }
    }
}
