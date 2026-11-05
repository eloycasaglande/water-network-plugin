using System;

namespace RedesAgua
{
    // DADOS PROVISORIOS, em MILIMETROS, como nos catalogos de fabricantes.
    // - DE (diametro externo) conforme ISO 2531 / NBR 7675 (ferro ductil e DEFoFo).
    // - Flanges PN10 conforme EN 1092-2 / ISO 7005-2 (compativel com NBR 7675 PN10).
    // Conferir com o catalogo do fabricante antes de usar em projeto executivo.
    // O desenho e em METROS: toda dimensao passa por MmParaDesenho.
    public static class Catalogo
    {
        public const double MmParaDesenho = 0.001;

        public static readonly int[] DNs = { 50, 65, 80, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600, 700 };

        static readonly double[] De      = { 66, 82, 98, 118, 144, 170, 222, 274, 326, 378, 429, 480, 532, 635, 738 };
        static readonly double[] FlOD    = { 165, 185, 200, 220, 250, 285, 340, 395, 445, 505, 565, 615, 670, 780, 895 };
        static readonly double[] FlPCD   = { 125, 145, 160, 180, 210, 240, 295, 350, 400, 460, 515, 565, 620, 725, 840 };
        static readonly double[] FlT     = { 19, 19, 19, 19, 19, 19, 20, 22, 24.5, 26, 28, 28, 28.5, 31.5, 35 };
        static readonly int[]    ParN    = { 4, 4, 8, 8, 8, 8, 8, 12, 12, 16, 16, 20, 20, 20, 24 };
        static readonly int[]    ParM    = { 16, 16, 16, 16, 16, 20, 20, 20, 20, 20, 24, 24, 24, 27, 27 };
        static readonly double[] RegL    = { 150, 170, 180, 190, 200, 210, 230, 250, 270, 290, 310, 330, 350, 390, 430 };

        // Junta mecanica (provisorio, tipo AWWA C111 / NBR 7676): parafusos T e porcas por extremidade.
        static readonly int[] JmN = { 4, 4, 4, 4, 6, 6, 6, 8, 8, 10, 12, 12, 14, 16, 18 };
        static readonly int[] JmM = { 16, 16, 16, 16, 16, 20, 20, 20, 20, 20, 20, 24, 24, 24, 24 };

        public static readonly double[] AngulosPadrao = { 90.0, 45.0, 22.5, 11.25 };

        static readonly int[] ComprimentosParafuso =
            { 40, 45, 50, 55, 60, 65, 70, 75, 80, 90, 100, 110, 120, 130, 140, 150, 160, 180, 200 };

        public static int Indice(int dn)
        {
            int melhor = 0;
            for (int i = 1; i < DNs.Length; i++)
                if (Math.Abs(DNs[i] - dn) < Math.Abs(DNs[melhor] - dn)) melhor = i;
            return melhor;
        }

        public static int DnPadrao(int dn) { return DNs[Indice(dn)]; }

        public static double DiametroExternoMm(int dn) { return De[Indice(dn)]; }
        public static double RaioExternoMm(int dn) { return De[Indice(dn)] / 2.0; }
        public static double FlangeDiametroMm(int dn) { return FlOD[Indice(dn)]; }
        public static double FlangeCirculoMm(int dn) { return FlPCD[Indice(dn)]; }
        public static double FlangeEspessuraMm(int dn) { return FlT[Indice(dn)]; }
        public static int ParafusosQtd(int dn) { return ParN[Indice(dn)]; }
        public static int ParafusosBitola(int dn) { return ParM[Indice(dn)]; }
        public static double FuroMm(int dn)
        {
            int m = ParM[Indice(dn)];
            return m <= 16 ? 18 : (m <= 20 ? 22 : (m <= 24 ? 26 : 30));
        }
        public static double RegistroComprimentoMm(int dn) { return RegL[Indice(dn)]; }

        // Profundidade da bolsa (mm).
        public static double BolsaMm(int dn) { return Math.Round(80 + 0.4 * dn); }

        // Comprimento util do parafuso: 2 flanges + junta (3) + porca (0,8 d) + arruela (0,2 d)
        // + sobra de rosca (0,6 d), arredondado para o comprimento comercial acima.
        public static int ParafusoComprimentoMm(int dn)
        {
            double d = ParafusosBitola(dn);
            double necessario = 2 * FlangeEspessuraMm(dn) + 3 + 0.8 * d + 0.2 * d + 0.6 * d;
            foreach (int c in ComprimentosParafuso)
                if (c >= necessario) return c;
            return (int)Math.Ceiling(necessario / 10.0) * 10;
        }

        public static int JmParafusosQtd(int dn) { return JmN[Indice(dn)]; }
        public static int JmBitola(int dn) { return JmM[Indice(dn)]; }

        // Dimensoes visuais da extremidade JM (mm): contraflange + flange da bolsa.
        public const double JmContraflangeMm = 20.0;
        public const double JmFlangeMm = 22.0;

        // Comprimento do parafuso T: contraflange + flange + porca + folga, para o comprimento comercial acima.
        public static int JmParafusoComprimentoMm(int dn)
        {
            double d = JmBitola(dn);
            double necessario = JmContraflangeMm + JmFlangeMm + 0.8 * d + 15;
            foreach (int c in ComprimentosParafuso)
                if (c >= necessario) return c;
            return (int)Math.Ceiling(necessario / 10.0) * 10;
        }

        public static double RaioExterno(int dn) { return RaioExternoMm(dn) * MmParaDesenho; }

        // Raio de curvatura da linha de centro da curva (mm).
        public static double RaioCurvaMm(int dn) { return Math.Max(1.5 * dn, 100.0); }

        // Joelho: raio curto (mm).
        public static double RaioJoelhoMm(int dn) { return Math.Max(1.0 * dn, 60.0); }

        public static double AnguloPadraoMaisProximo(double anguloGraus)
        {
            double melhor = AngulosPadrao[0];
            foreach (double a in AngulosPadrao)
                if (Math.Abs(a - anguloGraus) < Math.Abs(melhor - anguloGraus))
                    melhor = a;
            return melhor;
        }
    }
}
