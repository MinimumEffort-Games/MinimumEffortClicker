using CommunityToolkit.Mvvm.ComponentModel;

namespace MinimumEffortClicker.App;

public sealed class ShellViewModel : ObservableObject
{
    public string Title => "Minimum Effort Clicker";
    public string Status => "Stopped · Foundation build";
    public string Description => "The project foundation is ready. Clicking, recording and global hotkeys arrive in later milestones.";
}
