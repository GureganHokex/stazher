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
    public sealed class AdDemoShowcase : MonoBehaviour
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
        float panelWidth=350;

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
            surfaces.Add(CreatePlacement("office_coffee",new Vector3(-10.9f,2.05f,-4.1f),Vector3.right,2.6f));
            surfaces.Add(CreatePlacement("office_screen",new Vector3(10.65f,2.1f,-3.2f),Vector3.left,2.9f));
            blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="DemoOccluder";
            blocker.transform.localScale=new Vector3(4,4,.15f);blocker.GetComponent<Renderer>().material.color=new Color(.22f,.27f,.32f);blocker.SetActive(false);
            GoTo(0);
            if(Environment.GetCommandLineArgs().Contains("-igs-tour")) { StartCoroutine(Tour()); }
        }

        AdPlacement CreatePlacement(string id,Vector3 position,Vector3 normal,float width)
        {
            float height=width*9/16;
            var root=new GameObject("IGS_"+id).transform;root.SetParent(transform);root.SetPositionAndRotation(position,Quaternion.LookRotation(-normal));
            var frame=GameObject.CreatePrimitive(PrimitiveType.Cube);frame.name="Frame";frame.transform.SetParent(root,false);
            frame.transform.localPosition=new Vector3(0,0,.08f);frame.transform.localScale=new Vector3(width+.16f,height+.16f,.12f);
            Destroy(frame.GetComponent<Collider>());frame.GetComponent<Renderer>().material.color=new Color(.025f,.04f,.06f);
            var surface=GameObject.CreatePrimitive(PrimitiveType.Quad);surface.name="AdSurface";surface.transform.SetParent(root,false);
            surface.transform.localScale=new Vector3(width,height,1);Destroy(surface.GetComponent<Collider>());
            var template=Resources.Load<Material>("IGS/AdSurface");
            var shader=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Texture");
            var material=template!=null?new Material(template):new Material(shader);
            surface.GetComponent<Renderer>().sharedMaterial=material;
            var brand=Text(root,"IGS",new Vector3(0,.12f,-.025f),width*.12f,Color.white);
            var slogan=Text(root,"IN GAME SURFACE",new Vector3(0,-height*.19f,-.027f),width*.033f,new Color(.93f,.9f,.8f));
            Text(root,"IGS  /  ДЕМОНСТРАЦИОННЫЙ КРЕАТИВ",new Vector3(0,-height*.43f,-.03f),width*.018f,new Color(.64f,.7f,.73f));
            var p=root.gameObject.AddComponent<AdPlacement>();p.Initialize(session,id,surface.transform,material,brand,slogan);return p;
        }
        static TextMesh Text(Transform parent,string value,Vector3 position,float size,Color color)
        {
            var go=new GameObject("AdText");go.transform.SetParent(parent,false);go.transform.localPosition=position;
            var text=go.AddComponent<TextMesh>();text.font=OfficeBuilder.DefaultFont;text.fontSize=100;text.characterSize=size/10;
            text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;text.text=value;
            go.GetComponent<Renderer>().sharedMaterial=text.font.material;return text;
        }

        void EnsureStreet()
        {
            if(city!=null)return;
            city=CityBuilder.Build();
            // Near the bus stop. No LunchRun is created, so combat and timers are absent.
            surfaces.Add(CreatePlacement("street_board",CityBuilder.Origin+new Vector3(5.8f,2.1f,26),Vector3.left,3.6f));
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
            panelWidth=Mathf.Min(370,Screen.width*.35f);
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
            if(session==null)return;Styles();GUI.DrawTexture(new Rect(0,0,panelWidth,Screen.height),background);
            GUILayout.BeginArea(new Rect(17,18,panelWidth-34,Screen.height-32));
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
            if(!string.IsNullOrEmpty(message))GUILayout.Label(message,small);
            GUILayout.Space(14);GUILayout.Label("Зажми ПКМ: обзор + WASD. Q/E: вниз/вверх. Esc: пауза. Кнопки мест возвращают к экрану.",small);
            GUILayout.Space(9);GUILayout.Label("Демо-правило: 50% поверхности, 1% кадра, угол до 60°, 2 секунды. Оценка перекрытий — сетка 5×5. Методика не сертифицирована.",small);
            GUILayout.EndScrollView();GUILayout.EndArea();
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
        void OnDestroy() { Cursor.lockState=CursorLockMode.None;Cursor.visible=true;if(background!=null)Destroy(background); }
    }
}
