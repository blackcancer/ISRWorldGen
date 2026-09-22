using System.Globalization;
using System.Text.Json;
using ISRWorldGen.Core.Geology.Evolution;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var checks = new List<object>(); int failures = 0;
object? diagnostic = null;
LegacySpreadingChecks.Run(Check);
Check("exported controlled sections and carrier dates agree with their source", () =>
{
    const int n=128; var scale=new TectonicScalePlan(1000000,1000000);
    RiftRibbon[] material=[new(10,100000,35,4,5),new(20,50000,28,1,3),new(30,150000,40,6,6)];
    var rift=new RiftNecking(material,[new(0,60,-1500,2500)],300000,1000000,.01,7);
    var timelines=RiftSpreadingAdapter.ToTimelines(rift,"controlled-raster",0,0,1,0);
    var sections=Enumerable.Range(0,121).Select(i=>rift.Sample(i*.5)).ToArray();
    var last=sections[^1]; double[] ocean=new double[n*n]; int covered=0;
    for(int z=0;z<n;z++) for(int x=0;x<n;x++)
    {
        double px=(x+.5)*1000000/n;
        if(px>last.GapLeftReference && px<last.GapRightReference) { ocean[z*n+x]=7; covered++; }
    }
    var map=SpreadingTimeline.Reconstruct(73,scale,n,ocean,timelines,"controlled-rupture-created-carrier");
    double maxError=0;
    for(int i=0;i<ocean.Length;i++)
    {
        if(ocean[i]==0)
        {
            if(map.SourceEventIds[i]!=-1 || map.AgeMyr[i]!=0) throw new ArithmeticException("Absent ocean has a date");
            continue;
        }
        double x=(i%n+.5)*1000000/n;
        double expected=Math.Abs(x-last.RidgeReference!.Value)/2000;
        maxError=Math.Max(maxError,Math.Abs(expected-map.AgeMyr[i]));
        if(map.SourceEventIds[i]!=0) throw new ArithmeticException("Changed source identity");
    }
    if(covered==0 || maxError>1e-9) throw new ArithmeticException("Raster chronology does not match direct motion");
    diagnostic=new { scope="CONTROLLED_OPEN_TRANSECT_EXTRUSION_NOT_A_GENERATED_WORLD", width=n,height=n,
        referenceWidth=1000000,referenceLength=1000000,spacingReference=1000000d/n,
        referenceKmPerUnit=.01,observationTimeMyr=60, rift.Checksum,sections,
        oceanCarrierKm=ocean,ageMyr=map.AgeMyr,birthTimeMyr=map.BirthTimeMyr,sourceEventIds=map.SourceEventIds,
        coveredCells=covered,maximumOracleErrorMyr=maxError,
        absenceMeaning="Carrier zero means no ocean in this test, not a newborn sea floor",
        boundary="OPEN_UNWRAPPED_STRIP_NOT_PERIODIC_WORLD", heightmapGenerated=false };
});
Console.WriteLine(JsonSerializer.Serialize(new {status=failures==0?"PASS_LEGACY_SUBSET_AND_DIAGNOSTIC":"FAIL",checks,failures,diagnostic,
    legacySource="316ca3e477035d9ee179d6dde76d0c85a397375f",legacySubsetCount=27,
    excludedLegacyScope="Thermal, full material world and spatial-index branch tests are not imported",
    geographicAcceptance="NOT_EVALUATED",erosion="NOT_RUN"},new JsonSerializerOptions{WriteIndented=true}));
return failures==0?0:1;
void Check(string name,Action action)
{
    try { action();checks.Add(new{name,status="PASS",error=(string?)null});Console.Error.WriteLine("PASS: "+name); }
    catch(Exception ex) { failures++;checks.Add(new{name,status="FAIL",error=ex.ToString()});Console.Error.WriteLine("FAIL: "+name+" "+ex.Message); }
}
