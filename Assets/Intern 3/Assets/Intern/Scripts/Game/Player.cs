// Игрок: персонаж, камера от третьего/первого лица, ходьба, бег, прыжок.
using UnityEngine;

namespace Intern.Game
{
    public class PlayerController : MonoBehaviour
    {
        public static float FovThird = 60f, FovFirst = 72f;   // поле зрения (настройки)
        public Camera cam;
        public CharacterAnim avatar;
        public bool firstPerson;
        public bool cinematic;          // камерой управляет сцена (посадка за компьютер, гардероб, меню)
        public float speedBoostUntil;

        CharacterController cc;
        float camYaw, camPitch = 12f, bodyYaw, vy, camDist = 3.1f, curDist = 3.1f;
        Vector3 planarVel;
        Vector3 blendPos; Quaternion blendRot; float blendFov, blendStart = -99f, blendDur;

        public float CamYaw { get { return camYaw; } }
        public Vector3 Position { get { return transform.position; } }

        void Awake()
        {
            gameObject.layer = 2; // Ignore Raycast: камера и взаимодействие не упираются в самого игрока
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);

            var camGo = new GameObject("PlayerCamera");
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60; cam.nearClipPlane = 0.05f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Pal.Hex("BDE7FF");
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
        }

        public bool Tall { get { return avatar != null && avatar.imported; } }

