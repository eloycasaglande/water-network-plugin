using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RedesAgua
{
    // Parametros da peca mostrada na previa 3D do dialogo (nomes de extremidade como no dialogo).
    public class ParamPreview
    {
        public string Familia = "RETA";
        public int DN = 100, DN2 = 100, DN3;
        public string EIn = "Flange", EOut = "Flange", EBr = "Flange", EBrOut = "Flange";
        public string Direcao = "Cima";
        public double Angulo = 90, Comprimento = 300;

        public string Chave()
        {
            return string.Join("|", Familia, DN, DN2, DN3, EIn, EOut, EBr, EBrOut, Direcao,
                Angulo.ToString("0.##"), Comprimento.ToString("0.#"));
        }
    }

    public struct V
    {
        public double X, Y, Z;
        public V(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V operator +(V a, V b) { return new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V operator -(V a, V b) { return new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V operator *(V a, double k) { return new V(a.X * k, a.Y * k, a.Z * k); }
        public static V operator -(V a) { return new V(-a.X, -a.Y, -a.Z); }
        public double Dot(V b) { return X * b.X + Y * b.Y + Z * b.Z; }
        public V Cross(V b) { return new V(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X); }
        public double Len { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public V Norm() { double l = Len; return l < 1e-12 ? new V(0, 0, 1) : new V(X / l, Y / l, Z / l); }
    }

    // Malha de triangulos com cor por triangulo e normais por vertice, desenhada por um
    // rasterizador proprio (z-buffer + sombreamento Gouraud + perspectiva leve).
    public sealed class Malha
    {
        struct Vt { public float X, Y, Z, NX, NY, NZ; }
        readonly List<Vt> vs = new List<Vt>();
        readonly List<int> idx = new List<int>();
        readonly List<int> cores = new List<int>();

        public static readonly int CAzul = Color.FromArgb(40, 104, 186).ToArgb();
        public static readonly int CAzulEscuro = Color.FromArgb(28, 80, 156).ToArgb();
        public static readonly int CCinza = Color.FromArgb(178, 181, 190).ToArgb();
        public static readonly int CEscuro = Color.FromArgb(22, 30, 46).ToArgb();
        public static readonly int CLaranja = Color.FromArgb(234, 132, 30).ToArgb();
        public static readonly int CFerro = Color.FromArgb(122, 128, 140).ToArgb();
        public static readonly int CFerroEscuro = Color.FromArgb(88, 94, 106).ToArgb();
        public static readonly int CVermelho = Color.FromArgb(198, 44, 44).ToArgb();

        public int Triangulos { get { return idx.Count / 3; } }

        // Troca uma cor por outra em todos os triangulos (ex.: peca de ferro cinza em vez de azul).
        public void Trocar(int de, int para)
        {
            for (int i = 0; i < cores.Count; i++)
                if (cores[i] == de) cores[i] = para;
        }

        void Tri(V a, V b, V c, V na, V nb, V nc, int cor)
        {
            int i = vs.Count;
            vs.Add(new Vt { X = (float)a.X, Y = (float)a.Y, Z = (float)a.Z, NX = (float)na.X, NY = (float)na.Y, NZ = (float)na.Z });
            vs.Add(new Vt { X = (float)b.X, Y = (float)b.Y, Z = (float)b.Z, NX = (float)nb.X, NY = (float)nb.Y, NZ = (float)nb.Z });
            vs.Add(new Vt { X = (float)c.X, Y = (float)c.Y, Z = (float)c.Z, NX = (float)nc.X, NY = (float)nc.Y, NZ = (float)nc.Z });
            idx.Add(i); idx.Add(i + 1); idx.Add(i + 2);
            cores.Add(cor);
        }

        void Quad(V a, V b, V c, V d, V na, V nb, V nc, V nd, int cor)
        {
            Tri(a, b, c, na, nb, nc, cor);
            Tri(a, c, d, na, nc, nd, cor);
        }

        public static V Perp(V a)
        {
            V r = Math.Abs(a.Y) < 0.9 ? new V(0, 1, 0) : new V(1, 0, 0);
            return a.Cross(r).Norm();
        }

        // Cilindro/tronco de cone entre p0 e p1. plano = true: faces planas (porcas sextavadas).
        public void Cil(V p0, V p1, double r0, double r1, int cor, bool cap0 = true, bool cap1 = true, int seg = 28, bool plano = false)
        {
            V ax = p1 - p0;
            double len = ax.Len;
            if (len < 1e-9) return;
            V A = ax * (1.0 / len), U = Perp(A), W = A.Cross(U);
            double slope = (r0 - r1) / len;
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                V d0 = U * Math.Cos(a0) + W * Math.Sin(a0), d1 = U * Math.Cos(a1) + W * Math.Sin(a1);
                V n0 = (d0 + A * slope).Norm(), n1 = (d1 + A * slope).Norm();
                if (plano) { n0 = n1 = ((d0 + d1) * 0.5 + A * slope).Norm(); }
                Quad(p0 + d0 * r0, p0 + d1 * r0, p1 + d1 * r1, p1 + d0 * r1, n0, n1, n1, n0, cor);
                if (cap0 && r0 > 1e-9) Tri(p0, p0 + d1 * r0, p0 + d0 * r0, -A, -A, -A, cor);
                if (cap1 && r1 > 1e-9) Tri(p1, p1 + d0 * r1, p1 + d1 * r1, A, A, A, cor);
            }
        }

        public void Cil(V p0, V p1, double r, int cor, bool cap0 = true, bool cap1 = true, int seg = 28, bool plano = false)
        {
            Cil(p0, p1, r, r, cor, cap0, cap1, seg, plano);
        }

        // Anel (cilindro vazado) entre p0 e p1.
        public void Anel(V p0, V p1, double rin, double rout, int cor, int seg = 32)
        {
            Cil(p0, p1, rout, rout, cor, false, false, seg);
            Cil(p0, p1, rin, rin, cor, false, false, seg);
            V A = (p1 - p0).Norm(), U = Perp(A), W = A.Cross(U);
            for (int i = 0; i < seg; i++)
            {
                double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                V d0 = U * Math.Cos(a0) + W * Math.Sin(a0), d1 = U * Math.Cos(a1) + W * Math.Sin(a1);
                Quad(p0 + d0 * rin, p0 + d0 * rout, p0 + d1 * rout, p0 + d1 * rin, -A, -A, -A, -A, cor);
                Quad(p1 + d0 * rin, p1 + d1 * rin, p1 + d1 * rout, p1 + d0 * rout, A, A, A, A, cor);
            }
        }

        // Meia elipsoide (domo): base de raio r em c, altura alt ao longo de eixo.
        public void Domo(V c, V eixo, double r, double alt, int cor, int aneis = 8, int seg = 28)
        {
            V A = eixo.Norm(), U = Perp(A), W = A.Cross(U);
            for (int k = 0; k < aneis; k++)
            {
                double t0 = Math.PI / 2 * k / aneis, t1 = Math.PI / 2 * (k + 1) / aneis;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    V d0 = U * Math.Cos(a0) + W * Math.Sin(a0), d1 = U * Math.Cos(a1) + W * Math.Sin(a1);
                    V p00 = c + A * (alt * Math.Cos(t0)) + d0 * (r * Math.Sin(t0));
                    V p01 = c + A * (alt * Math.Cos(t0)) + d1 * (r * Math.Sin(t0));
                    V p11 = c + A * (alt * Math.Cos(t1)) + d1 * (r * Math.Sin(t1));
                    V p10 = c + A * (alt * Math.Cos(t1)) + d0 * (r * Math.Sin(t1));
                    V n00 = (d0 * (Math.Sin(t0) * alt) + A * (Math.Cos(t0) * r)).Norm();
                    V n01 = (d1 * (Math.Sin(t0) * alt) + A * (Math.Cos(t0) * r)).Norm();
                    V n11 = (d1 * (Math.Sin(t1) * alt) + A * (Math.Cos(t1) * r)).Norm();
                    V n10 = (d0 * (Math.Sin(t1) * alt) + A * (Math.Cos(t1) * r)).Norm();
                    Quad(p00, p01, p11, p10, n00, n01, n11, n10, cor);
                }
            }
        }

        // Tubo varrido ao longo de uma curva plana: pts/tg = centros e tangentes; b = normal do plano da curva.
        public void Varredura(List<V> pts, List<V> tg, V b, double r, int cor, int seg = 28)
        {
            for (int s = 0; s + 1 < pts.Count; s++)
            {
                V n0 = b.Cross(tg[s]).Norm(), n1 = b.Cross(tg[s + 1]).Norm();
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    V d00 = n0 * Math.Cos(a0) + b * Math.Sin(a0), d01 = n0 * Math.Cos(a1) + b * Math.Sin(a1);
                    V d10 = n1 * Math.Cos(a0) + b * Math.Sin(a0), d11 = n1 * Math.Cos(a1) + b * Math.Sin(a1);
                    Quad(pts[s] + d00 * r, pts[s] + d01 * r, pts[s + 1] + d11 * r, pts[s + 1] + d10 * r, d00, d01, d11, d10, cor);
                }
            }
        }

        public void Caixa(V c, V ex, V ey, V ez, double hx, double hy, double hz, int cor)
        {
            V[] ax = { ex.Norm(), ey.Norm(), ez.Norm() };
            double[] h = { hx, hy, hz };
            for (int k = 0; k < 3; k++)
            {
                V n = ax[k], u = ax[(k + 1) % 3], w = ax[(k + 2) % 3];
                double hu = h[(k + 1) % 3], hw = h[(k + 2) % 3];
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    V cc = c + n * (sg * h[k]), nn = n * sg;
                    Quad(cc - u * hu - w * hw, cc + u * hu - w * hw, cc + u * hu + w * hw, cc - u * hu + w * hw, nn, nn, nn, nn, cor);
                }
            }
        }

        // ---------------------------------------------------------------- renderizacao

        public Bitmap Renderizar(int w, int h, double yaw, double pitch, double zoom)
        {
            int n = vs.Count;
            var pix = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                double k = h > 1 ? (double)y / (h - 1) : 0;
                int r = (int)(250 - 22 * k), g = (int)(251 - 18 * k), b = (int)(254 - 12 * k);
                int c = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                for (int x = 0; x < w; x++) pix[y * w + x] = c;
            }
            if (n == 0) return Montar(pix, w, h);

            double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
            foreach (Vt v in vs)
            {
                minx = Math.Min(minx, v.X); maxx = Math.Max(maxx, v.X);
                miny = Math.Min(miny, v.Y); maxy = Math.Max(maxy, v.Y);
                minz = Math.Min(minz, v.Z); maxz = Math.Max(maxz, v.Z);
            }
            double cx = (minx + maxx) / 2, cy = (miny + maxy) / 2, cz = (minz + maxz) / 2;
            double rad = Math.Max(1e-6, Math.Sqrt((maxx - minx) * (maxx - minx) + (maxy - miny) * (maxy - miny) + (maxz - minz) * (maxz - minz)) / 2);
            double dist = 3.4 * rad;
            double cyw = Math.Cos(yaw), syw = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);

            var px = new double[n]; var py = new double[n]; var iz = new double[n];
            var li = new double[n]; var ls = new double[n];
            double[] gx = new double[n], gy = new double[n];
            double fx0 = double.MaxValue, fx1 = double.MinValue, fy0 = double.MaxValue, fy1 = double.MinValue;
            V L1 = new V(-0.45, 0.65, 0.62).Norm(), L2 = new V(0.7, -0.2, 0.5).Norm();
            V Hh = (L1 + new V(0, 0, 1)).Norm();
            for (int i = 0; i < n; i++)
            {
                Vt v = vs[i];
                double x = v.X - cx, y = v.Y - cy, z = v.Z - cz;
                double x1 = x * cyw + z * syw, z1 = -x * syw + z * cyw;
                double y2 = y * cp - z1 * sp, z2 = y * sp + z1 * cp;
                double d = dist - z2;
                gx[i] = x1 / d; gy[i] = y2 / d; iz[i] = 1.0 / d;
                fx0 = Math.Min(fx0, gx[i]); fx1 = Math.Max(fx1, gx[i]);
                fy0 = Math.Min(fy0, gy[i]); fy1 = Math.Max(fy1, gy[i]);
                double nx = v.NX, ny = v.NY, nz = v.NZ;
                double nx1 = nx * cyw + nz * syw, nz1 = -nx * syw + nz * cyw;
                double ny2 = ny * cp - nz1 * sp, nz2 = ny * sp + nz1 * cp;
                V nn = new V(nx1, ny2, nz2).Norm();
                if (nn.Z < 0) nn = -nn;
                li[i] = 0.30 + 0.72 * Math.Max(0, nn.Dot(L1)) + 0.18 * Math.Max(0, nn.Dot(L2));
                ls[i] = 0.30 * Math.Pow(Math.Max(0, nn.Dot(Hh)), 30);
            }
            double ex = Math.Max(1e-9, fx1 - fx0), ey = Math.Max(1e-9, fy1 - fy0);
            double f = zoom * 0.90 * Math.Min(w / ex, h / ey);
            double ox = w / 2.0 - f * (fx0 + fx1) / 2, oy = h / 2.0 + f * (fy0 + fy1) / 2;
            for (int i = 0; i < n; i++) { px[i] = ox + f * gx[i]; py[i] = oy - f * gy[i]; }

            var zb = new float[w * h];
            int nt = idx.Count / 3;
            for (int t = 0; t < nt; t++)
            {
                int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
                double x0 = px[a], y0 = py[a], x1 = px[b], y1 = py[b], x2 = px[c], y2 = py[c];
                double area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
                if (Math.Abs(area) < 1e-9) continue;
                double inv = 1.0 / area;
                int bx0 = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
                int bx1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
                int by0 = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
                int by1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
                int cor = cores[t];
                int cr = (cor >> 16) & 255, cg = (cor >> 8) & 255, cb = cor & 255;
                for (int y = by0; y <= by1; y++)
                {
                    double qy = y + 0.5;
                    for (int x = bx0; x <= bx1; x++)
                    {
                        double qx = x + 0.5;
                        double w0 = ((x1 - qx) * (y2 - qy) - (x2 - qx) * (y1 - qy)) * inv;
                        double w1 = ((x2 - qx) * (y0 - qy) - (x0 - qx) * (y2 - qy)) * inv;
                        double w2 = 1 - w0 - w1;
                        if (w0 < -1e-6 || w1 < -1e-6 || w2 < -1e-6) continue;
                        float z = (float)(w0 * iz[a] + w1 * iz[b] + w2 * iz[c]);
                        int p = y * w + x;
                        if (z <= zb[p]) continue;
                        zb[p] = z;
                        double I = w0 * li[a] + w1 * li[b] + w2 * li[c];
                        double S = (w0 * ls[a] + w1 * ls[b] + w2 * ls[c]) * 255;
                        int r = (int)Math.Min(255, cr * I + S), g = (int)Math.Min(255, cg * I + S), bl = (int)Math.Min(255, cb * I + S);
                        pix[p] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
                    }
                }
            }
            return Montar(pix, w, h);
        }

        static Bitmap Montar(int[] pix, int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pix, 0, bd.Scan0, pix.Length); }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }
    }

    // Monta a malha de cada peca (eixo do fluxo = +X, "cima" = +Y, direita = +Z), em mm.
    public static class Preview3D
    {
        static readonly V X = new V(1, 0, 0), Y = new V(0, 1, 0), Z = new V(0, 0, 1);

        static double De(int dn) { return Catalogo.DiametroExternoMm(dn) / 2.0; }

        static double FimLen(string e, int dn)
        {
            switch (e)
            {
                case "Flange": return Catalogo.FlangeEspessuraMm(dn);
                case "Bolsa": return Catalogo.BolsaMm(dn);
                case "Junta mecânica": return Catalogo.JmContraflangeMm + Catalogo.JmFlangeMm + 0.8 * Catalogo.BolsaMm(dn);
                case "Eletrofusão": return Math.Round(40 + 0.35 * dn);
                case "Termofusão": return Math.Max(50.0, Math.Round(0.4 * dn));
                default: return 0;
            }
        }

        // Comprimento realmente ocupado (flange inclui o pescoco).
        static double CorpoLen(string e, int dn)
        {
            return e == "Flange" ? Catalogo.FlangeEspessuraMm(dn) * 1.9 : FimLen(e, dn);
        }

        static V DirDe(string d)
        {
            switch (d)
            {
                case "Baixo": return -Y;
                case "Direita": return Z;
                case "Esquerda": return -Z;
                default: return Y;
            }
        }

        // Extremidade em P; A = direcao para dentro da peca.
        static void Fim(Malha m, string tipo, V P, V A, int dn, bool pescoco = true)
        {
            double r = De(dn);
            A = A.Norm();
            V U = Malha.Perp(A), W = A.Cross(U);
            switch (tipo)
            {
                case "Flange":
                {
                    double t = Catalogo.FlangeEspessuraMm(dn), ro = Catalogo.FlangeDiametroMm(dn) / 2.0;
                    double pcd = Catalogo.FlangeCirculoMm(dn) / 2.0, rf = Catalogo.FuroMm(dn) / 2.0;
                    int n = Catalogo.ParafusosQtd(dn);
                    m.Cil(P, P + A * t, ro, Malha.CAzulEscuro, true, true, 48);
                    for (int k = 0; k < n; k++)
                    {
                        double a = Math.PI / n + 2 * Math.PI * k / n;
                        V c = P + U * (Math.Cos(a) * pcd) + W * (Math.Sin(a) * pcd);
                        m.Cil(c - A * 0.3, c, rf, Malha.CEscuro, true, false, 12);
                    }
                    m.Cil(P - A * 0.3, P, r * 0.97, Malha.CEscuro, true, false, 28);
                    if (pescoco) m.Cil(P + A * t, P + A * (1.9 * t), r * 1.22, r * 1.05, Malha.CAzul);
                    break;
                }
                case "Bolsa":
                {
                    double len = Catalogo.BolsaMm(dn), rb = r * 1.28;
                    m.Cil(P, P + A * len, rb, Malha.CAzul);
                    m.Cil(P, P + A * 10, rb * 1.07, Malha.CAzulEscuro);
                    m.Cil(P - A * 0.3, P, r * 0.97, Malha.CEscuro, true, false, 28);
                    break;
                }
                case "Junta mecânica":
                {
                    double gt = Catalogo.JmContraflangeMm, ft = Catalogo.JmFlangeMm, bolsa = Catalogo.BolsaMm(dn) * 0.8;
                    double rb = r * 1.28, rF = r * 1.62, pcd = r * 1.47;
                    int n = Catalogo.JmParafusosQtd(dn);
                    double rp = Catalogo.JmBitola(dn) * 0.5, porca = Catalogo.JmBitola(dn) * 0.8;
                    m.Cil(P, P + A * gt, rF * 0.97, Malha.CAzulEscuro, true, true, 40);
                    m.Cil(P + A * gt, P + A * (gt + ft), rF, Malha.CAzul, true, true, 40);
                    m.Cil(P + A * (gt + ft), P + A * (gt + ft + bolsa), rb, Malha.CAzul);
                    for (int k = 0; k < n; k++)
                    {
                        double a = Math.PI / n + 2 * Math.PI * k / n;
                        V c = U * (Math.Cos(a) * pcd) + W * (Math.Sin(a) * pcd);
                        m.Cil(P + c - A * porca, P + c + A * (gt + ft + 6), rp, Malha.CCinza, true, true, 10);
                        m.Cil(P + c - A * porca, P + c, rp * 1.7, Malha.CCinza, true, true, 6, true);
                    }
                    m.Cil(P - A * 0.3, P, r * 0.97, Malha.CEscuro, true, false, 28);
                    break;
                }
                case "Eletrofusão":
                {
                    double len = FimLen(tipo, dn), rb = r * 1.2;
                    m.Cil(P, P + A * len, rb, Malha.CLaranja);
                    m.Cil(P, P + A * 8, rb * 1.06, Malha.CLaranja);
                    double rt = Math.Max(5, r * 0.06), ht = Math.Max(14, r * 0.22);
                    V pm = P + A * (len * 0.5);
                    m.Cil(pm + U * (rb * 0.9), pm + U * (rb + ht * 0.6), rt * 1.7, Malha.CEscuro, true, true, 14);
                    m.Cil(pm + U * (rb + ht * 0.6), pm + U * (rb + ht * 1.4), rt, Malha.CCinza, true, true, 10);
                    m.Cil(P - A * 0.3, P, r * 0.99, Malha.CEscuro, true, false, 28);
                    break;
                }
                case "Termofusão":
                {
                    double len = FimLen(tipo, dn), cd = Math.Max(2, r * 0.06);
                    m.Cil(P, P + A * len, r, Malha.CAzul);
                    m.Cil(P, P + A * 7, r + cd, Malha.CLaranja);
                    break;
                }
                case "Soldada":
                {
                    double cd = Math.Max(2, r * 0.06);
                    m.Cil(P, P + A * 6, r + cd, Malha.CLaranja);
                    break;
                }
                default:
                    break;   // Ponta
            }
        }

        static void Tubo(Malha m, V a, V b, double r, int cor) { m.Cil(a, b, r, cor); }

        public static Malha Construir(ParamPreview p)
        {
            var m = new Malha();
            int dn = Catalogo.DnPadrao(p.DN), dn2 = Catalogo.DnPadrao(p.DN2);
            double r = De(dn), r2 = De(dn2);
            string fam = p.Familia;
            V O = new V(0, 0, 0);

            switch (fam)
            {
                case "TE":
                case "Y":
                {
                    double a = CorpoLen(p.EIn, dn), b = CorpoLen(p.EOut, dn);
                    double Lr = a + b + 3 * Math.Max(r, r2 * 1.5);
                    double xc = fam == "Y" ? Lr * 0.4 : Lr / 2;
                    Tubo(m, O, new V(Lr, 0, 0), r, Malha.CAzul);
                    m.Cil(new V(xc - r * 1.1, 0, 0), new V(xc + r * 1.1, 0, 0), r * 1.12, Malha.CAzul);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(Lr, 0, 0), -X, dn);
                    V D = DirDe(p.Direcao);
                    V bd = fam == "Y" ? (X + D).Norm() : D;
                    V C = new V(xc, 0, 0);
                    double L1 = CorpoLen(p.EBr, dn2) + (fam == "Y" ? 1.5 * r + 60 : r * 1.1 + 40);
                    V P1 = C + bd * L1;
                    m.Cil(C, P1, r2, Malha.CAzul);
                    Fim(m, p.EBr, P1, -bd, dn2);
                    if (fam == "TE" && p.DN3 > 0 && p.DN3 < dn2)
                    {
                        int dn3 = Catalogo.DnPadrao(p.DN3);
                        double r3 = De(dn3), lc = Math.Max(60, 3 * Math.Abs(r2 - r3));
                        if (p.EBr == "Flange") Fim(m, "Flange", P1, bd, dn2);
                        V P2 = P1 + bd * lc, P3 = P2 + bd * CorpoLen(p.EBrOut, dn3);
                        m.Cil(P1, P2, r2, r3, Malha.CAzul);
                        m.Cil(P2, P3, r3, Malha.CAzul);
                        Fim(m, p.EBrOut, P3, -bd, dn3);
                    }
                    break;
                }
                case "CURVA":
                case "JOELHO":
                {
                    double ang = p.Angulo * Math.PI / 180.0;
                    double R = fam == "CURVA" ? Math.Max(1.5 * dn, 100.0) : Math.Max(1.0 * dn, 60.0);
                    double a = CorpoLen(p.EIn, dn), b = CorpoLen(p.EOut, dn);
                    V D = DirDe(p.Direcao);
                    V P1 = new V(a, 0, 0);
                    V Cc = P1 + D * R;
                    V Bn = X.Cross(D).Norm();
                    var pts = new List<V>(); var tg = new List<V>();
                    int steps = Math.Max(6, (int)Math.Ceiling(p.Angulo / 4.0));
                    for (int i = 0; i <= steps; i++)
                    {
                        double th = ang * i / steps;
                        pts.Add(Cc + (-D * Math.Cos(th) + X * Math.Sin(th)) * R);
                        tg.Add((X * Math.Cos(th) + D * Math.Sin(th)).Norm());
                    }
                    Tubo(m, O, P1, r, Malha.CAzul);
                    m.Varredura(pts, tg, Bn, r, Malha.CAzul);
                    V Pe = pts[pts.Count - 1], T2 = tg[tg.Count - 1];
                    V Pf = Pe + T2 * b;
                    Tubo(m, Pe, Pf, r, Malha.CAzul);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, Pf, -T2, dn);
                    break;
                }
                case "REDUCAO":
                {
                    double a = CorpoLen(p.EIn, dn), b = CorpoLen(p.EOut, dn2);
                    double lc = Math.Max(60, 3 * Math.Abs(r - r2));
                    double L = a + lc + b;
                    Tubo(m, O, new V(a + 1, 0, 0), r, Malha.CAzul);
                    m.Cil(new V(a, 0, 0), new V(a + lc, 0, 0), r, r2, Malha.CAzul);
                    Tubo(m, new V(a + lc - 1, 0, 0), new V(L, 0, 0), r2, Malha.CAzul);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn2);
                    break;
                }
                case "REGISTRO":
                {
                    double L = Math.Max(Catalogo.RegistroComprimentoMm(dn), CorpoLen(p.EIn, dn) + CorpoLen(p.EOut, dn) + 30);
                    double xc = L / 2;
                    Tubo(m, O, new V(L, 0, 0), r * 1.1, Malha.CAzul);
                    m.Cil(new V(xc - r * 0.95, 0, 0), new V(xc + r * 0.95, 0, 0), r * 1.45, Malha.CAzul);
                    m.Cil(new V(xc, 0, 0), new V(xc, 1.7 * r, 0), r * 0.9, Malha.CAzul);
                    m.Cil(new V(xc, 1.5 * r, 0), new V(xc, 1.75 * r, 0), r * 1.2, Malha.CAzulEscuro, true, true, 36);
                    m.Cil(new V(xc, 1.75 * r, 0), new V(xc, 2.7 * r, 0), r * 0.75, Malha.CAzul);
                    m.Cil(new V(xc, 2.7 * r, 0), new V(xc, 2.95 * r, 0), r * 0.95, Malha.CAzulEscuro, true, true, 36);
                    m.Cil(new V(xc, 2.95 * r, 0), new V(xc, 3.9 * r, 0), r * 0.12, Malha.CCinza, true, true, 12);
                    V h0 = new V(xc, 3.55 * r, 0);
                    m.Anel(h0 - Y * (0.07 * r), h0 + Y * (0.07 * r), r * 0.9, r * 1.12, Malha.CVermelho, 40);
                    m.Caixa(h0, X, Y, Z, r * 1.0, 0.06 * r, 0.07 * r, Malha.CVermelho);
                    m.Caixa(h0, X, Y, Z, 0.07 * r, 0.06 * r, r * 1.0, Malha.CVermelho);
                    m.Cil(h0 - Y * (0.12 * r), h0 + Y * (0.12 * r), r * 0.2, Malha.CVermelho, true, true, 20);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn);
                    break;
                }
                case "BORBOLETA":
                {
                    double L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 0.85, CorpoLen(p.EIn, dn) + CorpoLen(p.EOut, dn) + 30);
                    double xc = L / 2, rBar = r * 1.4;
                    Tubo(m, O, new V(L, 0, 0), r * 1.12, Malha.CAzul);
                    m.Cil(new V(CorpoLen(p.EIn, dn), 0, 0), new V(L - CorpoLen(p.EOut, dn), 0, 0), rBar, Malha.CAzul);
                    double yN = rBar + r * 0.3;
                    m.Cil(new V(xc, 0, 0), new V(xc, yN, 0), r * 0.42, Malha.CAzul);
                    m.Cil(new V(xc, yN - 0.1 * r, 0), new V(xc, yN + 0.12 * r, 0), r * 0.55, Malha.CAzulEscuro);
                    double yH = yN + 0.12 * r;
                    m.Cil(new V(xc, yH, 0), new V(xc, yH + 0.4 * r, 0), r * 0.24, Malha.CCinza, true, true, 16);
                    V cm = new V(xc, yH + 0.4 * r, 0);
                    m.Caixa(cm + X * (0.3 * r), X, Y, Z, 1.5 * r, 0.1 * r, 0.25 * r, Malha.CVermelho);
                    m.Caixa(cm, X, Y, Z, 0.25 * r, 0.1 * r, 0.55 * r, Malha.CVermelho);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn);
                    break;
                }
                case "RETENCAO":
                {
                    double L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 1.1, CorpoLen(p.EIn, dn) + CorpoLen(p.EOut, dn) + 30);
                    double xc = L / 2;
                    Tubo(m, O, new V(L, 0, 0), r * 1.1, Malha.CAzul);
                    m.Cil(new V(CorpoLen(p.EIn, dn), 0, 0), new V(L - CorpoLen(p.EOut, dn), 0, 0), r * 1.4, Malha.CAzul);
                    m.Cil(new V(xc, 0, 0), new V(xc, 1.5 * r, 0), r * 1.05, Malha.CAzul);
                    m.Cil(new V(xc, 1.5 * r, 0), new V(xc, 1.75 * r, 0), r * 1.35, Malha.CAzulEscuro, true, true, 36);
                    for (int k = 0; k < 6; k++)
                    {
                        double a = 2 * Math.PI * k / 6;
                        V c = new V(xc + Math.Cos(a) * r * 1.1, 1.75 * r, Math.Sin(a) * r * 1.1);
                        m.Cil(c, c + Y * (0.2 * r), r * 0.12, Malha.CCinza, true, true, 6, true);
                    }
                    m.Domo(new V(xc, 1.75 * r, 0), Y, r * 1.0, r * 0.7, Malha.CAzul);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn);
                    break;
                }
                case "VRP":
                {
                    double L = Math.Max(Catalogo.RegistroComprimentoMm(dn) * 1.2, CorpoLen(p.EIn, dn) + CorpoLen(p.EOut, dn) + 30);
                    double xc = L / 2;
                    Tubo(m, O, new V(L, 0, 0), r * 1.1, Malha.CAzul);
                    m.Cil(new V(CorpoLen(p.EIn, dn), 0, 0), new V(L - CorpoLen(p.EOut, dn), 0, 0), r * 1.35, Malha.CAzul);
                    m.Cil(new V(xc, 0, 0), new V(xc, 1.6 * r, 0), r * 0.85, Malha.CAzul);
                    m.Cil(new V(xc, 1.6 * r, 0), new V(xc, 1.95 * r, 0), r * 1.9, Malha.CAzulEscuro, true, true, 40);
                    m.Domo(new V(xc, 1.95 * r, 0), Y, r * 1.8, r * 1.1, Malha.CAzul, 8, 36);
                    m.Cil(new V(xc, 3.0 * r, 0), new V(xc, 3.35 * r, 0), r * 0.15, Malha.CCinza, true, true, 12);
                    V a1 = new V(xc + r * 1.5, 1.7 * r, r * 0.9), a2 = new V(L - CorpoLen(p.EOut, dn) - r * 0.4, 1.7 * r, r * 0.9);
                    m.Cil(a1, a2, r * 0.1, Malha.CCinza, true, true, 10);
                    m.Cil(a2, new V(a2.X, r * 1.0, a2.Z), r * 0.1, Malha.CCinza, true, true, 10);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn);
                    break;
                }
                case "VENTOSA":
                {
                    string ent = p.EIn == "Ponta" ? "Flange" : p.EIn;
                    double rN = r * 1.1, rB = Math.Max(r * 1.25, 40), rF = rB * 1.45, tF = rB * 0.22;
                    double zT = CorpoLen(ent, dn);
                    double z1 = zT + rB * 0.6, z2 = z1 + rB * 2.4, z3 = z2 + tF * 1.6, z4 = z3 + rB * 0.35, z5 = z4 + rB * 0.3;
                    double rBoss = rB * 0.5;
                    Tubo(m, O, new V(0, zT, 0), rN, Malha.CAzul);
                    Fim(m, ent, O, Y, dn);
                    m.Cil(new V(0, zT - 1, 0), new V(0, z1, 0), rN, rB, Malha.CAzul);
                    m.Cil(new V(0, z1 - 1, 0), new V(0, z2 + 1, 0), rB, Malha.CAzul);
                    m.Cil(new V(0, z2, 0), new V(0, z3, 0), rF, Malha.CAzulEscuro, true, true, 40);
                    double rpb = rF * 0.07;
                    for (int k = 0; k < 6; k++)
                    {
                        double a = 2 * Math.PI * k / 6;
                        V c = new V(Math.Cos(a) * rF * 0.8, z3, Math.Sin(a) * rF * 0.8);
                        m.Cil(c, c + Y * (rpb * 1.4), rpb, Malha.CCinza, true, true, 6, true);
                    }
                    m.Cil(new V(0, z3 - 1, 0), new V(0, z4, 0), rF * 0.85, rBoss, Malha.CAzul);
                    m.Cil(new V(0, z4 - 1, 0), new V(0, z5, 0), rBoss, Malha.CAzul);
                    double rS = rB * 0.5, xS = rF + rS * 0.55;
                    double zS0 = z1 + rB * 0.7, zS1 = z2 - tF, tFs = tF * 0.8, zc = zS0 + (zS1 - zS0) * 0.35;
                    m.Cil(new V(0, zc, 0), new V(0, zc, xS), rS * 0.45, Malha.CAzul);
                    m.Cil(new V(0, zS0, xS), new V(0, zS1 + 1, xS), rS, Malha.CAzul);
                    m.Cil(new V(0, zS1, xS), new V(0, zS1 + tFs, xS), rS * 1.4, Malha.CAzulEscuro, true, true, 32);
                    m.Cil(new V(0, zS1 + tFs - 1, xS), new V(0, zS1 + tFs + rS * 0.45, xS), rS * 0.5, Malha.CAzul);
                    break;
                }
                case "CAP":
                {
                    string ent = p.EIn == "Ponta" ? "Bolsa" : p.EIn;
                    if (ent == "Flange")
                    {
                        double t = Catalogo.FlangeEspessuraMm(dn);
                        Fim(m, "Flange", O, X, dn, false);
                        m.Cil(new V(t - 0.1, 0, 0), new V(t * 1.15, 0, 0), r * 0.6, Malha.CAzul);
                    }
                    else
                    {
                        double ext = FimLen(ent, dn), fundo = Math.Max(14, r * 0.25);
                        m.Cil(O, new V(ext + fundo, 0, 0), r * 1.28, Malha.CAzul);
                        Fim(m, ent, O, X, dn);
                    }
                    break;
                }
                case "FLANGE":
                {
                    double t = Catalogo.FlangeEspessuraMm(dn), ro = Catalogo.FlangeDiametroMm(dn) / 2.0;
                    double pcd = Catalogo.FlangeCirculoMm(dn) / 2.0, rf = Catalogo.FuroMm(dn) / 2.0;
                    int n = Catalogo.ParafusosQtd(dn);
                    m.Anel(O, new V(t, 0, 0), r, ro, Malha.CAzulEscuro, 48);
                    V U = Malha.Perp(X), W = X.Cross(U);
                    for (int k = 0; k < n; k++)
                    {
                        double a = Math.PI / n + 2 * Math.PI * k / n;
                        V c = U * (Math.Cos(a) * pcd) + W * (Math.Sin(a) * pcd);
                        m.Cil(c - X * 0.3, c, rf, Malha.CEscuro, true, false, 12);
                        m.Cil(c + X * t, c + X * (t + 0.3), rf, Malha.CEscuro, false, true, 12);
                    }
                    break;
                }
                case "JM":
                {
                    double ext = Catalogo.JmContraflangeMm + Catalogo.JmFlangeMm + 0.8 * Catalogo.BolsaMm(dn);
                    double L = 2 * ext + 40;
                    Tubo(m, O, new V(L, 0, 0), r * 1.28, Malha.CAzul);
                    Fim(m, "Junta mecânica", O, X, dn);
                    Fim(m, "Junta mecânica", new V(L, 0, 0), -X, dn);
                    break;
                }
                case "LUVA":
                {
                    bool eletro = p.EIn == "Eletrofusão", termo = p.EIn == "Termofusão";
                    if (eletro)
                    {
                        double len = FimLen("Eletrofusão", dn), L = 2 * len + 6;
                        Fim(m, "Eletrofusão", O, X, dn);
                        Fim(m, "Eletrofusão", new V(L, 0, 0), -X, dn);
                    }
                    else if (termo)
                    {
                        double prof = Math.Round(40 + 0.35 * dn), L = 2 * prof + 6, rb = r * 1.1, cd = Math.Max(2, r * 0.06);
                        m.Cil(O, new V(L, 0, 0), rb, Malha.CAzul);
                        m.Cil(O, new V(7, 0, 0), rb + cd, Malha.CLaranja);
                        m.Cil(new V(L - 7, 0, 0), new V(L, 0, 0), rb + cd, Malha.CLaranja);
                        m.Cil(new V(-0.3, 0, 0), O, r * 0.99, Malha.CEscuro, true, false, 28);
                        m.Cil(new V(L, 0, 0), new V(L + 0.3, 0, 0), r * 0.99, Malha.CEscuro, false, true, 28);
                    }
                    else
                    {
                        double b = Catalogo.BolsaMm(dn), L = 2 * b + 6, rb = r * 1.28;
                        m.Cil(O, new V(L, 0, 0), rb, Malha.CAzul);
                        m.Cil(O, new V(10, 0, 0), rb * 1.07, Malha.CAzulEscuro);
                        m.Cil(new V(L - 10, 0, 0), new V(L, 0, 0), rb * 1.07, Malha.CAzulEscuro);
                        m.Cil(new V(L / 2 - 6, 0, 0), new V(L / 2 + 6, 0, 0), rb * 1.05, Malha.CAzulEscuro);
                        m.Cil(new V(-0.3, 0, 0), O, r * 0.97, Malha.CEscuro, true, false, 28);
                        m.Cil(new V(L, 0, 0), new V(L + 0.3, 0, 0), r * 0.97, Malha.CEscuro, false, true, 28);
                    }
                    break;
                }
                case "LUVA_LR":
                {
                    double b1 = Catalogo.BolsaMm(dn), b2 = Catalogo.BolsaMm(dn2);
                    double rb1 = r * 1.28, rb2 = r2 * 1.28, lc = Math.Max(40, Math.Abs(rb1 - rb2) * 1.2), L = b1 + lc + b2;
                    m.Cil(O, new V(b1, 0, 0), rb1, Malha.CAzul);
                    m.Cil(new V(b1 - 1, 0, 0), new V(b1 + lc + 1, 0, 0), rb1, rb2, Malha.CAzul);
                    m.Cil(new V(b1 + lc - 1, 0, 0), new V(L, 0, 0), rb2, Malha.CAzul);
                    m.Cil(O, new V(10, 0, 0), rb1 * 1.07, Malha.CAzulEscuro);
                    m.Cil(new V(L - 10, 0, 0), new V(L, 0, 0), rb2 * 1.07, Malha.CAzulEscuro);
                    m.Cil(new V(-0.3, 0, 0), O, r * 0.97, Malha.CEscuro, true, false, 28);
                    m.Cil(new V(L, 0, 0), new V(L + 0.3, 0, 0), r2 * 0.97, Malha.CEscuro, false, true, 28);
                    break;
                }
                case "JD":
                {
                    double tF = Catalogo.FlangeEspessuraMm(dn);
                    double L = Math.Max(p.Comprimento > 0 ? p.Comprimento : Catalogo.JdComprimentoMm(dn), Catalogo.JdMinimoMm(dn));
                    double pcd = Catalogo.FlangeCirculoMm(dn) / 2.0;
                    int nT = Catalogo.ParafusosQtd(dn);
                    double bit = Catalogo.ParafusosBitola(dn), rp = bit / 2;
                    double rb = Math.Max(Math.Min(r * 1.22, pcd - rp - 4), r * 1.04);
                    double rA = Math.Max(Math.Min(rb * 1.12, pcd - rp - 2), rb);
                    Tubo(m, O, new V(L, 0, 0), r, Malha.CAzul);
                    Fim(m, "Flange", O, X, dn, false);
                    Fim(m, "Flange", new V(L, 0, 0), -X, dn, false);
                    double ls = (L - 2 * tF) * 0.5, zL = L - tF - ls;
                    m.Cil(new V(zL, 0, 0), new V(L - tF + 1, 0, 0), rb, Malha.CAzul);
                    m.Cil(new V(zL, 0, 0), new V(zL + tF * 0.7, 0, 0), rA, Malha.CAzulEscuro, true, true, 36);
                    double porca = bit * 0.8, ponta = 4;
                    V U = Malha.Perp(X), W = X.Cross(U);
                    for (int k = 0; k < nT; k++)
                    {
                        double a = Math.PI / nT + 2 * Math.PI * k / nT;
                        V c = U * (Math.Cos(a) * pcd) + W * (Math.Sin(a) * pcd);
                        m.Cil(c - X * (porca + ponta), c + X * (L + porca + ponta), rp, Malha.CCinza, true, true, 10);
                        m.Cil(c - X * porca, c, rp * 1.7, Malha.CCinza, true, true, 6, true);
                        m.Cil(c + X * L, c + X * (L + porca), rp * 1.7, Malha.CCinza, true, true, 6, true);
                    }
                    m.Trocar(Malha.CAzul, Malha.CFerro);   // junta de desmontagem em ferro cinza
                    m.Trocar(Malha.CAzulEscuro, Malha.CFerroEscuro);
                    break;
                }
                default:   // RETA (peca reta, carretel, bolsa-flange, ponta-flange)
                {
                    double min = FimLen(p.EIn, dn) + FimLen(p.EOut, dn) + 30;
                    double L = Math.Max(p.Comprimento, min);
                    Tubo(m, O, new V(L, 0, 0), r, Malha.CAzul);
                    Fim(m, p.EIn, O, X, dn);
                    Fim(m, p.EOut, new V(L, 0, 0), -X, dn);
                    break;
                }
            }
            return m;
        }
    }
}
