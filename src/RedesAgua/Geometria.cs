using System;

namespace RedesAgua
{
    // Primitivas de solidos 3D (coordenadas do mundo, unidade do desenho = metros).
    public static class Geometria
    {
        public const string AppXData = "REDESAGUA";

        // Vetor perpendicular estavel: usa o eixo Z do mundo como referencia ("para cima").
        public static Vector3d Perpendicular(Vector3d z)
        {
            Vector3d referencia = Math.Abs(z.Z) > 0.99 ? Vector3d.XAxis : Vector3d.ZAxis;
            return referencia.CrossProduct(z).GetNormal();
        }

        public static Solid3d Cilindro(Point3d inicio, Point3d fim, double raio)
        {
            Vector3d eixo = fim - inicio;
            var solido = new Solid3d();
            solido.CreateFrustum(eixo.Length, raio, raio, raio);
            Alinhar(solido, inicio + eixo * 0.5, eixo.GetNormal());
            return solido;
        }

        // Tronco de cone com base em 'a' (raio rBase) e topo em 'b' (raio rTopo).
        // Confere a orientacao real do frustum do ZWCAD com uma sonda e corrige se preciso.
        public static Solid3d Cone(Point3d a, Point3d b, double rBase, double rTopo)
        {
            Vector3d eixo = b - a;
            double h = eixo.Length;
            var s = new Solid3d();
            s.CreateFrustum(h, rBase, rBase, rTopo);

            double rMeio = (rBase + rTopo) / 2.0;
            double lado = Math.Max(h * 0.01, 1e-4);
            using (var sonda = new Solid3d())
            {
                sonda.CreateBox(lado, lado, lado);
                sonda.TransformBy(Matrix3d.Displacement(new Vector3d(rMeio, 0, -h / 2.0 + h * 0.05)));
                bool tocou = s.CheckInterference(sonda);
                bool invertido = tocou != (rBase > rTopo);
                if (invertido)
                    s.TransformBy(Matrix3d.Rotation(Math.PI, Vector3d.XAxis, Point3d.Origin));
            }
            Alinhar(s, a + eixo * 0.5, eixo.GetNormal());
            return s;
        }

        // Caixa centrada em 'centro', com arestas lx,ly,lz ao longo de ex,ey,ez (ortonormais, destros).
        public static Solid3d Caixa(Point3d centro, Vector3d ex, Vector3d ey, Vector3d ez,
                                    double lx, double ly, double lz)
        {
            var s = new Solid3d();
            s.CreateBox(lx, ly, lz);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                centro, ex, ey, ez));
            return s;
        }

        static void Alinhar(Solid3d s, Point3d meio, Vector3d z)
        {
            Vector3d x = Perpendicular(z);
            Vector3d y = z.CrossProduct(x);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                meio, x, y, z));
        }

        public static void Unir(Solid3d alvo, Solid3d outro)
        {
            alvo.BooleanOperation(BooleanOperationType.BoolUnite, outro);
            outro.Dispose();
        }

        public static void Subtrair(Solid3d alvo, Solid3d outro)
        {
            alvo.BooleanOperation(BooleanOperationType.BoolSubtract, outro);
            outro.Dispose();
        }

        // Arco de curva por revolucao de um circulo em torno do eixo normal ao plano da curva.
        // 'inicio' = centro da secao de entrada; devolve tambem o ponto final do arco.
        public static Solid3d Arco(Point3d inicio, Vector3d dEntrada, Vector3d dSaida,
                                   double rad, double raioTubo, double raioCurva, out Point3d fimArco)
        {
            double tangente = raioCurva * Math.Tan(rad / 2.0);
            fimArco = inicio + dEntrada * tangente + dSaida * tangente;

            Vector3d normal = dEntrada.CrossProduct(dSaida).GetNormal();
            Vector3d paraCentro = (dSaida - dEntrada * dSaida.DotProduct(dEntrada)).GetNormal();
            Point3d centro = inicio + paraCentro * raioCurva;

            using (var circulo = new Circle(inicio, dEntrada, raioTubo))
            {
                var curvas = new DBObjectCollection();
                curvas.Add(circulo);
                DBObjectCollection regioes = Region.CreateFromCurves(curvas);
                try
                {
                    var solido = new Solid3d();
                    solido.Revolve((Region)regioes[0], centro, normal, rad);
                    return solido;
                }
                finally
                {
                    foreach (DBObject r in regioes) r.Dispose();
                }
            }
        }
    }
}
