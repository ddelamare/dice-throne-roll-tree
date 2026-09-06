# Hero token delta values

These are strategic heuristics for the advisor's `expectedDelta` calculation. They are not official card values and do not mean that a token is worth that many immediate damage. A token value is the estimated conditional value of gaining one token when an objective succeeds; repeated tokens in an objective multiply that value.

The scale is deliberately close to the existing evaluator defaults:

- `1-1.5`: setup, bookkeeping, or a situational resource
- `2-2.5`: useful recurring resource, moderate control, or conditional defense
- `3-3.5`: strong control, reliable defense, or an engine that materially improves later turns
- `4`: swingy premium value such as stun, a major lockout, or a strong companion denial effect

This is a standard 1v1, base-board estimate. It does not model current health, cards in hand, upgrades, existing resource stacks, opponent matchup, team play, or whether the token can be used immediately. Those conditions can make a token worth substantially more or less.

## Values and strategy summary

| Hero | Token delta values | Strategy implication |
| --- | --- | --- |
| Barbarian | `Stun 4` | Prioritize aggressive attacks and stun chains; Fortitude is the attrition fallback because he has little damage prevention. |
| Black Panther | `Kinetic Energy 1.5`, `Vibranium Suit 3` | Trade damage to build Kinetic Energy, then convert the charge into scaling attacks; preserve the Suit for a meaningful hit. |
| Black Widow | `Agility 2.5`, `Time Bomb 3` | Build upgrades and plant bombs over time; Agility covers her early-game softness while the upgrade engine compounds. |
| Cyclops | `Battle Plan 2`, `Focus Fire 3`, `Support 2` | Set up Focus Fire early; Battle Plan and Support are flexible economy that enable rerolls, cards, CP, and team utility. |
| Druid | `Regenerate 2.5`, `Shape Shift 2` | Shift into the form that fits the moment, using Regenerate for the long game and Bear form when survival matters. |
| Duelist | `Disarm 3`, `Guard Break 2`, `Step 1` | Advance the Footwork track and use Disarm/Guard Break to win key exchanges; Step is positioning value rather than direct output. |
| Forgemaster | `Mine 2.5`, `Ore 2.5` | Mine and bank Ore before forcing the grind; Armor is the real defensive engine and the dice attacks are the tempo layer. |
| Gambit | `Molecular Acceleration 1.5`, `Dissolution 2`, `Disruption 2.5` | Charge Ace Cards steadily, then explode them at the right time; Disruption taxes the opponent while Dissolution provides flexible recovery or cleansing. |
| Headless Horseman | `Dreadful 3`, `Grim Pursuit 3` | Build Dreadful early because it scales both offense and defense; save Grim Pursuit for extra rolls or a high-value damage conversion. |
| Iceman | `Dice Cube 2.5`, `Ice Shard 2.5` | Lock the opponent's lowest die and accumulate Shards for damage and Glide chains; the engine rewards planning across turns. |
| Jean Grey | `Acuity 1.5`, `Flame Blast 1.5`, `Force Field 3` | Use Jean turns for economy and defense, then spend Flame Blast on Dark Phoenix burst turns; Force Field is premium survival. |
| Loki | `Bag of Tricks 1.5`, `Illusion 4`, `Spellbound 3` | Win through disruption rather than raw damage: lock important abilities, use Bag of Tricks for resource pressure, and keep Illusion for dangerous attacks. |
| Necromancer | `Corpse 1.5`, `Decrepify 2.5`, `Resurrect 4` | Build the Undead swarm before chasing damage; Decrepify slows an opponent and Resurrect is a powerful but costly insurance policy. |
| Pale Lady | `Prey 1.5`, `Bleed 1.5`, `Moon Shard 3` | Accumulate Moon Shards to time the Werewolf transformation; Prey and Bleed are useful chip but secondary to the transformation window. |
| Pale Lady Werewolf | `Prey 1.5`, `Bleed 1.5`, `Moon Shard 3` | Exploit the short Werewolf window for undefendable burst, using Prey/Bleed to extend pressure rather than replacing the transformation plan. |
| Psylocke | `Paralyze 4`, `Agility 2.5`, `Infiltration 2.5` | Press the tempo with the Manifest die; Paralyze shuts down status engines while Agility and Infiltration protect key exchanges. |
| Pyromancer | `Fire Mastery 2.5`, `Burn 2`, `Knockdown 3`, `Stun 4` | Build Fire Mastery and detonate before it cools off; Burn, Knockdown, and Stun compensate for her weak defensive profile. |
| Raveness | `Feather 2.5`, `Hex 4` | Feed and manipulate Nevermore with Feathers; Hex is a high-impact control payoff against dice that rely on sixes. |
| Rogue | `Influence 2.5`, `Skyward 2.5` | Drain the opponent's rerolls and convert incoming damage into Ionic Energy; Skyward gives her a conditional defensive swing. |
| Scarlet Witch | `Conjure 2.5`, `Crackle 2`, `Probability Manipulation 2.5`, `Reality Warp 3` | Stack status effects and preserve dice control for the right conversion; Crackle is strongest when several statuses are already present. |
| Miles Morales Spider-Man | `Combo 3`, `Webbed 3`, `Invisibility 2.5` | Maintain tempo through a second offensive phase, make the next hit undefendable with Webbed, and reserve Invisibility for otherwise unavoidable attacks. |
| Storm | `Lightning 2.5`, `Tornado 2.5`, `Wind Shear 3` | Charge abilities before spending them; Tornado expands future rolls and Wind Shear combines meaningful prevention with counter damage. |
| Doctor Strange | `Deja Vu 3.5`, `Premonition 2`, `Crimson Bands 2.5` | Prepare spells and manage the hand; Deja Vu is a premium second chance, while Crimson Bands is matchup-sensitive control. |
| Sun Elf | `Sun Dial 2.5`, `Charged Gem 2`, `Sun Marked 2` | Ramp the Dial on Dusk turns and cash it out on Dawn turns; Charged Gem and Sun Marked add flexible economy and sustain. |
| Thor | `Electrokinesis 1.5`, `Guard Break 2` | Keep Electrokinesis flowing so later attacks scale; use Guard Break when making a large attack matters more than saving the token. |
| Wolverine | `Rage 3`, `Alpha 2` | Fight the attrition game with healing and Rage-powered attacks; Alpha is useful pressure but less reliable than Rage's direct conversion. |

## Research basis

The strategy notes were synthesized from the [Dice Slayer hero map](https://diceslayer.com/map/) and its hero pages, including the [Barbarian guide](https://diceslayer.com/heroes/barbarian/), [Gambit guide](https://diceslayer.com/heroes/gambit/), [Headless Horseman guide](https://diceslayer.com/heroes/headless-horseman/), [Doctor Strange guide](https://diceslayer.com/heroes/doctor-strange/), [Forgemaster guide](https://diceslayer.com/heroes/forgemaster/), and [Thor guide](https://diceslayer.com/heroes/thor/). Token mechanics were cross-checked against the map's status-effect index and the [Dice Throne rulebook](https://files.roxley.com/Dice-Throne-Rulebook-v2.0.pdf).

The external strategy material is community analysis, not a game-balance specification. The values should therefore be tuned as the advisor gains stateful modeling. In particular, tokens that scale with existing stacks, a specific form, a prepared spell, an Ace Card, a companion, or an opponent's exact board state should not be treated as universally fixed-value resources.
