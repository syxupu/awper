using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace RayTraceAPI;

// Compile-time copy of FUNPLAY-pro-CS2/Ray-Trace's public managed contract.
// The server-installed RayTraceApi assembly supplies these types at runtime.
[Flags]
public enum InteractionLayers : ulong
{
    None = 0,
    Solid = 0x1,
    Hitboxes = 0x2,
    Trigger = 0x4,
    Sky = 0x8,
    PlayerClip = 0x10,
    NPCClip = 0x20,
    BlockLOS = 0x40,
    BlockLight = 0x80,
    Ladder = 0x100,
    Pickup = 0x200,
    BlockSound = 0x400,
    NoDraw = 0x800,
    Window = 0x1000,
    PassBullets = 0x2000,
    WorldGeometry = 0x4000,
    Water = 0x8000,
    Slime = 0x10000,
    TouchAll = 0x20000,
    Player = 0x40000,
    NPC = 0x80000,
    Debris = 0x100000,
    Physics_Prop = 0x200000,
    NavIgnore = 0x400000,
    NavLocalIgnore = 0x800000,
    PostProcessingVolume = 0x1000000,
    UnusedLayer3 = 0x2000000,
    CarriedObject = 0x4000000,
    PushAway = 0x8000000,
    ServerEntityOnClient = 0x10000000,
    CarriedWeapon = 0x20000000,
    StaticLevel = 0x40000000,
    CsgoTeam1 = 0x80000000,
    CsgoTeam2 = 0x100000000,
    CsgoGrenadeClip = 0x200000000,
    CsgoDroneClip = 0x400000000,
    CsgoMoveable = 0x800000000,
    CsgoOpaque = 0x1000000000,
    CsgoMonster = 0x2000000000,
    CsgoThrownGrenade = 0x8000000000,

    MaskShotPhysics = Solid | PlayerClip | Window | PassBullets | Player | NPC | Physics_Prop,
    MaskShotHitbox = Hitboxes | Player | NPC,
    MaskShotFull = MaskShotPhysics | Hitboxes,
    MaskWorldOnly = Solid | Window | PassBullets,
    MaskGrenade = Solid | Window | Physics_Prop | PassBullets,
    MaskBrushOnly = Solid | Window,
    MaskPlayerMove = Solid | Window | PlayerClip | PassBullets,
    MaskNpcMove = Solid | Window | NPCClip | PassBullets
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct TraceOptions
{
    [FieldOffset(0)] public ulong InteractsWith;
    [FieldOffset(8)] public ulong InteractsExclude;
    [FieldOffset(16)] public int DrawBeam;

    public TraceOptions(InteractionLayers interactsWith, InteractionLayers interactsExclude = 0, bool drawBeam = false)
    {
        InteractsWith = (ulong)interactsWith;
        InteractsExclude = (ulong)interactsExclude;
        DrawBeam = drawBeam ? 1 : 0;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 44)]
public struct TraceResult
{
    [FieldOffset(0)] public float EndPosX;
    [FieldOffset(4)] public float EndPosY;
    [FieldOffset(8)] public float EndPosZ;
    [FieldOffset(16)] public nint HitEntity;
    [FieldOffset(24)] public float Fraction;
    [FieldOffset(28)] public int AllSolid;
    [FieldOffset(32)] public float NormalX;
    [FieldOffset(36)] public float NormalY;
    [FieldOffset(40)] public float NormalZ;

    public Vector3 EndPos => new(EndPosX, EndPosY, EndPosZ);
    public Vector3 Normal => new(NormalX, NormalY, NormalZ);
    public bool DidHit => Fraction < 1.0f;
    public bool IsAllSolid => AllSolid != 0;
}

public interface CRayTraceInterface
{
    bool TraceShape(Vector start, QAngle angles, CEntityInstance? ignore, TraceOptions options, out TraceResult result);
    bool TraceEndShape(Vector start, Vector end, CEntityInstance? ignore, TraceOptions options, out TraceResult result);
    bool TraceHullShape(Vector start, Vector end, Vector mins, Vector maxs, CEntityInstance? ignore, TraceOptions options, out TraceResult result);
    bool TraceShapeEx(Vector start, Vector end, nint filter, nint ray, out TraceResult result);
}
