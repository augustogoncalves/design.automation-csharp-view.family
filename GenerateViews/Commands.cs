using Autodesk.Revit.Addin.ExportViewSelector;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DesignAutomationFramework;
using System.Collections.Generic;

namespace GenerateViews
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Commands : IExternalDBApplication
    {
        #region Startup/Event/Shudown
        public ExternalDBApplicationResult OnStartup(Autodesk.Revit.ApplicationServices.ControlledApplication app)
        {
            DesignAutomationBridge.DesignAutomationReadyEvent += HandleDesignAutomationReadyEvent;

            return ExternalDBApplicationResult.Succeeded;
        }

        public void HandleDesignAutomationReadyEvent(object sender, DesignAutomationReadyEventArgs e)
        {
            e.Succeeded = GenerateViews(e.DesignAutomationData.RevitApp, e.DesignAutomationData.RevitDoc);
        }

        public ExternalDBApplicationResult OnShutdown(Autodesk.Revit.ApplicationServices.ControlledApplication app)
        {
            return ExternalDBApplicationResult.Succeeded;
        }
        #endregion

        private const string RFA_FILE = "family.rfa";

        private bool GenerateViews(Application revitApp, Document revitDoc)
        {
            if (revitDoc.IsFamilyDocument) { LogTrace("Document must be a project"); return false; }

            // store that for later...
            View3D view3d = Get3dView(revitDoc);

            // load family and create instances for all symbols and types
            using (Transaction trans = new Transaction(revitDoc))
            {
                trans.Start("Load family and create instances");

                Family theFamily = null;
                if (!revitDoc.LoadFamily(RFA_FILE, out theFamily)) { LogTrace("Cannot load family"); return false; }

                // just to avoid overriding them, let's adjust the X position
                double x = 0;
                int count = 1;

                // get all symbols and iterate
                ISet<ElementId> symbols = theFamily.GetFamilySymbolIds();
                List<int> ids = new List<int>();
                Dictionary<string, ElementId> viewsIds = new Dictionary<string, ElementId>();
                foreach (ElementId symbolId in symbols)
                {
                    FamilySymbol symbol = (FamilySymbol)revitDoc.GetElement(symbolId);
                    symbol.Activate();
                    if (ids.Contains(symbol.GetTypeId().IntegerValue)) continue;
                    FamilyInstance newInstance = revitDoc.Create.NewFamilyInstance(new XYZ(x, 0, 0), symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                    ICollection<ElementId> types = newInstance.GetValidTypes();
                    ElementId thisType = newInstance.GetTypeId();
                    revitDoc.Regenerate();
                    double displacement = newInstance.get_BoundingBox(revitDoc.ActiveView).Max.X - newInstance.get_BoundingBox(revitDoc.ActiveView).Min.X;
                    x += displacement * 2;
                    ids.Add(thisType.IntegerValue);

                    // keep track of elements and respective view where it should go...
                    viewsIds.Add(string.Format("{0} - Symbol {1} Type {2}", ++count, symbol.Name, newInstance.Name), newInstance.Id);

                    // now get all types for each symbol
                    foreach (ElementId type in types)
                    {
                        //if (type.IntegerValue == thisType.IntegerValue) continue;
                        if (ids.Contains(type.IntegerValue)) continue;
                        FamilyInstance duplicatedInstance = revitDoc.Create.NewFamilyInstance(new XYZ(x, 0, 0), symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        x += displacement * 2;
                        duplicatedInstance.ChangeTypeId(type);

                        // keep track of elements and respective view where it should go...
                        viewsIds.Add(string.Format("{0} - Symbol {1} Type {2}", ++count, symbol.Name, duplicatedInstance.Name), duplicatedInstance.Id);
                    }
                }

                // create a list of all elements
                List<ElementId> allIds = new List<ElementId>();
                foreach (KeyValuePair<string, ElementId> view in viewsIds) allIds.Add(view.Value);
              

                // prepare to create 
                foreach (KeyValuePair<string, ElementId> view in viewsIds)
                {
                    // create the view for this type
                    View3D newView = View3D.CreateIsometric(revitDoc, view3d.GetTypeId());
                    newView.ViewTemplateId = Get3dViewTemplate(revitDoc).Id;
                    newView.Name = view.Key;
                    revitDoc.Regenerate();

                    // apply filter
                    List<ElementId> copy = new List<ElementId>();
                    copy.AddRange(allIds.ToArray());
                    copy.Remove(view.Value);
                    SelectionFilterElement filter = SelectionFilterElement.Create(revitDoc, view.Key);
                    filter.SetElementIds(copy);
                    newView.AddFilter(filter.Id);
                    newView.SetFilterVisibility(filter.Id, false);
                }
                trans.Commit();
            }

            using (Transaction trans = new Transaction(revitDoc))
            {
                trans.Start("Delete default 3d view");
                revitDoc.Delete(view3d.Id); // default 3d view
                trans.Commit();
            }



            return true;
        }

        // From https://thebuildingcoder.typepad.com/blog/2011/09/activate-a-3d-view.html
        View3D Get3dView(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(View3D));
            foreach (View3D v in collector) if (!v.IsTemplate) return v;
            return null;
        }
        View3D Get3dViewTemplate(Document doc)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(View3D));
            foreach (View3D v in collector) if (v.IsTemplate) return v;
            return null;
        }

        private static void LogTrace(string format, params object[] args) { System.Console.WriteLine(format, args); }
    }
}
