using Godot;
using System;

/// <summary>One campaign system and the authored operations it contains.</summary>
[GlobalClass]
public partial class CampaignSystemResource : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public Vector2 MapPosition { get; set; }
    [Export] public string[] PlanetMissionPaths { get; set; } = Array.Empty<string>();
    [Export] public string StoryMissionPath { get; set; } = "";
}
