using InGameSurface;
using UnityEngine;

namespace Intern.Game
{
    // Shared placements for the normal game and the technical showcase.
    public static class AdGameSurfaces
    {
        public static AdPlacement Coffee(Transform parent,AdSession session) =>
            Create(parent,session,"office_coffee",new Vector3(-10.9f,2.05f,-4.1f),Vector3.right,2.6f);
        public static AdPlacement Office(Transform parent,AdSession session) =>
            Create(parent,session,"office_screen",new Vector3(10.65f,2.1f,-3.2f),Vector3.left,2.9f);
        public static AdPlacement Street(Transform parent,AdSession session) =>
            Create(parent,session,"street_board",CityBuilder.Origin+new Vector3(5.8f,2.1f,26),Vector3.left,3.6f);

        static AdPlacement Create(Transform parent,AdSession session,string id,Vector3 position,Vector3 normal,float width)
        {
            float height=width*9/16;
            var root=new GameObject("IGS_"+id).transform;
            root.SetParent(parent);root.SetPositionAndRotation(position,Quaternion.LookRotation(-normal));
            var frame=GameObject.CreatePrimitive(PrimitiveType.Cube);frame.name="Frame";frame.transform.SetParent(root,false);
            frame.transform.localPosition=new Vector3(0,0,.08f);frame.transform.localScale=new Vector3(width+.16f,height+.16f,.12f);
            Object.Destroy(frame.GetComponent<Collider>());frame.GetComponent<Renderer>().material.color=new Color(.025f,.04f,.06f);
            if(id=="street_board")
            {
                float legHeight=position.y-CityBuilder.Origin.y-height*.5f;
                foreach(float x in new[]{-width*.32f,width*.32f})
                {
                    var leg=GameObject.CreatePrimitive(PrimitiveType.Cube);leg.name="Billboard support";leg.transform.SetParent(root,false);
                    leg.transform.localPosition=new Vector3(x,-height*.5f-legHeight*.5f,.08f);
                    leg.transform.localScale=new Vector3(.09f,legHeight,.09f);
                    leg.GetComponent<Renderer>().sharedMaterial=frame.GetComponent<Renderer>().sharedMaterial;
                }
            }
            var surface=GameObject.CreatePrimitive(PrimitiveType.Quad);surface.name="AdSurface";surface.transform.SetParent(root,false);
            surface.transform.localScale=new Vector3(width,height,1);Object.Destroy(surface.GetComponent<Collider>());
            var template=Resources.Load<Material>("IGS/AdSurface");
            var shader=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Texture");
            var material=template!=null?new Material(template):new Material(shader);
            surface.GetComponent<Renderer>().sharedMaterial=material;
            var brand=Text(root,"IGS",new Vector3(0,.12f,-.025f),width*.12f,Color.white);
            var slogan=Text(root,"IN GAME SURFACE",new Vector3(0,-height*.19f,-.027f),width*.033f,new Color(.93f,.9f,.8f));
            var p=root.gameObject.AddComponent<AdPlacement>();p.Initialize(session,id,surface.transform,material,brand,slogan);return p;
        }

        static TextMesh Text(Transform parent,string value,Vector3 position,float size,Color color)
        {
            var go=new GameObject("AdText");go.transform.SetParent(parent,false);go.transform.localPosition=position;
            var text=go.AddComponent<TextMesh>();text.font=OfficeBuilder.DefaultFont;text.fontSize=100;text.characterSize=size/10;
            text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=color;text.text=value;
            go.GetComponent<Renderer>().sharedMaterial=text.font.material;return text;
        }
    }
}
