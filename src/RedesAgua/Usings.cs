// Namespaces da API do CAD: ZWCAD (padrao) ou AutoCAD (simbolo de compilacao ACAD).
#if ACAD
global using Autodesk.AutoCAD.ApplicationServices;
global using Autodesk.AutoCAD.DatabaseServices;
global using Autodesk.AutoCAD.EditorInput;
global using Autodesk.AutoCAD.Geometry;
global using Autodesk.AutoCAD.Runtime;
global using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#else
global using ZwSoft.ZwCAD.ApplicationServices;
global using ZwSoft.ZwCAD.DatabaseServices;
global using ZwSoft.ZwCAD.EditorInput;
global using ZwSoft.ZwCAD.Geometry;
global using ZwSoft.ZwCAD.Runtime;
global using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif
