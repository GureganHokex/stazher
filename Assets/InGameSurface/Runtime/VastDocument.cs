using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace InGameSurface
{
    // Deliberately small VAST subset: one InLine, progressive MP4, local demo pixels.
    // No executable creatives, wrappers, ad pods, verification scripts or external requests.
    public sealed class VastDocument
    {
        public string MediaUrl;
        public double Duration;
        public readonly Dictionary<string,List<string>> Tracking = new Dictionary<string,List<string>>();
        static readonly HashSet<string> Events = new HashSet<string> {
            "impression","creativeView","start","firstQuartile","midpoint","thirdQuartile","complete","error"
        };
        static IEnumerable<XElement> Children(XElement node,string name) => node.Elements().Where(x=>x.Name.LocalName==name);

        public static bool LocalUrl(string value,string pathPattern)
        {
            return Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme=="http" &&
                uri.Host=="127.0.0.1" && uri.Port==8765 && string.IsNullOrEmpty(uri.UserInfo) &&
                string.IsNullOrEmpty(uri.Fragment) && value.Length<2048 && Regex.IsMatch(uri.AbsolutePath,pathPattern);
        }
        public static bool MediaAllowed(string url) => LocalUrl(url,@"^/media/[a-f0-9]{32}\.mp4$");
        public static bool PixelAllowed(string url) => LocalUrl(url,@"^/pixel/(impression|creativeView|start|firstQuartile|midpoint|thirdQuartile|complete|error)\.gif$");

        public static VastDocument Parse(string xml)
        {
            if(string.IsNullOrWhiteSpace(xml) || xml.Length>262144)throw new FormatException("VAST пустой или слишком большой");
            var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144};
            XDocument doc;
            using(var reader=XmlReader.Create(new StringReader(xml),settings))doc=XDocument.Load(reader);
            if(doc.Root?.Name.LocalName!="VAST")throw new FormatException("Ожидается VAST XML");
            var ads=Children(doc.Root,"Ad").ToArray();
            if(ads.Length!=1)throw new FormatException("Демо поддерживает один InLine, без ad pods");
            var inline=Children(ads[0],"InLine").SingleOrDefault();
            if(inline==null)throw new FormatException("VAST Wrapper пока не поддерживается");
            var linear=inline.Descendants().Where(x=>x.Name.LocalName=="Linear").SingleOrDefault();
            if(linear==null)throw new FormatException("Нужен один Linear-креатив");
            var duration=Children(linear,"Duration").SingleOrDefault()?.Value.Trim();
            if(!TimeSpan.TryParse(duration,CultureInfo.InvariantCulture,out var time) || time.TotalSeconds<=0 || time.TotalHours>1)
                throw new FormatException("Некорректная длительность VAST");
            var files=linear.Descendants().Where(x=>x.Name.LocalName=="MediaFile" &&
                (string)x.Attribute("type")=="video/mp4" && (string)x.Attribute("delivery")=="progressive" &&
                string.IsNullOrWhiteSpace((string)x.Attribute("apiFramework"))).ToArray();
            var media=files.FirstOrDefault(x=>MediaAllowed(x.Value.Trim()));
            if(media==null)throw new FormatException("Нет локального progressive MP4; VPAID не исполняется");
            var result=new VastDocument{MediaUrl=media.Value.Trim(),Duration=time.TotalSeconds};
            foreach(var item in Children(inline,"Impression"))result.Add("impression",item.Value);
            foreach(var item in Children(inline,"Error"))result.Add("error",item.Value);
            foreach(var item in linear.Descendants().Where(x=>x.Name.LocalName=="Tracking"))
                result.Add((string)item.Attribute("event"),item.Value);
            return result;
        }
        void Add(string kind,string value)
        {
            if(kind==null || !Events.Contains(kind))return;
            value=value.Trim();if(value=="about:blank")return;
            if(!PixelAllowed(value))throw new FormatException("Демо принимает только локальные пиксели");
            if(!Tracking.TryGetValue(kind,out var urls))Tracking[kind]=urls=new List<string>();
            if(urls.Count>=16)throw new FormatException("Слишком много пикселей");
            if(!urls.Contains(value))urls.Add(value);
        }
        public static string Expand(string url,string errorCode="900") => url
            .Replace("[CACHEBUSTING]",(BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0)%90000000+10000000).ToString(CultureInfo.InvariantCulture))
            .Replace("[ERRORCODE]",errorCode);
    }
}
