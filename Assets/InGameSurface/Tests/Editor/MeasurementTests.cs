using NUnit.Framework;
using UnityEngine;

namespace InGameSurface.Tests
{
    public class MeasurementTests
    {
        [Test] public void RequiresContinuousVisibility()
        {
            var gate=new ExposureGate();
            for(int i=0;i<19;i++)Assert.IsFalse(gate.Tick(true,.1));
            gate.Tick(false,.1);
            for(int i=0;i<20;i++)Assert.IsFalse(gate.Tick(true,.1));
            Assert.IsTrue(gate.Tick(true,.1));
            for(int i=0;i<30;i++)Assert.IsFalse(gate.Tick(true,.1));
        }
        [Test] public void StallsAndFocusChangesDoNotAccumulateTime()
        {
            var gate=new ExposureGate();
            for(int i=0;i<10;i++)gate.Tick(true,.1);
            gate.Tick(true,4);Assert.AreEqual(0,gate.ContinuousSeconds);
            gate.Interrupt();gate.Tick(true,.1);Assert.AreEqual(0,gate.ContinuousSeconds);
        }
        [Test] public void AlreadyCountedCreativeDoesNotCountAgain()
        { var gate=new ExposureGate(2,true);for(int i=0;i<40;i++)Assert.IsFalse(gate.Tick(true,.1)); }

        [Test] public void OccluderStopsVisiblePlacement()
        {
            var cameraGo=new GameObject("test_camera");var camera=cameraGo.AddComponent<Camera>();camera.aspect=16f/9;camera.fieldOfView=60;
            var surface=new GameObject("test_surface");surface.transform.position=new Vector3(0,0,5);surface.transform.localScale=new Vector3(3,2,1);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(0,0,2.5f);wall.transform.localScale=new Vector3(4,4,.2f);
            try
            {
                wall.SetActive(false);Physics.SyncTransforms();Assert.IsTrue(VisibilityProbe.Measure(camera,surface.transform,true,true).eligible);
                wall.SetActive(true);Physics.SyncTransforms();Assert.IsFalse(VisibilityProbe.Measure(camera,surface.transform,true,true).eligible);
                Assert.IsFalse(VisibilityProbe.Measure(camera,surface.transform,false,true).eligible);
            }
            finally { Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(surface);Object.DestroyImmediate(wall); }
        }
        [Test] public void BehindCameraAndTinySurfaceDoNotCount()
        {
            var go=new GameObject("test_camera");var camera=go.AddComponent<Camera>();camera.aspect=16f/9;
            var surface=new GameObject("test_surface");
            try
            {
                surface.transform.position=new Vector3(0,0,-5);Assert.IsFalse(VisibilityProbe.Measure(camera,surface.transform,true,true).eligible);
                surface.transform.position=new Vector3(0,0,100);Assert.IsFalse(VisibilityProbe.Measure(camera,surface.transform,true,true).eligible);
            }
            finally { Object.DestroyImmediate(go);Object.DestroyImmediate(surface); }
        }
    }
}
