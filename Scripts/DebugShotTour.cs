using Godot;
using System.Collections.Generic;

/// <summary>Temporary debug bootstrap: screenshots the hangar and campaign map, then quits.</summary>
public partial class DebugShotTour : Node2D
{
    public override void _Ready()
    {
        var tour = new DebugTourRunner();
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, tour);
    }
}

public partial class DebugTourRunner : Node
{
    const string OutDir = "res://debug_shots";

    record Stop(string Name, System.Action<SceneTree> Go);

    static readonly List<Stop> Stops = new()
    {
        new Stop("flagship", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/Flagship.tscn");
        }),
        new Stop("hangar", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/Hangar.tscn");
        }),
        new Stop("recruitment", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/Recruitment.tscn");
        }),
        new Stop("shipyard", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/Shipyard.tscn");
        }),
        new Stop("memorial", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/Memorial.tscn");
        }),
        new Stop("campaign_map", tree =>
        {
            CampaignData.Initialize();
            tree.ChangeSceneToFile("res://Scenes/CampaignMap.tscn");
        }),
    };

    int _index = -1;
    double _elapsed;

    public override void _Ready() => DirAccess.MakeDirRecursiveAbsolute(OutDir);

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < 1.2)
            return;
        _elapsed = 0;

        if (_index >= 0)
        {
            var image = GetViewport().GetTexture().GetImage();
            image.SavePng($"{OutDir}/{Stops[_index].Name}.png");
        }
        _index++;
        if (_index >= Stops.Count)
        {
            GetTree().Quit();
            return;
        }
        Stops[_index].Go(GetTree());
    }
}
