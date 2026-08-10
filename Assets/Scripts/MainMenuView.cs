using System;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    public static class MainMenuView
    {
        static readonly Vector2 Center = new(.5f,.5f);
        public static void Build(Transform root,int tithe,int dailyLength,int castleFloor,int castleTotal,
            Action daily,Action castle,Action endless,Action wardrobe,Action bestiary,Action multiplayer,
            Action leaderboard,Action settings,Action<Transform,int> notices)
        {
            var oldFrame=root.Find("Frame");
            if(oldFrame!=null) oldFrame.gameObject.SetActive(false);
            var texture=Resources.Load<Texture2D>("MainMenu/main_menu_bg");
            if(texture!=null){var go=new GameObject("MainMenuEnvironment",typeof(RectTransform),typeof(Image));go.transform.SetParent(root,false);var image=go.GetComponent<Image>();image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);image.raycastTarget=false;var rt=image.rectTransform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;go.transform.SetAsFirstSibling();}
            else Crimson.Backdrop(root,360,-50,true,1);

            var top=new Vector2(.5f,1);
            Title(root,Theme.Title,108,Color.black,new Vector2(7,-174),new Vector2(1320,190),top);
            Title(root,Theme.Title,108,Theme.Hex("C7192D"),new Vector2(0,-168),new Vector2(1320,190),top);
            Line(root,"◆  HONOR IS OPTIONAL. SURVIVAL ISN'T.  ◆",27,Theme.Hex("D6BFA5"),new Vector2(0,-292),new Vector2(1100,42),top);
            Mode(root,"BLOOD MOON",$"TONIGHT  ·  {dailyLength} FLOORS  ·  SHARED SEED",new Vector2(0,112),daily);
            Mode(root,"THE CASTLE",$"FLOOR {castleFloor} / {castleTotal}  ·  BEGIN THE DESCENT",new Vector2(0,-6),castle);
            Mode(root,"ENDLESS NIGHTS","ONE RUN  ·  NO BOTTOM",new Vector2(0,-124),endless);

            int depth=PlayerPrefs.GetInt("best_endless_distance",0);
            var record=Crimson.Panel_(root,Center,new Vector2(-720,-14),new Vector2(390,392),new Color(.035f,.012f,.025f,.95f),Crimson.Rail);
            Line(record,"ENDLESS NIGHTS  ·  YOUR RECORD",18,Crimson.GoldLit,new Vector2(0,-25),new Vector2(360,34),new Vector2(.5f,1));
            Line(record,"YOUR DEEPEST NIGHT",15,Crimson.Mute,new Vector2(0,-86),new Vector2(330,28),new Vector2(.5f,1));
            Line(record,depth>0?$"{depth:N0}M":"0M",64,Theme.Hex("806B61"),new Vector2(0,-150),new Vector2(330,80),new Vector2(.5f,1));
            Line(record,depth>0?"THE CASTLE REMEMBERS":"NO DEPTH RECORDED. THE CASTLE ISN'T\nIMPRESSED YET.",16,Theme.Hex("B8A48F"),new Vector2(0,-231),new Vector2(340,64),new Vector2(.5f,1));
            Line(record,"FIRST RANK  ·  TOURIST AT 1M",14,Crimson.Mute,new Vector2(0,-285),new Vector2(340,28),new Vector2(.5f,1));
            Footer(record,"SET A HIGH SCORE",new Vector2(0,24),new Vector2(386,54),endless,new Vector2(.5f,0));

            var rules=Crimson.Panel_(root,Center,new Vector2(720,-14),new Vector2(390,438),new Color(.035f,.012f,.025f,.95f),Crimson.Rail);
            Line(rules,"THREE WAYS DOWN",18,Crimson.GoldLit,new Vector2(0,-25),new Vector2(350,32),new Vector2(.5f,1));
            Rule(rules,105,"THE CASTLE","40 FLOORS. HAND-BUILT\nTO LIE TO YOU. START HERE.");
            Rule(rules,15,"BLOOD MOON","FIVE FLOORS. SAME LAYOUT\nFOR EVERYONE, TONIGHT ONLY.");
            Rule(rules,-80,"ENDLESS NIGHTS","ONE RUN. NO BOTTOM. THIS IS\nWHERE YOUR SCORE LIVES.");
            Line(rules,"DYING PAYS. YOU EARN BLOOD EVERY\nTIME THE CASTLE WINS.",14,Crimson.Mute,new Vector2(0,-175),new Vector2(340,48),Center);

            var bottom=new Vector2(.5f,0);var size=new Vector2(244,62);
            Footer(root,"WARDROBE",new Vector2(-536,152),size,wardrobe,bottom);
            Footer(root,$"BESTIARY {Codex.KnownCount()}/{Codex.Total}",new Vector2(-268,152),size,bestiary,bottom);
            Footer(root,"MULTIPLAYER",new Vector2(0,152),size,multiplayer,bottom);
            Footer(root,"LEADERBOARD",new Vector2(268,152),size,leaderboard,bottom);
            Footer(root,"SETTINGS",new Vector2(536,152),size,settings,bottom);
            Line(root,"†   IN THE DARKNESS, ONLY THE STRONG SURVIVE   †",22,Theme.Hex("B9A28D"),new Vector2(0,44),new Vector2(900,42),bottom);
            // Daily rewards still apply before this view is built; the reference has
            // no transient banner stack, so the menu remains visually stable.
        }

        static void Mode(Transform root,string heading,string caption,Vector2 pos,Action action){var plate=Crimson.Panel_(root,Center,pos,new Vector2(720,92),new Color(.035f,.008f,.018f,.97f),Crimson.BloodDeep,3);Click(plate,action);Line(plate,heading,38,Theme.Hex("F14A59"),new Vector2(0,13),new Vector2(660,48),Center);Line(plate,caption,15,Theme.Hex("D6AF82"),new Vector2(0,-25),new Vector2(660,24),Center);Line(plate,"◆",20,Crimson.BloodHot,new Vector2(-354,0),new Vector2(30,30),Center);Line(plate,"◆",20,Crimson.BloodHot,new Vector2(354,0),new Vector2(30,30),Center);}
        static void Footer(Transform parent,string text,Vector2 pos,Vector2 size,Action action,Vector2 anchor){var plate=Crimson.Panel_(parent,anchor,pos,size,new Color(.035f,.008f,.018f,.98f),Crimson.Rail,2);Click(plate,action);Line(plate,text,20,Theme.Hex("EE4051"),Vector2.zero,size,Center);}
        static void Rule(Transform panel,float y,string heading,string body){Line(panel,"◆",15,Crimson.BloodHot,new Vector2(-166,y),new Vector2(22,22),Center);var h=Crimson.Line(panel,heading,17,Crimson.GoldLit,new Vector2(-142,y+8),new Vector2(160,30),TextAnchor.MiddleLeft);h.fontStyle=FontStyle.Bold;Crimson.Line(panel,body,13,Theme.Hex("BBA795"),new Vector2(45,y-24),new Vector2(245,58),TextAnchor.UpperLeft);}
        static void Click(RectTransform plate,Action action){var fill=plate.Find("Fill").GetComponent<Image>();var b=plate.gameObject.AddComponent<Button>();b.targetGraphic=fill;b.onClick.AddListener(()=>{Audio.Play("click",.6f);action();});}
        static void Title(Transform p,string text,int size,Color color,Vector2 pos,Vector2 dims,Vector2 anchor){var t=Theme.Label(p,text,size,color,anchor,pos,dims);t.font=Theme.TitleFont;t.raycastTarget=false;}
        static void Line(Transform p,string text,int size,Color color,Vector2 pos,Vector2 dims,Vector2 anchor){var t=Crimson.Line(p,text,size,color,pos,dims,TextAnchor.MiddleCenter,anchor);t.fontStyle=FontStyle.Bold;}
    }
}
