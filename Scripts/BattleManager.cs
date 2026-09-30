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

    public const float ExecTime = 1.6f;      // sim-seconds per simultaneous move
    public const float ExecGrace = 0.45f;    // let last bullets land
    public const float CombatTimeScale = 0.8f; // slow-motion factor during execution (visual only)
    // The portrait battlefield: maps are authored landscape and turned so the
    // player's squadron starts at the bottom and the enemy wing at the top.
    public const float ArenaW = BattleMaps.AuthoredHeight;
    public const float ArenaH = BattleMaps.AuthoredWidth;
    // Touch tolerances in viewport pixels. A finger that moves less than the
    // slop is a tap; anything within the touch radius of a ship counts as a hit.
    const float TapSlop = 18f;
    const float TouchRadius = 60f;
    const float MinWorldTouchRadius = 34f;
    public const float BulletSpeed = 900f;
    public const float ShipCollisionRadius = 18f;
    public const int AsteroidMaxScrapeDamage = 14;
    /// <summary>A ship that starts its turn in nebula gas flies this share of its maneuver.</summary>
    public const float NebulaRouteScale = 0.75f;
    public const float NebulaAccuracyMultiplier = 0.7f;
    // Movement advances in fixed slices so asteroid contacts are checked
    // along the whole route, whatever the frame rate.
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
    BattleMission CurrentMission => GameSetup.Mission;

    // Planning state (read by the overlay and HUD for drawing).
    public Fighter Selected { get; private set; }
    Fighter _pinnedTarget;
    public Fighter PinnedTarget => _pinnedTarget != null && _pinnedTarget.IsAlive ? _pinnedTarget : null;
    /// <summary>A command waiting for the player to tap an enemy (Hunter Lock or Sensor Scramble).</summary>
    public ManeuverAction? PendingTargetAction { get; private set; }

    Node2D _fighterLayer, _bulletLayer;
    BattleCameraRig _camera;
    BattleHud _hud;
    int _turn = 1;
    int _firstDamageTurn;
    public int TurnNumber => _turn;
    float _execT;
    float _graceT;
    float _movementSimulationPending;
    Fighter _draggingGhost;
    Vector2 _ghostGrabOffset;
    bool _escortReinforcementsSpawned;
    string _notice;
    double _noticeUntil;

    // Touch gesture state. Screen positions are in viewport pixels.
    enum Gesture { None, Pending, DragGhost, Pan, Pinch }
    Gesture _gesture;
    readonly Dictionary<int, Vector2> _touches = new();
    int _primaryTouch;
    Vector2 _pressScreen;
    bool _pressDoubleTap;
    Fighter _pressGhost;
    float _pinchDistance;
    Vector2 _pinchMidpoint;

    public BattleCameraRig Camera => _camera;
    public float CameraZoom => _camera?.ZoomLevel ?? 1f;
    /// <summary>Viewport-pixel to world-unit conversion for things drawn at a constant screen size.</summary>
    public float ScreenToWorldScale => 1f / CameraZoom;

    public List<Fighter> GetTeam(int team) => team == 0 ? PlayerFighters : EnemyFighters;

    /// <summary>Marks the first combat turn where any fighter is damaged.</summary>
    public bool IsOpeningDamageTurn()
    {
        if (_firstDamageTurn == 0)
            _firstDamageTurn = _turn;
        return _firstDamageTurn == _turn;
    }

    /// <summary>
    /// Where a maneuver will be at a fraction of the execution clock. Every
    /// maneuver spreads evenly over the clock, so this is also that fraction
    /// of the route.
    /// </summary>
    public void PredictExecutionPoint(Fighter fighter, ManeuverType maneuver, float turn, float distance,
        float executionFraction, out Vector2 position, out float heading)
    {
        turn = ClampPlannedTurn(fighter, maneuver, turn, distance);
        fighter.RoutePoint(maneuver, turn, distance, Mathf.Clamp(executionFraction, 0f, 1f), out position, out heading);
    }

    /// <summary>Fills evenly spaced execution-time samples for a maneuver.</summary>
    public void PredictExecutionPath(Fighter fighter, ManeuverType maneuver, float turn, float distance,
        Vector2[] positions, float[] headings)
    {
        if (positions.Length != headings.Length || positions.Length < 2)
            throw new System.ArgumentException("Prediction paths need matching position and heading samples.");

        turn = ClampPlannedTurn(fighter, maneuver, turn, distance);
        int finalSample = positions.Length - 1;
        for (int sample = 0; sample <= finalSample; sample++)
            fighter.RoutePoint(maneuver, turn, distance, sample / (float)finalSample, out positions[sample], out headings[sample]);
    }

    static float ClampPlannedTurn(Fighter fighter, ManeuverType maneuver, float turn, float distance)
    {
        if (maneuver != ManeuverType.Normal)
            return turn;
        float maxTurn = Mathf.DegToRad(fighter.GetNormalTurnLimitDegrees(distance));
        return Mathf.Clamp(turn, -maxTurn, maxTurn);
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
            fighter.RoutePoint(maneuver, turn, distance, i / (float)samples, out Vector2 next, out _);
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
            fighter.RoutePoint(maneuver, turn, distance, i / (float)samples, out Vector2 next, out _);
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
            fighter.RoutePoint(maneuver, turn, distance, i / (float)samples, out Vector2 next, out _);
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
                SpawnImpact(fighter.Position, fighter.Velocity, ImpactSparks.Kind.Rock);
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

    public bool IsInNebula(Vector2 point)
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

        _camera = new BattleCameraRig { Name = "BattleCamera", Position = new Vector2(ArenaW / 2f, ArenaH / 2f) };
        AddChild(_camera);

        Map = BattleMaps.ForCurrentBattle();

        _fighterLayer = new Node2D();
        _bulletLayer = new Node2D();
        AddChild(new BattleBackdrop(Map.Id));
        AddChild(new TerrainLayer(Map.Terrain));
        AddChild(_fighterLayer);
        AddChild(_bulletLayer);
        AddChild(new BattleOverlay());

        // Pilots come from the quick-battle setup or the run's briefing; run
        // missions supply the enemy wing.
        List<Pilot> squad = GameSetup.PlayerPilots.Where(p => p.Alive).Take(Map.PlayerSpawns.Length).ToList();
        if (squad.Count < 1)
        {
            GD.PushError("A battle requires at least one pilot.");
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                GameSetup.IsTestBattle ? "res://Scenes/TestBattleSelect.tscn" : "res://Scenes/Run.tscn");
            return;
        }
        for (int i = 0; i < squad.Count; i++)
        {
            ShipType playerType = squad[i].Ship;
            BattleSpawn playerSpawn = Map.PlayerSpawns[i];
            SpawnFighter(0, playerSpawn.Position, Mathf.DegToRad(playerSpawn.HeadingDegrees), playerType, squad[i]);
        }

        ShipType[] enemySquad = GameSetup.IsTestBattle
            ? new[] { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt }
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
            BattleSpawn escortSpawn = Map.EscortSpawn ?? new BattleSpawn(ArenaW / 2f, ArenaH - 360f, -90f);
            Vector2 destination = Map.EscortDestination == Vector2.Zero
                ? new Vector2(ArenaW / 2f, 350f)
                : Map.EscortDestination;
            float destinationRadius = Map.EscortDestinationRadius > 0f ? Map.EscortDestinationRadius : 95f;
            EscortShip.Setup(escortSpawn.Position, destination, destinationRadius);
            _fighterLayer.AddChild(EscortShip);
        }

        _hud = new BattleHud();
        AddChild(_hud);
        BeginPlanningPhase(frameCamera: false);
        _initialFramePending = true;
    }

    bool _initialFramePending;

    void SpawnFighter(int team, Vector2 pos, float heading, ShipType type, Pilot pilot = null, float statMultiplier = 1f)
    {
        var f = new Fighter();
        f.ApplyType(type);
        f.ApplyPilot(pilot);
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
            ? new[] { ShipTypes.Scout, ShipTypes.Raptor, ShipTypes.Zt }
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
        Announce($"REINFORCEMENTS · {spawnCount} HOSTILES ENTERING THE CORRIDOR");
    }

    /// <summary>Shows a short-lived message in the HUD's objective line.</summary>
    public void Announce(string text, double seconds = 4.0)
    {
        _notice = text;
        _noticeUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    /// <summary>The current announcement, or null once it has expired.</summary>
    public string Notice => _notice != null && Time.GetTicksMsec() / 1000.0 < _noticeUntil ? _notice : null;

    /// <summary>One-line mission objective and progress for the HUD.</summary>
    public string ObjectiveText
    {
        get
        {
            if (EscortShip != null)
                return EscortShip.Escaped ? "TRANSPORT SECURED"
                    : EscortShip.IsAlive ? $"ESCORT · TRANSPORT {EscortShip.DistanceRemaining:0} FROM JUMP"
                    : "TRANSPORT LOST";
            int hostiles = EnemyFighters.Count(f => f.IsAlive);
            if (_priorityTarget != null)
                return _priorityTarget.IsAlive ? "DESTROY THE MARKED TARGET" : "TARGET DESTROYED";
            return hostiles == 1 ? "ELIMINATE · 1 HOSTILE LEFT" : $"ELIMINATE · {hostiles} HOSTILES LEFT";
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_hud == null)
            return; // setup failed and the scene is changing

        if (_initialFramePending && _hud.LayoutReady)
        {
            _initialFramePending = false;
            FrameBattle(instant: true);
        }
        _camera.Bounds = CameraBounds();

        if (CurrentPhase == Phase.Executing)
            UpdateExecution(dt);
    }

    /// <summary>The arena, grown to include every ship and ghost that has left it.</summary>
    Rect2 CameraBounds()
    {
        var bounds = new Rect2(0, 0, ArenaW, ArenaH);
        foreach (Fighter fighter in AllAlive())
        {
            bounds = bounds.Expand(fighter.Position);
            if (fighter.Team == 0)
                bounds = bounds.Expand(GetGhostEndpoint(fighter));
        }
        return bounds;
    }

    /// <summary>World points the camera should keep in view when showing the whole battle.</summary>
    IEnumerable<Vector2> BattlePoints(bool includeGhosts)
    {
        foreach (Fighter fighter in AllAlive())
        {
            yield return fighter.Position;
            if (includeGhosts && fighter.Team == 0)
                yield return GetGhostEndpoint(fighter);
        }
        if (EscortShip != null && EscortShip.IsAlive && !EscortShip.Escaped)
            yield return EscortShip.Position;
    }

    /// <summary>Eases the camera to show every combatant and every planned ghost.</summary>
    public void FrameBattle(bool instant = false) =>
        _camera.Frame(BattlePoints(includeGhosts: CurrentPhase == Phase.Planning).ToList(), maxZoom: 1.1f, instant);

    /// <summary>
    /// Eases the camera onto one ship's planning area: the ship, the reach of
    /// its maneuvers, its ghost's firing cone and any pinned target.
    /// </summary>
    public void FocusOn(Fighter fighter)
    {
        var points = new List<Vector2> { fighter.Position };
        fighter.RoutePoint(fighter.PlannedManeuver, fighter.PlannedTurnAngleRadians ?? 0f, fighter.PlannedPathDistance, 1f,
            out Vector2 ghost, out float ghostHeading);
        points.Add(ghost);
        points.Add(ghost + Vector2.FromAngle(ghostHeading) * fighter.EffectiveFireRange * 0.8f);
        float reach = fighter.NormalMoveMaxDistance;
        foreach (float angle in new[] { -1.1f, 0f, 1.1f })
            points.Add(fighter.Position + Vector2.FromAngle(fighter.Heading + angle) * reach);
        // A pinned target joins the framing only when it is close enough to
        // matter this turn; otherwise it would pull the camera far back.
        if (PinnedTarget != null && PinnedTarget.Position.DistanceTo(fighter.Position) < reach + fighter.EffectiveFireRange * 1.5f)
            points.Add(PinnedTarget.Position);
        _camera.Frame(points, maxZoom: 1.25f);
    }

    bool CanExecuteTurn() => CurrentPhase == Phase.Planning && PlayerFighters.Any(f => f.IsAlive);

    /// <summary>True once the player has given this fighter an order this turn.</summary>
    public static bool HasOrders(Fighter fighter) => fighter.PlannedTurnAngleRadians.HasValue;

    /// <summary>Makes a player fighter the subject of the maneuver bar.</summary>
    public void SelectFighter(Fighter fighter, bool focus = false)
    {
        if (CurrentPhase != Phase.Planning || fighter == null || !fighter.IsAlive || fighter.Team != 0)
            return;
        if (Selected != fighter)
            PendingTargetAction = null;
        Selected = fighter;
        if (focus)
            FocusOn(fighter);
    }

    /// <summary>Selects the first living player fighter, or the one after the current selection.</summary>
    void CycleSelectedPlayerFighter()
    {
        List<Fighter> livingFighters = PlayerFighters.Where(fighter => fighter.IsAlive).ToList();
        if (livingFighters.Count == 0)
            return;
        int selectedIndex = livingFighters.IndexOf(Selected);
        SelectFighter(livingFighters[(selectedIndex + 1) % livingFighters.Count], focus: true);
    }

    public override void _Input(InputEvent @event)
    {
        // Handle this before Control focus navigation can consume Tab.
        if (CurrentPhase == Phase.Planning && @event is InputEventKey key &&
            key.Pressed && !key.Echo && key.Keycode == Key.Tab)
        {
            CycleSelectedPlayerFighter();
            GetViewport().SetInputAsHandled();
        }
    }

    // Leaving mid-turn must not carry the execution slow-motion into menus.
    public override void _ExitTree() => Engine.TimeScale = 1f;

    public override void _Notification(int what)
    {
        // Android's back gesture opens the battle menu instead of quitting.
        if (what == NotificationWMGoBackRequest && CurrentPhase != Phase.GameOver)
            _hud?.ToggleMenu();
    }

    /// <summary>
    /// Touch is the primary input. With touch emulation on, a desktop mouse
    /// produces the same screen-touch events; the wheel, right button and a
    /// few keys remain as desktop conveniences.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventScreenTouch touch:
                HandleTouch(touch);
                break;
            case InputEventScreenDrag drag:
                HandleDrag(drag);
                break;
            case InputEventMouseButton { Pressed: true } wheel
                when wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.ZoomAtScreen(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                UndoLastOrder();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Space)
                    RequestEngage();
                else if (key.Keycode is Key.Backspace or Key.Z)
                    UndoLastOrder();
                else if (key.Keycode == Key.Escape && CurrentPhase != Phase.GameOver)
                    _hud?.ToggleMenu();
                break;
        }
    }

    void HandleTouch(InputEventScreenTouch touch)
    {
        if (touch.Pressed)
        {
            _touches[touch.Index] = touch.Position;
            if (_touches.Count == 1)
            {
                _gesture = Gesture.Pending;
                _primaryTouch = touch.Index;
                _pressScreen = touch.Position;
                _pressDoubleTap = touch.DoubleTap;
                _pressGhost = CurrentPhase == Phase.Planning && PendingTargetAction == null ? GhostAt(touch.Position) : null;
            }
            else if (_touches.Count == 2)
            {
                // A second finger turns any gesture into a pinch.
                FinishGhostDrag();
                StartPinch();
            }
            return;
        }

        if (!_touches.Remove(touch.Index))
            return;

        switch (_gesture)
        {
            case Gesture.Pending when touch.Index == _primaryTouch:
                if (!touch.Canceled)
                    HandleTap(touch.Position);
                _gesture = Gesture.None;
                break;
            case Gesture.DragGhost when touch.Index == _primaryTouch:
                FinishGhostDrag();
                _gesture = Gesture.None;
                break;
            case Gesture.Pinch when _touches.Count == 1:
                // Keep panning with the finger that remains.
                _primaryTouch = _touches.Keys.First();
                _gesture = Gesture.Pan;
                break;
            default:
                if (_touches.Count == 0)
                    _gesture = Gesture.None;
                break;
        }
    }

    void HandleDrag(InputEventScreenDrag drag)
    {
        if (!_touches.ContainsKey(drag.Index))
            return;
        _touches[drag.Index] = drag.Position;

        switch (_gesture)
        {
            case Gesture.Pinch:
                UpdatePinch();
                break;
            case Gesture.Pending when drag.Index == _primaryTouch:
                if (drag.Position.DistanceTo(_pressScreen) < TapSlop)
                    break;
                if (_pressGhost != null && _pressGhost.IsAlive && CurrentPhase == Phase.Planning)
                {
                    _gesture = Gesture.DragGhost;
                    StartGhostDrag(_pressGhost, _pressScreen);
                    UpdateGhostDrag(_draggingGhost, _camera.ScreenToWorld(drag.Position) + _ghostGrabOffset);
                }
                else
                {
                    _gesture = Gesture.Pan;
                    _camera.PanByScreen(drag.Position - _pressScreen);
                }
                break;
            case Gesture.DragGhost when drag.Index == _primaryTouch && _draggingGhost != null:
                UpdateGhostDrag(_draggingGhost, _camera.ScreenToWorld(drag.Position) + _ghostGrabOffset);
                break;
            case Gesture.Pan when drag.Index == _primaryTouch:
                _camera.PanByScreen(drag.Relative);
                break;
        }
    }

    void StartPinch()
    {
        Vector2[] points = _touches.Values.Take(2).ToArray();
        _pinchDistance = Mathf.Max(1f, points[0].DistanceTo(points[1]));
        _pinchMidpoint = (points[0] + points[1]) / 2f;
        _gesture = Gesture.Pinch;
    }

    void UpdatePinch()
    {
        Vector2[] points = _touches.Values.Take(2).ToArray();
        float distance = Mathf.Max(1f, points[0].DistanceTo(points[1]));
        Vector2 midpoint = (points[0] + points[1]) / 2f;
        _camera.PanByScreen(midpoint - _pinchMidpoint);
        _camera.ZoomAtScreen(midpoint, distance / _pinchDistance);
        _pinchDistance = distance;
        _pinchMidpoint = midpoint;
    }

    /// <summary>
    /// A tap either completes a pending Lock On / Scramble, pins an enemy for
    /// the targeting preview, or selects one of the player's ships. Where an
    /// enemy and a ghost overlap, the nearer one to the finger wins.
    /// </summary>
    void HandleTap(Vector2 screen)
    {
        if (CurrentPhase != Phase.Planning)
            return;

        Vector2 world = _camera.ScreenToWorld(screen);
        Fighter enemy = EnemyAt(screen);
        if (PendingTargetAction != null)
        {
            if (enemy != null)
                ApplyTargetAction(enemy);
            else
                PendingTargetAction = null;
            return;
        }

        Fighter ghost = GhostAt(screen);
        Fighter ship = ShipAt(screen);
        float ownDistance = Mathf.Min(ghost != null ? GetGhostEndpoint(ghost).DistanceTo(world) : float.MaxValue,
            ship != null ? ship.Position.DistanceTo(world) : float.MaxValue);
        if (enemy != null && enemy.Position.DistanceTo(world) <= ownDistance)
        {
            TogglePinnedTarget(enemy);
            return;
        }
        if (ghost != null || ship != null)
        {
            SelectFighter(ghost ?? ship);
            return;
        }
        if (_pressDoubleTap)
            FrameBattle();
    }

    float WorldTouchRadius => Mathf.Max(MinWorldTouchRadius, TouchRadius * ScreenToWorldScale);

    Fighter GhostAt(Vector2 screen) => NearestWithin(PlayerFighters, GetGhostEndpoint, screen);
    Fighter ShipAt(Vector2 screen) => NearestWithin(PlayerFighters, fighter => fighter.Position, screen);
    Fighter EnemyAt(Vector2 screen) => NearestWithin(EnemyFighters, fighter => fighter.Position, screen);

    Fighter NearestWithin(IEnumerable<Fighter> fighters, System.Func<Fighter, Vector2> point, Vector2 screen)
    {
        Vector2 world = _camera.ScreenToWorld(screen);
        float radius = WorldTouchRadius;
        return fighters
            .Where(fighter => fighter.IsAlive)
            .Select(fighter => (Fighter: fighter, Distance: point(fighter).DistanceTo(world)))
            .Where(hit => hit.Distance <= radius)
            .OrderBy(hit => hit.Distance)
            .Select(hit => hit.Fighter)
            .FirstOrDefault();
    }

    void TogglePinnedTarget(Fighter target) => _pinnedTarget = _pinnedTarget == target ? null : target;

    void ApplyTargetAction(Fighter enemy)
    {
        Fighter fighter = Selected;
        ManeuverAction? action = PendingTargetAction;
        PendingTargetAction = null;
        if (fighter == null || !fighter.IsAlive)
            return;
        if (action == ManeuverAction.HunterLock && fighter.SetHunterLock(enemy))
            Announce($"LOCK ON · {CallsignOf(fighter)} LOCKED A {enemy.Type.DisplayName.ToUpper()}");
        else if (action == ManeuverAction.SensorScramble && fighter.ApplySensorScramble(enemy, out Fighter splash))
            Announce(splash == null
                ? $"SCRAMBLE · {enemy.Type.DisplayName.ToUpper()} ACCURACY DOWN FOR {fighter.Moves.SensorScrambleDurationTurns} TURNS"
                : $"SCRAMBLE · 2 ENEMIES' ACCURACY DOWN FOR {fighter.Moves.SensorScrambleDurationTurns} TURNS");
    }

    public static string CallsignOf(Fighter fighter) =>
        fighter.Pilot?.Callsign?.ToUpper() ?? fighter.Type?.DisplayName.ToUpper() ?? "SHIP";

    public Vector2 GetGhostEndpoint(Fighter fighter)
    {
        fighter.RoutePoint(fighter.PlannedManeuver, fighter.PlannedTurnAngleRadians ?? 0f, fighter.PlannedPathDistance, 1f,
            out Vector2 endpoint, out _);
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
        // Nebula drag shortens the route flown for a given throttle, so the
        // ghost under the finger needs that much more throttle.
        fighter.SetPlannedMoveDistance(offset.Length() / chordFraction / fighter.RouteScale);

        // The Reckless penalty depends on the resulting throttle, so clamp once
        // more after the requested endpoint has established the move distance.
        maxTurn = Mathf.DegToRad(fighter.PlannedNormalTurnLimitDegrees);
        turn = Mathf.Clamp(2f * bearing, -maxTurn, maxTurn);
        absTurn = Mathf.Abs(turn);
        chordFraction = absTurn < 0.001f ? 1f : 2f * Mathf.Sin(absTurn / 2f) / absTurn;
        fighter.SetPlannedMoveDistance(offset.Length() / chordFraction / fighter.RouteScale);
        fighter.PlannedTurnAngleRadians = turn;
    }

    /// <summary>
    /// Grabbing a ghost keeps the offset between finger and ghost, so the
    /// ghost stays visible beside the finger instead of jumping under it.
    /// </summary>
    void StartGhostDrag(Fighter fighter, Vector2 pressScreen)
    {
        SelectFighter(fighter);
        _draggingGhost = fighter;
        _ghostGrabOffset = GetGhostEndpoint(fighter) - _camera.ScreenToWorld(pressScreen);
    }

    void FinishGhostDrag()
    {
        if (_draggingGhost == null)
            return;
        ConfirmManeuver(_draggingGhost);
        _draggingGhost = null;
    }

    /// <summary>
    /// Dragging steers aimable maneuvers and picks the side of fixed turns;
    /// Turret mode flies a fixed straight line and ignores the drag.
    /// </summary>
    void UpdateGhostDrag(Fighter fighter, Vector2 endpoint)
    {
        Vector2 offset = endpoint - fighter.Position;
        if (offset.LengthSquared() < 1f)
            return;
        float bearing = Mathf.Wrap(offset.Angle() - fighter.Heading, -Mathf.Pi, Mathf.Pi);

        switch (fighter.PlannedManeuver)
        {
            case ManeuverType.Normal:
                SetGhostManeuver(fighter, endpoint);
                break;
            case ManeuverType.EngineBoost:
            case ManeuverType.PursuitBurn:
            case ManeuverType.EcmJink:
            case ManeuverType.GhostRun:
            case ManeuverType.EmergencyThrusters:
                PlanAimed(fighter, fighter.PlannedManeuver, 2f * bearing);
                break;
            case ManeuverType.UTurn:
            case ManeuverType.BreakTurn:
            case ManeuverType.SnapTurn:
            case ManeuverType.EvasiveDodge:
                if (Mathf.Abs(bearing) > 0.05f)
                    PlanDirectional(fighter, fighter.PlannedManeuver, Mathf.Sign(bearing));
                break;
        }
    }

    static void PlanAimed(Fighter fighter, ManeuverType maneuver, float turn)
    {
        switch (maneuver)
        {
            case ManeuverType.EngineBoost: fighter.PlanEngineBoost(turn); break;
            case ManeuverType.PursuitBurn: fighter.PlanPursuitBurn(turn); break;
            case ManeuverType.EcmJink: fighter.PlanEcmJink(turn); break;
            case ManeuverType.GhostRun: fighter.PlanGhostRun(turn); break;
            case ManeuverType.EmergencyThrusters: fighter.PlanEmergencyThrusters(turn); break;
            case ManeuverType.RotatingGuns: fighter.PlanRotatingGuns(); break;
        }
    }

    static void PlanDirectional(Fighter fighter, ManeuverType maneuver, float side)
    {
        switch (maneuver)
        {
            case ManeuverType.UTurn: fighter.PlanUTurn(side); break;
            case ManeuverType.BreakTurn: fighter.PlanBreakTurn(side); break;
            case ManeuverType.SnapTurn: fighter.PlanSnapTurn(side); break;
            case ManeuverType.EvasiveDodge: fighter.PlanEvasiveDodge(side); break;
        }
    }

    /// <summary>
    /// Applies a maneuver-bar command to the selected fighter. Fixed turns
    /// start on the side the ship was already steering toward and flip on a
    /// second tap; tapping an active special maneuver returns to normal flight.
    /// </summary>
    public void ChooseAction(ManeuverAction action)
    {
        Fighter fighter = Selected;
        if (CurrentPhase != Phase.Planning || fighter == null || !fighter.IsAlive)
            return;
        ManeuverInfo info = ManeuverCatalog.Get(action);
        if (info.Ability is ShipAbility ability && !fighter.HasAbility(ability))
            return;
        if (ManeuverCatalog.CooldownTurns(fighter, info) > 0)
            return;

        if (info.TargetsEnemy)
        {
            PendingTargetAction = PendingTargetAction == action ? null : action;
            return;
        }
        PendingTargetAction = null;

        float currentTurn = fighter.PlannedTurnAngleRadians ?? 0f;
        bool alreadyActive = fighter.PlannedManeuver == info.Maneuver;
        if (action == ManeuverAction.Normal || (alreadyActive && !info.Directional))
        {
            if (fighter.PlannedManeuver != ManeuverType.Normal)
                fighter.ClearPlannedManeuver();
            fighter.PlannedTurnAngleRadians ??= 0f;
        }
        else if (info.Directional)
        {
            float side = alreadyActive ? -Mathf.Sign(currentTurn) : currentTurn < 0f ? -1f : 1f;
            PlanDirectional(fighter, info.Maneuver.Value, side == 0f ? 1f : side);
        }
        else
        {
            PlanAimed(fighter, info.Maneuver.Value, currentTurn);
        }
        ConfirmManeuver(fighter);
    }

    void ConfirmManeuver(Fighter fighter)
    {
        _orderHistory.Remove(fighter);
        _orderHistory.Add(fighter);
    }

    readonly List<Fighter> _orderHistory = new();

    public bool CanUndo => CurrentPhase == Phase.Planning &&
        _orderHistory.Any(f => f.IsAlive && f.PlannedTurnAngleRadians.HasValue);

    /// <summary>Clears the most recently given order and reselects that fighter.</summary>
    public void UndoLastOrder()
    {
        if (CurrentPhase != Phase.Planning)
            return;
        PendingTargetAction = null;
        for (int i = _orderHistory.Count - 1; i >= 0; i--)
        {
            Fighter f = _orderHistory[i];
            _orderHistory.RemoveAt(i);
            if (f.IsAlive && f.PlannedTurnAngleRadians.HasValue)
            {
                f.ClearPlannedManeuver();
                SelectFighter(f);
                return;
            }
        }
    }

    /// <summary>Retreat is offered between turns; it ends the battle as a defeat.</summary>
    public bool CanRetreat => CurrentPhase == Phase.Planning;

    public void Retreat()
    {
        if (CanRetreat)
            FinishBattle(false, "RETREAT", Warning);
    }

    double _fatalConfirmUntil;

    /// <summary>Player ships whose current plan flies into an asteroid.</summary>
    public List<Fighter> ShipsOnFatalCourse() => PlayerFighters
        .Where(f => f.IsAlive && PathHitsAsteroid(f, f.PlannedManeuver, f.PlannedTurnAngleRadians ?? 0f, f.PlannedPathDistance))
        .ToList();

    /// <summary>True while Engage is waiting for a second tap to confirm a fatal course.</summary>
    public bool AwaitingFatalConfirm => Time.GetTicksMsec() / 1000.0 < _fatalConfirmUntil;

    /// <summary>
    /// Engage as the player asks for it: because ships without orders hold
    /// course, a first tap that would send a ship into an asteroid only warns,
    /// and a second tap within a few seconds confirms.
    /// </summary>
    public void RequestEngage()
    {
        if (!CanExecuteTurn())
            return;
        List<Fighter> doomed = ShipsOnFatalCourse();
        if (doomed.Count > 0 && !AwaitingFatalConfirm)
        {
            _fatalConfirmUntil = Time.GetTicksMsec() / 1000.0 + 5.0;
            string names = string.Join(", ", doomed.Select(CallsignOf));
            Announce($"COLLISION COURSE · {names}", 5.0);
            return;
        }
        _fatalConfirmUntil = 0;
        Engage();
    }

    /// <summary>Locks in every order and runs the turn. Ships without orders hold their course.</summary>
    public void Engage()
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

        _draggingGhost = null;
        _gesture = Gesture.None;
        _touches.Clear();
        PendingTargetAction = null;
        _orderHistory.Clear();
        _execT = 0f;
        _graceT = 0f;
        _movementSimulationPending = 0f;
        CurrentPhase = Phase.Executing;
        _camera.Follow(() => BattlePoints(includeGhosts: false));
        // Slow the whole execution down a touch. Scaling engine time (not the
        // sim constants) keeps balance identical: cooldowns, bullets and
        // movement all stretch together.
        Engine.TimeScale = CombatTimeScale;
    }

    /// <summary>Opens a planning phase: the first ship is selected and the battle reframed.</summary>
    void BeginPlanningPhase(bool frameCamera = true)
    {
        CurrentPhase = Phase.Planning;
        // Nebula drag is fixed for the whole turn by where each ship starts it.
        foreach (Fighter f in AllAlive())
            f.RouteScale = IsInNebula(f.Position) ? NebulaRouteScale : 1f;
        if (EscortShip != null && EscortShip.IsAlive)
            EscortShip.RouteScale = IsInNebula(EscortShip.Position) ? NebulaRouteScale : 1f;
        PendingTargetAction = null;
        _camera.StopFollowing();
        Selected = PlayerFighters.FirstOrDefault(f => f.IsAlive);
        if (frameCamera)
            FrameBattle();
    }

    IEnumerable<Fighter> AllAlive() =>
        PlayerFighters.Concat(EnemyFighters).Where(f => f.IsAlive);

    void UpdateExecution(float dt)
    {
        // The execution clock is fixed: every ship flies its whole planned
        // route in it, so a turn ends exactly where the ghosts showed.
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
            f.AdvanceExecute(dt);
            ResolveShipAsteroidContacts(f, previousPosition, f.Position);
        }

        if (EscortShip == null || !EscortShip.IsAlive || EscortShip.Escaped)
            return;

        Vector2 previousEscortPosition = EscortShip.Position;
        EscortShip.AdvanceExecute(dt);
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
                f.BarrageShotsLeft = f.StartVolley(target);
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
        shooter.NoteFiringTraits(target);
        target.NoteDefendingTraits();
        if (hits)
        {
            if (shooter.HasAbility(ShipAbility.SuppressionFire))
                target.ApplySuppression(shooter.Moves.SuppressionTurnPenaltyDeg);
            dir = dir.Rotated((float)GD.RandRange(-1.0, 1.0) * Mathf.DegToRad(0.8f));
        }
        else
        {
            // Deflect a missed shot so it visibly streaks past the target.
            float side = GD.Randf() < 0.5f ? -1f : 1f;
            dir = dir.Rotated(side * Mathf.DegToRad((float)GD.RandRange(3.5, 7.0)));
        }

        var b = new Bullet();
        Color col = ShipPaint.TeamGlow(shooter.Team);
        b.Init(shooter, nose, dir * BulletSpeed, shooter.EffectiveFireRange * 1.4f, col, shooter.ShotDamage, hits,
            shooter.FireTimeDamageMultiplier(target.Position));
        _bulletLayer.AddChild(b);
        _bulletLayer.AddChild(new MuzzleFlash { Shooter = shooter, Glow = ShipPaint.TeamGlow(shooter.Team) });
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
            ShipPaint.TeamGlow(shooter.Team), shooter.ShotDamage, hits, shooter.FireTimeDamageMultiplier(target.Position));
        _bulletLayer.AddChild(bullet);
        _bulletLayer.AddChild(new MuzzleFlash { Shooter = shooter, Glow = ShipPaint.TeamGlow(shooter.Team) });
    }

    public void SpawnFlash(Vector2 pos)
    {
        _bulletLayer.AddChild(new Flash { Position = pos });
    }

    /// <summary>Sparks where a shot or a rock met something.</summary>
    public void SpawnImpact(Vector2 pos, Vector2 incoming, ImpactSparks.Kind kind)
    {
        _bulletLayer?.AddChild(new ImpactSparks { Position = pos, Incoming = incoming, Type = kind });
    }

    /// <summary>Floats the damage a ship just took over it.</summary>
    public void ShowDamage(Fighter fighter, int shield, int hull) => DamageNumber.Show(_bulletLayer, fighter, shield, hull);

    /// <summary>A ship going up: a burst of debris, a shock ring and a jolt of the camera.</summary>
    public void ShowDestruction(Fighter fighter)
    {
        SpawnImpact(fighter.Position, Vector2.Zero, ImpactSparks.Kind.Kill);
        _camera?.Shake(fighter.Team == 0 ? 14f : 9f);
    }

    /// <summary>Floats a trait's name up from a ship, readable at any zoom.</summary>
    public void ShowCallout(Fighter fighter, string text, Color color)
    {
        if (_bulletLayer == null || fighter == null)
            return;
        // Each ship's callouts form a short feed: the newest sits just above
        // the callsign and pushes older ones up.
        foreach (TraitCallout older in _bulletLayer.GetChildren().OfType<TraitCallout>().Where(c => c.Anchor == fighter))
            older.PushUp();
        _bulletLayer.AddChild(new TraitCallout { Anchor = fighter, Text = text, Color = color, Position = fighter.Position });
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
            bool won = playersAlive && objectiveSuccess; // mutual destruction counts as a defeat
            (string headline, Color headlineColor) = won
                ? ("VICTORY", Positive)
                : enemiesAlive ? ("DEFEAT", Negative) : ("MUTUAL DESTRUCTION", Body);
            FinishBattle(won, headline, headlineColor);
            return;
        }

        _turn++;
        foreach (Fighter f in AllAlive())
        {
            ManeuverType completedManeuver = f.PlannedManeuver;
            f.ClearPlannedManeuver();
            f.AdvanceManeuverCooldowns();
            f.StartManeuverCooldown(completedManeuver);
            f.AdvanceTacticalEffects();
            f.RegenerateShield();
        }
        BeginPlanningPhase();
    }

    /// <summary>Resolves the battle's consequences once and replaces the HUD with the debrief.</summary>
    void FinishBattle(bool won, string headline, Color headlineColor)
    {
        Engine.TimeScale = 1f;
        CurrentPhase = Phase.GameOver;
        Selected = null;
        _camera.StopFollowing();

        var report = new BattleReport
        {
            Won = won,
            Headline = headline,
            HeadlineColor = headlineColor,
            Mission = CurrentMission,
            MapName = Map.DisplayName,
            Squad = PlayerFighters.ToList(),
            Turns = _turn,
        };
        if (!GameSetup.IsTestBattle && RunState.Current != null)
        {
            (report.Results, report.Run) = RunState.Current.ResolveBattle(won, PlayerFighters);
        }

        _hud.Visible = false;
        AddChild(new BattleDebrief(report));
    }

    public override void _Draw()
    {
        // The arena border; the sky behind it is BattleBackdrop.
        DrawRect(new Rect2(1, 1, ArenaW - 2, ArenaH - 2), new Color(1, 1, 1, 0.12f), false, 2f);
    }
}
