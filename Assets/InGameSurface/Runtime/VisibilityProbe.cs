using System.Collections.Generic;
using UnityEngine;

namespace InGameSurface
{
    public struct VisibilityResult
    {
        public bool eligible;
        public float visibleFraction, screenFraction, angle;
        public string reason;
    }

    public static class VisibilityProbe
    {
        // Surface is a unit Quad with its front along local -Z. 5x5 sampling is an approximation.
        public static VisibilityResult Measure(Camera camera, Transform surface, bool active, bool ready)
        {
            var result = new VisibilityResult { reason = "Пауза / окно неактивно" };
            if (!active) return result;
            result.reason = "Креатив ещё не готов";
            if (!ready || camera == null || !camera.enabled || !surface.gameObject.activeInHierarchy) return result;
            result.angle = Vector3.Angle(-surface.forward, camera.transform.position - surface.position);
            result.reason = "Обратная сторона / угол > 60°";
            if (result.angle > 60) return result;
            var poly = new List<Vector2>(4);
            foreach (var p in new[] { new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0) })
            {
                var v = camera.WorldToViewportPoint(surface.TransformPoint(p));
                result.reason = "За камерой / пересекает ближнюю плоскость";
                if (v.z <= camera.nearClipPlane) return result; // Conservative near-plane policy.
                poly.Add(v);
            }
            for (int edge = 0; edge < 4; edge++) poly = Clip(poly, edge);
            float area = 0;
            for (int i = 0; i < poly.Count; i++)
            { var a = poly[i]; var b = poly[(i+1)%poly.Count]; area += a.x*b.y-b.x*a.y; }
            result.screenFraction = Mathf.Abs(area)*.5f;
            result.reason = "Слишком мало места в кадре (<1%)";
            if (result.screenFraction < .01f) return result;
            int clear = 0;
            for (int x = 0; x < 5; x++) for (int y = 0; y < 5; y++)
            {
                var point = surface.TransformPoint(new Vector3((x+.5f)/5f-.5f,(y+.5f)/5f-.5f,0));
                var viewport = camera.WorldToViewportPoint(point);
                if (viewport.z <= camera.nearClipPlane || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) continue;
                var ray = point-camera.transform.position;
                if (!Physics.Raycast(camera.transform.position,ray.normalized,Mathf.Max(0,ray.magnitude-.025f),Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) clear++;
            }
            result.visibleFraction = clear/25f;
            result.eligible = result.visibleFraction >= .5f;
            result.reason = result.eligible ? "Виден · идёт учёт" : "Перекрыт / за краем кадра";
            return result;
        }

        static List<Vector2> Clip(List<Vector2> input, int edge)
        {
            var output = new List<Vector2>(8);
            if (input.Count == 0) return output;
            Vector2 prev = input[input.Count-1]; float pd = Distance(prev,edge);
            foreach (var cur in input)
            {
                float cd = Distance(cur,edge);
                if ((pd>=0)!=(cd>=0)) output.Add(Vector2.LerpUnclamped(prev,cur,pd/(pd-cd)));
                if (cd>=0) output.Add(cur);
                prev=cur; pd=cd;
            }
            return output;
        }
        static float Distance(Vector2 p,int edge) => edge==0?p.x:edge==1?1-p.x:edge==2?p.y:1-p.y;
    }
}
