using AwperTrainer.Core;

namespace AwperTrainer.Plugin;

internal sealed class EditorSession
{
    private EditorSession(int playerSlot, string mapName, string? editProfileName)
    {
        PlayerSlot = playerSlot;
        Draft = new ProfileDraft(mapName);
        EditProfileName = editProfileName;
    }

    public int PlayerSlot { get; }
    public ProfileDraft Draft { get; }
    public string? EditProfileName { get; private set; }
    public bool IsEditing => EditProfileName is not null;
    public PlayerAnchor? PlayerAnchor { get; set; }
    public AwperProfile? PendingProfile { get; set; }
    public AwperProfile? LoadedProfile { get; set; }
    public bool CameraVerified { get; set; }

    public static EditorSession BeginEditing(int playerSlot, string mapName, string profileName)
        => new(playerSlot, mapName, ProfileNames.Normalize(profileName));

    public static EditorSession CreateTrainingSession(int playerSlot, string mapName)
        => new(playerSlot, mapName, null);

    public void FinishEditing() => EditProfileName = null;
}
