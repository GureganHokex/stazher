using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace InGameSurface
{
    public sealed class AdPlacement : MonoBehaviour
    {
        public string Id;
        public Transform Surface;
        public TextMesh BrandText, SloganText;
        public AdSession Session;
        public Creative Creative { get; private set; }
        public VisibilityResult Visibility { get; private set; }
        public ExposureGate Gate { get; private set; } = new ExposureGate();
        public bool MediaReady { get; private set; }
        public string MediaStatus { get; private set; } = "Загрузка";
        public float TotalVisible { get; private set; }
        public double PlaybackSeconds => video!=null && MediaReady?video.time:0;
        Material material;
        Texture2D ownedImage;
        RenderTexture videoTexture;
        VideoPlayer video;
        GameObject videoObject;
        Coroutine loading;
        double lastSample;
        float pendingSeconds;
        bool previousEligible, started;
        int milestone;
        Vector3 originalScale;
        bool failed;
        VastDocument vast;

        public void Initialize(AdSession session,string id,Transform surface,Material mat,TextMesh brand,TextMesh slogan)
        {
            Session=session; Id=id; Surface=surface; material=mat; BrandText=brand; SloganText=slogan;
            originalScale=surface.localScale;lastSample=Time.realtimeSinceStartupAsDouble; session.Register(this);
        }

        public void Apply(Creative creative,bool force=false)
        {
            if(!force && Creative!=null && Creative.Signature==creative.Signature)return;
            Flush();
            if(loading!=null)StopCoroutine(loading);
            CleanupMedia();
            Creative=creative;vast=null; ResetSession(); MediaReady=false;failed=false;Surface.localScale=originalScale;
            if(!creative.enabled) { Fallback("Место свободно","Кампания выключена"); MediaStatus="Кампания выключена"; return; }
            if(creative.kind=="builtin")
            {
                material.color=new Color(.035f,.09f,.14f);
                SetTexture(null);
                BrandText.text="IGS";
                SloganText.text="IN GAME SURFACE";
                BrandText.color=new Color(.4f,.91f,.76f);
                BrandText.gameObject.SetActive(true); SloganText.gameObject.SetActive(true);
                MediaReady=true; MediaStatus="Встроенный баннер"; Log("loaded"); return;
            }
            Fallback("ЗАГРУЗКА","Готовим креатив");
            loading=StartCoroutine(LoadMedia(creative));
        }

        IEnumerator LoadMedia(Creative creative)
        {
            string mediaUrl=AdSession.Endpoint+creative.mediaPath;
            if(creative.kind=="vast")
            {
                using(var request=UnityWebRequest.Get(mediaUrl+"?session="+Session.SessionId+"&creative="+Uri.EscapeDataString(creative.creativeId)))
                {
                    request.timeout=6;request.redirectLimit=0;
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success) { Failed("VAST недоступен");yield break; }
                    try { vast=VastDocument.Parse(request.downloadHandler.text); }
                    catch(Exception e) { Failed("VAST: "+e.Message); }
                    if(failed)yield break;
                    mediaUrl=vast.MediaUrl;
                }
            }
            string file=Path.Combine(Session.Folder,Path.GetFileName(new Uri(mediaUrl).AbsolutePath));
            if(!File.Exists(file))
            {
                string temporary=file+"."+Id+".part";
                using(var request=UnityWebRequest.Get(mediaUrl))
                {
                    request.downloadHandler=new DownloadHandlerFile(temporary){removeFileOnAbort=true};
                    request.timeout=30; request.redirectLimit=0;
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success)
                    { if(File.Exists(temporary))File.Delete(temporary); Failed("Файл недоступен · показ не считается","401"); yield break; }
                }
                if(File.Exists(file))File.Delete(temporary);else File.Move(temporary,file);
            }
            if(creative.kind=="image")
            {
                using(var request=UnityWebRequestTexture.GetTexture(new Uri(file).AbsoluteUri))
                {
                    request.timeout=10; yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success) { Failed("Не удалось прочитать изображение");yield break; }
                    ownedImage=DownloadHandlerTexture.GetContent(request);
                    if(ownedImage.width>8192 || ownedImage.height>8192) { Failed("Изображение слишком большое");yield break; }
                    SetMedia(ownedImage,(float)ownedImage.width/ownedImage.height);
                    MediaReady=true; MediaStatus="Изображение · локальный кэш"; Log("loaded");
                }
                yield break;
            }
            videoObject=new GameObject("AdVideo"); videoObject.transform.SetParent(transform);
            video=videoObject.AddComponent<VideoPlayer>();
            video.playOnAwake=false; video.source=VideoSource.Url; video.url=file;
            video.isLooping=true; video.waitForFirstFrame=true; video.skipOnDrop=true;
            video.audioOutputMode=VideoAudioOutputMode.None;
            video.renderMode=VideoRenderMode.RenderTexture;
            video.aspectRatio=VideoAspectRatio.FitInside;
            video.errorReceived+=OnVideoError; video.sendFrameReadyEvents=true; video.frameReady+=FirstFrame;
            video.loopPointReached+=VideoComplete;
            video.Prepare();
            double deadline=Time.realtimeSinceStartupAsDouble+15;
            while(video!=null && !failed && !video.isPrepared && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(video==null || failed)yield break;
            if(!video.isPrepared) { Failed("Видео не подготовилось за 15 секунд","402");yield break; }
            float aspect=video.height>0?(float)video.width/video.height:16f/9f;
            int height=Mathf.Min(720,Mathf.RoundToInt(1280/aspect)),width=Mathf.RoundToInt(height*aspect);
            videoTexture=new RenderTexture(Mathf.Max(16,width),Mathf.Max(16,height),0,RenderTextureFormat.ARGB32);videoTexture.Create();
            video.targetTexture=videoTexture;
            // Decode one frame first: a prepared decoder alone is not a rendered advertisement.
            video.Play(); deadline=Time.realtimeSinceStartupAsDouble+10;
            while(video!=null && !failed && !MediaReady && Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(!MediaReady)Failed("Нет видеокадра · попробуйте MP4 H.264");
        }

        void FirstFrame(VideoPlayer source,long frame)
        {
            if(source!=video || failed)return;
            if(!MediaReady)
            {
                SetMedia(videoTexture,video.height>0?(float)video.width/video.height:16f/9f);
                source.Pause(); MediaReady=true; MediaStatus=vast==null?"MP4 · без звука":"VAST → MP4 · локальные пиксели"; Log("loaded");
                return;
            }
            if(!started && source.isPlaying && VisibilityProbe.Measure(Session.ViewingCamera,Surface,Session.IsActive,Creative.enabled).eligible)
            {
                started=true;source.sendFrameReadyEvents=false;Log("video_start");
                FirePixel("impression");FirePixel("creativeView");FirePixel("start");
            }
        }
        void OnVideoError(VideoPlayer source,string error) { if(source==video)Failed("Ошибка декодирования MP4","405"); }
        void VideoComplete(VideoPlayer source)
        {
            if(source!=video || !started || milestone>=4 || !VisibilityProbe.Measure(Session.ViewingCamera,Surface,Session.IsActive,MediaReady).eligible)return;
            Quartiles(1);Log("video_complete");FirePixel("complete");milestone=4;
        }
        void Quartiles(double ratio)
        {
            string[] names={"firstQuartile","midpoint","thirdQuartile"};
            while(milestone<3 && ratio>=(milestone+1)*.25)
            { milestone++;Log("video_"+(milestone*25));FirePixel(names[milestone-1]); }
        }
        void FirePixel(string kind,string errorCode="900")
        {
            if(vast==null || !vast.Tracking.TryGetValue(kind,out var urls))return;
            for(int i=0;i<urls.Count;i++)Session.Pixel(Id+"|"+Creative.creativeId+"|"+kind+"|"+i,urls[i],errorCode);
        }
        void SetMedia(Texture texture,float aspect)
        {
            material.color=Color.white; SetTexture(texture);
            // Measure only the inner quad occupied by the creative, excluding the frame.
            if(Creative.kind=="image" || Creative.kind=="video" || Creative.kind=="vast")
            {
                var scale=Surface.localScale; float target=scale.x/scale.y;
                material.mainTextureScale=Vector2.one;
                // Letterboxing uses a fitted inner quad; placement measurement follows that quad.
                if(aspect>target)scale.y=scale.x/aspect;else scale.x=scale.y*aspect;
                Surface.localScale=scale;
            }
            BrandText.gameObject.SetActive(false); SloganText.gameObject.SetActive(false);
        }
        void SetTexture(Texture texture)
        {
            material.mainTexture=texture;
            if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
        }
        void Fallback(string title,string subtitle)
        {
            SetTexture(null);material.color=new Color(.09f,.12f,.17f);
            BrandText.gameObject.SetActive(true);SloganText.gameObject.SetActive(true);
            BrandText.text=title;BrandText.color=Color.white;SloganText.text=subtitle;
        }
        void Failed(string message,string errorCode="900")
        {
            if(failed)return;failed=true;
            MediaReady=false;MediaStatus=message;Interrupt();
            Fallback("IN GAME SURFACE",message);if(video!=null)video.Stop();Log("error");FirePixel("error",errorCode);
        }
        void Log(string kind,float seconds=0) { if(Session!=null && Creative!=null)Session.Record(Id,Creative.creativeId,kind,seconds); }

        void Update()
        {
            if(Session==null || Creative==null)return;
            if(!Session.IsActive) { Interrupt(); return; }
            double now=Time.realtimeSinceStartupAsDouble;
            if(now-lastSample<.1)return;
            double dt=now-lastSample;lastSample=now;
            Visibility=VisibilityProbe.Measure(Session.ViewingCamera,Surface,Session.IsActive,MediaReady && Creative.enabled);
            bool eligible=Visibility.eligible && (video==null || started);
            if(eligible && previousEligible && dt<=.3)
            { pendingSeconds+=(float)dt;TotalVisible+=(float)dt; }
            previousEligible=eligible;
            if(Gate.Tick(eligible,dt))
            {
                if(Session.Counted.Add(Id+"|"+Creative.creativeId))Log("impression");
            }
            if(pendingSeconds>=5)Flush();
            if(video!=null && MediaReady)
            {
                if(Visibility.eligible)
                {
                    if(!video.isPlaying)video.Play();
                    double ratio=video.length>0?video.time/video.length:0;
                    if(started)Quartiles(ratio);
                }
                else if(video.isPlaying)video.Pause();
            }
        }
        public void Interrupt()
        {
            Gate.Interrupt();previousEligible=false;lastSample=Time.realtimeSinceStartupAsDouble;
            Visibility=new VisibilityResult{reason="Пауза / окно неактивно"};
            if(video!=null && video.isPlaying && MediaReady)video.Pause();
        }
        public void Flush() { if(pendingSeconds>0) { Log("view_time",pendingSeconds); pendingSeconds=0; } }
        public void ResetSession()
        {
            Gate=new ExposureGate(2,Creative!=null && Session.Counted.Contains(Id+"|"+Creative.creativeId));
            TotalVisible=0;previousEligible=false;lastSample=Time.realtimeSinceStartupAsDouble; started=false;milestone=0;
            if(video!=null && video.isPrepared) { video.Pause();video.time=0;video.sendFrameReadyEvents=true; }
        }
        void CleanupMedia()
        {
            MediaReady=false;
            if(video!=null) { video.errorReceived-=OnVideoError;video.frameReady-=FirstFrame;video.loopPointReached-=VideoComplete;video.Stop(); }
            if(videoObject!=null)Destroy(videoObject);video=null;
            if(videoTexture!=null) { videoTexture.Release();Destroy(videoTexture);videoTexture=null; }
            if(ownedImage!=null)Destroy(ownedImage);ownedImage=null;
        }
        void OnDisable() { Interrupt();Flush(); }
        void OnDestroy() { Flush();CleanupMedia();if(material!=null)Destroy(material);if(Session!=null)Session.Placements.Remove(this); }
    }
}
