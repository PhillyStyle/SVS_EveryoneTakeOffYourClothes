using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Character;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using SaveData;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using KeyCode = UnityEngine.KeyCode;
using Vector2 = UnityEngine.Vector2;
using Color = UnityEngine.Color;
using Random = System.Random;
using Scene = UnityEngine.SceneManagement.Scene;
using HarmonyLib;
using SV;
using SV.H;
using Manager;

namespace SVS_EveryoneTakeOffYourClothes;

public class HumanAndFrames
{
    public HumanAndFrames(Human h, int f)
    {
        hum = h;
        frames = f;
    }
    public Human hum;
    public int frames;
}

public class ActorAndHDC
{
    public ActorAndHDC(Actor a, HumanDataCoordinate ofit)
    {
        act = a;
        outfit = ofit;
    }
    public Actor act;
    public HumanDataCoordinate outfit;
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static readonly string[] TakeOffYourClothes =
    {
            "Everyone! Take off your clothes!",
            "TAKE OFF YOUR CLOTHES!",
            "STRIP DOWN!",
            "Let's all get naked!"
        };

    public static readonly string[] PutYourClothesOn =
{
            "Everyone! Put your clothes back on!",
            "PUT ON YOUR CLOTHES!",
            "GET DRESSED!",
            "Let's all get dressed!"
        };

    internal static new ManualLogSource Log;
    private static GameObject ETOYCObject;

    public static Plugin Instance { get; private set; }
    public ConfigEntry<KeyboardShortcut> ETOYCKeys1 { get; private set; }
    public static ConfigEntry<int> NakedMinutes { get; set; }
    public static ConfigEntry<bool> AllowChangingOutfit { get; set; }

    public static HScene HSceneInstance = null;
    public static float HSceneStartTime = 0;

    public static bool AreClothesOn = true;
    public static Random Rand;

    public static List<HumanAndFrames> LateUpdateMatchHuman;
    public static List<ActorAndHDC> ActorListDuration;
    public static bool LateUpdateRunning = false;

    public static bool ignoreThisUpdate = false;
    public static int curTimeZone = 0;

    public static HumanGraphic HumanGraphicInstance = null;
    public static Actor MainPlayerActor = null;
    public static Human MainPlayerHuman = null;

    public static Human LatestHiPolyPlayerHuman = null;
    public static Human PutClothesOnThisGuy = null;
    public static int PutClothesOnFrames = 5;

    public static List<Actor> ActorsInH;

    public static float TimeWeWentNaked = 0.0f;

    public override void Load()
    {
        Log = base.Log;
        Instance = this;
        Rand = new Random();
        LateUpdateMatchHuman = new List<HumanAndFrames>();
        ActorListDuration = new List<ActorAndHDC>();
        Plugin.ActorsInH = new List<Actor>();

        ETOYCKeys1 = Config.Bind("Hotkey", "Everyone! Take off your clothes!", new KeyboardShortcut(KeyCode.C, KeyCode.LeftAlt), "Keyboard Shortcut");
        NakedMinutes = Config.Bind("General", "Naked Time (Minutes)", 5, "The amount of time that people are nude. \nNudity will persist until time is up or period is over. (Whichever comes first)");
        AllowChangingOutfit = Config.Bind("General", "Allow changing outfits", true, "Allow/Disallow them to change outfits when nude. \nChanging outfits will disable nudity on them.");

        ClassInjector.RegisterTypeInIl2Cpp<PluginHelper>();

        ETOYCObject = new GameObject("SVS_EveryoneTakeOffYourClothes");
        GameObject.DontDestroyOnLoad(ETOYCObject);
        ETOYCObject.hideFlags = HideFlags.HideAndDontSave;
        ETOYCObject.AddComponent<PluginHelper>();

        Harmony.CreateAndPatchAll(typeof(Hooks));
        Harmony.CreateAndPatchAll(typeof(Hooks2));
    }
}


public class PluginHelper : MonoBehaviour
{
    public PluginHelper(IntPtr handle) : base(handle) { }
    public static bool InsideMaker => CharacterCreation.HumanCustom.Initialized;

    private static GameObject subtitleObject;
    private static TextMeshProUGUI subtitle;
    float displayingText = 0.0f;

