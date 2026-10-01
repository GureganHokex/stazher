// Помощники суставов (после спринта 7 «Живые персонажи»): в локтях и коленях моделей v4 есть кости ElbowHelp и KneeHelp.
// Кожа у самого сустава висит на них. Помощник поворачивается на половину сгиба (по биссектрисе угла) и растягивает
// сечение сустава поперёк сгиба в 1/cos(угол/2) раз — стык «на ус», как у согнутой трубы из двух отрезков.
// Толщина руки и ноги на сгибе сохраняется, снаружи выходит угол локтя и колена, внутри — складка,
// а не согнутая «сосиска». Работает всегда — в анимации, в ragdoll, в старой анимации кодом, — поэтому это
// отдельный компонент, и он считает последним в кадре (после IK ступней и рук).
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    [DefaultExecutionOrder(1000)]
    public class JointHelpers : MonoBehaviour
    {
        public const float MaxStretch = 1.5f;
        public static bool Stretch = true;          // проверка: без растяжения сечения (только полсгиба)
        static readonly string[][] Pairs =
        {
            new[] { "ElbowHelpL", "ElbowL", "HandL" }, new[] { "ElbowHelpR", "ElbowR", "HandR" },
            new[] { "KneeHelpL", "KneeL", "FootL" }, new[] { "KneeHelpR", "KneeR", "FootR" },
        };

        class H { public Transform help, joint; public Vector3 axis; }   // axis — кость сустава в покое (в осях родителя)
        readonly List<H> list = new List<H>();
        public int Count { get { return list.Count; } }

        // Найти помощников в скелете (после NormalizeRig у всех костей в покое поворот 0, поэтому «половина поворота»
        // кости сустава — это и есть поворот помощника). Моделей без помощников компонент не касается.
        public static JointHelpers Attach(GameObject host, Transform rig)
        {
            var jh = host.GetComponent<JointHelpers>();
            var found = new List<H>();
            if (rig != null)
                foreach (var p in Pairs)
                {
                    var h = ModelLib.Find(rig, p[0]); var j = ModelLib.Find(rig, p[1]); var e = ModelLib.Find(rig, p[2]);
                    if (h == null || j == null || e == null || h.parent != j.parent || e.parent != j) continue;
                    found.Add(new H { help = h, joint = j, axis = e.localPosition.normalized });
                }
            if (found.Count == 0) { if (jh != null) Destroy(jh); return null; }
            if (jh == null) jh = host.AddComponent<JointHelpers>();
            jh.list.Clear(); jh.list.AddRange(found);
            jh.LateUpdate();
            return jh;
        }

        void LateUpdate()
        {
            for (int i = 0; i < list.Count; i++)
            {
                var x = list[i];
                if (x.help == null || x.joint == null) continue;
                Vector3 a = x.axis, b = x.joint.localRotation * a;
                float ang = Vector3.Angle(a, b);
                // только сгиб: закручивание вокруг самой кости остаётся у кости сустава (предплечье, голень)
                x.help.localRotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(a, b), 0.5f);
                Vector3 k = Vector3.Cross(a, b);
                float hinge = k.sqrMagnitude > 1e-10f ? Mathf.Abs(k.normalized.x) : 1f;   // сгиб вокруг поперечной оси
                float s = 1f / Mathf.Cos(Mathf.Min(ang, 96f) * hinge * 0.5f * Mathf.Deg2Rad);
                x.help.localScale = new Vector3(1f, 1f, Stretch ? Mathf.Min(MaxStretch, s) : 1f);   // вперёд-назад, в осях помощника
            }
        }

        // Для самопроверки: поворот помощника (градусы) и растяжение
        public void DevState(int i, out float helpAngle, out float jointAngle, out float stretch)
        {
            var x = list[i];
            helpAngle = Quaternion.Angle(Quaternion.identity, x.help.localRotation);
            jointAngle = Vector3.Angle(x.axis, x.joint.localRotation * x.axis);
            stretch = x.help.localScale.z;
        }
    }
}
