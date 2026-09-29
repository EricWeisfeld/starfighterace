# Campaign Structure

## Current campaign loop

The campaign currently contains one authored system: **Orion Spur**.

1. Select one of its five planets.
2. Win that planet's authored operation. A defeat leaves the operation available
   for another attempt.
3. After all five planetary operations are complete, the system story unlocks.
4. Win the story operation to secure the system and earn the system-capture
   payout.

The Orion Spur story operation is the escort mission. It is not part of the
random mission pool.

## Content model

`CampaignData` loads each system from a `CampaignSystemResource`:

```text
CampaignSystemResource
  -> five planetary MissionDefinitionResources
  -> one story MissionDefinitionResource
```

Both mission types own their briefing, reward, threat, objective, map, and
enemy wing. The `Source` field distinguishes a planetary operation from a
system story. This keeps tactical mission data authored in resources while
campaign progression remains in `CampaignData`.

Current Orion Spur content lives under `Resources/CampaignSystems` and
`Resources/CampaignMissions`.

## Rules

- A planetary operation is completed only on victory.
- Failed planetary operations remain on their planet.
- The story operation is available only after every planetary operation in its
  system is complete.
- Failed story operations remain available until won.
- Planet control changes after every mission result.
- A system becomes secured only when its story operation is won.
- System capture pays **350 credits** once.

## Adding the next system

Create five planetary mission resources and one story mission resource, then
create a system resource that lists those paths. Add that system resource to
`CampaignData.SystemPaths`. No random-mission or global story-queue changes
are required.

Future work can add system-unlock prerequisites when a second system is added;
the current campaign intentionally exposes only Orion Spur.
