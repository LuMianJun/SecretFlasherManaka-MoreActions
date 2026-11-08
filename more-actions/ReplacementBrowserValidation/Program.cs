using SFM.Actions;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var selection=new ReplacementSelection();var catalog=new ReplacementCatalog(selection);
var values=new[]{
    new ReplacementOption("A.walk","Walk","包 A","walk"),
    new ReplacementOption("A.idle1","Idle","包 A","a-idle"),
    new ReplacementOption("A.idle2","Idle","包 A","b-idle"),
    new ReplacementOption("B.idle","Idle","包 B","idle"),
    new ReplacementOption("B.pose","Pose","包 B","pose")};
catalog.Update(values);
Check(catalog.Options.Count==5&&catalog.Packs.Length==2,"Catalog retains variants and pack boundaries");
Check(catalog.Slots().Length==3&&catalog.Slots("包 A").Length==2,"By-animation view deduplicates native slots across packs");
Check(catalog.Variants("Idle").Length==3,"One slot selector exposes every available source");
Check(catalog.Choose("A.idle2",true)&&catalog.Selected("Idle")=="A.idle2","Pack checkbox selects a source");
Check(catalog.EnabledInPack("包 A")==1,"Pack count tracks selected slots, not checked duplicates");
Check(catalog.ApplyPack("包 A")==1&&catalog.Selected("Idle")=="A.idle2"&&catalog.Selected("Walk")=="A.walk","Applying a pack retains a deliberate within-pack variant");
Check(catalog.ApplyPack("包 A")==0,"Repeated bulk application is idempotent");
Check(catalog.ApplyPack("包 B")==2&&catalog.Selected("Idle")=="B.idle"&&catalog.Selected("Walk")=="A.walk","Applying another pack replaces its overlaps without clearing unrelated slots");
Check(!catalog.Choose("A.idle2",false)&&catalog.Selected("Idle")=="B.idle","Unchecking an inactive variant cannot remove another pack's active source");
Check(catalog.ClearPack("包 A")==1&&catalog.Selected("Idle")=="B.idle"&&catalog.Selected("Pose")=="B.pose","Clearing a pack withdraws only the slots it currently owns");
Check(catalog.ClearPack("包 A")==0,"Repeated clear is idempotent");
Check(catalog.Original("Idle")&&catalog.Selected("Idle")==null,"Selecting Original removes exactly one slot override");
Check(!catalog.Original("Idle")&&!catalog.Choose("missing",true),"Original and stale row callbacks do not create phantom selections");
Check(catalog.ApplyPack("unknown")==0&&catalog.ClearPack("unknown")==0,"Missing packs have no side effects");
int revision=catalog.Revision;catalog.Update(values.Reverse());
Check(catalog.Revision==revision,"Load order changes alone do not rebuild the browser");
catalog.ApplyPack("包 A");
Check(catalog.Selected("Idle")=="A.idle1","Unconfigured duplicate sources use the first displayed source deterministically");
var saved=selection.Serialize();var reloaded=new ReplacementSelection();reloaded.Load(saved);
var restored=new ReplacementCatalog(reloaded);restored.Update(values);
Check(restored.Selected("Idle")=="A.idle1"&&restored.Selected("Pose")=="B.pose","Browser changes retain the existing slot-to-ID persistence format");
restored.Update(values.Where(o=>o.Pack!="包 A"));
Check(restored.Options.Count==2&&restored.Selected("Idle")=="A.idle1","Temporarily missing packs retain their saved source IDs");
restored.Update(values);Check(restored.EnabledInPack("包 A")==2,"Returning packs recover their selected indicators");
var browser=new ReplacementBrowserModel();browser.Synchronize(catalog);browser.Pack("包 A");
Check(browser.VisibleOptions(catalog).All(o=>o.Pack=="包 A"),"Pack view only lists the chosen pack");
browser.Mode(true);
Check(browser.VisibleSlots(catalog).Contains("Pose")&&browser.VisibleSlots(catalog).Length==3,"Slot mode ignores the remembered pack and includes every native slot");
browser.Mode(false);Check(browser.SelectedPack=="包 A","Returning to pack mode restores the remembered pack");
browser.Filter(true);Check(browser.VisibleOptions(catalog).All(o=>catalog.Selected(o.Slot)==o.Id),"Selected-only pack filter reflects actual choices");
browser.Mode(true);catalog.Original("Walk");
Check(!browser.VisibleSlots(catalog).Contains("Walk")&&browser.VisibleSlots(catalog).Contains("Pose"),"Selected-only slot filter remains global across packs");
int browserRevision=browser.Revision;browser.Mode(true);browser.Filter(true);
Check(browser.Revision==browserRevision,"Unchanged browser state does not force a rebuild");
Check(selection.Remap(id=>id=="A.idle1"?"new-A-idle":id)&&selection.Get("Idle")=="new-A-idle"&&selection.Get("Pose")=="B.pose","Legacy identity migration changes only resolved selections");
Check(!selection.Remap(id=>id),"Canonical selection migration is idempotent");
Console.WriteLine($"PASS {checks} assertions: replacement browser catalog, compact slot view, exclusive sources, deterministic bulk apply, owned-only clear and configuration persistence.");
