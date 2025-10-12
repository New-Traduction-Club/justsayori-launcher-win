namespace justsayo_win;

public enum OnLaunchBehavior
{
    DoNothing,
    Close,
    MinimizeToTray
}

public class SettingsModel
{
    public OnLaunchBehavior LaunchBehavior { get; set; } = OnLaunchBehavior.DoNothing;
}