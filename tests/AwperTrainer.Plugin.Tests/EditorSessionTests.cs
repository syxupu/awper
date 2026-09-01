using AwperTrainer.Plugin;
using Xunit;

namespace AwperTrainer.Plugin.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void EditingSessionDeclaresAndNormalizesNameBeforeAnyAnchors()
    {
        var session = EditorSession.BeginEditing(7, "DE_MIRAGE", "  first_angle  ");

        Assert.True(session.IsEditing);
        Assert.Equal("first_angle", session.EditProfileName);
        Assert.Equal("de_mirage", session.Draft.MapName);
        Assert.Null(session.Draft.EditAnchor);
    }

    [Fact]
    public void SuccessfulSaveCanEndEditing()
    {
        var session = EditorSession.BeginEditing(7, "de_mirage", "first_angle");

        session.FinishEditing();

        Assert.False(session.IsEditing);
        Assert.Null(session.EditProfileName);
    }

    [Fact]
    public void TrainingSessionIsNotAnEditingSession()
    {
        var session = EditorSession.CreateTrainingSession(7, "de_mirage");

        Assert.False(session.IsEditing);
        Assert.Null(session.EditProfileName);
    }
}
