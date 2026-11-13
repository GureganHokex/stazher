using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace InGameSurface
{
    public sealed class AdSession : MonoBehaviour
    {
        public const string Endpoint = "http://127.0.0.1:8765";
        public Camera ViewingCamera;
        public bool Paused;
        public string SessionId { get; private set; }
        public string ConnectionStatus { get; private set; } = "Встроенные креативы · подключение…";
        public int Pending => queue.events.Count;
        public int PendingPixels => pixels.requests.Count;
        public string PixelStatus { get; private set; } = "VAST-пиксели: ожидание видео";
        public readonly HashSet<string> PixelEvents = new HashSet<string>();
        public bool IsActive => !Paused && Application.isFocused && !suspended;
        public readonly List<AdPlacement> Placements = new List<AdPlacement>();
        public readonly HashSet<string> Counted = new HashSet<string>();
        public int Impressions { get; private set; }
        public float VisibleSeconds { get; private set; }
        public string Folder { get; private set; }
        public AdConfig Config { get; private set; }
        readonly EventBatch queue = new EventBatch();
        readonly PixelQueue pixels = new PixelQueue();
        bool suspended;

        void Awake()
        {
            Folder = Path.Combine(Application.persistentDataPath,"InGameSurface");
            Directory.CreateDirectory(Folder);
            SessionId = Guid.NewGuid().ToString();
            try
            {
                string file=Path.Combine(Folder,"queue.json");
                if(File.Exists(file)) queue.events.AddRange(JsonUtility.FromJson<EventBatch>(File.ReadAllText(file)).events);
            }
            catch(Exception e) { Debug.LogWarning("InGameSurface queue: "+e.Message); }
            try
            {
                var file=Path.Combine(Folder,"pixels.json");
                if(File.Exists(file))pixels.requests.AddRange(JsonUtility.FromJson<PixelQueue>(File.ReadAllText(file)).requests
                    .Where(x=>x!=null && VastDocument.PixelAllowed(x.url) && x.attempts<3));
            }
            catch(Exception e) { Debug.LogWarning("InGameSurface pixels: "+e.Message); }
            Config=DefaultConfig();
            try { var f=Path.Combine(Folder,"config.json"); if(File.Exists(f)) ApplyConfig(JsonUtility.FromJson<AdConfig>(File.ReadAllText(f))); }
            catch(Exception e) { Debug.LogWarning("InGameSurface cache: "+e.Message); }
            StartCoroutine(Poll()); StartCoroutine(Send()); StartCoroutine(SendPixels());
        }

        public static AdConfig DefaultConfig() => new AdConfig { placements = new[] {
            new Creative { placementId="office_coffee",creativeId="igs",title="In Game Surface",kind="builtin",theme="igs" },
            new Creative { placementId="office_screen",creativeId="igs",title="In Game Surface",kind="builtin",theme="igs" },
            new Creative { placementId="street_board",creativeId="igs",title="In Game Surface",kind="builtin",theme="igs" }
        }};

        public void Register(AdPlacement placement)
        { Placements.Add(placement); var c=Config.placements.FirstOrDefault(x=>x.placementId==placement.Id); if(c!=null) placement.Apply(c); }

        public bool ApplyConfig(AdConfig config)
        {
            if(config==null || config.schemaVersion!=1 || config.placements==null || config.placements.Length>30) return false;
            if(config.placements.Any(x=>x==null || string.IsNullOrEmpty(x.placementId) || string.IsNullOrEmpty(x.creativeId) ||
                !(x.kind=="builtin" || x.kind=="image" || x.kind=="video" || x.kind=="vast") ||
                (x.kind=="vast" && !System.Text.RegularExpressions.Regex.IsMatch(x.mediaPath??"",@"^/vast/(office_coffee|office_screen|street_board)\.xml$")) ||
                (x.kind!="builtin" && x.kind!="vast" && !System.Text.RegularExpressions.Regex.IsMatch(x.mediaPath??"",@"^/media/[a-f0-9]{32}\.(mp4|png|jpg|jpeg)$")))) return false;
            Config=config;
            foreach(var p in Placements) { var c=config.placements.FirstOrDefault(x=>x.placementId==p.Id); if(c!=null)p.Apply(c); }
            return true;
        }

        IEnumerator Poll()
        {
            while(true)
            {
                using(var request=UnityWebRequest.Get(Endpoint+"/api/config"))
                {
                    request.timeout=3; request.redirectLimit=0;
                    yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            if(ApplyConfig(JsonUtility.FromJson<AdConfig>(request.downloadHandler.text)))
                            { WriteAtomic("config.json",JsonUtility.ToJson(Config)); ConnectionStatus="Локальная панель подключена"; }
                            else ConnectionStatus="Ошибка конфигурации · сохранён предыдущий креатив";
                        }
                        catch(Exception e) { ConnectionStatus="Ошибка конфигурации: "+e.Message; }
                    }
                    else ConnectionStatus="Офлайн · креативы и события сохранены локально";
                }
                yield return new WaitForSecondsRealtime(3);
            }
        }

        public void Record(string placement,string creative,string kind,float seconds=0)
        {
            var e=new AdEvent { eventId=Guid.NewGuid().ToString(),sessionId=SessionId,placementId=placement,creativeId=creative,
                kind=kind,seconds=seconds,timestamp=DateTime.UtcNow.ToString("o") };
            queue.events.Add(e);
            if(kind=="impression") Impressions++;
            if(kind=="view_time") VisibleSeconds+=seconds;
            try { File.AppendAllText(Path.Combine(Folder,"events.jsonl"),JsonUtility.ToJson(e)+"\n"); }
            catch(Exception ex) { Debug.LogWarning("InGameSurface journal: "+ex.Message); }
            SaveQueue();
        }

        IEnumerator Send()
        {
            while(true)
            {
                yield return new WaitForSecondsRealtime(2);
                if(queue.events.Count==0) continue;
                var batch=new EventBatch { events=queue.events.Take(100).ToList() };
                using(var request=new UnityWebRequest(Endpoint+"/api/events","POST"))
                {
                    request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(batch)));
                    request.downloadHandler=new DownloadHandlerBuffer(); request.timeout=4; request.redirectLimit=0;
                    request.SetRequestHeader("Content-Type","application/json"); request.SetRequestHeader("X-IGS-Demo","local");
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success) continue;
                    try
                    {
                        var ack=JsonUtility.FromJson<EventAck>(request.downloadHandler.text);
                        if(ack?.accepted==null) continue;
                        var sent=new HashSet<string>(batch.events.Select(e=>e.eventId));
                        var ids=new HashSet<string>(ack.accepted.Where(sent.Contains));
                        queue.events.RemoveAll(e=>ids.Contains(e.eventId)); SaveQueue();
                    }
                    catch(Exception e) { Debug.LogWarning("InGameSurface response: "+e.Message); }
                }
            }
        }

        void WriteAtomic(string name,string text)
        {
            string target=Path.Combine(Folder,name), temp=target+".tmp";
            File.WriteAllText(temp,text);
            if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
        }
        void SaveQueue() { try { WriteAtomic("queue.json",JsonUtility.ToJson(queue)); } catch(Exception e) { Debug.LogWarning("InGameSurface queue: "+e.Message); } }
        public void Pixel(string key,string url,string errorCode="900")
        {
            if(!VastDocument.PixelAllowed(url) || !PixelEvents.Add(key))return;
            pixels.requests.Add(new PixelRequest{url=VastDocument.Expand(url,errorCode)});SavePixels();
        }
        void SavePixels() { try { WriteAtomic("pixels.json",JsonUtility.ToJson(pixels)); } catch(Exception e) { Debug.LogWarning("IGS pixels: "+e.Message); } }
        IEnumerator SendPixels()
        {
            while(true)
            {
                yield return new WaitForSecondsRealtime(1);
                if(pixels.requests.Count==0)continue;
                var item=pixels.requests[0];item.attempts++;SavePixels();
                using(var request=UnityWebRequest.Get(item.url))
                {
                    request.timeout=4;request.redirectLimit=0;request.SetRequestHeader("X-IGS-Demo","local");
                    yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success)
                    { pixels.requests.Remove(item);PixelStatus="VAST-пиксель доставлен · HTTP "+request.responseCode; }
                    else if(item.attempts>=3)
                    { pixels.requests.Remove(item);PixelStatus="Пиксель не доставлен после 3 попыток"; }
                    else PixelStatus="Повтор отправки VAST-пикселя";
                    SavePixels();
                }
            }
        }
        public void NewSession()
        {
            foreach(var p in Placements)p.Flush();
            SessionId=Guid.NewGuid().ToString(); Counted.Clear();PixelEvents.Clear(); Impressions=0; VisibleSeconds=0;
            foreach(var p in Placements) { if(p.Creative?.kind=="vast")p.Apply(p.Creative,true);else p.ResetSession(); }
        }
        public string ExportCsv()
        {
            foreach(var p in Placements)p.Flush();
            var output=new StringBuilder("event_id,session_id,placement,creative,event,seconds,utc\n");
            string source=Path.Combine(Folder,"events.jsonl");
            if(File.Exists(source)) foreach(var line in File.ReadLines(source))
            {
                try { var e=JsonUtility.FromJson<AdEvent>(line); output.AppendLine(string.Join(",",new[]{e.eventId,e.sessionId,e.placementId,e.creativeId,e.kind,e.seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),e.timestamp}.Select(Csv))); }
                catch { /* An incomplete trailing line after a crash is skipped. */ }
            }
            string file=Path.Combine(Folder,"igs-events-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");
            File.WriteAllText(file,output.ToString(),new UTF8Encoding(true)); return file;
        }
        static string Csv(string s) { s=s??""; if(s.Length>0 && "=+-@".Contains(s[0].ToString()))s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
        void OnApplicationFocus(bool focus) { if(!focus) foreach(var p in Placements)p.Interrupt(); }
        void OnApplicationPause(bool pause) { suspended=pause; if(pause)foreach(var p in Placements)p.Interrupt(); }
        void OnApplicationQuit() { foreach(var p in Placements)p.Flush(); SaveQueue(); }
    }
}
