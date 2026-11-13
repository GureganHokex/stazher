using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using InGameSurface;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Intern.Game
{
    // Game-specific adapter. The InGameSurface assembly does not reference Intern.Game.
    public sealed partial class AdDemoShowcase : MonoBehaviour
    {
        public static bool Requested => SceneManager.GetActiveScene().name=="IGSDemo" || Environment.GetCommandLineArgs().Contains("-igs-demo");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        { if(Requested && FindFirstObjectByType<AdDemoShowcase>()==null)new GameObject("In Game Surface Demo").AddComponent<AdDemoShowcase>(); }
        AdSession session;
        Camera cameraView;
        CityRefs city;
        readonly List<AdPlacement> surfaces=new List<AdPlacement>();
        GameObject blocker;
        int selected;
        float yaw,pitch;
        bool paused,covered,turned;
        GUIStyle title,subtitle,small,button,card,label,big;
        Texture2D background;
        Vector2 scroll;
        string message="";
        float panelWidth=350,uiScale=1;

        void Start()
        {
            Application.runInBackground=true;Application.targetFrameRate=60;
            // No SaveData / progression / Docker initialization in this presentation mode.
            foreach(var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))cam.gameObject.SetActive(false);
            if(ModelLib.HasOffice)ImportedOffice.Build();else OfficeBuilder.Build();
            cameraView=new GameObject("DemoCamera").AddComponent<Camera>();
            cameraView.tag="MainCamera";cameraView.nearClipPlane=.05f;cameraView.farClipPlane=700;
            cameraView.fieldOfView=60;cameraView.clearFlags=CameraClearFlags.SolidColor;cameraView.backgroundColor=new Color(.55f,.72f,.84f);
            session=gameObject.AddComponent<AdSession>();session.ViewingCamera=cameraView;
            surfaces.Add(AdGameSurfaces.Coffee(transform,session));
            surfaces.Add(AdGameSurfaces.Office(transform,session));
            blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="DemoOccluder";
            blocker.transform.localScale=new Vector3(4,4,.15f);blocker.GetComponent<Renderer>().material.color=new Color(.22f,.27f,.32f);blocker.SetActive(false);
            GoTo(0);
            if(Environment.GetCommandLineArgs().Contains("-igs-tour")) { StartCoroutine(Tour()); }
        }

        void EnsureStreet()
        {
            if(city!=null)return;
            city=CityBuilder.Build();
            // Near the bus stop. No LunchRun is created, so combat and timers are absent.
            surfaces.Add(AdGameSurfaces.Street(transform,session));
        }
        public void GoTo(int index)
        {
            if(index==2)EnsureStreet();selected=index;turned=false;covered=false;blocker.SetActive(false);
            var surface=surfaces[index].Surface;Vector3 normal=-surface.forward;
            cameraView.transform.position=surface.position+normal*(index==2?6:4.5f);
            cameraView.transform.rotation=Quaternion.LookRotation(surface.position-cameraView.transform.position);
            yaw=cameraView.transform.eulerAngles.y;pitch=0;
            foreach(var p in surfaces)p.Interrupt();
        }
        void Update()
        {
            if(session==null)return;
            uiScale=Mathf.Clamp(Screen.height/900f,.8f,2.25f);
            panelWidth=Mathf.Min(370*uiScale,Screen.width*.35f);
            cameraView.rect=new Rect(panelWidth/Screen.width,0,1-panelWidth/Screen.width,1);
            session.Paused=paused;
            var kb=Keyboard.current;var mouse=Mouse.current;
            if(kb!=null && kb.escapeKey.wasPressedThisFrame)paused=!paused;
            bool control=!paused && mouse!=null && mouse.rightButton.isPressed;
            Cursor.lockState=control?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!control;
            if(control)
            {
                var delta=mouse.delta.ReadValue();yaw+=delta.x*.13f;pitch=Mathf.Clamp(pitch-delta.y*.13f,-80,80);
                cameraView.transform.rotation=Quaternion.Euler(pitch,yaw,0);
                if(kb!=null)
                {
                    var move=new Vector3((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),(kb.eKey.isPressed?1:0)-(kb.qKey.isPressed?1:0),(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));
                    cameraView.transform.position+=cameraView.transform.TransformDirection(Vector3.ClampMagnitude(move,1))*Time.unscaledDeltaTime*(kb.leftShiftKey.isPressed?7:3);
                }
            }
        }
        void Cover()
        {
            covered=!covered;var p=surfaces[selected].Surface;
            blocker.transform.position=(cameraView.transform.position+p.position)*.5f;
            blocker.transform.rotation=Quaternion.LookRotation(p.position-cameraView.transform.position);blocker.SetActive(covered);
        }
        void Turn()
        { turned=!turned;cameraView.transform.Rotate(0,180,0,Space.World);yaw=cameraView.transform.eulerAngles.y; }
        void Styles()
        {
            if(title!=null)return;
            background=new Texture2D(1,1);background.SetPixel(0,0,new Color(.055f,.075f,.105f));background.Apply();
            title=new GUIStyle(GUI.skin.label){fontSize=25,fontStyle=FontStyle.Bold,wordWrap=true};title.normal.textColor=Color.white;
            subtitle=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};subtitle.normal.textColor=new Color(.43f,.88f,.75f);
            small=new GUIStyle(GUI.skin.label){fontSize=11,wordWrap=true};small.normal.textColor=new Color(.66f,.74f,.82f);
            label=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};label.normal.textColor=new Color(.9f,.94f,.97f);
            big=new GUIStyle(label){fontSize=22,fontStyle=FontStyle.Bold};
            button=new GUIStyle(GUI.skin.button){fontSize=12,fixedHeight=33,wordWrap=true};
            card=new GUIStyle(GUI.skin.box){padding=new RectOffset(13,13,12,12)};
        }
        void OnGUI()
        {
            if(session==null)return;Styles();
            var previousMatrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            float width=panelWidth/uiScale,height=Screen.height/uiScale;
            GUI.DrawTexture(new Rect(0,0,width,height),background);
            GUILayout.BeginArea(new Rect(17,18,width-34,height-32));
            scroll=GUILayout.BeginScrollView(scroll,GUIStyle.none,GUIStyle.none);
            GUILayout.Label("IGS  /  ПРОТОТИП 01",subtitle);GUILayout.Label("In Game Surface",title);
            GUILayout.Label("Реклама внутри игрового мира",small);GUILayout.Space(15);
            GUILayout.Label(session.ConnectionStatus,small);GUILayout.Label(session.PixelStatus+" · очередь "+session.PendingPixels,small);GUILayout.Space(13);
            GUILayout.Label("Рекламные места",label);
            if(GUILayout.Button("01  Кофейная зона",button))GoTo(0);
            if(GUILayout.Button("02  Экран в офисе",button))GoTo(1);
            if(GUILayout.Button("03  Городской щит",button))GoTo(2);
            GUILayout.Space(13);var p=surfaces[selected];
            GUILayout.BeginVertical(card);GUILayout.Label(p.Creative?.title??"Загрузка",label);GUILayout.Label(p.MediaStatus,small);
            GUILayout.Label($"Позиция видео: {p.PlaybackSeconds:0.0} с",small);
            GUILayout.Space(7);GUILayout.Label(paused?"Демонстрация на паузе":p.Visibility.reason,label);
            GUILayout.Label($"Видно: {p.Visibility.visibleFraction:P0}   В кадре: {p.Visibility.screenFraction:P1}\nУгол: {p.Visibility.angle:0}°   Непрерывно: {p.Gate.ContinuousSeconds:0.0} / 2 с",small);
            GUILayout.Label(p.Gate.Counted?"✓ Показ засчитан":"Ожидание видимого показа",subtitle);GUILayout.EndVertical();
            GUILayout.Space(10);GUILayout.BeginHorizontal();
            if(GUILayout.Button(covered?"Убрать стену":"Перекрыть",button))Cover();
            if(GUILayout.Button(turned?"Повернуться":"Отвернуться",button))Turn();GUILayout.EndHorizontal();
            if(GUILayout.Button(paused?"Продолжить":"Пауза",button)) { paused=!paused;if(paused)foreach(var a in surfaces)a.Interrupt(); }
            GUILayout.Space(13);GUILayout.Label($"{session.Impressions} показов   ·   {session.VisibleSeconds:0.0} сек",big);
            GUILayout.Label($"Сеанс {session.SessionId.Substring(0,8)} · в очереди {session.Pending}",small);
            if(GUILayout.Button("Открыть панель кампаний ↗",button))Application.OpenURL(AdSession.Endpoint);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Новый сеанс",button))session.NewSession();
            if(GUILayout.Button("Экспорт CSV",button)){message="CSV сохранён: "+session.ExportCsv();}
            GUILayout.EndHorizontal();
#if UNITY_EDITOR
            if(GUILayout.Button(recording?"Остановить запись":"Записать демонстрацию · MP4",button))
            { if(recording)FinishRecording();else StartCoroutine(RecordTour()); }
            if(recording)GUILayout.Label(recordingStatus,subtitle);
#endif
            if(!string.IsNullOrEmpty(message))GUILayout.Label(message,small);
            GUILayout.Space(14);GUILayout.Label("Зажми ПКМ: обзор + WASD. Q/E: вниз/вверх. Esc: пауза. Кнопки мест возвращают к экрану.",small);
            GUILayout.Space(9);GUILayout.Label("Демо-правило: 50% поверхности, 1% кадра, угол до 60°, 2 секунды. Оценка перекрытий — сетка 5×5. Методика не сертифицирована.",small);
            GUILayout.EndScrollView();GUILayout.EndArea();GUI.matrix=previousMatrix;
        }
        IEnumerator Tour()
        {
            yield return new WaitForSecondsRealtime(5);ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(session.Folder,"igs-office.png"));
            yield return new WaitForSecondsRealtime(1);Cover();yield return new WaitForSecondsRealtime(2);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(session.Folder,"igs-occluded.png"));
            yield return new WaitForSecondsRealtime(1);GoTo(1);yield return new WaitForSecondsRealtime(4);
            GoTo(2);yield return new WaitForSecondsRealtime(5);ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(session.Folder,"igs-city.png"));
            session.ExportCsv();
        }
        void OnDestroy()
        {
#if UNITY_EDITOR
            FinishRecording();
#endif
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;if(background!=null)Destroy(background);
        }
    }
}
