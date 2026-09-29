using Godot;
using System.Collections.Generic;
using System.Linq;
using static SignalUi;

/// <summary>
/// Runs the whole battle: planning (time frozen, player drags arcs), then
/// simultaneous execution where every fighter flies its arc and guns fire
/// continuously at anything in the forward cone.
/// </summary>
public partial class BattleManager : Node2D
{
    public enum Phase { Planning, Executing, GameOver }
    public enum ManeuverIcon
    {
        Normal,
        UTurnLeft,
        UTurnRight,
        BreakTurnLeft,
        BreakTurnRight,
        EngineBoost,
        RotatingGuns,
        EmergencyThrusters,
        SnapTurnLeft,
        SnapTurnRight,
        PursuitBurn,
        HunterLock,
        EcmJink,
        GhostRun,
        EvasiveDodgeLeft,
        EvasiveDodgeRight,
        SensorScramble,
    }

    public const float ExecTime = 1.6f;      // sim-seconds per simultaneous move
    public const float ExecGrace = 0.45f;    // let last bullets land
    public const float CombatTimeScale = 0.8f; // slow-motion factor during execution (visual only)
    // The battlefield is deliberately larger than the fixed game viewport. A
    // Camera2D exposes it as a tactical space players can pan around and zoom.
    public const float ArenaW = 2400f;
    public const float ArenaH = 1350f;
    const float ViewportW = 1152f;
    const float ViewportH = 648f;
    const float InitialZoom = 0.82f * 1.15f;
    const float MinZoom = 0.55f;
    const float MaxZoom = 1.7f;
    const float CameraPanSpeed = 900f;
    // Keep a little empty space around the battlefield, then extend that
    // space to encompass ships and their planning ghosts as they leave it.
    const float OffMapCameraPadding = 320f;
    public const float BulletSpeed = 900f;
    public const float ShipCollisionRadius = 18f;
    public const int AsteroidMaxScrapeDamage = 14;
    public const float NebulaSpeedMultiplier = 0.55f;
    public const float NebulaAccuracyMultiplier = 0.55f;
    // Movement and the planning preview share this fixed integration step so
    // terrain crossings produce the same position at every preview time slice.
    const float MovementSimulationStep = ExecTime / 240f;
    const float EscortReinforcementProgress = 0.60f;

    public static BattleManager Instance { get; private set; }

    public Phase CurrentPhase { get; private set; } = Phase.Planning;
    public readonly List<Fighter> PlayerFighters = new();
    public readonly List<Fighter> EnemyFighters = new();
    public BattleMapDefinition Map { get; private set; }
    public ObjectiveShip EscortShip { get; private set; }
    Fighter _priorityTarget;
    public Fighter PriorityTarget => _priorityTarget;
    CampaignMission _debriefMission;
    CampaignResolution _debriefResolution;
    CampaignMission CurrentMission => CampaignData.SelectedMission;

    // Planning state (read by the overlay for drawing).
    public Fighter Selected;
    Fighter _pinnedTarget;
    public Fighter PinnedTarget => _pinnedTarget != null && _pinnedTarget.IsAlive ? _pinnedTarget : null;

    Node2D _fighterLayer, _bulletLayer;
    Camera2D _camera;
    CanvasLayer _ui;
    Label _phaseLabel, _mapLabel, _hintLabel;
    Button _executeBtn;
    int _turn = 1;
    int _firstDamageTurn;
    public int TurnNumber => _turn;
    float _execT;
    float _graceT;
    float _movementSimulationPending;
    bool _isPanning;
    Fighter _draggingGhost;
    Fighter _hunterLockingFighter;
    Fighter _sensorScramblingFighter;
    Fighter _lastOverlapGhost;
    Fighter _lastOverlapTarget;
    bool _chooseGhostOnNextOverlap;
    bool _escortReinforcementsSpawned;
    readonly HashSet<Fighter> _selectedPlayerFightersThisTurn = new();

    public List<Fighter> GetTeam(int team) => team == 0 ? PlayerFighters : EnemyFighters;

    /// <summary>Marks the first combat turn where any fighter is damaged.</summary>
    public bool IsOpeningDamageTurn()
    {
        if (_firstDamageTurn == 0)
            _firstDamageTurn = _turn;
        return _firstDamageTurn == _turn;
    }

    /// <summary>Returns the movement multiplier for the ship's current space.</summary>
    public float MovementSpeedMultiplier(Vector2 position) =>
        IsInNebula(position) ? NebulaSpeedMultiplier : 1f;

