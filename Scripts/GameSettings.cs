using Godot;

/// <summary>How the battle camera frames a turn while it plays out.</summary>
public enum BattleCameraMode
{
    /// <summary>Keeps every ship in view.</summary>
    Overview,
    /// <summary>Moves in on the ships that are fighting; your squadron when nobody is.</summary>
    Action,
}

/// <summary>Player preferences kept on the device between sessions.</summary>
public static class GameSettings
{
    const string Path = "user://settings.cfg";

    static ConfigFile _file;

    static ConfigFile File
    {
        get
        {
            if (_file == null)
            {
                _file = new ConfigFile();
                _file.Load(Path); // a missing file just means defaults
            }
            return _file;
        }
    }

    public static BattleCameraMode CameraMode
    {
        get => (BattleCameraMode)(int)File.GetValue("battle", "camera", (int)BattleCameraMode.Overview);
        set
        {
            File.SetValue("battle", "camera", (int)value);
            File.Save(Path);
        }
    }
}
