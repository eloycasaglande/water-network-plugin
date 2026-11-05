using System;
using System.Collections.Generic;
using System.Globalization;

namespace RedesAgua
{
    // JuntaMecanica = bolsa com flange + contraflange (anel de aperto) + anel de borracha + parafusos T.
    // Fechada = extremidade terminada (cap / flange cego): nada se liga depois dela.
    public enum Extremidade { Ponta, Bolsa, Flange, JuntaMecanica, Fechada, Eletrofusao, Termofusao, Solda }

    // Ponto de conexao: posicao, sentido do fluxo (saindo da peca), DN e tipo de extremidade.
    public class Porta
    {
        public Point3d P;
        public Vector3d D;
        public int DN;
        public Extremidade E;
        public bool TemDirecao;
    }

    // Escolha feita pelo usuario no dialogo (ou nas palavras-chave).
    public class PecaEscolha
    {
        public string Familia = "TE";   // TE, REDUCAO, REGISTRO, RETA, LUVA, JM, CAP
        public int DN;                  // DN da peca (0 = DN da linha)
        public int DN2;                 // saida (reducao) ou derivacao (te)
        public Extremidade EIn = Extremidade.Flange;
        public Extremidade EOut = Extremidade.Flange;
        public Extremidade EBr = Extremidade.Flange;
        public double ComprimentoMm = 300;
        public double ComprimentoForcadoMm;   // reducao: 0 = automatico
        public int DN3;                 // te: DN final da reducao na derivacao (0 = sem reducao)
        public Extremidade EBrOut = Extremidade.Flange;   // te: extremidade de saida da reducao da derivacao
        public double Angulo = 90;      // curvas e joelhos (graus)
        public string Derivacao = "Cima";   // direcao da derivacao (te) ou da curva
        public int PN = 10;
    }

    public class Elemento
    {
        public string Tipo;       // TUBO, CURVA, PECA
        public string Familia;    // TUBO, CURVA, TE, REDUCAO, REGISTRO, RETA, LUVA
        public string Material;
        public string RunMat = "";   // material da rede (para retomar a rede depois)
        public int DN, DN2, PN = 10;
        public Extremidade EIn, EOut, EBr;
        public double Comprimento;   // m, ao longo do eixo
        public double Angulo;        // graus (curvas)
        public Porta Entrada, Saida, Derivacao;
        public short Cor;
        public Solid3d Solido;
        public List<ObjectId> Ids = new List<ObjectId>();

        public int DnSaida { get { return (Familia == "REDUCAO" || Familia == "LUVA_LR") ? DN2 : DN; } }

        // XData: lista de "chave=valor" (versao V2). Lida por Quantitativo.cs.
        static string Pt(Point3d p)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return p.X.ToString("R", inv) + ";" + p.Y.ToString("R", inv) + ";" + p.Z.ToString("R", inv);
        }

