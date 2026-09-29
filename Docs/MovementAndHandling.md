# Movement and Handling Vocabulary

Use this vocabulary in player-facing copy, balance notes, and movement code.

| Term | Meaning |
| --- | --- |
| **Handling** | The general quality of a ship's ability to change direction. |
| **Maximum normal turn** | The greatest total heading change allowed during one normal move, measured in degrees. A higher value gives better handling. |
| **Turn limit** | The code term for maximum normal turn: a cap on the turn angle, not a turn made by the ship. |
| **Turn angle** | The signed heading change selected for one planned maneuver, measured in radians in code. |
| **Tight turn** | A smaller derived turn radius; the ship changes direction more sharply. This is better handling. |
| **Wide turn** | A larger derived turn radius; the ship changes direction less sharply. This is worse handling. |
| **Normal move distance** | The path length selected between the normal minimum and maximum distance. Ships cannot stop. |
| **Path distance** | The distance used by any planned maneuver. Special maneuvers use fixed path distances. |

Normal moves follow constant-rate arcs. For a move with path distance `D` and total
turn angle `A` in radians, the turn radius is `D / abs(A)`. Turn radius is a
derived geometric result, not a gameplay stat.

- Increasing maximum normal turn makes a turn tighter.
- Decreasing path distance at the same turn angle makes a turn tighter.
- Increasing turn radius makes a turn wider and is a handling penalty.

Prefer **"+5% maximum normal turn"** or **"sharper/tighter normal turns"** for
handling improvements. Do not say **"increased turn radius"** when describing an
improvement.

Normal-flight identifiers use `NormalTurnLimitDegrees`, `NormalMoveMinDistance`,
and `NormalMoveMaxDistance`. `PlannedTurnAngleRadians` is the selected turn,
while `PlannedPathDistance` is the distance travelled along that maneuver.
Special maneuvers retain their own explicit turn limits, such as
`EngineBoostTurnLimitDegrees`, because normal handling modifiers do not apply to
those maneuvers.

`NormalMinimumTurnRadius` is an optional normal-flight constraint. Its default
value is `0`, which disables it and preserves current gameplay. When a positive
radius is configured, a ship cannot make a normal arc tighter than that radius;
larger values therefore force wider turns.
