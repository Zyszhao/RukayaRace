using UnityEngine;

namespace RukayaAviation;

public static class AviationArt
{
    public const string Commands = "UI/Commands/Aviation/";
    public const string Chemfuel = "Things/Item/Resource/Chemfuel";
    public const string ArrivalMode = Commands + "Rukaya_Aerial_ArriveMode";
    public const string Move = "Things/Mote/FeedbackGoto";
    public const string ReturnHome = "UI/Designators/HomeAreaOn";
    public const string Trade = "UI/Commands/Trade";
    public static string Command(string name) => Commands + "Rukaya_" + name;
    public static string TakeoffMode(bool vertical) => Command(vertical ? "VtolEnabled_true" : "VtolEnabled_false");

    // Square transparent source canvases must stay square when drawn. Individual
    // stores retain their own silhouette and padding; no width stretching.
    public static Vector2 StoreSize(StoreKind kind) => kind switch
    {
        StoreKind.Bomb or StoreKind.ClusterBomb => Vector2.one * 1.4f,
        StoreKind.IncendiaryBomb or StoreKind.Cannon => Vector2.one * 1.6f,
        StoreKind.Rocket => Vector2.one * 1.5f,
        StoreKind.GroundMissile => Vector2.one * 1.8f,
        StoreKind.ActiveMatterMissile => Vector2.one * 2.5f,
        StoreKind.Tank => Vector2.one * 2f,
        StoreKind.CargoPod or StoreKind.PassengerPod => Vector2.one * 1.8f,
        _ => new Vector2(0.65f, 1.4f)
    };
    public static bool HasVisibleStore(Hardpoint mount) => mount.kind != StoreKind.Empty
        && (mount.rounds > 0 || mount.kind is not (StoreKind.Bomb or StoreKind.IncendiaryBomb or StoreKind.ClusterBomb or StoreKind.GroundMissile or StoreKind.ActiveMatterMissile));

    public static string WeaponIcon(AttackKind kind) => Hardpoint.Texture(Ordnance.Store(kind));
}
