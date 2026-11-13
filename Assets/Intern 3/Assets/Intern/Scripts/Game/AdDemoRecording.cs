#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor.Media;
using UnityEngine;

namespace Intern.Game
{
    // Records only the game's own rendered frame. No desktop, microphone or external service.
    public sealed partial class AdDemoShowcase
    {
        bool recording;
        string recordingStatus="",recordingPath;
        MediaEncoder movieEncoder;
        RenderTexture captureSource,captureTarget;
        Texture2D captureFrame;
        StreamWriter recordingEvidence;
        int captureWidth,captureHeight;

        IEnumerator RecordTour()
        {
            recording=true;message="";recordingStatus="Подготовка видео… Оставь окно Game активным.";
            paused=false;GoTo(0);session.NewSession();
            double deadline=Time.realtimeSinceStartupAsDouble+20;
            while(recording && (!surfaces[0].MediaReady || surfaces[0].Creative?.kind!="vast" || !Application.isFocused))
            {
                if(Time.realtimeSinceStartupAsDouble>deadline)
                { message="Запись не началась: нужен загруженный VAST и активное окно Game.";recording=false;yield break; }
                yield return null;
            }
            if(!recording || !BeginRecording())yield break;
            double start=Time.realtimeSinceStartupAsDouble,nextFrame=0,nextEvidence=0;
            int stage=0;
            var endOfFrame=new WaitForEndOfFrame();
            try
            {
                while(recording)
                {
                    yield return endOfFrame;
                    if(!recording)break;
                    double elapsed=Time.realtimeSinceStartupAsDouble-start;
                    if(elapsed>=45)break;
                    if(stage==0 && elapsed>=16.5) { Cover();stage=1; }
                    if(stage==1 && elapsed>=20) { Cover();stage=2; }
                    if(stage==2 && elapsed>=22.5) { Turn();stage=3; }
                    if(stage==3 && elapsed>=25.5) { Turn();stage=4; }
                    if(stage==4 && elapsed>=28) { paused=true;stage=5; }
                    if(stage==5 && elapsed>=31) { paused=false;stage=6; }
                    if(stage==6 && elapsed>=33) { GoTo(1);stage=7; }
                    if(stage==7 && elapsed>=39) { GoTo(2);stage=8; }
                    recordingStatus=$"REC · {elapsed:0} / 45 с · "+StageName(stage);
                    if(elapsed>=nextFrame)
                    {
                        if(!CaptureMovieFrame(elapsed))break;
                        nextFrame=elapsed+1.0/30;
                    }
                    if(elapsed>=nextEvidence)
                    {
                        var p=surfaces[selected];
                        recordingEvidence.WriteLine(JsonUtility.ToJson(new RecordingSample {
                            time=elapsed,stage=StageName(stage),session=session.SessionId,placement=p.Id,
                            foreground=Application.isFocused,paused=paused,covered=covered,
                            videoSeconds=p.PlaybackSeconds,visibleSeconds=p.TotalVisible,
                            qualified=p.Visibility.eligible,visibleFraction=p.Visibility.visibleFraction,
                            continuousSeconds=p.Gate.ContinuousSeconds,counted=p.Gate.Counted,
                            pendingPixels=session.PendingPixels,pixelStatus=session.PixelStatus
                        }));
                        recordingEvidence.Flush();nextEvidence=elapsed+.5;
                    }
                }
            }
            finally { FinishRecording(); }
        }

        static string StageName(int stage)
        {
            switch(stage)
            {
                case 1:return "Экран перекрыт";
                case 3:return "Камера отвернулась";
                case 5:return "Пауза";
                case 7:return "Экран в офисе";
                case 8:return "Городской щит";
                default:return "Кофейная зона";
            }
        }

        bool BeginRecording()
        {
            try
            {
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Prototype","recordings");
                Directory.CreateDirectory(folder);
                recordingPath=Path.Combine(folder,"IGS-gameplay-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".mp4");
                captureWidth=Screen.width;captureHeight=Screen.height;
                int width=Math.Min(1920,captureWidth)/2*2;
                int height=Math.Max(2,(int)Math.Round((double)captureHeight*width/captureWidth)/2*2);
                var attrs=new VideoTrackAttributes { width=(uint)width,height=(uint)height,frameRate=new MediaRational(30),includeAlpha=false };
                movieEncoder=new MediaEncoder(recordingPath,attrs);
                captureSource=new RenderTexture(captureWidth,captureHeight,0,RenderTextureFormat.ARGB32);
                captureTarget=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);
                captureSource.Create();captureTarget.Create();
                captureFrame=new Texture2D(width,height,TextureFormat.RGBA32,false);
                recordingEvidence=new StreamWriter(Path.ChangeExtension(recordingPath,"jsonl"));
                return true;
            }
            catch(Exception e)
            { Debug.LogError("IGS recording could not start: "+e.Message);FinishRecording();message="Ошибка записи: "+e.Message;return false; }
        }

        bool CaptureMovieFrame(double seconds)
        {
            var previous=RenderTexture.active;
            try
            {
                if(Screen.width!=captureWidth || Screen.height!=captureHeight)
                    throw new InvalidOperationException("Размер окна Game изменился. Запусти запись ещё раз.");
                ScreenCapture.CaptureScreenshotIntoRenderTexture(captureSource);
                Graphics.Blit(captureSource,captureTarget);
                RenderTexture.active=captureTarget;
                captureFrame.ReadPixels(new Rect(0,0,captureFrame.width,captureFrame.height),0,0,false);
                captureFrame.Apply(false,false);
                // Real timestamps keep video playback and VAST event timing in sync on slower machines.
                if(!movieEncoder.AddFrame(captureFrame,new MediaTime((long)(seconds*1000000),1000000,1)))
                    throw new IOException("Unity не приняла видеокадр");
                return true;
            }
            catch(Exception e) { Debug.LogError("IGS recording stopped: "+e.Message);message="Запись прервана: "+e.Message;return false; }
            finally { RenderTexture.active=previous; }
        }

        void FinishRecording()
        {
            recording=false;
            if(movieEncoder!=null)
            {
                try { movieEncoder.Dispose();if(string.IsNullOrEmpty(message))message="MP4 сохранён в Prototype/recordings";Debug.Log("IGS_RECORDING_SAVED "+recordingPath); }
                catch(Exception e) { message="Не удалось завершить MP4: "+e.Message;Debug.LogError(message); }
                movieEncoder=null;
            }
            recordingEvidence?.Dispose();recordingEvidence=null;
            if(captureSource!=null) { captureSource.Release();Destroy(captureSource);captureSource=null; }
            if(captureTarget!=null) { captureTarget.Release();Destroy(captureTarget);captureTarget=null; }
            if(captureFrame!=null) { Destroy(captureFrame);captureFrame=null; }
        }

        [Serializable]
        sealed class RecordingSample
        {
            public double time,videoSeconds,continuousSeconds;
            public string stage,session,placement,pixelStatus;
            public bool foreground,paused,covered,qualified,counted;
            public float visibleSeconds,visibleFraction;
            public int pendingPixels;
        }
    }
}
#endif