        public void SetAvatar(Appearance ap)
        {
            if (ModelLib.HasCharacter("Intern"))
            {
                if (avatar == null || !avatar.imported)
                {
                    if (avatar != null) Destroy(avatar.gameObject);
                    var go = new GameObject("Avatar");
                    go.transform.SetParent(transform, false);
                    avatar = go.AddComponent<CharacterAnim>();
                    avatar.BuildImported("Intern", ap);
                    cc.height = 1.9f; cc.center = new Vector3(0, 0.95f, 0); cc.radius = 0.32f;
                }
                else avatar.ApplyLook(ap);
            }
            else if (avatar == null) avatar = Look.Bean("Avatar", transform, Vector3.zero, 0, ap);
            else avatar.Build(ap);
            SetLayerRecursive(avatar.gameObject, 2);
            avatar.SetHeadVisible(!firstPerson || cinematic);
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public void Teleport(Vector3 pos, float yawDeg)
        {
            cc.enabled = false; transform.position = pos; cc.enabled = true;
            bodyYaw = camYaw = yawDeg;
            transform.rotation = Quaternion.Euler(0, bodyYaw, 0);
            planarVel = Vector3.zero;
        }

        // Поставить персонажа точно в точку (например, на кресло), без физики
        public void Place(Vector3 pos, float yawDeg)
        {
            cc.enabled = false;
            transform.position = pos;
            bodyYaw = yawDeg;
            transform.rotation = Quaternion.Euler(0, bodyYaw, 0);
        }

        public void EnablePhysics(bool on) { cc.enabled = on; }

        public void ToggleView()
        {
            firstPerson = !firstPerson;
            if (firstPerson) camYaw = bodyYaw;
            avatar.SetHeadVisible(!firstPerson);
        }

        public void Tick(bool active)
        {
            float dt = Time.deltaTime;
            if (active)
            {
                var look = InputX.Look();
                camYaw += look.x;
                camPitch = Mathf.Clamp(camPitch - look.y, firstPerson ? -80f : -30f, 70f);
            }

            // Движение относительно направления камеры
            var mv = active ? InputX.Move() : Vector2.zero;
            var fwd = Quaternion.Euler(0, camYaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, camYaw, 0) * Vector3.right;
            var dir = fwd * mv.y + right * mv.x;
            if (dir.sqrMagnitude > 1) dir.Normalize();
            float speed = (InputX.Sprint() ? 6.5f : 3.8f) * (Time.time < speedBoostUntil ? 1.35f : 1f);
            planarVel = Vector3.Lerp(planarVel, dir * speed, 1 - Mathf.Exp(-12f * dt));

            if (cc.enabled)
            {
                if (cc.isGrounded) { vy = -1f; if (active && InputX.Jump()) vy = 4.6f; }
                vy -= 14f * dt;
                cc.Move((planarVel + Vector3.up * vy) * dt);
            }

            // Персонаж поворачивается туда, куда идёт (в режиме от первого лица — куда смотрит камера)
            if (firstPerson) bodyYaw = camYaw;
            else if (planarVel.sqrMagnitude > 0.05f)
            {
                float want = Mathf.Atan2(planarVel.x, planarVel.z) * Mathf.Rad2Deg;
                bodyYaw = Mathf.LerpAngle(bodyYaw, want, 1 - Mathf.Exp(-14f * dt));
            }
            transform.rotation = Quaternion.Euler(0, bodyYaw, 0);

            if (avatar != null)
            {
                avatar.moveSpeed = new Vector3(planarVel.x, 0, planarVel.z).magnitude;
                avatar.grounded = !cc.enabled || cc.isGrounded;
            }
            if (!cinematic) UpdateCamera(dt);
        }

        // Плавный переход от текущего кадра к обычной камере (после кат-сцены)
        public void BlendFromCurrent(float duration)
        {
            blendPos = cam.transform.position; blendRot = cam.transform.rotation; blendFov = cam.fieldOfView;
            blendStart = Time.time; blendDur = duration;
        }

        void ApplyBlend()
        {
            float k = blendDur > 0 ? Mathf.Clamp01((Time.time - blendStart) / blendDur) : 1;
            if (k >= 1) return;
            k = k * k * (3 - 2 * k);
            cam.transform.position = Vector3.Lerp(blendPos, cam.transform.position, k);
            cam.transform.rotation = Quaternion.Slerp(blendRot, cam.transform.rotation, k);
            cam.fieldOfView = Mathf.Lerp(blendFov, cam.fieldOfView, k);
        }

        void UpdateCamera(float dt)
        {
            UpdateCameraRaw(dt);
            ApplyBlend();
        }

        void UpdateCameraRaw(float dt)
        {
            if (firstPerson)
            {
                cam.transform.position = transform.position + Vector3.up * (Tall ? 1.8f : 1.58f) + transform.forward * 0.12f;
                cam.transform.rotation = Quaternion.Euler(camPitch, camYaw, 0);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, FovFirst, dt * 6f);
                return;
            }
            var pivot = transform.position + Vector3.up * (Tall ? 1.65f : 1.45f);
            var rot = Quaternion.Euler(camPitch, camYaw, 0);
            var back = rot * Vector3.back;
            var shoulder = rot * Vector3.right * 0.35f;
            float want = camDist;
            RaycastHit hit;
            if (Physics.SphereCast(pivot, 0.22f, (back * camDist + shoulder).normalized, out hit, camDist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                want = Mathf.Max(0.6f, hit.distance - 0.05f);
            curDist = want < curDist ? want : Mathf.Lerp(curDist, want, 1 - Mathf.Exp(-6f * dt));
            float k = curDist / camDist;
            cam.transform.position = pivot + back * curDist + shoulder * k;
            cam.transform.rotation = rot;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, FovThird, dt * 6f);
            // если камера подъехала вплотную — прячем голову, чтобы не смотреть изнутри
            avatar.SetHeadVisible(curDist > 0.9f);
        }

        // Главное меню: медленный облёт офиса
        public void MenuOrbit(float time)
        {
            float a = time * 0.06f;
            var center = new Vector3(0, 1.2f, 0.5f);
            cam.transform.position = center + new Vector3(Mathf.Sin(a) * 7.5f, 1.3f, -Mathf.Cos(a) * 5.2f);
            cam.transform.rotation = Quaternion.LookRotation(center - cam.transform.position);
            cam.fieldOfView = 55f;
        }

        // Ручная установка камеры (для кат-сцен)
        public void SetCamera(Vector3 pos, Quaternion rot, float fov)
        {
            cam.transform.position = pos; cam.transform.rotation = rot; cam.fieldOfView = fov;
        }

        public void GetCamera(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = cam.transform.position; rot = cam.transform.rotation; fov = cam.fieldOfView;
        }

        public void FaceCameraYaw(float yaw) { camYaw = yaw; }
    }
}
