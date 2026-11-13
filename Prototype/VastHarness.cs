using System;
using System.Text.RegularExpressions;
using InGameSurface;
public static class VastHarness
{
    static void Check(bool condition,string title) { if(!condition)throw new Exception(title); }
    static void Reject(string xml) { try { VastDocument.Parse(xml); } catch { return; } throw new Exception("Unsafe/unsupported VAST was accepted"); }
    public static void Main()
    {
        const string media="http://127.0.0.1:8765/media/10000000000040008000000000000001.mp4";
        const string pixel="http://127.0.0.1:8765/pixel/start.gif?ad=10000000000040008000000000000001&amp;cb=[CACHEBUSTING]";
        string xml="<VAST version='4.3'><Ad><InLine><Impression>"+pixel.Replace("start","impression")+"</Impression><Creatives><Creative><Linear><Duration>00:00:15.000</Duration><TrackingEvents><Tracking event='start'>"+pixel+"</Tracking></TrackingEvents><MediaFiles><MediaFile delivery='progressive' type='video/mp4'>"+media+"</MediaFile></MediaFiles></Linear></Creative></Creatives></InLine></Ad></VAST>";
        var doc=VastDocument.Parse(xml);Check(doc.Duration==15 && doc.MediaUrl==media,"MP4 or duration missing");
        Check(doc.Tracking["impression"].Count==1 && doc.Tracking["start"].Count==1,"tracking missing");
        Check(VastDocument.Parse(xml.Replace("<VAST ","<VAST xmlns='http://www.iab.com/VAST' ")).MediaUrl==media,"XML namespace unsupported");
        Reject("<!DOCTYPE VAST [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>"+xml);
        Reject(xml.Replace(media,"https://example.com/ad.mp4"));
        Reject(xml.Replace("type='video/mp4'","type='video/mp4' apiFramework='VPAID'"));
        Reject(xml.Replace("<InLine>","<Wrapper>").Replace("</InLine>","</Wrapper>"));
        Reject(xml.Replace("http://127.0.0.1:8765/pixel/start.gif","http://127.0.0.1:8765/admin"));
        Reject("<VAST/>");
        string expanded=VastDocument.Expand("http://127.0.0.1:8765/pixel/error.gif?cb=[CACHEBUSTING]&code=[ERRORCODE]","405");
        Check(Regex.IsMatch(expanded,@"cb=\d{8}&code=405$"),"macro expansion");
        Console.WriteLine("PASS: VAST media, tracking, namespaces, DTD rejection, external URL rejection, VPAID rejection, Wrapper rejection, pixel URL restriction, empty VAST, macros (10 scenarios)");
    }
}
