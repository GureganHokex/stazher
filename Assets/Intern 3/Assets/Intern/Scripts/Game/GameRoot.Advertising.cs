using InGameSurface;
using UnityEngine;

namespace Intern.Game
{
    public partial class GameRoot
    {
        AdSession inGameAds;
        bool cityAdsCreated;

        void Start()
        {
            var root=new GameObject("In Game Surface · game placements");root.transform.SetParent(transform,false);
            inGameAds=root.AddComponent<AdSession>();
            inGameAds.ViewingCamera=player.cam;
            // No impressions behind menus, dialogues, IDE, transitions or the pause screen.
            inGameAds.ActivityPolicy=()=>mode==Mode.Walk || mode==Mode.Lunch;
            AdGameSurfaces.Coffee(root.transform,inGameAds);
            AdGameSurfaces.Office(root.transform,inGameAds);
            Debug.Log("IGS_GAME_ADS_READY office_coffee office_screen");
        }

        void LateUpdate()
        {
            if(inGameAds==null || cityAdsCreated || city==null)return;
            // The city is created lazily when the player leaves for lunch.
            AdGameSurfaces.Street(inGameAds.transform,inGameAds);cityAdsCreated=true;
            Debug.Log("IGS_GAME_ADS_READY street_board");
        }
    }
}