    /// <summary>
    /// Predicts where a maneuver will be at a fraction of the execution clock,
    /// using the same terrain-aware route progression as live movement.
    /// </summary>
    public void PredictExecutionPoint(Fighter fighter, ManeuverType maneuver, float turn, float distance,
        float executionFraction, out Vector2 position, out float heading)
    {
        float maxTime = Mathf.Clamp(executionFraction, 0f, 1f) * ExecTime;
        float routeProgress = 0f;
        float elapsed = 0f;
        position = fighter.Position;
        heading = fighter.Heading;

        if (maneuver == ManeuverType.Normal)
        {
            float maxTurn = Mathf.DegToRad(fighter.GetNormalTurnLimitDegrees(distance));
            turn = Mathf.Clamp(turn, -maxTurn, maxTurn);
        }

        while (elapsed < maxTime)
        {
            float step = Mathf.Min(MovementSimulationStep, maxTime - elapsed);
            routeProgress = Fighter.AdvanceManeuverProgress(routeProgress, step, MovementSpeedMultiplier(position));
            Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, routeProgress,
                out position, out heading);
            elapsed += step;
        }
    }

    /// <summary>Fills evenly spaced execution-time samples for a terrain-aware maneuver.</summary>
    public void PredictExecutionPath(Fighter fighter, ManeuverType maneuver, float turn, float distance,
        Vector2[] positions, float[] headings)
    {
        if (positions.Length != headings.Length || positions.Length < 2)
            throw new System.ArgumentException("Prediction paths need matching position and heading samples.");

        if (maneuver == ManeuverType.Normal)
        {
            float maxTurn = Mathf.DegToRad(fighter.GetNormalTurnLimitDegrees(distance));
            turn = Mathf.Clamp(turn, -maxTurn, maxTurn);
        }

        float routeProgress = 0f;
        float elapsed = 0f;
        Vector2 position = fighter.Position;
        float heading = fighter.Heading;
        positions[0] = position;
        headings[0] = heading;
        int finalSample = positions.Length - 1;
        for (int sample = 1; sample <= finalSample; sample++)
        {
            float targetTime = ExecTime * sample / finalSample;
            while (elapsed < targetTime)
            {
                float step = Mathf.Min(MovementSimulationStep, targetTime - elapsed);
                routeProgress = Fighter.AdvanceManeuverProgress(routeProgress, step, MovementSpeedMultiplier(position));
                Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, routeProgress,
                    out position, out heading);
                elapsed += step;
            }
            positions[sample] = position;
            headings[sample] = heading;
        }
    }

    /// <summary>Checks whether a bullet segment is stopped by an asteroid.</summary>
    public bool ShotBlocked(Vector2 from, Vector2 to) => SegmentHitsAsteroid(from, to, 0f);

    /// <summary>Checks a proposed maneuver against every asteroid's fatal core.</summary>
    public bool PathHitsAsteroid(Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        const int samples = 32;
        Vector2 previous = fighter.Position;
        for (int i = 1; i <= samples; i++)
        {
            Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, i / (float)samples,
                out Vector2 next, out _);
            if (SegmentHitsAsteroid(previous, next, 0f))
                return true;
            previous = next;
        }
        return false;
    }

    /// <summary>
    /// Returns the closest clearance between a ship's collision envelope and
    /// an asteroid along a proposed maneuver. A negative result means the
    /// ship would scrape; values at or below -ShipCollisionRadius enter the
    /// asteroid's fatal core.
    /// </summary>
    public float PathAsteroidClearance(Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        const int samples = 32;
        float closestClearance = float.PositiveInfinity;
        Vector2 previous = fighter.Position;
        for (int i = 1; i <= samples; i++)
        {
            Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, i / (float)samples,
                out Vector2 next, out _);
            foreach (TerrainFeature asteroid in Map.Terrain.Where(feature => feature.Type == TerrainFeatureType.Asteroid))
            {
                float clearance = DistanceToSegment(previous, next, asteroid.Position) - asteroid.Radius - ShipCollisionRadius;
                closestClearance = Mathf.Min(closestClearance, clearance);
            }
            previous = next;
        }
        return closestClearance;
    }

    /// <summary>Total guaranteed hull damage from grazing asteroid edges along a non-fatal planned route.</summary>
    public int PathScrapeDamage(Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        if (PathHitsAsteroid(fighter, maneuver, turn, distance))
            return 0;

        const int samples = 32;
        var peakDamageByAsteroid = new Dictionary<TerrainFeature, int>();
        Vector2 previous = fighter.Position;
        for (int i = 1; i <= samples; i++)
        {
            Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, i / (float)samples,
                out Vector2 next, out _);
            foreach (TerrainFeature asteroid in Map.Terrain.Where(feature => feature.Type == TerrainFeatureType.Asteroid))
            {
                int damage = ScrapeDamageForContact(asteroid, previous, next);
                if (damage > peakDamageByAsteroid.GetValueOrDefault(asteroid))
                    peakDamageByAsteroid[asteroid] = damage;
            }
            previous = next;
        }
        return peakDamageByAsteroid.Values.Sum();
    }

    /// <summary>Fraction of a planned route that passes through speed-reducing nebula gas.</summary>
    public float NebulaPathFraction(Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        const int samples = 24;
        int insideSamples = 0;
        for (int i = 1; i <= samples; i++)
        {
            Fighter.ManeuverPoint(maneuver, fighter.Position, fighter.Heading, turn, distance, i / (float)samples,
                out Vector2 point, out _);
            if (IsInNebula(point))
                insideSamples++;
        }
        return insideSamples / (float)samples;
    }

    void ResolveShipAsteroidContacts(Fighter fighter, Vector2 from, Vector2 to)
    {
        // Crossing the visible rock itself remains fatal. The ship-radius
        // envelope around it is now a costly but passable scrape band.
        if (SegmentHitsAsteroid(from, to, 0f))
        {
            fighter.RecordAsteroidHit();
            fighter.TakeDamage(fighter.Hp + fighter.Shield);
            return;
        }

        foreach (TerrainFeature asteroid in Map.Terrain.Where(feature => feature.Type == TerrainFeatureType.Asteroid))
        {
            int contactDamage = ScrapeDamageForContact(asteroid, from, to);
            int additionalDamage = fighter.UpdateAsteroidScrapeDamage(asteroid, contactDamage);
            if (additionalDamage > 0)
            {
                fighter.RecordAsteroidHit();
                fighter.TakeDamage(additionalDamage);
                SpawnFlash(fighter.Position);
                if (!fighter.IsAlive)
                    return;
            }
        }
    }

    bool SegmentHitsAsteroid(Vector2 from, Vector2 to, float extraRadius)
    {
        foreach (TerrainFeature feature in Map.Terrain)
        {
            if (feature.Type == TerrainFeatureType.Asteroid && SegmentIntersectsCircle(from, to, feature.Position, feature.Radius + extraRadius))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A shallow edge touch deals very little damage; pushing farther into the
    /// ship-radius scrape band ramps up quadratically to a heavy hull strike.
    /// </summary>
    static int ScrapeDamageForContact(TerrainFeature asteroid, Vector2 from, Vector2 to)
    {
        float centerDistance = DistanceToSegment(from, to, asteroid.Position);
        float penetration = asteroid.Radius + ShipCollisionRadius - centerDistance;
        if (penetration <= 0f)
            return 0;
        float severity = Mathf.Clamp(penetration / ShipCollisionRadius, 0f, 1f);
        return Mathf.Max(1, Mathf.RoundToInt(AsteroidMaxScrapeDamage * severity * severity));
    }

    bool IsInNebula(Vector2 point)
    {
        foreach (TerrainFeature feature in Map.Terrain)
        {
            if (feature.Type == TerrainFeatureType.Nebula && point.DistanceSquaredTo(feature.Position) <= feature.Radius * feature.Radius)
                return true;
        }
        return false;
    }

    /// <summary>Whether a shot's line of fire passes through a nebula, reducing its accuracy.</summary>
    public bool ShotPassesNebula(Vector2 from, Vector2 to)
    {
        foreach (TerrainFeature feature in Map.Terrain)
        {
            if (feature.Type == TerrainFeatureType.Nebula && SegmentIntersectsCircle(from, to, feature.Position, feature.Radius))
                return true;
        }
        return false;
    }

    static bool SegmentIntersectsCircle(Vector2 from, Vector2 to, Vector2 center, float radius)
        => DistanceToSegment(from, to, center) <= radius;

    static float DistanceToSegment(Vector2 from, Vector2 to, Vector2 point)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared < 0.0001f)
            return from.DistanceTo(point);
        float progress = Mathf.Clamp((point - from).Dot(segment) / lengthSquared, 0f, 1f);
        return (from + segment * progress).DistanceTo(point);
    }

    /// <summary>1-based battle turn number.</summary>
    public int Turn => _turn;

    public override void _Ready()
    {
        Instance = this;
        Engine.TimeScale = 1f; // in case a previous battle was torn down mid-execution
        RenderingServer.SetDefaultClearColor(Bg);

        BuildCamera();

        Map = BattleMaps.ForCurrentBattle();

        _fighterLayer = new Node2D();
        _bulletLayer = new Node2D();
        AddChild(new TerrainLayer(Map.Terrain));
        AddChild(_fighterLayer);
        AddChild(_bulletLayer);
        AddChild(new BattleOverlay());

        // Player pilots come from the selection screen; campaign missions
        // provide a curated enemy wing rather than a random encounter.
        List<Pilot> squad = GameSetup.PlayerPilots.Count >= 1 && GameSetup.PlayerPilots.All(p => p.CanDeploy)
            ? GameSetup.PlayerPilots
            : PilotRoster.Living.Where(p => p.CanDeploy).Take(3).ToList();
        if (squad.Count < 1)
        {
            GD.PushError("A battle requires at least one ready pilot.");
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                GameSetup.IsTestBattle ? "res://Scenes/TestBattleSelect.tscn" : "res://Scenes/SelectScreen.tscn");
            return;
        }
        for (int i = 0; i < squad.Count; i++)
        {
            ShipType playerType = squad[i].Ship;
            BattleSpawn playerSpawn = Map.PlayerSpawns[i];
            SpawnFighter(0, playerSpawn.Position, Mathf.DegToRad(playerSpawn.HeadingDegrees), playerType, squad[i]);
        }

        ShipType[] enemySquad = GameSetup.IsTestBattle
            ? new[] { ShipTypes.All[0], ShipTypes.All[1], ShipTypes.All[2] }
            : CurrentMission?.EnemySquad ?? new[] { ShipTypes.Scout, ShipTypes.Scout, ShipTypes.Scout };
        for (int i = 0; i < enemySquad.Length; i++)
        {
            ShipType enemyType = enemySquad[i];
            BattleSpawn enemySpawn = Map.EnemySpawns[i];
            float statMultiplier = GameSetup.IsTestBattle ? 1f : CurrentMission?.EnemyStatMultiplier ?? 1f;
            SpawnFighter(1, enemySpawn.Position, Mathf.DegToRad(enemySpawn.HeadingDegrees), enemyType,
                statMultiplier: statMultiplier);
        }
        if (!GameSetup.IsTestBattle && CurrentMission?.Objective == MissionObjective.DestroyTarget)
            _priorityTarget = EnemyFighters.FirstOrDefault();
        if (!GameSetup.IsTestBattle && CurrentMission?.Objective == MissionObjective.EscortShip)
        {
            EscortShip = new ObjectiveShip();
            BattleSpawn escortSpawn = Map.EscortSpawn ?? new BattleSpawn(360, ArenaH / 2f, 0f);
            Vector2 destination = Map.EscortDestination == Vector2.Zero
                ? new Vector2(ArenaW - 350f, ArenaH / 2f)
                : Map.EscortDestination;
            float destinationRadius = Map.EscortDestinationRadius > 0f ? Map.EscortDestinationRadius : 95f;
            EscortShip.Setup(escortSpawn.Position, destination, destinationRadius);
            _fighterLayer.AddChild(EscortShip);
        }

        BuildUi();
        Selected = null;
    }

    void BuildCamera()
    {
        _camera = new Camera2D
        {
            Name = "BattleCamera",
            Position = new Vector2(ArenaW / 2f, ArenaH / 2f),
            Zoom = Vector2.One * InitialZoom,
            // Camera limits must stay wider than the tactical bounds below:
            // a fighter is allowed to fly beyond the authored map.
            LimitLeft = -1_000_000,
            LimitTop = -1_000_000,
            LimitRight = 1_000_000,
            LimitBottom = 1_000_000,
        };
        AddChild(_camera);
    }

    void SpawnFighter(int team, Vector2 pos, float heading, ShipType type, Pilot pilot = null, float statMultiplier = 1f)
    {
        var f = new Fighter();
        f.ApplyType(type);
        f.ApplyPilot(pilot);
        if (team == 0 && !GameSetup.IsTestBattle)
            f.MaxHp += CampaignData.SquadronLevel * 3;
        if (team == 1)
            f.ApplyCombatStatMultiplier(statMultiplier);
        f.Setup(team, pos, heading, type.GetSkin(team));
        _fighterLayer.AddChild(f);
        GetTeam(team).Add(f);
    }

    void SpawnEscortReinforcements()
    {
        _escortReinforcementsSpawned = true;
        ShipType[] enemyWave = GameSetup.IsTestBattle
            ? new[] { ShipTypes.All[0], ShipTypes.All[1], ShipTypes.All[2] }
            : CurrentMission?.EnemySquad ?? new[] { ShipTypes.Scout, ShipTypes.Scout, ShipTypes.Scout };
        BattleSpawn[] spawnPoints = Map.EscortReinforcementSpawns;
        if (spawnPoints == null || spawnPoints.Length == 0 || enemyWave.Length == 0)
            return;

        float statMultiplier = GameSetup.IsTestBattle ? 1f : CurrentMission?.EnemyStatMultiplier ?? 1f;
        int spawnCount = Mathf.Min(enemyWave.Length, spawnPoints.Length);
        for (int i = 0; i < spawnCount; i++)
        {
            BattleSpawn spawn = spawnPoints[i];
            SpawnFighter(1, spawn.Position, Mathf.DegToRad(spawn.HeadingDegrees), enemyWave[i],
                statMultiplier: statMultiplier);
            SpawnFlash(spawn.Position);
        }
        _hintLabel.Text = $"REINFORCEMENTS // {spawnCount} HOSTILES ENTERING THE JUMP CORRIDOR";
    }

    void BuildUi()
    {
        _ui = new CanvasLayer();
        AddChild(_ui);
        var ui = _ui;

        _phaseLabel = Text("", 14, TextBright, 4);
        _phaseLabel.Position = new Vector2(16, 12);
        ui.AddChild(_phaseLabel);

        _mapLabel = Text($"{Map.DisplayName.ToUpper()} · {CurrentMission?.ObjectiveLabel.ToUpper() ?? "TACTICAL ENGAGEMENT"}\n{Map.Briefing}", 9, Muted, 2);
        _mapLabel.Position = new Vector2(16, 36);
        ui.AddChild(_mapLabel);

        _hintLabel = Text(
            "Drag a ghost ship to set its maneuver  ·  release to lock it in  ·  right-click: undo last  ·  Space: execute" +
            "\nWith a ghost selected, hover an enemy to inspect  ·  click it to pin/unpin  ·  middle-drag or arrows: pan  ·  scroll: zoom",
            10, Dim);
        _hintLabel.Position = new Vector2(16, ViewportH - 92f);
        ui.AddChild(_hintLabel);
        _hintLabel.Text = "Drag a ghost ship to set its maneuver  ·  release to lock it in  ·  right-click: undo last  ·  Space: execute" +
            "\nWith a ghost selected, click an enemy to target/release it  ·  middle-drag or arrows: pan  ·  scroll: zoom";

        _executeBtn = FlatButton("EXECUTE TURN  >", 12);
        _executeBtn.Position = new Vector2(ViewportW / 2f - 85f, ViewportH - 54f);
        _executeBtn.Size = new Vector2(170, 38);
        _executeBtn.Disabled = true;
        _executeBtn.Pressed += TryStartExecution;
        ui.AddChild(_executeBtn);

    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        UpdateCameraPan(dt);

        if (CurrentPhase == Phase.Executing)
            UpdateExecution(dt);

        bool ready = CanExecuteTurn();
        _executeBtn.Disabled = !ready;
        _phaseLabel.Text = CurrentPhase switch
        {
            Phase.Planning => $"Turn {_turn}  —  PLANNING  ({PlayerFighters.Count(f => f.IsAlive && f.PlannedTurnAngleRadians.HasValue)} maneuvers adjusted)",
            Phase.Executing => $"Turn {_turn}  —  EXECUTING",
            _ => $"Turn {_turn}  —  BATTLE OVER",
        };
        if (CurrentPhase == Phase.Planning)
            _phaseLabel.Text += $"  // {SelectedPlayerFightersThisTurnCount()}/{PlayerFighters.Count(f => f.IsAlive)} ships selected";
        if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped)
            _phaseLabel.Text += $"  ·  TRANSPORT {EscortShip.DistanceRemaining:0} TO JUMP";
        _hintLabel.Visible = CurrentPhase == Phase.Planning;
    }

    void UpdateCameraPan(float dt)
    {
        Vector2 direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) direction.X -= 1f;
        if (Input.IsKeyPressed(Key.Right)) direction.X += 1f;
        if (Input.IsKeyPressed(Key.Up)) direction.Y -= 1f;
        if (Input.IsKeyPressed(Key.Down)) direction.Y += 1f;
        if (direction == Vector2.Zero)
            return;

        _camera.Position += direction.Normalized() * CameraPanSpeed * dt / _camera.Zoom.X;
        ClampCameraPosition();
    }

    int SelectedPlayerFightersThisTurnCount() =>
        PlayerFighters.Count(f => f.IsAlive && _selectedPlayerFightersThisTurn.Contains(f));

    bool CanExecuteTurn() =>
        CurrentPhase == Phase.Planning &&
        PlayerFighters.Any(f => f.IsAlive) &&
        PlayerFighters.Where(f => f.IsAlive).All(_selectedPlayerFightersThisTurn.Contains);

    void SelectPlayerFighter(Fighter fighter)
    {
        Selected = fighter;
        _selectedPlayerFightersThisTurn.Add(fighter);
    }

    /// <summary>Selects the first living player fighter, or the one after the current selection.</summary>
    void CycleSelectedPlayerFighter()
    {
        List<Fighter> livingFighters = PlayerFighters.Where(fighter => fighter.IsAlive).ToList();
        if (livingFighters.Count == 0)
        {
            Selected = null;
            return;
        }

        int selectedIndex = livingFighters.IndexOf(Selected);
        SelectPlayerFighter(livingFighters[(selectedIndex + 1) % livingFighters.Count]);
        ResetOverlapCycle();
    }

    public override void _Input(InputEvent @event)
    {
        // Handle this before Control focus navigation can consume Tab for the
        // execute button. A tab always moves the tactical selection forward.
        if (CurrentPhase == Phase.Planning && @event is InputEventKey key &&
            key.Pressed && !key.Echo && key.Keycode == Key.Tab)
        {
            CycleSelectedPlayerFighter();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && _isPanning)
        {
            _camera.Position -= motion.Relative / _camera.Zoom;
            ClampCameraPosition();
            return;
        }

        if (@event is InputEventMouseMotion && _draggingGhost != null)
        {
            UpdateGhostDrag(_draggingGhost, GetGlobalMousePosition());
            return;
        }

        if (@event is InputEventMouseButton cameraMouse)
        {
            if (cameraMouse.ButtonIndex == MouseButton.Middle)
            {
                _isPanning = cameraMouse.Pressed;
                return;
            }

            if (cameraMouse.Pressed &&
                cameraMouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ZoomAtCursor(cameraMouse.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f);
                return;
            }
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Space && CanExecuteTurn())
                TryStartExecution();
            return;
        }

        if (CurrentPhase != Phase.Planning || @event is not InputEventMouseButton mb)
            return;

        if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
        {
            if (_draggingGhost != null)
            {
                _orderHistory.Remove(_draggingGhost);
                _orderHistory.Add(_draggingGhost);
                _draggingGhost = null;
            }
            return;
        }

        if (!mb.Pressed)
            return;

        Vector2 pos = GetGlobalMousePosition();
        if (_sensorScramblingFighter != null)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _sensorScramblingFighter = null;
                _hintLabel.Text = "Sensor Scramble cancelled.";
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                Fighter target = EnemyFighters.Where(f => f.IsAlive && f.Position.DistanceTo(pos) < 30f)
                    .OrderBy(f => f.Position.DistanceSquaredTo(pos)).FirstOrDefault();
                if (target != null && _sensorScramblingFighter.ApplySensorScramble(target))
                {
                    _hintLabel.Text = $"SENSOR SCRAMBLE // {target.Type.DisplayName} accuracy reduced for 2 turns.";
                    _sensorScramblingFighter = null;
                }
                return;
            }
        }
        if (_hunterLockingFighter != null)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _hunterLockingFighter = null;
                _hintLabel.Text = "Hunter Lock cancelled.";
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                Fighter target = EnemyFighters.Where(f => f.IsAlive && f.Position.DistanceTo(pos) < 30f)
                    .OrderBy(f => f.Position.DistanceSquaredTo(pos)).FirstOrDefault();
                if (target != null && _hunterLockingFighter.SetHunterLock(target))
                {
                    _hintLabel.Text = $"HUNTER LOCK // {_hunterLockingFighter.Pilot?.Callsign ?? "STRIKER"} locked {target.Type.DisplayName}.";
                    _hunterLockingFighter = null;
                }
                return;
            }
        }
        if (mb.ButtonIndex == MouseButton.Left && TryActivateManeuverIcon(pos))
        {
            ResetOverlapCycle();
            return;
        }

        Fighter clickedTarget = EnemyFighters
            .Where(f => f.IsAlive && f.Position.DistanceTo(pos) < 34f)
            .OrderBy(f => f.Position.DistanceSquaredTo(pos))
            .FirstOrDefault();
        Fighter clickedShip = PlayerFighters
            .Where(f => f.IsAlive && f.Position.DistanceTo(pos) < 30f)
            .OrderBy(f => f.Position.DistanceSquaredTo(pos))
            .FirstOrDefault();
        Fighter clickedGhost = PlayerFighters
            .Where(f => f.IsAlive && GetGhostEndpoint(f).DistanceTo(pos) < 32f)
            .OrderBy(f => GetGhostEndpoint(f).DistanceSquaredTo(pos))
            .FirstOrDefault();

        if (mb.ButtonIndex == MouseButton.Left)
        {
            // A real ship changes selection only; its ghost remains the
            // drag handle used to alter that ship's maneuver.
            if (clickedShip != null)
            {
                ResetOverlapCycle();
                SelectPlayerFighter(clickedShip);
                return;
            }

            if (clickedTarget != null && clickedGhost != null)
            {
                bool sameOverlap = clickedTarget == _lastOverlapTarget && clickedGhost == _lastOverlapGhost;
                bool chooseGhost = sameOverlap && _chooseGhostOnNextOverlap;
                _lastOverlapTarget = clickedTarget;
                _lastOverlapGhost = clickedGhost;
                _chooseGhostOnNextOverlap = !chooseGhost;

                if (chooseGhost)
                {
                    SelectPlayerFighter(clickedGhost);
                    BeginGhostDrag(clickedGhost, pos);
                    _hintLabel.Text = "OVERLAP // ghost selected. Click the overlap again to inspect the enemy.";
                }
                else
                {
                    TogglePinnedTarget(clickedTarget);
                    _hintLabel.Text = _pinnedTarget == clickedTarget
                        ? "OVERLAP // enemy pinned. Click the overlap again to grab the ghost."
                        : "OVERLAP // enemy released. Click the overlap again to grab the ghost.";
                }
                return;
            }

            ResetOverlapCycle();
            if (clickedTarget != null)
            {
                TogglePinnedTarget(clickedTarget);
                return;
            }
            if (clickedGhost != null)
            {
                SelectPlayerFighter(clickedGhost);
                BeginGhostDrag(clickedGhost, pos);
            }
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            UndoLastOrder();
        }
    }

    void TogglePinnedTarget(Fighter target)
    {
        _pinnedTarget = _pinnedTarget == target ? null : target;
        _hintLabel.Text = _pinnedTarget == null
            ? "TARGETING ASSIST // target released. Click an enemy to target it."
            : $"TARGETING ASSIST // {_pinnedTarget.Type.DisplayName} pinned for preview. Click again to release.";
    }

    void ResetOverlapCycle()
    {
        _lastOverlapGhost = null;
        _lastOverlapTarget = null;
        _chooseGhostOnNextOverlap = false;
    }

    Vector2 GetGhostEndpoint(Fighter fighter)
    {
        Fighter.ManeuverPoint(fighter.PlannedManeuver, fighter.Position, fighter.Heading, fighter.PlannedTurnAngleRadians ?? 0f,
            fighter.PlannedPathDistance, 1f, out Vector2 endpoint, out _);
        return endpoint;
    }

    void SetGhostManeuver(Fighter fighter, Vector2 endpoint)
    {
        fighter.PlannedManeuver = ManeuverType.Normal;
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;

        float bearing = Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi);
        float maxTurn = Mathf.DegToRad(fighter.PlannedNormalTurnLimitDegrees);
        float turn = Mathf.Clamp(2f * bearing, -maxTurn, maxTurn);
        float absTurn = Mathf.Abs(turn);
        float chordFraction = absTurn < 0.001f ? 1f : 2f * Mathf.Sin(absTurn / 2f) / absTurn;
        fighter.SetPlannedMoveDistance(offset.Length() / chordFraction);

        // The Reckless penalty depends on the resulting throttle, so clamp once
        // more after the requested endpoint has established the move distance.
        maxTurn = Mathf.DegToRad(fighter.PlannedNormalTurnLimitDegrees);
        turn = Mathf.Clamp(2f * bearing, -maxTurn, maxTurn);
        absTurn = Mathf.Abs(turn);
        chordFraction = absTurn < 0.001f ? 1f : 2f * Mathf.Sin(absTurn / 2f) / absTurn;
        fighter.SetPlannedMoveDistance(offset.Length() / chordFraction);
        fighter.PlannedTurnAngleRadians = turn;
    }

    void BeginNormalManeuver(Fighter fighter, Vector2 endpoint)
    {
        fighter.PlannedManeuver = ManeuverType.Normal;
        _draggingGhost = fighter;
        SetGhostManeuver(fighter, endpoint);
    }

    /// <summary>Re-grabbing a ghost preserves its selected maneuver instead of reverting to normal flight.</summary>
    void BeginGhostDrag(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        UpdateGhostDrag(fighter, endpoint);
    }

    /// <summary>Only maneuvers with an aimable endpoint respond to ghost dragging.</summary>
    void UpdateGhostDrag(Fighter fighter, Vector2 endpoint)
    {
        switch (fighter.PlannedManeuver)
        {
            case ManeuverType.Normal:
                SetGhostManeuver(fighter, endpoint);
                break;
            case ManeuverType.EngineBoost:
                SetEngineBoostManeuver(fighter, endpoint);
                break;
            case ManeuverType.PursuitBurn:
                SetPursuitBurnManeuver(fighter, endpoint);
                break;
            case ManeuverType.EcmJink:
                SetEcmJinkManeuver(fighter, endpoint);
                break;
            case ManeuverType.GhostRun:
                SetGhostRunManeuver(fighter, endpoint);
                break;
            case ManeuverType.EmergencyThrusters:
                SetEmergencyThrustersManeuver(fighter, endpoint);
                break;
            // Fixed maneuvers (U-turn, break turn, and rotating guns) retain
            // their selected route while their ghost is re-grabbed.
        }
    }

    void SetEngineBoostManeuver(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        float bearing = Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi);
        fighter.PlanEngineBoost(2f * bearing);
    }

    void BeginEngineBoost(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        SetEngineBoostManeuver(fighter, endpoint);
    }

    void SetPursuitBurnManeuver(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        float bearing = Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi);
        fighter.PlanPursuitBurn(2f * bearing);
    }

    void BeginPursuitBurn(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        SetPursuitBurnManeuver(fighter, endpoint);
    }

    void SetEcmJinkManeuver(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        fighter.PlanEcmJink(2f * Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi));
    }

    void BeginEcmJink(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        SetEcmJinkManeuver(fighter, endpoint);
    }

    void SetGhostRunManeuver(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        fighter.PlanGhostRun(2f * Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi));
    }

    void BeginGhostRun(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        SetGhostRunManeuver(fighter, endpoint);
    }

    void SetEmergencyThrustersManeuver(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        float bearing = Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi);
        fighter.PlanEmergencyThrusters(2f * bearing);
    }

    void BeginEmergencyThrusters(Fighter fighter, Vector2 endpoint)
    {
        _draggingGhost = fighter;
        SetEmergencyThrustersManeuver(fighter, endpoint);
    }

    /// <summary>Returns the compact maneuver controls shown beside a selected ghost ship.</summary>
    public List<ManeuverIcon> GetManeuverIcons(Fighter fighter)
    {
        var icons = new List<ManeuverIcon> { ManeuverIcon.Normal };
        if (fighter.HasAbility(ShipAbility.UTurn) && fighter.IsManeuverReady(ManeuverType.UTurn))
        {
            icons.Add(ManeuverIcon.UTurnLeft);
            icons.Add(ManeuverIcon.UTurnRight);
        }

        if (fighter.HasAbility(ShipAbility.BreakTurn) && fighter.IsManeuverReady(ManeuverType.BreakTurn))
        {
            icons.Add(ManeuverIcon.BreakTurnLeft);
            icons.Add(ManeuverIcon.BreakTurnRight);
        }
        if (fighter.HasAbility(ShipAbility.EngineBoost) && fighter.IsManeuverReady(ManeuverType.EngineBoost))
            icons.Add(ManeuverIcon.EngineBoost);
        if (fighter.HasAbility(ShipAbility.RotatingGuns) && fighter.IsManeuverReady(ManeuverType.RotatingGuns))
            icons.Add(ManeuverIcon.RotatingGuns);
        if (fighter.HasAbility(ShipAbility.EmergencyThrusters) && fighter.IsManeuverReady(ManeuverType.EmergencyThrusters))
            icons.Add(ManeuverIcon.EmergencyThrusters);
        if (fighter.HasAbility(ShipAbility.SnapTurn) && fighter.IsManeuverReady(ManeuverType.SnapTurn))
        {
            icons.Add(ManeuverIcon.SnapTurnLeft);
            icons.Add(ManeuverIcon.SnapTurnRight);
        }
        if (fighter.HasAbility(ShipAbility.PursuitBurn) && fighter.IsManeuverReady(ManeuverType.PursuitBurn))
            icons.Add(ManeuverIcon.PursuitBurn);
        if (fighter.HasAbility(ShipAbility.HunterLock) && fighter.HunterLockCooldownTurns == 0)
            icons.Add(ManeuverIcon.HunterLock);
        if (fighter.HasAbility(ShipAbility.EcmJink) && fighter.IsManeuverReady(ManeuverType.EcmJink))
            icons.Add(ManeuverIcon.EcmJink);
        if (fighter.HasAbility(ShipAbility.GhostRun) && fighter.IsManeuverReady(ManeuverType.GhostRun))
            icons.Add(ManeuverIcon.GhostRun);
        if (fighter.HasAbility(ShipAbility.EvasiveDodge) && fighter.IsManeuverReady(ManeuverType.EvasiveDodge))
        {
            icons.Add(ManeuverIcon.EvasiveDodgeLeft);
            icons.Add(ManeuverIcon.EvasiveDodgeRight);
        }
        if (fighter.HasAbility(ShipAbility.SensorScramble) && fighter.SensorScrambleCooldownTurns == 0)
            icons.Add(ManeuverIcon.SensorScramble);
        return icons;
    }

    /// <summary>World-space hit bounds for the icon strip placed beside the ghost preview.</summary>
    public Rect2 GetManeuverIconBounds(Fighter fighter, int index, int iconCount)
    {
        const float iconSize = 34f;
        const float iconGap = 5f;
        Vector2 topLeft = GetGhostEndpoint(fighter) + new Vector2(30f, -(iconCount * (iconSize + iconGap) - iconGap) / 2f);
        return new Rect2(topLeft + new Vector2(0f, index * (iconSize + iconGap)), Vector2.One * iconSize);
    }

    public string GetManeuverIconSymbol(ManeuverIcon icon) => icon switch
    {
        ManeuverIcon.Normal => ">",
        ManeuverIcon.UTurnLeft => "U<",
        ManeuverIcon.UTurnRight => "U>",
        ManeuverIcon.BreakTurnLeft => "B<",
        ManeuverIcon.BreakTurnRight => "B>",
        ManeuverIcon.EngineBoost => ">>",
        ManeuverIcon.RotatingGuns => "O",
        ManeuverIcon.EmergencyThrusters => "!",
        ManeuverIcon.SnapTurnLeft => "S<",
        ManeuverIcon.SnapTurnRight => "S>",
        ManeuverIcon.PursuitBurn => ">>>",
        ManeuverIcon.HunterLock => "L",
        ManeuverIcon.EcmJink => "J",
        ManeuverIcon.GhostRun => "G",
        ManeuverIcon.EvasiveDodgeLeft => "D<",
        ManeuverIcon.EvasiveDodgeRight => "D>",
        ManeuverIcon.SensorScramble => "X",
        _ => "?",
    };

    public string GetManeuverIconName(Fighter fighter, ManeuverIcon icon) => icon switch
    {
        ManeuverIcon.Normal => "Normal maneuver - drag the ghost to set heading and distance",
        ManeuverIcon.UTurnLeft => "U-turn left",
        ManeuverIcon.UTurnRight => "U-turn right",
        ManeuverIcon.BreakTurnLeft => "Break turn left - wide 180 degree turn",
        ManeuverIcon.BreakTurnRight => "Break turn right - wide 180 degree turn",
        ManeuverIcon.EngineBoost => "Engine boost - drag to set heading",
        ManeuverIcon.RotatingGuns => "Rotating guns",
        ManeuverIcon.EmergencyThrusters => "Emergency thrusters - drag to set heading",
        ManeuverIcon.SnapTurnLeft => "Snap turn left - tight 145 degree turn",
        ManeuverIcon.SnapTurnRight => "Snap turn right - tight 145 degree turn",
        ManeuverIcon.PursuitBurn => "Pursuit burn - drag to set heading",
        ManeuverIcon.HunterLock => "Hunter Lock - select an enemy target",
        ManeuverIcon.EcmJink => "ECM Jink - drag to set heading",
        ManeuverIcon.GhostRun => "Ghost Run - drag to set heading",
        ManeuverIcon.EvasiveDodgeLeft => "Evasive Dodge left - 135 degree turn, then a short forward burst (+40% evasion)",
        ManeuverIcon.EvasiveDodgeRight => "Evasive Dodge right - 135 degree turn, then a short forward burst (+40% evasion)",
        ManeuverIcon.SensorScramble => "Sensor Scramble - select an enemy target",
        _ => string.Empty,
    };

    public Color GetManeuverIconColor(ManeuverIcon icon) => icon switch
    {
        ManeuverIcon.UTurnLeft or ManeuverIcon.UTurnRight => new Color(0.62f, 0.48f, 1f),
        ManeuverIcon.BreakTurnLeft or ManeuverIcon.BreakTurnRight => new Color(1f, 0.62f, 0.3f),
        ManeuverIcon.EngineBoost => new Color(0.3f, 1f, 0.75f),
        ManeuverIcon.RotatingGuns => new Color(1f, 0.78f, 0.32f),
        ManeuverIcon.EmergencyThrusters => new Color(1f, 0.36f, 0.3f),
        ManeuverIcon.SnapTurnLeft or ManeuverIcon.SnapTurnRight => new Color(0.95f, 0.55f, 1f),
        ManeuverIcon.PursuitBurn => new Color(0.4f, 1f, 0.7f),
        ManeuverIcon.HunterLock => new Color(1f, 0.85f, 0.3f),
        ManeuverIcon.EcmJink => new Color(0.45f, 0.7f, 1f),
        ManeuverIcon.GhostRun => new Color(0.6f, 0.95f, 1f),
        ManeuverIcon.EvasiveDodgeLeft or ManeuverIcon.EvasiveDodgeRight => new Color(0.35f, 0.9f, 1f),
        ManeuverIcon.SensorScramble => new Color(0.75f, 0.55f, 1f),
        _ => new Color(0.45f, 0.9f, 1f),
    };

    bool TryActivateManeuverIcon(Vector2 position)
    {
        if (Selected == null || !Selected.IsAlive)
            return false;

        List<ManeuverIcon> icons = GetManeuverIcons(Selected);
        for (int i = 0; i < icons.Count; i++)
        {
            if (!GetManeuverIconBounds(Selected, i, icons.Count).HasPoint(position))
                continue;

            Fighter fighter = Selected;
            switch (icons[i])
            {
                case ManeuverIcon.Normal:
                    BeginNormalManeuver(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.UTurnLeft:
                    fighter.PlanUTurn(-1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.UTurnRight:
                    fighter.PlanUTurn(1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.BreakTurnLeft:
                    fighter.PlanBreakTurn(-1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.BreakTurnRight:
                    fighter.PlanBreakTurn(1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.EngineBoost:
                    BeginEngineBoost(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.RotatingGuns:
                    fighter.PlanRotatingGuns();
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.EmergencyThrusters:
                    BeginEmergencyThrusters(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.SnapTurnLeft:
                    fighter.PlanSnapTurn(-1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.SnapTurnRight:
                    fighter.PlanSnapTurn(1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.PursuitBurn:
                    BeginPursuitBurn(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.HunterLock:
                    _hunterLockingFighter = fighter;
                    _hintLabel.Text = "HUNTER LOCK // click an enemy ship to lock it. Right-click to cancel.";
                    break;
                case ManeuverIcon.EcmJink:
                    BeginEcmJink(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.GhostRun:
                    BeginGhostRun(fighter, GetGhostEndpoint(fighter));
                    break;
                case ManeuverIcon.EvasiveDodgeLeft:
                    fighter.PlanEvasiveDodge(-1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.EvasiveDodgeRight:
                    fighter.PlanEvasiveDodge(1f);
                    ConfirmManeuver(fighter);
                    break;
                case ManeuverIcon.SensorScramble:
                    _sensorScramblingFighter = fighter;
                    _hintLabel.Text = "SENSOR SCRAMBLE // click an enemy ship. Right-click to cancel.";
                    break;
            }
            return true;
        }
        return false;
    }

    void ConfirmManeuver(Fighter fighter)
    {
        _orderHistory.Remove(fighter);
        _orderHistory.Add(fighter);
    }

    void ZoomAtCursor(float multiplier)
    {
        Vector2 worldUnderCursor = GetGlobalMousePosition();
        float zoom = Mathf.Clamp(_camera.Zoom.X * multiplier, MinZoom, MaxZoom);
        _camera.Zoom = Vector2.One * zoom;
        _camera.Position += worldUnderCursor - GetGlobalMousePosition();
        ClampCameraPosition();
    }

    void ClampCameraPosition()
    {
        float halfWidth = ViewportW / (2f * _camera.Zoom.X);
        float halfHeight = ViewportH / (2f * _camera.Zoom.Y);

        float minX = -OffMapCameraPadding;
        float minY = -OffMapCameraPadding;
        float maxX = ArenaW + OffMapCameraPadding;
        float maxY = ArenaH + OffMapCameraPadding;

        // The map itself remains the default panning area. Once a ship or a
        // player ghost crosses its edge, expand that area so it can always be
        // brought back into view and given a new maneuver.
        foreach (Fighter fighter in PlayerFighters.Concat(EnemyFighters))
        {
            if (!fighter.IsAlive)
                continue;

            IncludeInCameraBounds(fighter.Position, ref minX, ref minY, ref maxX, ref maxY);
            if (fighter.Team == 0)
                IncludeInCameraBounds(GetGhostEndpoint(fighter), ref minX, ref minY, ref maxX, ref maxY);
        }

        _camera.Position = new Vector2(
            Mathf.Clamp(_camera.Position.X, minX + halfWidth, maxX - halfWidth),
            Mathf.Clamp(_camera.Position.Y, minY + halfHeight, maxY - halfHeight));
    }

    static void IncludeInCameraBounds(Vector2 point, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        minX = Mathf.Min(minX, point.X - OffMapCameraPadding);
        minY = Mathf.Min(minY, point.Y - OffMapCameraPadding);
        maxX = Mathf.Max(maxX, point.X + OffMapCameraPadding);
        maxY = Mathf.Max(maxY, point.Y + OffMapCameraPadding);
    }

    readonly List<Fighter> _orderHistory = new();

    /// <summary>Right-click: unlock the most recently confirmed order and reselect that fighter.</summary>
    void UndoLastOrder()
    {
        for (int i = _orderHistory.Count - 1; i >= 0; i--)
        {
            Fighter f = _orderHistory[i];
            _orderHistory.RemoveAt(i);
            if (f.IsAlive && f.PlannedTurnAngleRadians.HasValue)
            {
                f.ClearPlannedManeuver();
                SelectPlayerFighter(f);
                return;
            }
        }
        Selected = null;
    }

    void TryStartExecution()
    {
        if (!CanExecuteTurn())
            return;

        // Plan the objective first so enemy interceptors can lead its real next
        // movement instead of chasing its previous position.
        if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped)
            EscortShip.PlanEscapeMove(this);

        var enemyTargets = PlayerFighters.Where(f => f.IsAlive).Cast<Fighter>().ToList();
        if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped)
            enemyTargets.Add(EscortShip);
        MissionAITuning aiTuning = CurrentMission?.EnemyAITuning ?? new MissionAITuning();
        foreach (Fighter e in EnemyFighters.Where(f => f.IsAlive))
            EnemyAI.Plan(e, enemyTargets, EnemyFighters, aiTuning);
        foreach (Fighter f in AllAlive())
            f.BeginExecute();
        if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped)
            EscortShip.BeginExecute();

        Selected = null;
        _orderHistory.Clear();
        _execT = 0f;
        _graceT = 0f;
        _movementSimulationPending = 0f;
        CurrentPhase = Phase.Executing;
        // Slow the whole execution down a touch. Scaling engine time (not the
        // sim constants) keeps balance identical: cooldowns, bullets and
        // movement all stretch together.
        Engine.TimeScale = CombatTimeScale;
    }

    IEnumerable<Fighter> AllAlive() =>
        PlayerFighters.Concat(EnemyFighters).Where(f => f.IsAlive);

    void UpdateExecution(float dt)
    {
        // The execution clock is fixed. Nebulae slow a ship's progress along
        // its maneuver, which leaves it short of its planned endpoint when
        // the clock expires rather than making the whole turn run longer.
        float movementDt = Mathf.Min(dt, Mathf.Max(0f, ExecTime - _execT));
        if (movementDt > 0f)
        {
            _movementSimulationPending += movementDt;
            while (_movementSimulationPending >= MovementSimulationStep)
            {
                AdvanceExecutionMovement(MovementSimulationStep);
                _movementSimulationPending = Mathf.Max(0f, _movementSimulationPending - MovementSimulationStep);
            }

            UpdateFiring(movementDt);
            _execT += movementDt;

            // ExecTime is an exact number of fixed slices. The fallback keeps
            // a final floating-point remainder from leaving a ship short.
            if (_execT >= ExecTime && _movementSimulationPending > 0f)
            {
                AdvanceExecutionMovement(_movementSimulationPending);
                _movementSimulationPending = 0f;
            }
        }

        if (_execT < ExecTime)
            return;

        _graceT += dt - movementDt;
        if (_graceT >= ExecGrace)
            EndExecution();
    }

    /// <summary>Advances every moving ship one terrain-aware simulation slice.</summary>
    void AdvanceExecutionMovement(float dt)
    {
        foreach (Fighter f in AllAlive().ToList())
        {
            Vector2 previousPosition = f.Position;
            f.AdvanceExecute(dt, MovementSpeedMultiplier(previousPosition));
            ResolveShipAsteroidContacts(f, previousPosition, f.Position);
        }

        if (EscortShip == null || !EscortShip.IsAlive || EscortShip.Escaped)
            return;

        Vector2 previousEscortPosition = EscortShip.Position;
        EscortShip.AdvanceExecute(dt, MovementSpeedMultiplier(previousEscortPosition));
        ResolveShipAsteroidContacts(EscortShip, previousEscortPosition, EscortShip.Position);
        EscortShip.UpdateDestinationProgress(previousEscortPosition);
        if (!_escortReinforcementsSpawned && EscortShip.EscapeProgress >= EscortReinforcementProgress)
            SpawnEscortReinforcements();
    }

    const float BarrageShotInterval = 0.07f; // gap between shots inside one barrage

    void UpdateFiring(float dt)
    {
        foreach (Fighter f in AllAlive())
        {
            if (!f.CanFire)
                continue;
            f.Cooldown -= dt;

            // Continue an in-progress barrage first.
            if (f.BarrageShotsLeft > 0)
            {
                f.BarrageShotTimer -= dt;
                if (f.BarrageShotTimer <= 0f)
                {
                    if (f.BarrageTarget != null && f.BarrageTarget.IsAlive)
                    {
                        FireShotAtSelectedTarget(f, f.BarrageTarget);
                        f.BarrageShotsLeft--;
                        f.BarrageShotTimer += BarrageShotInterval;
                    }
                    else
                    {
                        f.BarrageShotsLeft = 0; // target destroyed mid-burst
                    }
                }
                continue;
            }

            if (f.Cooldown > 0f)
                continue;

            IEnumerable<Fighter> possibleTargets = GetTeam(1 - f.Team)
                .Where(t => t.IsAlive)
                .Where(t => f.Position.DistanceTo(t.Position) <= f.EffectiveFireRange)
                .Where(t => Mathf.Abs(Mathf.Wrap((t.Position - f.Position).Angle() - f.Heading, -Mathf.Pi, Mathf.Pi)) <= Mathf.DegToRad(f.EffectiveFireConeDeg));
            Fighter target;
            if (f.Team == 1)
            {
                if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped &&
                    f.Position.DistanceTo(EscortShip.Position) <= f.EffectiveFireRange &&
                    Mathf.Abs(Mathf.Wrap((EscortShip.Position - f.Position).Angle() - f.Heading, -Mathf.Pi, Mathf.Pi)) <= Mathf.DegToRad(f.EffectiveFireConeDeg))
                    possibleTargets = possibleTargets.Append(EscortShip);
                target = EnemyAI.SelectTarget(f, possibleTargets,
                    CurrentMission?.EnemyAITuning ?? new MissionAITuning());
            }
            else
            {
                target = possibleTargets.OrderBy(t => f.Position.DistanceSquaredTo(t.Position)).FirstOrDefault();
            }
            if (target != null)
            {
                f.Cooldown = f.EffectiveFireCooldown;
                f.BarrageTarget = target;
                f.BarrageShotsLeft = GD.RandRange(f.BarrageMin, f.BarrageMax);
                f.BarrageShotTimer = 0f;
                f.PlayFireAnimation(f.BarrageShotsLeft * BarrageShotInterval);
            }
        }
    }

    void FireShotAtSelectedTarget(Fighter shooter, Fighter target)
    {
        if (target is ObjectiveShip objective)
            FireShotAtObjective(shooter, objective);
        else
            FireShot(shooter, target);
    }

    void FireShot(Fighter shooter, Fighter target)
    {
        shooter.ShotsFired++;
        Vector2 nose = shooter.Position + Vector2.FromAngle(shooter.Heading) * 20f;
        float travelTime = nose.DistanceTo(target.Position) / BulletSpeed;
        Vector2 aim = target.Position + target.Velocity * travelTime; // basic lead
        Vector2 dir = (aim - nose).Normalized();

        float terrainAccuracy = ShotPassesNebula(nose, target.Position) ? NebulaAccuracyMultiplier : 1f;
        float hitChance = Mathf.Clamp(shooter.EffectiveAccuracyAgainst(target) * terrainAccuracy * (1f - target.EffectiveEvasion), 0.05f, 0.95f);
        bool hits = GD.Randf() < hitChance;
        // Hits from the outer edge of the envelope feed the Long Shot perk.
        if (hits && shooter.Position.DistanceTo(target.Position) >= shooter.EffectiveFireRange * Perks.EdgeRangeFraction)
            shooter.EdgeHits++;
        if (hits)
        {
            if (shooter.HasAbility(ShipAbility.SuppressionFire))
                target.ApplySuppression(shooter.Type.SuppressionTurnPenaltyDeg);
            dir = dir.Rotated((float)GD.RandRange(-1.0, 1.0) * Mathf.DegToRad(0.8f));
        }
        else
        {
            // Deflect a missed shot so it visibly streaks past the target.
            float side = GD.Randf() < 0.5f ? -1f : 1f;
            dir = dir.Rotated(side * Mathf.DegToRad((float)GD.RandRange(3.5, 7.0)));
        }

        var b = new Bullet();
        Color col = shooter.Team == 0 ? new Color(0.45f, 0.9f, 1f) : new Color(1f, 0.4f, 0.32f);
        b.Init(shooter, nose, dir * BulletSpeed, shooter.EffectiveFireRange * 1.4f, col, shooter.ShotDamage, hits);
        _bulletLayer.AddChild(b);
    }

    void FireShotAtObjective(Fighter shooter, ObjectiveShip target)
    {
        shooter.ShotsFired++;
        Vector2 nose = shooter.Position + Vector2.FromAngle(shooter.Heading) * 20f;
        Vector2 dir = (target.Position - nose).Normalized();
        bool hits = GD.Randf() < Mathf.Clamp(shooter.EffectiveAccuracyAgainst(null) * 0.9f, 0.1f, 0.9f);
        if (!hits)
            dir = dir.Rotated((float)GD.RandRange(3.5, 7.0) * Mathf.DegToRad(GD.Randf() < 0.5f ? -1 : 1));
        var bullet = new Bullet();
        bullet.InitObjective(shooter, target, nose, dir * BulletSpeed, shooter.EffectiveFireRange * 1.4f,
            new Color(1f, 0.4f, 0.32f), shooter.ShotDamage, hits);
        _bulletLayer.AddChild(bullet);
    }

    public void SpawnFlash(Vector2 pos)
    {
        _bulletLayer.AddChild(new Flash { Position = pos });
    }

    void EndExecution()
    {
        Engine.TimeScale = 1f;
        foreach (Node child in _bulletLayer.GetChildren())
            if (child is Bullet)
                child.QueueFree();

        bool playersAlive = PlayerFighters.Any(f => f.IsAlive);
        bool enemiesAlive = EnemyFighters.Any(f => f.IsAlive);
        bool objectiveSuccess = CurrentMission?.Objective switch
        {
            MissionObjective.DestroyTarget => _priorityTarget != null && !_priorityTarget.IsAlive,
            MissionObjective.EscortShip => EscortShip != null && EscortShip.Escaped,
            _ => !enemiesAlive,
        };
        bool objectiveFailed = CurrentMission?.Objective == MissionObjective.EscortShip && (EscortShip == null || !EscortShip.IsAlive);
        if (!playersAlive || objectiveSuccess || objectiveFailed || (!enemiesAlive && CurrentMission?.Objective != MissionObjective.EscortShip))
        {
            _debriefMission = CurrentMission;
            CurrentPhase = Phase.GameOver;
            _executeBtn.Visible = false;
            bool won = playersAlive && objectiveSuccess; // mutual destruction counts as a defeat
            (string headline, Color headlineColor) = won
                ? ("VICTORY", Positive)
                : enemiesAlive ? ("DEFEAT", Negative) : ("MUTUAL DESTRUCTION", Body);
            if (!GameSetup.IsTestBattle)
                _debriefResolution = CampaignData.ResolveSelectedMission(won);
            List<PilotResult> results = GameSetup.IsTestBattle
                ? new List<PilotResult>()
                : BattleResolution.Resolve(PlayerFighters, won, PlayerFighters.Concat(EnemyFighters).ToList());
            if (!GameSetup.IsTestBattle)
                CampaignData.SaveCampaign();
            ShowDebrief(results, won, headline, headlineColor);
            return;
        }

        _turn++;
        _selectedPlayerFightersThisTurn.Clear();
        foreach (Fighter f in AllAlive())
        {
            ManeuverType completedManeuver = f.PlannedManeuver;
            f.ClearPlannedManeuver();
            f.AdvanceManeuverCooldowns();
            f.StartManeuverCooldown(completedManeuver);
            f.AdvanceTacticalEffects();
            f.RegenerateShield();
        }
        CurrentPhase = Phase.Planning;
        Selected = null;
    }

    // Fixed column widths of the debrief ledger (icon/pilot/fate/kills/xp; advancement fills the rest).
    const float ColIcon = 44f, ColPilot = 205f, ColFate = 250f, ColKills = 70f, ColXp = 185f;

    /// <summary>
    /// Post-battle report: full-screen ledger — outcome headline, summary
    /// numerals, one aligned row per pilot, strategic results, and an explicit
    /// handoff when a frame choice is waiting in the hangar.
    /// </summary>
    void ShowDebrief(List<PilotResult> results, bool won, string headline, Color headlineColor)
    {
        // The report replaces the battle HUD entirely.
        _phaseLabel.Visible = false;
        _mapLabel.Visible = false;
        _hintLabel.Visible = false;
        _ui.AddChild(new ColorRect { Size = new Vector2(ViewportW, ViewportH), Color = Bg });

        var page = new VBoxContainer { Position = new Vector2(56, 36), Size = new Vector2(1040, 578) };
        page.AddThemeConstantOverride("separation", 10);
        _ui.AddChild(page);

        string context = GameSetup.IsTestBattle
            ? "TEST BATTLE REPORT"
            : $"MISSION DEBRIEF · {(_debriefMission?.PlanetName ?? Map.DisplayName).ToUpper()} · {_debriefMission?.ObjectiveLabel ?? "TACTICAL ENGAGEMENT"}";
        page.AddChild(Text(context, 9, Muted, 4));
        page.AddChild(Text(headline, 40, headlineColor, 8));

        if (results.Count > 0)
        {
            var kpis = new HBoxContainer();
            kpis.AddThemeConstantOverride("separation", 48);
            page.AddChild(kpis);
            AddKpi(kpis, results.Sum(r => r.Kills).ToString(), "KILLS", TextBright);
            AddKpi(kpis, $"+{results.Sum(r => r.XpGained)}", "SQUAD XP", TextBright);
            int credits = _debriefResolution?.CreditsAwarded ?? 0;
            if (credits > 0)
                AddKpi(kpis, $"+{credits}", "CREDITS", Positive);
            int lost = results.Count(r => !r.Survived);
            if (lost > 0)
                AddKpi(kpis, lost.ToString(), "LOST", Negative);

            page.AddChild(BuildLedgerHeader());
            page.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = new Color(0.47f, 0.71f, 0.9f, 0.38f) });
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            };
            page.AddChild(scroll);
            var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rows.AddThemeConstantOverride("separation", 0);
            scroll.AddChild(rows);
            foreach (PilotResult r in results)
            {
                rows.AddChild(BuildLedgerRow(r, won));
                rows.AddChild(new ColorRect { CustomMinimumSize = new Vector2(0, 1), Color = Hairline });
            }
        }
        else
        {
            page.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        }

        if (!GameSetup.IsTestBattle && _debriefResolution != null)
        {
            var strategic = new HBoxContainer();
            strategic.AddThemeConstantOverride("separation", 44);
            page.AddChild(strategic);
            foreach (ControlChange change in _debriefResolution.ControlChanges)
            {
                bool secured = change.After == PlanetControl.Alliance;
                AddKeyValue(strategic, "STRATEGIC", $"{change.PlanetName.ToUpper()} NOW {change.After.ToString().ToUpper()}", secured ? Positive : Negative);
            }
            if (_debriefResolution.CreditsAwarded > 0)
                AddKeyValue(strategic, "PAYOUT", $"+{_debriefResolution.CreditsAwarded} CREDITS", Positive);
            if (_debriefResolution.CapturedSystemName != null)
                AddKeyValue(strategic, "SYSTEM CAPTURED", $"{_debriefResolution.CapturedSystemName.ToUpper()} · +{CampaignData.SystemCaptureCredits} CREDITS", Positive);
        }

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 14);
        page.AddChild(footer);
        List<Pilot> decisions = GameSetup.IsTestBattle
            ? new List<Pilot>()
            : PilotRoster.Living.Where(p => p.NeedsCareerChoice).ToList();
        if (decisions.Count > 0)
        {
            string label = decisions.Count == 1
                ? $"1 DECISION AWAITS — {decisions[0].Callsign.ToUpper()}"
                : $"{decisions.Count} PILOT DECISIONS AWAIT";
            var strip = new AttentionStrip(label, "GO TO HANGAR  >") { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            strip.Pressed += () => GetTree().ChangeSceneToFile(HangarNavigation.ScenePath);
            footer.AddChild(strip);
        }
        else
        {
            footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        }
        Button cont = FlatButton("CONTINUE  >", 12);
        cont.CustomMinimumSize = new Vector2(240, 38);
        cont.Pressed += () => GetTree().ChangeSceneToFile(
            GameSetup.IsTestBattle ? "res://Scenes/HomeScreen.tscn" : "res://Scenes/CampaignMap.tscn");
        footer.AddChild(cont);
    }

    static void AddKpi(Container parent, string value, string key, Color valueColor)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        box.AddChild(Text(value, 28, valueColor));
        box.AddChild(Text(key, 8, Muted, 3));
        parent.AddChild(box);
    }

    static void AddKeyValue(Container parent, string key, string value, Color valueColor)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 1);
        box.AddChild(Text(key, 8, Muted, 3));
        box.AddChild(Text(value, 12, valueColor, 1));
        parent.AddChild(box);
    }

    static Control BuildLedgerHeader()
    {
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        head.AddChild(FixedCell(new Control(), ColIcon));
        head.AddChild(FixedCell(Text("PILOT", 9, Muted, 3), ColPilot));
        head.AddChild(FixedCell(Text("FATE", 9, Muted, 3), ColFate));
        head.AddChild(FixedCell(Text("KILLS", 9, Muted, 3), ColKills));
        head.AddChild(FixedCell(Text("XP", 9, Muted, 3), ColXp));
        Label adv = Text("ADVANCEMENT", 9, Muted, 3);
        adv.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(adv);
        return head;
    }

    Control BuildLedgerRow(PilotResult r, bool won)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);

        var icon = new TextureRect
        {
            Texture = r.Pilot.Ship.GetSkin(0).Base,
            CustomMinimumSize = new Vector2(ColIcon, 44),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        if (!r.Survived)
            icon.Modulate = new Color(1, 1, 1, 0.45f);
        row.AddChild(icon);

        var identity = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        identity.AddThemeConstantOverride("separation", 1);
        identity.AddChild(Text(r.Pilot.Callsign.ToUpper(), 14, r.Survived ? TextBright : Negative, 2));
        identity.AddChild(Text(r.Pilot.Ship.DisplayName.ToUpper(), 8, Muted, 2));
        row.AddChild(FixedCell(identity, ColPilot));

        (string fate, ChipRole fateRole, string fateNote) = r switch
        {
            { Survived: false, Ejected: true } => ("KIA", ChipRole.Loss, "EJECTED · LOST IN ENEMY SPACE"),
            { Survived: false } => ("KIA", ChipRole.Loss, "SHOT DOWN · NO EJECTION"),
            { Ejected: true } => (won ? "EJECTED · RECOVERED" : "EJECTED · ESCAPED", ChipRole.Impaired,
                $"WOUNDED {r.Pilot.RecoveryMissionsRemaining} MISSIONS"),
            _ => ("RETURNED", ChipRole.Gain,
                r.Pilot.HullDamage > 0 ? $"{r.Pilot.HullDamage} HULL DAMAGE REMAINS" : "HULL FULLY REPAIRED"),
        };
        var fateBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        fateBox.AddThemeConstantOverride("separation", 3);
        var fateChipRow = new HBoxContainer();
        fateChipRow.AddChild(Chip(fate, fateRole));
        fateBox.AddChild(fateChipRow);
        fateBox.AddChild(Text(fateNote, 8, Dim, 2));
        row.AddChild(FixedCell(fateBox, ColFate));

        Label kills = Text(r.Survived ? r.Kills.ToString() : "—", 16, r.Survived ? Body : Dim);
        kills.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(FixedCell(kills, ColKills));

        var xpCell = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        xpCell.AddThemeConstantOverride("separation", 8);
        if (r.Survived)
        {
            xpCell.AddChild(Text($"+{r.XpGained}", 13, r.LevelsGained > 0 ? Positive : Body));
            int xpToNext = PilotRoster.XpToNext(r.Pilot.Level);
            xpCell.AddChild(new XpTrack
            {
                Ratio = xpToNext > 0 ? (float)r.Pilot.Xp / xpToNext : 1f,
                CustomMinimumSize = new Vector2(0, 6),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
        }
        else
        {
            xpCell.AddChild(Text("—", 13, Dim));
        }
        row.AddChild(FixedCell(xpCell, ColXp));

        var adv = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        adv.AddThemeConstantOverride("h_separation", 6);
        adv.AddThemeConstantOverride("v_separation", 4);
        if (!r.Survived)
        {
            adv.AddChild(Chip("MEMORIAL HALL", ChipRole.Loss));
        }
        else
        {
            if (r.LevelsGained > 0)
                adv.AddChild(Chip($"▲ LEVEL {r.Pilot.Level}", ChipRole.Gain));
            if (r.ManeuverSlotsGained > 0)
                adv.AddChild(Chip($"+{r.ManeuverSlotsGained} MANEUVER SLOT", ChipRole.Gain));
            if (r.NewPerk != null)
            {
                Control perkChip = Chip(r.NewPerk.Name.ToUpper(), r.NewPerk.Positive ? ChipRole.Gain : ChipRole.Impaired,
                    $"{r.NewPerk.Name}\n{r.NewPerk.Description}");
                adv.AddChild(perkChip);
            }
            if (adv.GetChildCount() == 0)
                adv.AddChild(Text("—", 12, Dim));
        }
        row.AddChild(adv);

        var padded = new MarginContainer();
        padded.AddThemeConstantOverride("margin_top", 9);
        padded.AddThemeConstantOverride("margin_bottom", 9);
        padded.AddChild(row);
        return padded;
    }

    /// <summary>Pins a ledger cell to one fixed column width, centered vertically.</summary>
    static Control FixedCell(Control inner, float width)
    {
        var clamp = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(width, 0),
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        clamp.AddChild(inner);
        return clamp;
    }

    static string AbilityDisplayName(ShipAbility ability) => ability switch
    {
        ShipAbility.UTurn => "U-Turn",
        ShipAbility.BreakTurn => "Break Turn",
        ShipAbility.EngineBoost => "Engine Boost",
        ShipAbility.RotatingGuns => "Rotating Guns",
        ShipAbility.SuppressionFire => "Suppression Fire",
        ShipAbility.EmergencyThrusters => "Emergency Thrusters",
        ShipAbility.SnapTurn => "Snap Turn",
        ShipAbility.HunterLock => "Hunter Lock",
        ShipAbility.PursuitBurn => "Pursuit Burn",
        ShipAbility.EcmJink => "ECM Jink",
        ShipAbility.SensorScramble => "Sensor Scramble",
        ShipAbility.GhostRun => "Ghost Run",
        ShipAbility.EvasiveDodge => "Evasive Dodge",
        _ => ability.ToString(),
    };

    public override void _Draw()
    {
        // Static starfield + arena border, drawn behind everything.
        var rng = new RandomNumberGenerator();
        rng.Seed = 1234;
        for (int i = 0; i < 140; i++)
        {
            var p = new Vector2(rng.RandfRange(0, ArenaW), rng.RandfRange(0, ArenaH));
            DrawCircle(p, rng.RandfRange(0.6f, 1.6f), new Color(1, 1, 1, rng.RandfRange(0.12f, 0.45f)));
        }
        DrawRect(new Rect2(1, 1, ArenaW - 2, ArenaH - 2), new Color(1, 1, 1, 0.12f), false, 2f);
    }
}
