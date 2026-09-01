namespace BotControllerApi;

// Binary-compatible subset of the upstream BotControllerApi contract used by this plugin.
// Deployment must use the BotControllerApi.dll shipped with CS2-Bot-Controller.
using System.Runtime.InteropServices;

public enum LockKind { All = 0, Aim = 1, Weapon = 2 }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MovementSnapshot
{
    public float OriginX, OriginY, OriginZ;
    public float VelX, VelY, VelZ;
    public float Pitch, Yaw, Roll;
    public uint EntityFlags;
    public byte MoveType;
    public byte Pad0, Pad1, Pad2;
    public ulong Buttons;
    public ulong Buttons1;
    public ulong Buttons2;
    public float DuckAmount;
    public float DuckSpeed;
    public float LadderNormalX, LadderNormalY, LadderNormalZ;
    public byte Ducked;
    public byte Ducking;
    public byte DesiresDuck;
    public byte ActualMoveType;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ReplayTick
{
    public MovementSnapshot Pre;
    public MovementSnapshot Post;
    public int WeaponDefIndex;
    public uint NumSubtick;
    public uint EventFlags;
    public int EventWeaponDefIndex;
    public uint EventDropVectorFlags;
    public float EventDropTargetX, EventDropTargetY, EventDropTargetZ;
    public float EventDropVelocityX, EventDropVelocityY, EventDropVelocityZ;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SubtickMove
{
    public float When;
    public uint Button;
    public float Pressed;
    public float AnalogForward;
    public float AnalogLeft;
    public float PitchDelta;
    public float YawDelta;
}

public interface IBotControllerApi
{
    int AbiVersion { get; }
    bool Lock(int slot, LockKind kind);
    bool Unlock(int slot, LockKind kind);
    bool IsLocked(int slot, LockKind kind);
    bool LoadReplay(int slot, ReplayTick[] ticks, SubtickMove[] subs);
    bool SetReplayPawn(int slot, nint pawn);
    bool StartReplay(int slot, bool loop = false);
    bool StopReplay(int slot);
    bool IsReplaying(int slot);
    bool SwitchBotWeapon(int slot, int defIndex);
    int BotActiveWeaponDef(int slot);
    long StartUsercmdMovement(int slot, float forwardMove, float leftMove);
    bool UpdateUsercmdMovement(int slot, long movementId, float forwardMove, float leftMove);
    bool CancelUsercmdMovement(int slot, long movementId);
    long StartUsercmdSuppression(int slot, ulong buttonMask);
    bool CancelUsercmdSuppression(int slot, long suppressionId);
}