        static string Vt(Vector3d v)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return v.X.ToString("R", inv) + ";" + v.Y.ToString("R", inv) + ";" + v.Z.ToString("R", inv);
        }

        public ResultBuffer XData(string runId, int seq, string prev = null)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var extra = new List<string>
            {
                "rmat=" + RunMat,
                "sp=" + Pt(Saida.P),
                "sd=" + Vt(Saida.D),
                "sdn=" + Saida.DN.ToString(inv),
                "se=" + Saida.E,
                "ip=" + Pt(Entrada.P),
                "id=" + Vt(Entrada.D),
                "idn=" + Entrada.DN.ToString(inv),
                "ie=" + Entrada.E
            };
            if (Derivacao != null)
            {
                extra.Add("bp=" + Pt(Derivacao.P));
                extra.Add("bd=" + Vt(Derivacao.D));
                extra.Add("bdn=" + Derivacao.DN.ToString(inv));
                extra.Add("be=" + Derivacao.E);
            }
            if (!string.IsNullOrEmpty(prev)) extra.Add("prev=" + prev);
            try
            {
                Extents3d ex = Solido.GeometricExtents;
                extra.Add("ctr=" + Pt(new Point3d((ex.MinPoint.X + ex.MaxPoint.X) / 2,
                    (ex.MinPoint.Y + ex.MaxPoint.Y) / 2, (ex.MinPoint.Z + ex.MaxPoint.Z) / 2)));
            }
            catch (System.Exception) { }

            string[] campos =
            {
                "V2",
                "tipo=" + Tipo,
                "fam=" + Familia,
                "mat=" + Material,
                "dn=" + DN.ToString(inv),
                "dn2=" + DN2.ToString(inv),
                "pn=" + PN.ToString(inv),
                "ein=" + EIn,
                "eout=" + EOut,
                "ebr=" + EBr,
                "len=" + Comprimento.ToString("F4", inv),
                "ang=" + Angulo.ToString("F2", inv),
                "run=" + runId,
                "seq=" + seq.ToString(inv)
            };
            var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, Geometria.AppXData));
            foreach (string c in campos)
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, c));
            foreach (string c in extra)
                rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, c));
            return rb;
        }
    }

    // Eixo local de uma peca: Z = sentido do fluxo, X = "esquerda", Y = "cima" (em tubo horizontal).
    class Quadro
    {
        public Point3d O;
        public Vector3d X, Y, Z;

        public Quadro(Point3d origem, Vector3d z)
        {
            O = origem;
            Z = z.GetNormal();
            X = Geometria.Perpendicular(Z);
            Y = Z.CrossProduct(X);
        }

        public Point3d W(double x, double y, double z)
        {
            return O + X * x + Y * y + Z * z;
        }
    }

    public static class Pecas
    {
        const double Mm = Catalogo.MmParaDesenho;

        // Direcao (relativa ao rumo, com Z do mundo = "cima"). Em tubo vertical: X/Y do mundo.
        public static Vector3d VetorDirecao(Vector3d rumo, string direcao)
        {
            Vector3d direita, cima;
            if (Math.Abs(rumo.Z) > 0.99)
            {
                direita = Vector3d.XAxis;
                cima = Vector3d.YAxis;
            }
            else
            {
                direita = rumo.CrossProduct(Vector3d.ZAxis).GetNormal();
                cima = Vector3d.ZAxis;
            }
            switch (direcao)
            {
                case "Cima": return cima;
                case "Baixo": return cima.Negate();
                case "Esquerda": return direita.Negate();
                default: return direita;
            }
        }

        public static string MaterialPeca(string materialRede)
        {
            return (materialRede == "DEFoFo" || materialRede == "PVCO") ? "FoFo" : materialRede;
        }

        static double ComprimentoFim(Extremidade e, int dn)
        {
            if (e == Extremidade.Flange) return Catalogo.FlangeEspessuraMm(dn);
            if (e == Extremidade.Bolsa) return Catalogo.BolsaMm(dn);
            if (e == Extremidade.JuntaMecanica) return ComprimentoJm(dn);
            if (e == Extremidade.Eletrofusao) return Math.Round(40 + 0.35 * dn);
            if (e == Extremidade.Termofusao) return Math.Max(50.0, Math.Round(0.4 * dn));
            return 0.0;
        }

        // Comprimento realmente ocupado pela extremidade no desenho (flange inclui o pescoco).
        static double ComprimentoCorpoFim(Extremidade e, int dn)
        {
            if (e == Extremidade.Flange) return Catalogo.FlangeEspessuraMm(dn) * 1.9;
            return ComprimentoFim(e, dn);
        }

        static double ComprimentoJm(int dn)
        {
            return Catalogo.JmContraflangeMm + Catalogo.JmFlangeMm + 0.8 * Catalogo.BolsaMm(dn);
        }

        static Vector3d Cima(Vector3d rumo, Quadro q)
        {
            return Math.Abs(rumo.Z) > 0.99
                ? q.X
                : (Vector3d.ZAxis - rumo * Vector3d.ZAxis.DotProduct(rumo)).GetNormal();
        }

        // Barrilete do corpo de valvula: transicoes conicas entre as extremidades. Devolve o raio do barrilete.
        static double Barrilete(Solid3d corpo, Quadro q, double L, Extremidade ein, Extremidade eout, int dn, double fator)
        {
            double r = Catalogo.RaioExterno(dn);
            double rb = r * 1.12, rBar = r * fator;
            double zi = ComprimentoCorpoFim(ein, dn) * Mm;
            double zf = L - ComprimentoCorpoFim(eout, dn) * Mm;
            if (zf - zi < 4 * Mm) return rBar;
            double tr = Math.Min(r * 0.35, (zf - zi) * 0.3);
            if (tr > 2 * Mm)
            {
                Geometria.Unir(corpo, Geometria.Cone(q.W(0, 0, zi), q.W(0, 0, zi + tr + 1 * Mm), rb, rBar));
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, zi + tr), q.W(0, 0, zf - tr), rBar));
                Geometria.Unir(corpo, Geometria.Cone(q.W(0, 0, zf - tr - 1 * Mm), q.W(0, 0, zf), rBar, rb));
            }
            else
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, zi), q.W(0, 0, zf), rBar));
            return rBar;
        }

        // Meia-largura maxima (m) de uma coluna vertical (sapata, castelo) para nao invadir os flanges das extremidades.
        static double LarguraMaxima(double L, Extremidade ein, Extremidade eout, int dn)
        {
            double livre = L - (ComprimentoCorpoFim(ein, dn) + ComprimentoCorpoFim(eout, dn)) * Mm;
            return Math.Max(livre * 0.5 * 0.92, 10 * Mm);
        }

        // Sapata redonda sob o corpo da valvula (raio limitado a rMax).
        static void Sapata(Solid3d corpo, Point3d cc, Vector3d up, double rBar, double r, double rMax)
        {
            double t = Math.Max(8 * Mm, r * 0.14);
            Geometria.Unir(corpo, Geometria.Cilindro(cc - up * rBar, cc - up * (rBar - t), Math.Min(r * 1.15, rMax)));
        }

        // Flange aparafusado do castelo, com a face superior em yTopo.
        static void FlangeCastelo(Solid3d corpo, Point3d cc, Vector3d up, Vector3d rumo, Vector3d lado,
                                  double yTopo, double rF, double tF, double r, int nb)
        {
            Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yTopo - tF), cc + up * yTopo, rF));
            double rpb = Math.Max(5 * Mm, r * 0.07);
            for (int k = 0; k < nb; k++)
            {
                double a = 2 * Math.PI * k / nb + Math.PI / nb;
                Point3d pb = cc + rumo * (Math.Cos(a) * rF * 0.84) + lado * (Math.Sin(a) * rF * 0.84) + up * yTopo;
                Geometria.Unir(corpo, Geometria.Cilindro(pb - up * (1 * Mm), pb + up * (rpb * 1.8), rpb * 1.6));
            }
        }

        // Raio interno: as pecas sao ocas como tubos (parede ~10% do raio, minimo 4 mm).
        static double RaioInterno(int dn)
        {
            double r = Catalogo.RaioExterno(dn);
            return r - Math.Max(4 * Mm, r * 0.1);
        }

        static void FurarSolido(Solid3d corpo, Solid3d furo)
        {
            try { Geometria.Subtrair(corpo, furo); }
            catch (System.Exception) { furo.Dispose(); }
        }

        // Fura ao longo do eixo local entre z0 e z1 (1 mm de folga nas pontas).
        static void Furar(Solid3d corpo, Quadro q, double z0, double z1, double raio)
        {
            FurarSolido(corpo, Geometria.Cilindro(q.W(0, 0, z0 - 1 * Mm), q.W(0, 0, z1 + 1 * Mm), raio));
        }

        // ---------- TUBO ----------
        public static Elemento CriarTubo(Porta de, double comprimento, string material)
        {
            var e = new Elemento
            {
                Tipo = "TUBO", Familia = "TUBO", Material = material, RunMat = material, DN = de.DN,
                EIn = Extremidade.Ponta, EOut = Extremidade.Ponta, Comprimento = comprimento, Cor = (short)(material == "Aluminio" ? 9 : 5)
            };
            Point3d fim = de.P + de.D * comprimento;
            e.Solido = Geometria.Cilindro(de.P, fim, Catalogo.RaioExterno(de.DN));
            FurarSolido(e.Solido, Geometria.Cilindro(de.P - de.D * (1 * Mm), fim + de.D * (1 * Mm), RaioInterno(de.DN)));
            e.Entrada = Copiar(de, Extremidade.Ponta);
            e.Saida = new Porta { P = fim, D = de.D, DN = de.DN, E = Extremidade.Ponta, TemDirecao = true };
            return e;
        }

        // ---------- CURVA (bolsa-bolsa) ----------
        // Curva (raio longo) ou joelho (raio curto) em qualquer par de extremidades.
        public static Elemento CriarCurva(Porta de, double anguloGraus, Vector3d alvo, string material,
                                          Extremidade ein = Extremidade.Bolsa, Extremidade eout = Extremidade.Bolsa,
                                          bool joelho = false, int dnPeca = 0)
        {
            int dn = dnPeca > 0 ? dnPeca : de.DN;
            double r = Catalogo.RaioExterno(dn);
            double rc = (joelho ? Catalogo.RaioJoelhoMm(dn) : Catalogo.RaioCurvaMm(dn)) * Mm;
            double rad = anguloGraus * Math.PI / 180.0;

            // comprimento ocupado por cada extremidade (flange inclui o pescoco)
            double lenIn = ComprimentoCorpoFim(ein, dn) * Mm;
            double lenOut = ComprimentoCorpoFim(eout, dn) * Mm;

            Vector3d d0 = de.D;
            Vector3d eixo = d0.CrossProduct(alvo);
            if (eixo.Length < 1e-6)
                eixo = Math.Abs(d0.Z) > 0.99 ? Vector3d.XAxis : Vector3d.ZAxis;
            eixo = eixo.GetNormal();
            Vector3d d1 = d0.RotateBy(rad, eixo);

            // 1 mm de sobreposicao no arco para a uniao nao ficar com faces coincidentes
            double sob = 1 * Mm;
            Point3d inicioArco = de.P + d0 * (lenIn > 0 ? lenIn - sob : 0.0);
            Point3d fimArco;
            Solid3d corpo = Geometria.Arco(inicioArco, d0, d1, rad, r, rc, out fimArco);
            Point3d saida = fimArco + d1 * (lenOut > 0 ? lenOut - sob : 0.0);

            Fim(corpo, new Quadro(de.P, d0), 0, +1, ein, dn);
            Fim(corpo, new Quadro(saida, d1), 0, -1, eout, dn);

            // furo: arco + trechos retos das extremidades
            double ri = RaioInterno(dn);
            Point3d fimArcoFuro;
            FurarSolido(corpo, Geometria.Arco(inicioArco, d0, d1, rad, ri, rc, out fimArcoFuro));
            FurarSolido(corpo, Geometria.Cilindro(de.P - d0 * (1 * Mm), inicioArco + d0 * (1 * Mm), ri));
            FurarSolido(corpo, Geometria.Cilindro(fimArco - d1 * (1 * Mm), saida + d1 * (1 * Mm), ri));

            string fam = joelho ? "JOELHO" : "CURVA";
            var e = new Elemento
            {
                Tipo = "CURVA", Familia = fam, Material = MaterialPeca(material), RunMat = material, DN = dn,
                EIn = ein, EOut = eout,
                Comprimento = lenIn + lenOut + rc * rad, Angulo = anguloGraus, Cor = (short)((ein == Extremidade.Solda || eout == Extremidade.Solda) ? 30 : 250),
                Solido = corpo
            };
            e.Entrada = new Porta { P = de.P, D = d0, DN = dn, E = ein, TemDirecao = true };
            e.Saida = new Porta { P = saida, D = d1, DN = dn, E = eout, TemDirecao = true };
            return e;
        }

        // ---------- PECAS ----------
        public static Elemento CriarPeca(Porta de, PecaEscolha pe, string materialRede, Vector3d rumo)
        {
            int dn = pe.DN > 0 ? pe.DN : de.DN;
            int dn2 = pe.DN2 > 0 ? pe.DN2 : dn;

            if (pe.Familia == "CURVA" || pe.Familia == "JOELHO")
            {
                var entrada = new Porta { P = de.P, D = rumo, DN = dn, E = de.E, TemDirecao = true };
                Elemento c = CriarCurva(entrada, pe.Angulo, VetorDirecao(rumo, pe.Derivacao), materialRede,
                    pe.EIn, pe.EOut, pe.Familia == "JOELHO", dn);
                c.PN = pe.PN;
                return c;
            }

            var q = new Quadro(de.P, rumo);
            var e = new Elemento
            {
                Tipo = "PECA", Familia = pe.Familia, Material = MaterialPeca(materialRede), RunMat = materialRede,
                DN = dn,
                DN2 = (pe.Familia == "REDUCAO" || pe.Familia == "TE" || pe.Familia == "Y" || pe.Familia == "LUVA_LR") ? dn2 : 0,
                PN = pe.PN,
                EIn = pe.EIn, EOut = pe.EOut, EBr = pe.EBr, Cor = 250
            };

            double r = Catalogo.RaioExterno(dn);
            double L;
            Solid3d corpo;

            switch (pe.Familia)
            {
                case "LUVA":
                {
                    Extremidade tp = (pe.EIn == Extremidade.Eletrofusao || pe.EIn == Extremidade.Termofusao)
                        ? pe.EIn : Extremidade.Bolsa;
                    e.EIn = e.EOut = tp;
                    if (tp == Extremidade.Bolsa)
                    {
                        L = (2 * Catalogo.BolsaMm(dn) + 30) * Mm;
                        corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r);
                        Fim(corpo, q, 0, +1, Extremidade.Bolsa, dn);
                        Fim(corpo, q, L, -1, Extremidade.Bolsa, dn);
                        Geometria.Unir(corpo, Geometria.Cilindro(
                            q.W(0, 0, L / 2 - 12 * Mm), q.W(0, 0, L / 2 + 12 * Mm), r * 1.36));
                        Furar(corpo, q, 0, L, RaioInterno(dn));
                    }
                    else if (tp == Extremidade.Eletrofusao)
                    {
                        // Luva de eletrofusao (PEAD): manga com anel central, 2 terminais e aneis de ponta.
                        double lf = ComprimentoFim(tp, dn) * Mm;
                        L = 2 * lf + 20 * Mm;
                        corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 1.2);
                        Fim(corpo, q, 0, +1, tp, dn);
                        Fim(corpo, q, L, -1, tp, dn);
                        Geometria.Unir(corpo, Geometria.Cilindro(
                            q.W(0, 0, L / 2 - 6 * Mm), q.W(0, 0, L / 2 + 6 * Mm), r * 1.28));
                        Furar(corpo, q, 0, L, RaioInterno(dn));
                    }
                    else
                    {
                        // Luva de termofusao por soquete (PEAD): manga lisa com cordoes de solda nas bocas.
                        double prof = Math.Round(40 + 0.35 * dn) * Mm;
                        L = 2 * prof + 6 * Mm;
                        double rb = r * 1.1, cd = Math.Max(2 * Mm, r * 0.06);
                        corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), rb);
                        Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, 7 * Mm), rb + cd));
                        Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, L - 7 * Mm), q.W(0, 0, L), rb + cd));
                        FurarSolido(corpo, Geometria.Cilindro(q.W(0, 0, -1 * Mm), q.W(0, 0, prof), r * 0.99));
                        FurarSolido(corpo, Geometria.Cilindro(q.W(0, 0, L - prof), q.W(0, 0, L + 1 * Mm), r * 0.99));
                        Furar(corpo, q, 0, L, RaioInterno(dn));
                    }
                    break;
                }
                case "LUVA_LR":
                {
                    // Luva de reducao: bolsa no DN maior/menor, cone de transicao no meio.
                    e.EIn = e.EOut = Extremidade.Bolsa;
                    double r2 = Catalogo.RaioExterno(dn2);
                    double b1 = Catalogo.BolsaMm(dn) * Mm, b2 = Catalogo.BolsaMm(dn2) * Mm;
                    double rb1 = r * 1.28, rb2 = r2 * 1.28;
                    double lc = Math.Max(40 * Mm, Math.Abs(rb1 - rb2) * 1.2);
                    L = b1 + lc + b2;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, b1), rb1);
                    if (Math.Abs(rb1 - rb2) > 1e-6)
                        Geometria.Unir(corpo, Geometria.Cone(
                            q.W(0, 0, b1 - 1 * Mm), q.W(0, 0, b1 + lc + 1 * Mm), rb1, rb2));
                    else
                        Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, b1 - 1 * Mm), q.W(0, 0, b1 + lc + 1 * Mm), rb1));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, b1 + lc - 1 * Mm), q.W(0, 0, L), rb2));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, 10 * Mm), rb1 * 1.07));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, L - 10 * Mm), q.W(0, 0, L), rb2 * 1.07));
                    FurarSolido(corpo, Geometria.Cilindro(q.W(0, 0, -1 * Mm), q.W(0, 0, b1), r * 0.97));
                    FurarSolido(corpo, Geometria.Cilindro(q.W(0, 0, L - b2), q.W(0, 0, L + 1 * Mm), r2 * 0.97));
                    Furar(corpo, q, 0, b1 + lc / 2, RaioInterno(dn));
                    Furar(corpo, q, b1 + lc / 2, L, RaioInterno(dn2));
                    break;
                }
                case "BORBOLETA":
                {
                    // Registro de manobra por alavanca (conforme a foto): corpo abaulado entre duas pontas
                    // (bolsa/flange), pescoco curto com colar e manipulo em T ao longo do fluxo.
                    double min = ComprimentoCorpoFim(e.EIn, dn) + ComprimentoCorpoFim(e.EOut, dn) + 30;
                    L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 0.85, min) * Mm;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 1.12);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    double rBar = Barrilete(corpo, q, L, e.EIn, e.EOut, dn, 1.4);

                    Vector3d up = Cima(rumo, q);
                    Vector3d lado = rumo.CrossProduct(up).GetNormal();
                    Point3d cc = q.W(0, 0, L / 2);
                    double yN = rBar + r * 0.3;                       // topo do pescoco
                    Geometria.Unir(corpo, Geometria.Cilindro(cc, cc + up * yN, r * 0.42));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yN - r * 0.1), cc + up * (yN + r * 0.12), r * 0.55));
                    double yH = yN + r * 0.12;                        // base do manipulo
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yH - 1 * Mm), cc + up * (yH + r * 0.2), r * 0.24));
                    double tM = Math.Max(8 * Mm, r * 0.2);            // espessura do manipulo
                    double yM = yH + r * 0.2;
                    double lM = r * 2.8, wM = r * 0.5;
                    Point3d cm = cc + up * (yM + tM / 2 - 1 * Mm);
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yM - 1 * Mm), cc + up * (yM + tM), r * 0.42));
                    Geometria.Unir(corpo, Geometria.Caixa(cm + rumo * (lM * 0.1), rumo, lado, up, lM, wM, tM));
                    Geometria.Unir(corpo, Geometria.Caixa(cm, rumo, lado, up, wM, r * 1.1, tM));
                    // pontas arredondadas do manipulo
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cm + rumo * (lM * 0.6) - up * (tM / 2), cm + rumo * (lM * 0.6) + up * (tM / 2), wM / 2));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cm - rumo * (lM * 0.4) - up * (tM / 2), cm - rumo * (lM * 0.4) + up * (tM / 2), wM / 2));
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    e.Cor = 30;
                    break;
                }
                case "JM":
                {
                    // Luva de junta mecanica: luva central + contraflange, flange e parafusos T nas duas pontas.
                    e.EIn = e.EOut = Extremidade.JuntaMecanica;
                    double ext = ComprimentoJm(dn) * Mm;
                    L = Math.Max(2 * ext + 40 * Mm, pe.ComprimentoForcadoMm * Mm);
                    double rb = r * 1.28;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), rb);
                    Fim(corpo, q, 0, +1, Extremidade.JuntaMecanica, dn);
                    Fim(corpo, q, L, -1, Extremidade.JuntaMecanica, dn);
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    break;
                }
                case "JD":
                {
                    // Junta de desmontagem travada axialmente: flange nas duas pontas, tubo central, luva com
                    // anel de aperto saindo da flange de saida e tirantes roscados (qtd e bitola da flange).
                    e.EIn = e.EOut = Extremidade.Flange;
                    double tF = Catalogo.FlangeEspessuraMm(dn) * Mm;
                    double pad = pe.ComprimentoMm > 0 ? pe.ComprimentoMm : Catalogo.JdComprimentoMm(dn);
                    L = Math.Max(pad, Catalogo.JdMinimoMm(dn)) * Mm;
                    double pcd = Catalogo.FlangeCirculoMm(dn) / 2.0 * Mm;
                    int nT = Catalogo.ParafusosQtd(dn);
                    double bit = Catalogo.ParafusosBitola(dn) * Mm, rp = bit / 2;
                    double rb = Math.Max(Math.Min(r * 1.22, pcd - rp - 4 * Mm), r * 1.04);   // luva
                    double rA = Math.Max(Math.Min(rb * 1.12, pcd - rp - 2 * Mm), rb);         // anel de aperto
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r);
                    Fim(corpo, q, 0, +1, Extremidade.Flange, dn, false);
                    Fim(corpo, q, L, -1, Extremidade.Flange, dn, false);
                    double ls = (L - 2 * tF) * 0.5, zL = L - tF - ls;
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, zL), q.W(0, 0, L - tF + 1 * Mm), rb));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, zL), q.W(0, 0, zL + tF * 0.7), rA));
                    double porca = bit * 0.8, ponta = 4 * Mm;
                    for (int k = 0; k < nT; k++)
                    {
                        double a = Math.PI / nT + 2 * Math.PI * k / nT;
                        double x = Math.Cos(a) * pcd, y = Math.Sin(a) * pcd;
                        Geometria.Unir(corpo, Geometria.Cilindro(
                            q.W(x, y, -(porca + ponta)), q.W(x, y, L + porca + ponta), rp));
                        Geometria.Unir(corpo, Geometria.Cilindro(q.W(x, y, -porca), q.W(x, y, 0), rp * 1.7));
                        Geometria.Unir(corpo, Geometria.Cilindro(q.W(x, y, L), q.W(x, y, L + porca), rp * 1.7));
                    }
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    e.Cor = 8;   // cinza
                    break;
                }
                case "CAP":
                {
                    // Cap: fecha a ponta da rede. Entrada em Bolsa, JM ou Flange (flange cego).
                    Extremidade ent = pe.EIn == Extremidade.Ponta ? Extremidade.Bolsa : pe.EIn;
                    e.EIn = ent;
                    e.EOut = Extremidade.Fechada;
                    double rb = r * 1.28;
                    if (ent == Extremidade.Flange)
                    {
                        double t = Catalogo.FlangeEspessuraMm(dn) * Mm;
                        L = t * 1.15;
                        corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 0.6);
                        Fim(corpo, q, 0, +1, Extremidade.Flange, dn, false);
                    }
                    else
                    {
                        // Cap de luva: tampa cilindrica de fundo plano, borda levemente chanfrada.
                        double ext = ComprimentoFim(ent, dn) * Mm;
                        double fundo = Math.Max(14 * Mm, r * 0.25);
                        double chanfro = Math.Min(8 * Mm, fundo * 0.6);
                        L = ext + fundo;
                        corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L - chanfro + 1 * Mm), rb);
                        Geometria.Unir(corpo, Geometria.Cone(
                            q.W(0, 0, L - chanfro), q.W(0, 0, L), rb, rb * 0.9));
                        Fim(corpo, q, 0, +1, ent, dn);
                        Furar(corpo, q, 0, L - fundo, RaioInterno(dn));
                    }
                    break;
                }
                case "FLANGE":
                {
                    // Anel de flange (so o disco com furos), furo central = diametro externo do tubo.
                    e.EIn = Extremidade.Bolsa;
                    e.EOut = Extremidade.Flange;
                    L = Catalogo.FlangeEspessuraMm(dn) * Mm;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), Catalogo.FlangeDiametroMm(dn) / 2.0 * Mm);
                    double pcdF = Catalogo.FlangeCirculoMm(dn) / 2.0 * Mm;
                    double rfF = Catalogo.FuroMm(dn) / 2.0 * Mm;
                    int nF = Catalogo.ParafusosQtd(dn);
                    for (int k = 0; k < nF; k++)
                    {
                        double a = Math.PI / nF + 2 * Math.PI * k / nF;
                        Geometria.Subtrair(corpo, Geometria.Cilindro(
                            q.W(Math.Cos(a) * pcdF, Math.Sin(a) * pcdF, -1 * Mm),
                            q.W(Math.Cos(a) * pcdF, Math.Sin(a) * pcdF, L + 1 * Mm), rfF));
                    }
                    Furar(corpo, q, 0, L, r);
                    e.Cor = 250;
                    break;
                }
                case "RETA":
                {
                    double min = ComprimentoFim(e.EIn, dn) + ComprimentoFim(e.EOut, dn) + 30;
                    L = Math.Max(pe.ComprimentoMm, min) * Mm;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    break;
                }
                case "REDUCAO":
                {
                    double r2 = Catalogo.RaioExterno(dn2);
                    double dif = Math.Abs(Catalogo.DiametroExternoMm(dn) - Catalogo.DiametroExternoMm(dn2));
                    double min = ComprimentoFim(e.EIn, dn) + ComprimentoFim(e.EOut, dn2) + 40;
                    L = Math.Max(Math.Max(min, 2 * dif + 120), pe.ComprimentoForcadoMm) * Mm;
                    corpo = Geometria.Cone(q.W(0, 0, 0), q.W(0, 0, L), r, r2);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn2);
                    FurarSolido(corpo, Geometria.Cone(q.W(0, 0, 0), q.W(0, 0, L), RaioInterno(dn), RaioInterno(dn2)));
                    Furar(corpo, q, 0, 0, RaioInterno(dn));
                    Furar(corpo, q, L, L, RaioInterno(dn2));
                    break;
                }
                case "TE":
                {
                    double r2 = Catalogo.RaioExterno(dn2);
                    double B = ComprimentoFim(e.EBr, dn2) * Mm + r + 100 * Mm;
                    double minL = (ComprimentoFim(e.EIn, dn) + ComprimentoFim(e.EOut, dn)
                                   + Catalogo.DiametroExternoMm(dn2) + 80) * Mm;
                    L = Math.Max(2 * B, minL);
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r);

                    Vector3d b = VetorDirecao(rumo, pe.Derivacao);
                    Point3d centro = q.W(0, 0, L / 2);
                    Point3d topo = centro + b * B;
                    Geometria.Unir(corpo, Geometria.Cilindro(centro, topo, r2));
                    // reforco na juncao
                    Geometria.Unir(corpo, Geometria.Cilindro(centro, centro + b * (r * 0.9), r2 * 1.15));

                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    var qb = new Quadro(centro, b);
                    Fim(corpo, qb, B, -1, e.EBr, dn2);
                    e.Derivacao = new Porta { P = topo, D = b, DN = dn2, E = e.EBr, TemDirecao = true };
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    FurarSolido(corpo, Geometria.Cilindro(centro, topo + b * (1 * Mm), RaioInterno(dn2)));
                    break;
                }
                case "Y":
                {
                    // Juncao Y 45 graus: corpo reto + derivacao inclinada 45 graus a favor do fluxo.
                    double r2 = Catalogo.RaioExterno(dn2);
                    Vector3d b = VetorDirecao(rumo, pe.Derivacao);
                    double c45 = Math.Cos(Math.PI / 4);
                    Vector3d d = (rumo * c45 + b * c45).GetNormal();
                    double zc = ComprimentoCorpoFim(e.EIn, dn) * Mm + r2 * 1.5 + 30 * Mm;
                    double roMain = (e.EOut == Extremidade.Flange ? Catalogo.FlangeDiametroMm(dn) / 2.0 : Catalogo.RaioExternoMm(dn) * 1.28) * Mm;
                    double roBr = (e.EBr == Extremidade.Flange ? Catalogo.FlangeDiametroMm(dn2) / 2.0 : Catalogo.RaioExternoMm(dn2) * 1.28) * Mm;
                    double B = ComprimentoCorpoFim(e.EBr, dn2) * Mm + (r + r2) * 1.6 + 60 * Mm;
                    B = Math.Max(B, (roMain + roBr + 20 * Mm) / c45);
                    double minL = ComprimentoCorpoFim(e.EIn, dn) * Mm + ComprimentoCorpoFim(e.EOut, dn) * Mm + 2 * r + 80 * Mm;
                    L = Math.Max(minL, zc + B * c45 + r2 * c45);
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r);

                    Point3d centro = q.W(0, 0, zc);
                    Point3d topo = centro + d * B;
                    Geometria.Unir(corpo, Geometria.Cilindro(centro, topo, r2));
                    Geometria.Unir(corpo, Geometria.Cilindro(centro - d * (r2 * 0.3), centro + d * (r * 1.2), r2 * 1.15));

                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    var qb = new Quadro(centro, d);
                    Fim(corpo, qb, B, -1, e.EBr, dn2);
                    e.Derivacao = new Porta { P = topo, D = d, DN = dn2, E = e.EBr, TemDirecao = true };
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    FurarSolido(corpo, Geometria.Cilindro(centro, topo + d * (1 * Mm), RaioInterno(dn2)));
                    break;
                }
                case "REGISTRO":
                {
                    // Registro de gaveta de sede resiliente (fotos 05-08): corpo fundido alto (caixa com topo
                    // arredondado ao longo do fluxo), tampa aparafusada, pescoco com gaxeta, haste e cabecote.
                    double rmm = r / Mm;
                    double cfIn = ComprimentoCorpoFim(e.EIn, dn) * Mm, cfOut = ComprimentoCorpoFim(e.EOut, dn) * Mm;
                    double central = Math.Min(1.4 * rmm, 160) * Mm;   // trecho livre minimo entre as pontas
                    L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * Mm, cfIn + cfOut + central);
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 1.12);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    Barrilete(corpo, q, L, e.EIn, e.EOut, dn, 1.18);

                    Vector3d up = Cima(rumo, q);
                    Vector3d lado = rumo.CrossProduct(up).GetNormal();
                    double zi = cfIn, zf = L - cfOut;
                    Point3d cc = q.W(0, 0, (zi + zf) / 2);
                    double a = (zf - zi) * 0.5 * 0.94;          // meia-largura ao longo do fluxo
                    double b = Math.Min(r * 0.95, a * 1.5);     // meia-largura transversal
                    double yA = r * 1.4;
                    // corpo: caixa + topo arredondado (cilindro deitado, eixo transversal)
                    Geometria.Unir(corpo, Geometria.Caixa(cc + up * (yA / 2), lado, rumo, up, 2 * b, 2 * a, yA));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * yA - lado * b, cc + up * yA + lado * b, a));
                    double yTop = yA + a;
                    // tampa: base com parafusos e cilindro
                    double tF = Math.Max(8 * Mm, r * 0.1);
                    double rc = Math.Min(a, b) * 0.62, rF = rc * 1.35;
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yA + a * 0.3), cc + up * (yTop + tF), rF));
                    FlangeCastelo(corpo, cc, up, rumo, lado, yTop + tF, rF, tF, r, dn >= 300 ? 8 : 6);
                    double yc0 = yTop + tF, hc = r * 0.45;
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yc0 - 1 * Mm), cc + up * (yc0 + hc), rc));
                    // pescoco, gaxeta, haste e cabecote
                    double rn = Math.Max(rc * 0.55, 14 * Mm);
                    double yN0 = yc0 + hc, yN1 = yN0 + r * 0.35;
                    Geometria.Unir(corpo, Geometria.Cone(cc + up * (yN0 - 1 * Mm), cc + up * yN1, rc, rn));
                    double yN2 = yN1 + r * 0.3;
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yN1 - 1 * Mm), cc + up * yN2, rn));
                    double tG = Math.Max(8 * Mm, r * 0.1);
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * yN2, cc + up * (yN2 + tG), rn * 1.3));
                    double rh = Math.Max(7 * Mm, r * 0.09);
                    double yH = yN2 + tG, hh = Math.Max(r * 0.7, 30 * Mm);
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yH - 1 * Mm), cc + up * (yH + hh), rh));
                    double rcb = Math.Min(Math.Max(r * 0.26, 20 * Mm), rc * 0.9);
                    double hcb = Math.Max(r * 0.8, 50 * Mm), yC1 = yH + hh + hcb;
                    Geometria.Unir(corpo, Geometria.Cone(cc + up * (yH + hh - 1 * Mm), cc + up * yC1, rcb, rcb * 0.8));
                    double sq = rcb * 1.1, hsq = Math.Max(rcb * 0.9, 15 * Mm);
                    Geometria.Unir(corpo, Geometria.Caixa(cc + up * (yC1 + hsq / 2 - 1 * Mm), lado, rumo, up, sq, sq, hsq));
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    e.Cor = 1;   // vermelho
                    break;
                }
                case "RETENCAO":
                {
                    // Valvula de retencao de portinhola: barrilete, tampa de inspecao aparafusada e tampao do eixo.
                    double min = ComprimentoCorpoFim(e.EIn, dn) + ComprimentoCorpoFim(e.EOut, dn) + 40;
                    L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 1.1, min) * Mm;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 1.12);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    double rBar = Barrilete(corpo, q, L, e.EIn, e.EOut, dn, 1.3);

                    Vector3d up = Cima(rumo, q);
                    Vector3d lado = rumo.CrossProduct(up).GetNormal();
                    Point3d cc = q.W(0, 0, L / 2);
                    double lim = LarguraMaxima(L, e.EIn, e.EOut, dn);
                    double rV = Math.Min(r * 1.05, lim / 1.28), yTopo = r * 1.35;
                    double tF = Math.Max(10 * Mm, r * 0.13);
                    Geometria.Unir(corpo, Geometria.Cilindro(cc, cc + up * yTopo, rV));
                    Sapata(corpo, cc, up, rBar, r, lim);
                    FlangeCastelo(corpo, cc, up, rumo, lado, yTopo, rV * 1.28, tF, r, dn >= 300 ? 8 : 6);
                    // tampa em calota e tampao
                    double yCal = yTopo + r * 0.5;
                    Geometria.Unir(corpo, Geometria.Cone(cc + up * (yTopo - 1 * Mm), cc + up * yCal, rV * 1.05, rV * 0.45));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yCal - 1 * Mm), cc + up * (yCal + r * 0.15), rV * 0.45));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * (yCal + r * 0.15 - 1 * Mm), cc + up * (yCal + r * 0.3), rV * 0.3));
                    // tampao do eixo da portinhola, na lateral, a montante do centro
                    double zl = Math.Max(0.0, (L - (ComprimentoCorpoFim(e.EIn, dn) + ComprimentoCorpoFim(e.EOut, dn)) * Mm) * 0.2);
                    Point3d pl = cc - rumo * zl;
                    Geometria.Unir(corpo, Geometria.Cilindro(pl + lado * (rBar * 0.5), pl + lado * (rBar + r * 0.4), r * 0.3));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        pl + lado * (rBar + r * 0.4 - 1 * Mm), pl + lado * (rBar + r * 0.5), r * 0.38));
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    e.Cor = 3;   // verde
                    break;
                }
                case "VRP":
                {
                    // Valvula redutora de pressao: corpo globo, grande camara do diafragma com flanges
                    // aparafusados, indicador de curso e tubulacao do piloto.
                    double min = ComprimentoCorpoFim(e.EIn, dn) + ComprimentoCorpoFim(e.EOut, dn) + 40;
                    L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 1.15, min) * Mm;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, L), r * 1.12);
                    Fim(corpo, q, 0, +1, e.EIn, dn);
                    Fim(corpo, q, L, -1, e.EOut, dn);
                    double rBar = Barrilete(corpo, q, L, e.EIn, e.EOut, dn, 1.25);

                    Vector3d up = Cima(rumo, q);
                    Vector3d lado = rumo.CrossProduct(up).GetNormal();
                    Point3d cc = q.W(0, 0, L / 2);
                    double lim = LarguraMaxima(L, e.EIn, e.EOut, dn);
                    double rV = Math.Min(r * 0.95, lim), yCol = r * 1.3, rD = r * 1.75;
                    Geometria.Unir(corpo, Geometria.Cilindro(cc, cc + up * yCol, rV));
                    Sapata(corpo, cc, up, rBar, r, lim);
                    double yA = yCol + r * 0.5, yB = yA + r * 0.5, yC = yB + r * 0.7;
                    Geometria.Unir(corpo, Geometria.Cone(cc + up * (yCol - 1 * Mm), cc + up * yA, rV, rD));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yA - 1 * Mm), cc + up * (yB + 1 * Mm), rD));
                    double tR = Math.Max(8 * Mm, r * 0.1);
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * yA, cc + up * (yA + tR), rD * 1.08));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yB - tR), cc + up * yB, rD * 1.08));
                    Geometria.Unir(corpo, Geometria.Cone(cc + up * (yB - 1 * Mm), cc + up * yC, rD, r * 0.5));
                    Geometria.Unir(corpo, Geometria.Cilindro(cc + up * (yC - 1 * Mm), cc + up * (yC + r * 0.25), r * 0.5));
                    double rt = Math.Max(5 * Mm, r * 0.05);
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * (yC + r * 0.25 - 1 * Mm), cc + up * (yC + r * 0.75), Math.Max(6 * Mm, r * 0.07)));
                    // piloto: tubo da camara ate o barrilete, com valvula piloto no meio
                    double yP = yA + r * 0.25, xP = rD + r * 0.55, yJ = r * 0.2;
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * yP + lado * (rD * 0.9), cc + up * yP + lado * (xP + rt), rt));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * yP + lado * xP, cc + up * yJ + lado * xP, rt));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * yJ + lado * (xP + rt), cc + up * yJ + lado * (rBar * 0.6), rt));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        cc + up * ((yP + yJ) / 2 - r * 0.25) + lado * xP,
                        cc + up * ((yP + yJ) / 2 + r * 0.25) + lado * xP, r * 0.2));
                    Furar(corpo, q, 0, L, RaioInterno(dn));
                    e.Cor = 4;   // ciano
                    break;
                }
                case "VENTOSA":
                {
                    // Ventosa (fim de linha, sobre a derivacao de um te): pescoco, camara com flange
                    // aparafusado, calota e bocal de saida de ar.
                    Extremidade ent = pe.EIn == Extremidade.Ponta ? Extremidade.Flange : pe.EIn;
                    e.EIn = ent;
                    e.EOut = Extremidade.Fechada;
                    double rN = r * 1.1;
                    double rB = Math.Max(r * 1.25, 40 * Mm);          // corpo
                    double rF = rB * 1.45, tF = rB * 0.22;            // flange da tampa
                    double zT = ComprimentoCorpoFim(ent, dn) * Mm;
                    double z1 = zT + rB * 0.6;                        // fim do cone de transicao
                    double z2 = z1 + rB * 2.4;                        // topo do corpo (base do flange)
                    double z3 = z2 + tF * 1.6, z4 = z3 + rB * 0.35, z5 = z4 + rB * 0.3;
                    double rBoss = rB * 0.5;
                    L = z5;
                    corpo = Geometria.Cilindro(q.W(0, 0, 0), q.W(0, 0, zT), rN);
                    Fim(corpo, q, 0, +1, ent, dn);
                    Geometria.Unir(corpo, Geometria.Cone(q.W(0, 0, zT - 1 * Mm), q.W(0, 0, z1), rN, rB));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z1 - 1 * Mm), q.W(0, 0, z2 + 1 * Mm), rB));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z2), q.W(0, 0, z3), rF));
                    int nb = 6;
                    double rpb = rF * 0.07;
                    for (int k = 0; k < nb; k++)
                    {
                        double a = 2 * Math.PI * k / nb;
                        double bx = Math.Cos(a) * rF * 0.8, by = Math.Sin(a) * rF * 0.8;
                        Geometria.Unir(corpo, Geometria.Cilindro(
                            q.W(bx, by, z3 - 1 * Mm), q.W(bx, by, z3 + rpb * 1.4), rpb));
                    }
                    Geometria.Unir(corpo, Geometria.Cone(q.W(0, 0, z3 - 1 * Mm), q.W(0, 0, z4), rF * 0.85, rBoss));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z4 - 1 * Mm), q.W(0, 0, z5), rBoss));
                    // camara lateral pequena (orificio de purga), ao lado do corpo e abaixo do flange da tampa
                    double rS = rB * 0.5, xS = rF + rS * 0.55;
                    double zS0 = z1 + rB * 0.7, zS1 = z2 - tF * 1.0, tFs = tF * 0.8;
                    double zc = zS0 + (zS1 - zS0) * 0.35;
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, zc), q.W(xS, 0, zc), rS * 0.45));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(xS, 0, zS0), q.W(xS, 0, zS1 + 1 * Mm), rS));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(xS, 0, zS1), q.W(xS, 0, zS1 + tFs), rS * 1.4));
                    Geometria.Unir(corpo, Geometria.Cilindro(q.W(xS, 0, zS1 + tFs - 1 * Mm), q.W(xS, 0, zS1 + tFs + rS * 0.45), rS * 0.5));
                    // oco: passagem da derivacao, camara e orificio de saida
                    Furar(corpo, q, 0, z1, RaioInterno(dn));
                    Furar(corpo, q, z1, z2, rB * 0.82);
                    Furar(corpo, q, z2, z5, Math.Max(4 * Mm, rBoss * 0.35));
                    e.Cor = 2;   // amarelo
                    break;
                }
                default:
                    throw new ArgumentException("Familia desconhecida: " + pe.Familia);
            }

            e.Solido = corpo;
            e.Comprimento = L;
            e.Entrada = new Porta { P = de.P, D = rumo, DN = dn, E = e.EIn, TemDirecao = true };
            e.Saida = new Porta
            {
                P = q.W(0, 0, L), D = rumo, DN = e.DnSaida, E = e.EOut, TemDirecao = true
            };
            return e;
        }

        static Porta Copiar(Porta p, Extremidade e)
        {
            return new Porta { P = p.P, D = p.D, DN = p.DN, E = e, TemDirecao = p.TemDirecao };
        }

        // Extremidade no plano z (eixo local da peca); sinal +1 = a peca esta em +z, -1 = em -z.
        static void Fim(Solid3d corpo, Quadro q, double z, int sinal, Extremidade e, int dn, bool cubo = true)
        {
            double r = Catalogo.RaioExterno(dn);
            if (e == Extremidade.JuntaMecanica)
            {
                // Do plano da porta para dentro da peca: contraflange (anel de aperto),
                // flange da bolsa e bolsa. Parafusos T atravessam os dois aneis, porcas fora.
                double gt = Catalogo.JmContraflangeMm * Mm;
                double ft = Catalogo.JmFlangeMm * Mm;
                double bolsa = Catalogo.BolsaMm(dn) * 0.8 * Mm;
                double rb = r * 1.28;
                double rF = r * 1.62;
                double pcd = r * 1.47;
                int n = Catalogo.JmParafusosQtd(dn);
                double rp = Catalogo.JmBitola(dn) * 0.5 * Mm;
                double porca = Catalogo.JmBitola(dn) * 0.8 * Mm;

                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * gt), rF * 0.97));
                Geometria.Unir(corpo, Geometria.Cilindro(
                    q.W(0, 0, z + sinal * gt), q.W(0, 0, z + sinal * (gt + ft)), rF));
                Geometria.Unir(corpo, Geometria.Cilindro(
                    q.W(0, 0, z + sinal * (gt + ft)), q.W(0, 0, z + sinal * (gt + ft + bolsa)), rb));
                double fase = Math.PI / n;
                for (int k = 0; k < n; k++)
                {
                    double a = fase + 2 * Math.PI * k / n;
                    double x = Math.Cos(a) * pcd, y = Math.Sin(a) * pcd;
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        q.W(x, y, z - sinal * porca), q.W(x, y, z + sinal * (gt + ft + 6 * Mm)), rp));
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        q.W(x, y, z - sinal * porca), q.W(x, y, z), rp * 1.7));
                }
                FurarSolido(corpo, Geometria.Cilindro(
                    q.W(0, 0, z - sinal * 1 * Mm), q.W(0, 0, z + sinal * (gt + ft + bolsa)), r * 0.97));
            }
            else if (e == Extremidade.Bolsa)
            {
                double len = Catalogo.BolsaMm(dn) * Mm;
                double rb = r * 1.28;
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * len), rb));
                double anel = 10 * Mm;
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * anel), rb * 1.07));
                // interior da bolsa (o fundo da bolsa e o ombro entre 0,97 r e a parede do corpo)
                FurarSolido(corpo, Geometria.Cilindro(
                    q.W(0, 0, z - sinal * 1 * Mm), q.W(0, 0, z + sinal * len), r * 0.97));
            }
            else if (e == Extremidade.Eletrofusao)
            {
                // Manga de eletrofusao (PEAD): bolsa lisa com anel na boca e terminal de ligacao no meio.
                double len = ComprimentoFim(e, dn) * Mm;
                double rb = r * 1.2;
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * len), rb));
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * 8 * Mm), rb * 1.06));
                double rt = Math.Max(5 * Mm, r * 0.06), ht = Math.Max(14 * Mm, r * 0.22);
                double zt = z + sinal * len * 0.5;
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, rb * 0.9, zt), q.W(0, rb + ht * 0.6, zt), rt * 1.7));
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, rb + ht * 0.6 - 1 * Mm, zt), q.W(0, rb + ht * 1.4, zt), rt));
                FurarSolido(corpo, Geometria.Cilindro(
                    q.W(0, 0, z - sinal * 1 * Mm), q.W(0, 0, z + sinal * len), r * 0.99));
            }
            else if (e == Extremidade.Termofusao)
            {
                // Ponta lisa de termofusao (solda de topo, PEAD): trecho reto do mesmo diametro e cordao de solda.
                double len = ComprimentoFim(e, dn) * Mm;
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * len), r));
                double cd = Math.Max(2 * Mm, r * 0.06);
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * 7 * Mm), r + cd));
            }
            else if (e == Extremidade.Solda)
            {
                // Extremidade soldada (rede em aluminio): mesmo diametro, so o cordao de solda.
                double cd = Math.Max(2 * Mm, r * 0.06);
                Geometria.Unir(corpo, Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * 6 * Mm), r + cd));
            }
            else if (e == Extremidade.Flange)
            {
                double t = Catalogo.FlangeEspessuraMm(dn) * Mm;
                double ro = Catalogo.FlangeDiametroMm(dn) / 2.0 * Mm;
                double pcd = Catalogo.FlangeCirculoMm(dn) / 2.0 * Mm;
                double rf = Catalogo.FuroMm(dn) / 2.0 * Mm;
                int n = Catalogo.ParafusosQtd(dn);

                Solid3d disco = Geometria.Cilindro(q.W(0, 0, z), q.W(0, 0, z + sinal * t), ro);
                double fase = Math.PI / n;
                for (int k = 0; k < n; k++)
                {
                    double a = fase + 2 * Math.PI * k / n;
                    double x = Math.Cos(a) * pcd, y = Math.Sin(a) * pcd;
                    Geometria.Subtrair(disco, Geometria.Cilindro(
                        q.W(x, y, z - sinal * 1 * Mm), q.W(x, y, z + sinal * (t + 1 * Mm)), rf));
                }
                Geometria.Unir(corpo, disco);
                // pescoco (hub) apos a flange, em direcao ao corpo
                if (cubo)
                    Geometria.Unir(corpo, Geometria.Cilindro(
                        q.W(0, 0, z + sinal * t), q.W(0, 0, z + sinal * (t + 0.9 * t)), r * 1.22));
            }
        }
    }
}