    private static T GetResource<T>(string name) where T : UnityEngine.Object
    {
        var objs = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<T>());
        for (var i = objs.Length - 1; i >= 0; --i)
        {
            var obj = objs[i];
            if (obj.name == name)
            {
                return obj.TryCast<T>();
            }
        }
        return null;
    }

    internal void Start()
    {
        // Add or get Canvas component
        Canvas canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();

        // Configure Canvas
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 550;

        // Add or get CanvasScaler component
        CanvasScaler canvasScaler = gameObject.GetComponent<CanvasScaler>();
        if (canvasScaler == null)
            canvasScaler = gameObject.AddComponent<CanvasScaler>();

        // Configure CanvasScaler
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);

        // Add or get CanvasGroup component
        CanvasGroup canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        canvasGroup.blocksRaycasts = false;


        InitializeSubtitle();
    }

    internal void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        InitializeSubtitle();
    }

    private void InitializeSubtitle()
    {
        if (subtitleObject == null || subtitle == null)
        {
            subtitleObject = new GameObject("XUAIGNORE EveryoneTakeOffYourClothesText");
            subtitleObject.transform.SetParent(gameObject.transform);
            subtitleObject.SetActive(false);

            int fontSize = (int)(Screen.height / 20.0f);
            RectTransform subtitleRect = subtitleObject.GetComponent<RectTransform>();
            if (subtitleRect == null) subtitleRect = subtitleObject.AddComponent<RectTransform>();
            subtitleRect.pivot = new Vector2(0, -1);
            subtitleRect.sizeDelta = new Vector2(Screen.width * 0.990f, fontSize + (fontSize * 0.05f));

            subtitle = subtitleObject.GetComponent<TextMeshProUGUI>();
            if (subtitle == null) subtitle = subtitleObject.AddComponent<TextMeshProUGUI>();
            subtitle.font = GetResource<TMP_FontAsset>("tmp_sv_default");
            subtitle.fontSharedMaterial = GetResource<Material>("tmp_sv_default SVT-10");

            subtitle.fontSize = fontSize;
            subtitle.alignment = TextAlignmentOptions.Bottom;
            subtitle.overflowMode = TextOverflowModes.Overflow;
            subtitle.enableWordWrapping = true;
            subtitle.color = Color.white;
            subtitle.text = "Everyone! Take off your clothes!";
        }
    }

    internal void Update()
    {
        if (!Plugin.AreClothesOn)
        {
            if (Plugin.TimeWeWentNaked + (float)Plugin.NakedMinutes.Value * 60.0f < Time.time)
            {
                foreach (ActorAndHDC aaT in Plugin.ActorListDuration)
                {
                    HelperFunctions.EndPersistence(aaT.act, false, true);
                }

                Plugin.ActorListDuration.Clear();
                Plugin.AreClothesOn = true;
            }
        }

        if (displayingText > 0.0f)
        {
            subtitleObject.SetActive(true);
            displayingText -= Time.deltaTime;
            if (displayingText <= 0.0f)
            {
                displayingText = 0.0f;
                subtitleObject.SetActive(false);
            }
            else return;
        }

        if (Plugin.Instance.ETOYCKeys1.Value.IsDown())
        {
            if (Plugin.AreClothesOn)
            {
                Plugin.AreClothesOn = false;
                //Take off clothes
                subtitle.text = Plugin.TakeOffYourClothes[Plugin.Rand.Next(Plugin.TakeOffYourClothes.Length)];
                subtitleObject.SetActive(true);
                displayingText = 1.0f;
                Plugin.ActorListDuration.Clear();
                List<KeyValuePair<int, Actor>> CharaActorList = Manager.Game.Charas.AsManagedEnumerable().ToList<KeyValuePair<int, Actor>>();
                foreach (KeyValuePair<int, Actor> kvp in CharaActorList)
                {
                    Human h = kvp.Value.chaCtrl;
                    if (h != null)
                    {
                        var hdc = HelperFunctions.GetCoordPub(ref h);
                        if (hdc != null) Plugin.ActorListDuration.Add(new ActorAndHDC(kvp.Value, hdc));
                    }
                    if (kvp.Value.IsPC)
                    {
                        Plugin.MainPlayerActor = kvp.Value;
                        Plugin.MainPlayerHuman = kvp.Value.chaCtrl;
                    }

                    foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                    {
                        HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                        Plugin.ignoreThisUpdate = true;
                        kvp.Value.chaCtrl.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Naked, false);
                    }
                    HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                    Plugin.ignoreThisUpdate = true;
                    kvp.Value.chaCtrl.cloth.UpdateClothesStateAll();
                }
                Plugin.TimeWeWentNaked = Time.time;
            }
            else
            {
                Plugin.AreClothesOn = true;
                //Put clothes on
                subtitle.text = Plugin.PutYourClothesOn[Plugin.Rand.Next(Plugin.PutYourClothesOn.Length)];
                subtitleObject.SetActive(true);
                displayingText = 1.0f;
                Plugin.ActorListDuration.Clear();
                List<KeyValuePair<int, Actor>> CharaActorList = Manager.Game.Charas.AsManagedEnumerable().ToList<KeyValuePair<int, Actor>>();
                foreach (KeyValuePair<int, Actor> kvp in CharaActorList)
                {
                    foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                    {
                        HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                        Plugin.ignoreThisUpdate = true;
                        kvp.Value.chaCtrl.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Clothing, false);
                    }
                    HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                    Plugin.ignoreThisUpdate = true;
                    kvp.Value.chaCtrl.cloth.UpdateClothesStateAll();
                }
            }
        }
    }

    internal void LateUpdate()
    {
        try
        {
            if (Plugin.PutClothesOnThisGuy != null)
            {
                if (Plugin.PutClothesOnFrames > 0)
                {
                    Plugin.PutClothesOnFrames--;
                    return;
                }

                HelperFunctions.PutClothesOn(ref Plugin.PutClothesOnThisGuy);
                Plugin.PutClothesOnThisGuy = null;
            }
        }
        catch (ObjectDisposedException ex)
        {
            Plugin.Log.LogInfo("Object was disposed: " + ex.Message);
        }
    }
}

