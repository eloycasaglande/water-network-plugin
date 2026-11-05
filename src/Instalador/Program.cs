using System;
using System.IO;
using System.Reflection;

namespace Instalador
{
    // Instala (ou remove com /desinstalar) o plugin RedesAgua como "bundle" do AutoCAD:
    // %APPDATA%\Autodesk\ApplicationPlugins\RedesAgua.bundle  (carregado automaticamente ao abrir o AutoCAD 2025).
    static class Program
    {
        const string Versao = "16.0.0";

        const string PackageContents =
"<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
"<ApplicationPackage SchemaVersion=\"1.0\" AutoLoadVersion=\"1.0\" ProductType=\"Application\"\r\n" +
"    Name=\"RedesAgua\" Description=\"Redes de agua: tubos, curvas, pecas, quantitativo\"\r\n" +
"    AppVersion=\"" + Versao + "\" Author=\"RedesAgua\"\r\n" +
"    ProductCode=\"{6B2F1C0E-5B7A-4E0B-9A59-3D6E0C7A1F21}\" UpgradeCode=\"{0D8C2A44-91E3-4B6F-8C1A-52B7E9F3A610}\">\r\n" +
"  <CompanyDetails Name=\"RedesAgua\" />\r\n" +
"  <Components>\r\n" +
"    <RuntimeRequirements OS=\"Win64\" Platform=\"AutoCAD*\" SeriesMin=\"R25.0\" SeriesMax=\"R25.0\" />\r\n" +
"    <ComponentEntry AppName=\"RedesAgua\" Version=\"" + Versao + "\" ModuleName=\"./Contents/RedesAgua.dll\"\r\n" +
"        AppDescription=\"Redes de agua\" AppType=\".NET\" LoadOnAutoCADStartUp=\"True\" />\r\n" +
"  </Components>\r\n" +
"</ApplicationPackage>\r\n";

        static int Main(string[] args)
        {
            bool remover = args.Length > 0 &&
                (args[0].Equals("/desinstalar", StringComparison.OrdinalIgnoreCase) ||
                 args[0].Equals("-desinstalar", StringComparison.OrdinalIgnoreCase));
            int rc = 0;
            try
            {
                string pasta = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Autodesk", "ApplicationPlugins", "RedesAgua.bundle");

                if (remover)
                {
                    if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
                    Console.WriteLine("Plugin RedesAgua removido.");
                }
                else
                {
                    string contents = Path.Combine(pasta, "Contents");
                    Directory.CreateDirectory(contents);
                    File.WriteAllText(Path.Combine(pasta, "PackageContents.xml"), PackageContents);

                    using (Stream ent = Assembly.GetExecutingAssembly().GetManifestResourceStream("RedesAgua.dll"))
                    using (FileStream sai = File.Create(Path.Combine(contents, "RedesAgua.dll")))
                    {
                        if (ent == null) throw new InvalidOperationException("DLL nao encontrada no instalador.");
                        ent.CopyTo(sai);
                    }
                    Console.WriteLine("Plugin RedesAgua instalado em:");
                    Console.WriteLine("  " + pasta);
                    Console.WriteLine();
                    Console.WriteLine("Feche e abra o AutoCAD 2025. Os comandos AGUA_VISTA, AGUA_REDE,");
                    Console.WriteLine("AGUA_CONTINUAR, AGUA_ROTULO e AGUA_QUANT carregam sozinhos.");
                    Console.WriteLine("(Se preferir, use NETLOAD com a DLL da pasta Contents.)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro: " + ex.Message);
                if (ex is IOException)
                    Console.WriteLine("Feche o AutoCAD e tente de novo.");
                rc = 1;
            }
            Console.WriteLine();
            Console.WriteLine("Pressione uma tecla para sair...");
            try { Console.ReadKey(true); } catch (InvalidOperationException) { }
            return rc;
        }
    }
}
