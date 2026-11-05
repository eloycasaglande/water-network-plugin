using System;
using System.Collections.Generic;

namespace RedesAgua
{
    // Modelo em memoria de um trecho: lista ordenada de elementos (tubo, curva, peca),
    // cada um com portas de entrada/saida. A porta 'Atual' e sempre a saida do ultimo elemento.
    public class Trecho
    {
        public string Material;
        public string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);
        public List<Elemento> Elementos = new List<Elemento>();
        public Porta Atual;
        // Preenchido quando a rede e retomada de um elemento existente: "run:seq:S|B".
        public string Prev;
        readonly List<Porta> historico = new List<Porta>();

        public Trecho(Point3d inicio, int dn, string material)
        {
            Material = material;
            Atual = new Porta { P = inicio, D = Vector3d.XAxis, DN = dn, E = Extremidade.Ponta, TemDirecao = false };
        }

        public void Adicionar(Elemento e)
        {
            historico.Add(Atual);
            Elementos.Add(e);
            Atual = e.Saida;
        }

        public Elemento RemoverUltimo()
        {
            if (Elementos.Count == 0) return null;
            Elemento e = Elementos[Elementos.Count - 1];
            Elementos.RemoveAt(Elementos.Count - 1);
            Atual = historico[historico.Count - 1];
            historico.RemoveAt(historico.Count - 1);
            return e;
        }

        // Ponta entra em bolsa; flange so combina com flange.
        public static bool Compativel(Extremidade saida, Extremidade entrada)
        {
            if (saida == Extremidade.Flange && entrada == Extremidade.Flange) return true;
            if (saida == Extremidade.Bolsa && entrada == Extremidade.Ponta) return true;
            if (saida == Extremidade.Ponta && entrada == Extremidade.Bolsa) return true;
            if (saida == Extremidade.JuntaMecanica && entrada == Extremidade.Ponta) return true;
            if (saida == Extremidade.Ponta && entrada == Extremidade.JuntaMecanica) return true;
            if (saida == Extremidade.Eletrofusao && entrada == Extremidade.Ponta) return true;
            if (saida == Extremidade.Ponta && entrada == Extremidade.Eletrofusao) return true;
            foreach (Extremidade s in new[] { Extremidade.Termofusao, Extremidade.Solda })
            {
                if (saida == s && (entrada == s || entrada == Extremidade.Ponta)) return true;
                if (saida == Extremidade.Ponta && entrada == s) return true;
            }
            return false;   // inclui Fechada (cap): nada se liga depois
        }
    }
}