public static class Hooks
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Human), nameof(Human.LateUpdate))]
    public unsafe static void Postfix_Human_LateUpdate(ref Human __instance)
    {
        if (Plugin.LateUpdateRunning) return;

        Plugin.LateUpdateRunning = true;
        try
        {
            Human h = __instance;
            int lumhIndex = Plugin.LateUpdateMatchHuman.FindIndex(x => x.hum == h);
            if (lumhIndex != -1)
            {
                if (Plugin.LateUpdateMatchHuman[lumhIndex].frames > 0)
                {
                    Plugin.LateUpdateMatchHuman[lumhIndex].frames--;
                    return;
                }
                HumanAndFrames curHAndF = Plugin.LateUpdateMatchHuman[lumhIndex];
                Plugin.LateUpdateMatchHuman.Remove(curHAndF);

                if ((SV.H.HScene.Active()) && ((UnityEngine.Time.time - Plugin.HSceneStartTime) >= 1.0f))
                {
                    return;
                }

                Actor mainA = __instance.data.About.FindMainActorInstance().Value;
                int index = Plugin.ActorListDuration.FindIndex(x => x.act == mainA);
                if (index != -1)
                {
                    foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                    {
                        HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                        Plugin.ignoreThisUpdate = true;
                        h.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Naked, false);
                    }
                    HelperFunctions.SVS_PostHClothingStatePersistenceCompatibility();
                    Plugin.ignoreThisUpdate = true;
                    h.cloth.UpdateClothesStateAll();
                }
            }
            else
            {
                //Trying to get the Human in the Changing Room
                if ((!Plugin.AreClothesOn) && 
                    (Plugin.LatestHiPolyPlayerHuman != __instance) &&
                    ((__instance.hiPoly)) &&
                    (!SV.H.HScene.Active()))
                {
                    Actor mainA = __instance.data.About.FindMainActorInstance().Value;
                    if (mainA == null) return;
                    if (mainA == Plugin.MainPlayerActor)
                    {
                        Plugin.LatestHiPolyPlayerHuman = __instance;
                    }
                }
            }
        }
        finally
        {
            Plugin.LateUpdateRunning = false;
        }
    }


    [HarmonyPostfix]
    [HarmonyPatch(typeof(ConditionManager), nameof(ConditionManager.DayEnd))]
    public unsafe static void DoDayEnd(ref ConditionManager __instance, Actor _actor)
    {
        Plugin.AreClothesOn = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SimulationUICtrl), nameof(SimulationUICtrl.SetTimeZone))]
    public unsafe static void DoSetTimeZone(ref SimulationUICtrl __instance, int _timezone)
    {
        if (Plugin.curTimeZone != _timezone)
        {
            Plugin.curTimeZone = _timezone;
            Plugin.AreClothesOn = true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HScene), nameof(HScene.Start))]
    public static void HSceneInitialize(ref HScene __instance)
    {
        if (Plugin.HSceneInstance == __instance) return;
        Plugin.HSceneInstance = __instance;
        Plugin.HSceneStartTime = UnityEngine.Time.time;

        Plugin.ActorsInH.Clear();
        foreach (HActor ha in __instance.Actors) Plugin.ActorsInH.Add(ha.Actor);
    }

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(Human), nameof(Human.ReloadCoordinate), new Type[] { })]
    //public unsafe static void Postfix(ref Human __instance)
    //{
    //    HumanCloth c = __instance.cloth;
    //    HelperFunctions.DoUpdateClothes(ref c);
    //}

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(Human), nameof(Human.ReloadCoordinate), new Type[] { })]
    //public unsafe static void DoReloadCoordinate(ref Human __instance)
    //{
    //    HumanCloth c = __instance.cloth;
    //    HelperFunctions.DoUpdateClothes(ref c);
    //}

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(Human), nameof(Human.ReloadCoordinate), new Type[] { typeof(Human.ReloadFlags) })]
    //public unsafe static void DoReloadCoordinate2(ref Human __instance, Human.ReloadFlags flags)
    //{
    //    HumanCloth c = __instance.cloth;
    //    HelperFunctions.DoUpdateClothes(ref c);
    //}

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(Human), nameof(Human.Reload), new Type[] { })]
    //public unsafe static void DoReload(ref Human __instance)
    //{
    //    HumanCloth c = __instance.cloth;
    //    HelperFunctions.DoUpdateClothes(ref c);
    //}

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HumanGraphic), nameof(HumanGraphic.UpdateGraphics), new Type[] { typeof(Material), typeof(HumanGraphic.UpdateFlags) })]
    public static void Postfix_HumanGraphic_UpdateGraphics(ref HumanGraphic __instance, Material material)
    {
        if (Plugin.HumanGraphicInstance == __instance) return;
        Plugin.HumanGraphicInstance = __instance;

        HumanCloth hc = __instance._human.cloth;
        HelperFunctions.DoUpdateClothes(ref hc);

        if ((Plugin.AllowChangingOutfit.Value) && (!SV.H.HScene.Active()))
        {
            Actor a = __instance._human.data.About.FindMainActorInstance().Value;
            if (a == null) return;
            if (a != Plugin.MainPlayerActor) return;
            int index = Plugin.ActorListDuration.FindIndex(x => x.act == a);
            if (index != -1)
            {
                Human h = a.chaCtrl;
                if (!Plugin.AllowChangingOutfit.Value)
                {
                    HumanDataCoordinate HDC = HelperFunctions.GetCoordPub(ref h);
                    HumanDataCoordinate HDC2 = HelperFunctions.GetCoordPriv(ref h);
                    if ((HDC != null) && (HDC2 != null))
                    {
                        if ((!HelperFunctions.AreCoodinatesEqual(HDC, Plugin.ActorListDuration[index].outfit)) || 
                            (!HelperFunctions.AreCoodinatesEqual(HDC2, Plugin.ActorListDuration[index].outfit)))
                        {
                            HelperFunctions.SetCoord(ref h, Plugin.ActorListDuration[index].outfit);
                        }
                    }
                }
                else
                {
                    HumanDataCoordinate HDC = HelperFunctions.GetCoordPub(ref h);
                    HumanDataCoordinate HDC2 = HelperFunctions.GetCoordPriv(ref h);
                    if ((HDC != null) && (HDC2 != null))
                    {
                        if ((!HelperFunctions.AreCoodinatesEqual(HDC, Plugin.ActorListDuration[index].outfit)) ||
                            (!HelperFunctions.AreCoodinatesEqual(HDC2, Plugin.ActorListDuration[index].outfit)))
                        {
                            if (a == Plugin.MainPlayerActor)
                            {
                                Plugin.PutClothesOnThisGuy = Plugin.LatestHiPolyPlayerHuman;
                                Plugin.PutClothesOnFrames = 3;
                                HelperFunctions.EndPersistence(a, true, true);
                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HumanCloth), nameof(HumanCloth.UpdateClothesStateAll))]
    public static void DoUpdateClothesStateAll(ref HumanCloth __instance)
    {
        HelperFunctions.DoUpdateClothes(ref __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HumanCloth), nameof(HumanCloth.UpdateClothesAll))]
    public static void DoUpdateClothesAll(ref HumanCloth __instance)
    {
        HelperFunctions.DoUpdateClothes(ref __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HumanCloth), nameof(HumanCloth.SetClothesState), new[] { typeof(ChaFileDefine.ClothesKind), typeof(ChaFileDefine.ClothesState), typeof(bool) })]
    public unsafe static void DoSetClothesState(ref HumanCloth __instance, ChaFileDefine.ClothesKind kind, ChaFileDefine.ClothesState state, bool next = true)
    {
        HelperFunctions.DoUpdateClothes(ref __instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(HumanCloth), nameof(HumanCloth.SetClothesStateAll))]
    public unsafe static void DoSetClothesStateAll(ref HumanCloth __instance, ChaFileDefine.ClothesState state)
    {
        HelperFunctions.DoUpdateClothes(ref __instance);
    }
}

[HarmonyPatch(typeof(Human))]
public static class Hooks2
{
    private static bool _isPatched = false;
    private static Human HumanConstructorInstance = null;

    // Patching the constructor with (IntPtr) signature
    [HarmonyPatch(MethodType.Constructor, new Type[] { typeof(IntPtr) })]
    [HarmonyPostfix]
    public static void Postfix(ref Human __instance, IntPtr pointer)
    {
        if (_isPatched) return;

        _isPatched = true;
        try
        {
            if (HumanConstructorInstance == __instance) return;
            HumanConstructorInstance = __instance;
            if (!Plugin.AreClothesOn)
            {
                if (Plugin.LateUpdateRunning) return; //Try to prevent any looping.
                Plugin.LateUpdateMatchHuman.Add(new HumanAndFrames(__instance, 5));

                //Trying to get the Human in the Changing Room
                if (!__instance.hiPoly) return;
                if (SV.H.HScene.Active()) return;
                Actor mainA = __instance.data.About.FindMainActorInstance().Value;
                if (mainA == null) return;
                if (mainA == Plugin.MainPlayerActor)
                {
                    Plugin.LatestHiPolyPlayerHuman = __instance;
                }
            }
        }
        finally
        {
            _isPatched = false;
        }
    }
}

internal static class HelperFunctions
{
    public static void EndPersistence(Actor a, bool removeFromList, bool PutClothesOn)
    {
        //Remove from list
        if (removeFromList)
        {
            ActorAndHDC aaHDC = Plugin.ActorListDuration.Find(x => x.act == a);
            if (aaHDC != null) Plugin.ActorListDuration.Remove(aaHDC);
        }

        if (PutClothesOn)
        {
            //Put clothes back on (but not if they are in H)
            if (SV.H.HScene.Active())
            {
                bool foundChara = false;
                foreach (Actor chara in Plugin.ActorsInH)
                {
                    Actor charaMain = chara.FindMainActorInstance().Value;
                    if (charaMain == null)
                    {
                        if (chara == a)
                        {
                            foundChara = true;
                            break;
                        }
                    }
                    else
                    {
                        if (charaMain == a)
                        {
                            foundChara = true;
                            break;
                        }
                    }
                }
                if (foundChara)
                {
                    //Chara is currently in an H Scene.  Let's not bother them right now.
                    return;
                }
            }

            //Put clothes back on for real now
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
            {
                SVS_PostHClothingStatePersistenceCompatibility();
                Plugin.ignoreThisUpdate = true;
                a.chaCtrl.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Clothing, false);
            }
            SVS_PostHClothingStatePersistenceCompatibility();
            Plugin.ignoreThisUpdate = true;
            a.chaCtrl.cloth.UpdateClothesStateAll();
        }
    }

    public static void PutClothesOn(ref Human h)
    {
        if (!SV.H.HScene.Active())
        {
            foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
            {
                SVS_PostHClothingStatePersistenceCompatibility();
                Plugin.ignoreThisUpdate = true;
                h.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Clothing, false);
            }
            SVS_PostHClothingStatePersistenceCompatibility();
            Plugin.ignoreThisUpdate = true;
            h.cloth.UpdateClothesStateAll();
        }
    }

    public static void SVS_PostHClothingStatePersistenceCompatibility()
    {
        // Specify the full namespace and the assembly name.
        Type pluginType = Type.GetType("SVS_PostHClothingStatePersistence.Plugin, SVS_PostHClothingStatePersistence");

        if (pluginType != null)
        {
            // Get the static field and set it.
            var ignoreThisUpdateField = pluginType.GetField("ignoreThisUpdate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (ignoreThisUpdateField != null)
            {
                ignoreThisUpdateField.SetValue(null, true);
            }
        }
    }


    public static void DoUpdateClothes(ref HumanCloth hc)
    {
        if (Plugin.ignoreThisUpdate)
        {
            Plugin.ignoreThisUpdate = false;
            return;
        }
        if (hc == null) return;

        if (!SV.H.HScene.Active())
        {
            if (!Plugin.AreClothesOn)
            {
                Actor a = hc.human.data.About.FindMainActorInstance().Value;
                int index = Plugin.ActorListDuration.FindIndex(x => x.act == a);
                if (index != -1)
                {
                    Human h = hc.human;
                    if (!Plugin.AllowChangingOutfit.Value)
                    {
                        HumanDataCoordinate HDC = GetCoordPub(ref h);
                        if (HDC != null)
                        {
                            if (!AreCoodinatesEqual(HDC, Plugin.ActorListDuration[index].outfit))
                            {
                                SetCoord(ref h, Plugin.ActorListDuration[index].outfit);
                            }
                        }
                    }
                    else
                    {
                        HumanDataCoordinate HDC = GetCoordPub(ref h);
                        if (HDC != null)
                        {
                            if (!AreCoodinatesEqual(HDC, Plugin.ActorListDuration[index].outfit))
                            {
                                if (a != Plugin.MainPlayerActor)
                                {
                                    PutClothesOn(ref h);
                                    EndPersistence(a, true, true);
                                    return;
                                }
                            }
                        }
                    }

                    foreach (ChaFileDefine.ClothesKind kind in Enum.GetValues(typeof(ChaFileDefine.ClothesKind)))
                    {
                        SVS_PostHClothingStatePersistenceCompatibility();
                        Plugin.ignoreThisUpdate = true;
                        a.chaCtrl.cloth.SetClothesState(kind, ChaFileDefine.ClothesState.Naked, false);
                    }
                    SVS_PostHClothingStatePersistenceCompatibility();
                    Plugin.ignoreThisUpdate = true;
                    a.chaCtrl.cloth.UpdateClothesStateAll();
                }
            }
        }
    }

    public static HumanDataCoordinate GetCoordPriv(ref Human _selectedChara)
    {
        if (_selectedChara == null) return null;
        HumanDataCoordinate SavedHumanDataCoordinate = new HumanDataCoordinate();
        if (_selectedChara.coorde.nowCoordinate != null)
        {
            SavedHumanDataCoordinate.Copy(_selectedChara.coorde.nowCoordinate);
            return SavedHumanDataCoordinate;
        }
        return null;
    }

    public static HumanDataCoordinate GetCoordPub(ref Human _selectedChara)
    {
        if (_selectedChara == null) return null;
        HumanDataCoordinate SavedHumanDataCoordinate = new HumanDataCoordinate();
        if (_selectedChara.coorde.Now != null)
        {
            SavedHumanDataCoordinate.Copy(_selectedChara.coorde.Now);
            return SavedHumanDataCoordinate;
        }
        return null;
    }

    public static void SetCoord(ref Human _selectedChara, HumanDataCoordinate SHDC)
    {
        if (_selectedChara == null) return;
        if (SHDC == null) return;
        _selectedChara.coorde.SetNowCoordinate(SHDC);
        _selectedChara.ReloadCoordinate();
    }

    //I had to make this because the game always coppies coordinates so they return != if compared normally
    public static bool AreCoodinatesEqual(HumanDataCoordinate hdc1, HumanDataCoordinate hdc2)
    {
        //Compare Accessories
        //Return false if not the same number of accessories
        if (hdc1.Accessory.parts.Length != hdc2.Accessory.parts.Length) return false;
        for (int i = 0; i < hdc1.Accessory.parts.Length; i++)
        {
            //Return false if different IDs
            if (hdc1.Accessory.parts[i].id != hdc2.Accessory.parts[i].id) return false;
        }

        //Compare Clothing
        //Return false if not the same number of parts
        if (hdc1.Clothes.parts.Length != hdc2.Clothes.parts.Length) return false;
        for (int i = 0; i < hdc1.Clothes.parts.Length; i++)
        {
            //Return false if different IDs
            if (hdc1.Clothes.parts[i].id != hdc2.Clothes.parts[i].id) return false;
            if (hdc1.Clothes.parts[i].emblemeId != hdc2.Clothes.parts[i].emblemeId) return false;
            if (hdc1.Clothes.parts[i].emblemeId2 != hdc2.Clothes.parts[i].emblemeId2) return false;
            if (hdc1.Clothes.parts[i].paintInfos.Length != hdc2.Clothes.parts[i].paintInfos.Length) return false;
            for (int j = 0; j < hdc1.Clothes.parts[i].paintInfos.Length; j++)
            {
                if (hdc1.Clothes.parts[i].paintInfos[j].ID != hdc2.Clothes.parts[i].paintInfos[j].ID) return false;
                if (hdc1.Clothes.parts[i].paintInfos[j].color != hdc2.Clothes.parts[i].paintInfos[j].color) return false;
                if (hdc1.Clothes.parts[i].paintInfos[j].layout != hdc2.Clothes.parts[i].paintInfos[j].layout) return false;
            }
        }

        //Compare BodyMakeup
        //Return false if not the same number of paintInfos
        if (hdc1.BodyMakeup.paintInfos.Length != hdc2.BodyMakeup.paintInfos.Length) return false;
        for (int i = 0; i < hdc1.BodyMakeup.paintInfos.Length; i++)
        {
            //Return false if different IDs
            if (hdc1.BodyMakeup.paintInfos[i].layoutID != hdc2.BodyMakeup.paintInfos[i].layoutID) return false;
        }

        if (hdc1.BodyMakeup.nailInfo.colors.Length != hdc2.BodyMakeup.nailInfo.colors.Length) return false; //Dont think this one could happen but better check before assuming
        for (int i = 0; i < hdc1.BodyMakeup.nailInfo.colors.Length; i++)
        {
            //Return false if different colors
            if (hdc1.BodyMakeup.nailInfo.colors[i] != hdc2.BodyMakeup.nailInfo.colors[i]) return false;
        }

        if (hdc1.BodyMakeup.nailLegInfo.colors.Length != hdc2.BodyMakeup.nailLegInfo.colors.Length) return false; //Dont think this one could happen but better check before assuming
        for (int i = 0; i < hdc1.BodyMakeup.nailLegInfo.colors.Length; i++)
        {
            //Return false if different colors
            if (hdc1.BodyMakeup.nailLegInfo.colors[i] != hdc2.BodyMakeup.nailLegInfo.colors[i]) return false;
        }

        //Compare FaceMakeup
        //Return false if not the same number of parts
        if (hdc1.FaceMakeup.paintInfos.Length != hdc2.FaceMakeup.paintInfos.Length) return false;
        for (int i = 0; i < hdc1.FaceMakeup.paintInfos.Length; i++)
        {
            //Return false if different IDs
            if (hdc1.FaceMakeup.paintInfos[i].ID != hdc2.FaceMakeup.paintInfos[i].ID) return false;
        }
        if (hdc1.FaceMakeup.cheekColor != hdc2.FaceMakeup.cheekColor) return false;
        if (hdc1.FaceMakeup.cheekHighlightColor != hdc2.FaceMakeup.cheekHighlightColor) return false;
        if (hdc1.FaceMakeup.cheekId != hdc2.FaceMakeup.cheekId) return false;
        if (hdc1.FaceMakeup.cheekPos != hdc2.FaceMakeup.cheekPos) return false;
        if (hdc1.FaceMakeup.cheekRotation != hdc2.FaceMakeup.cheekRotation) return false;
        if (hdc1.FaceMakeup.cheekSize != hdc2.FaceMakeup.cheekSize) return false;
        if (hdc1.FaceMakeup.eyeshadowColor != hdc2.FaceMakeup.eyeshadowColor) return false;
        if (hdc1.FaceMakeup.eyeshadowId != hdc2.FaceMakeup.eyeshadowId) return false;
        if (hdc1.FaceMakeup.lipColor != hdc2.FaceMakeup.lipColor) return false;
        if (hdc1.FaceMakeup.lipHighlightColor != hdc2.FaceMakeup.lipHighlightColor) return false;
        if (hdc1.FaceMakeup.lipId != hdc2.FaceMakeup.lipId) return false;

        //Compare Hair
        if (hdc1.Hair.parts.Length != hdc2.Hair.parts.Length) return false;
        for (int i = 0; i < hdc1.Hair.parts.Length; i++)
        {
            if (hdc1.Hair.parts[i].id != hdc2.Hair.parts[i].id) return false;
            if (hdc1.Hair.parts[i].baseColor != hdc2.Hair.parts[i].baseColor) return false;
            if (hdc1.Hair.parts[i].bundleId != hdc2.Hair.parts[i].bundleId) return false;
            if (hdc1.Hair.parts[i].endColor != hdc2.Hair.parts[i].endColor) return false;
            if (hdc1.Hair.parts[i].glossColor != hdc2.Hair.parts[i].glossColor) return false;
            if (hdc1.Hair.parts[i].id != hdc2.Hair.parts[i].id) return false;
            if (hdc1.Hair.parts[i].innerColor != hdc2.Hair.parts[i].innerColor) return false;
            if (hdc1.Hair.parts[i].meshColor != hdc2.Hair.parts[i].meshColor) return false;
            if (hdc1.Hair.parts[i].outlineColor != hdc2.Hair.parts[i].outlineColor) return false;
            if (hdc1.Hair.parts[i].pos != hdc2.Hair.parts[i].pos) return false;
            if (hdc1.Hair.parts[i].rot != hdc2.Hair.parts[i].rot) return false;
            if (hdc1.Hair.parts[i].scl != hdc2.Hair.parts[i].scl) return false;
            if (hdc1.Hair.parts[i].shadowColor != hdc2.Hair.parts[i].shadowColor) return false;
            if (hdc1.Hair.parts[i].startColor != hdc2.Hair.parts[i].startColor) return false;
            if (hdc1.Hair.parts[i].useInner != hdc2.Hair.parts[i].useInner) return false;
            if (hdc1.Hair.parts[i].useMesh != hdc2.Hair.parts[i].useMesh) return false;
        }

        return true;
    }

    public static KeyValuePair<int, Actor> FindMainActorInstance(this Actor x) => x?.charFile.About.FindMainActorInstance() ?? default;

    public static KeyValuePair<int, Actor> FindMainActorInstance(this HumanDataAbout x) => x == null ? default : Manager.Game.Charas.AsManagedEnumerable().FirstOrDefault(y => x.dataID == y.Value.charFile.About.dataID);

    public static IEnumerable<KeyValuePair<T1, T2>> AsManagedEnumerable<T1, T2>(this Il2CppSystem.Collections.Generic.Dictionary<T1, T2> collection)
    {
        foreach (var val in collection)
            yield return new KeyValuePair<T1, T2>(val.Key, val.Value);
    }
}
