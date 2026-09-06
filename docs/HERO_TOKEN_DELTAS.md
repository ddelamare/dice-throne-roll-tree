# Hero token delta values

These are strategic heuristics for the advisor's `expectedDelta` calculation. They are not official balance values and do not mean that a token is worth that many immediate damage points.

The values are based on the token's actual payoff, not its label or whether it is positive/negative. The base value is the incremental value of one token. A threshold entry is an additional bonus for each complete threshold in the tokens granted by that objective:

`token delta = token count × base value + floor(token count / threshold) × threshold bonus`

Threshold bonuses model effects such as a charged Ace Card or a three-token attack modifier. They are deliberately additive and conservative; the advisor does not yet know the hero's current token stack, upgrade state, hand, health, or matchup. A threshold involving tokens already on the board therefore remains an approximation until that state is supplied.

## Values and strategy summary

| Hero | Token delta values | Strategy implication |
| --- | --- | --- |
| Barbarian | `Stun 5` | Stun is a premium tempo payoff because it removes an opponent turn and supports Barbarian's burst plan; keep pressure up while using Fortitude as the fallback. |
| Black Panther | `Kinetic Energy 0.5; +9 at 8`, `Vibranium Suit 3` | Kinetic Energy is mostly incremental attack scaling until the eight-token burst; Suit is immediate damage prevention, so do not value them alike. |
| Black Widow | `Agility 2`, `Time Bomb 2.5` | Agility protects the setup turns; Time Bomb is valuable but delayed and opponent-dependent, so it is discounted from a guaranteed payoff. |
| Cyclops | `Battle Plan 1.25; +2 at 3`, `Focus Fire 2.5`, `Support 1.5; +1.5 at 3` | Battle Plan and Support are flexible leadership resources, with extra value when enough are banked to unlock the stronger spend; Focus Fire is a persistent offensive engine. |
| Druid | `Regenerate 2`, `Shape Shift 1.5` | Regenerate is delayed healing and Shape Shift is flexible form access; both are useful, but neither should outrank a reliable attack by default. |
| Duelist | `Disarm 3.5`, `Guard Break 2`, `Step 0.75` | Disarm can deny the opponent's economy, Guard Break is a probabilistic attack-quality improvement, and Step is mainly setup for the Footwork engine. |
| Forgemaster | `Mine 1.5`, `Ore 2.5` | Mine is setup; Ore is closer to the payoff because it fuels the defensive and offensive forge loop. |
| Gambit | `Molecular Acceleration 0.75; +4 at 4`, `Dissolution 2`, `Disruption 2.5` | Molecular Acceleration is normally charge bookkeeping, but a full four-token charge can convert into an Ace Card explosion; Disruption is reliable opponent taxation and Dissolution is flexible recovery/cleansing. |
| Headless Horseman | `Dreadful 1.25`, `Grim Pursuit 2.5` | Dreadful compounds the board's offense/defense and is worth steady collection; Grim Pursuit is a higher-impact but more situational roll resource. |
| Iceman | `Dice Cube 2`, `Ice Shard 0.75; +4 at 4, +3 at 5` | Dice Cube is immediate control. Ice Shards are cheap individually but become much more valuable at the four-Shard combo threshold and again at five when they can support another Glide. |
| Jean Grey | `Acuity 0.75; +3 at 4`, `Flame Blast 1; +1 at 3`, `Force Field 3.5` | Acuity is setup for Dark Phoenix, Flame Blast is a modest incremental modifier with a meaningful three-token attack spend, and Force Field is reliable survival. |
| Loki | `Bag of Tricks 1`, `Illusion 4`, `Spellbound 3` | Bag of Tricks is flexible economy; Illusion and Spellbound are high-value control because they can deny or distort the opponent's best line. |
| Necromancer | `Corpse 1; +2 at 3`, `Decrepify 2.5`, `Resurrect 4.5` | Corpses are setup for the undead engine, with a breakpoint when enough are assembled; Decrepify is recurring control and Resurrect is premium insurance. |
| Pale Lady | `Prey 1`, `Bleed 2`, `Moon Shard 0.5; +6 at 3` | Bleed is worth its recurring damage, while Moon Shards are low-value until the three-Shard Werewolf transition becomes available. |
| Pale Lady Werewolf | `Prey 1`, `Bleed 2`, `Moon Shard 0.5; +6 at 3` | The Werewolf side makes the transformation window the priority; Prey and Bleed extend pressure but do not replace the shard breakpoint. |
| Psylocke | `Paralyze 4.5`, `Agility 2`, `Infiltration 2` | Paralyze is a major tempo denial effect; Agility and Infiltration are valuable defensive/positioning resources but are more conditional. |
| Pyromancer | `Burn 2`, `Fire Mastery 1.5`, `Knockdown 3`, `Stun 5` | Fire Mastery is a setup track whose value depends on cashing it out before it cools; Burn, Knockdown, and Stun are more immediate attack or tempo payoffs. |
| Raveness | `Feather 1; +2 at 3`, `Hex 4` | Feathers are setup for Nevermore and gain value when enough are assembled; Hex is a high-impact control payoff against important dice. |
| Rogue | `Influence 2.5`, `Skyward 2` | Influence directly constrains the opponent's future options; Skyward is a conditional defensive swing, so it is slightly less reliable. |
| Scarlet Witch | `Conjure 2`, `Crackle 1`, `Probability Manipulation 2`, `Reality Warp 3` | Conjure and Reality Warp support high-impact spell lines; Crackle is deliberately discounted because its payoff depends on statuses already in play. |
| Miles Morales Spider-Man | `Combo 4`, `Webbed 3`, `Invisibility 2` | Combo is the core extra-offensive-phase engine, Webbed converts the next attack into a reliable hit, and Invisibility is strongest when it protects a key turn rather than as generic defense. |
| Storm | `Lightning 0.5; +1 at 2`, `Tornado 1.5`, `Wind Shear 3` | Lightning is low-value until two charges can be converted into isolated damage; Tornado improves future roll access, while Wind Shear has a direct defensive/offensive payoff. |
| Doctor Strange | `Deja Vu 3.5`, `Premonition 1.5`, `Crimson Bands 2.5` | Deja Vu is a premium second chance, Premonition is preparation, and Crimson Bands is powerful control whose value depends on the opponent's hand. |
| Sun Elf | `Sun Dial 1.25`, `Charged Gem 1.5`, `Sun Marked 1.5` | Sun Dial is a timing resource that should be saved for the favorable side of the board cycle; the other tokens are flexible sustain/setup rather than raw damage. |
| Thor | `Electrokinesis 1`, `Guard Break 2` | Electrokinesis compounds future attacks, while Guard Break is a conditional way to improve one important attack. |
| Wolverine | `Rage 3`, `Alpha 1.5` | Rage is a strong repeatable attrition resource; Alpha helps the attack plan but is less dependable than Rage's direct conversion. |

## Research basis

The strategy framing was synthesized from the [BoardGameGeek Dice Throne Strategy series](https://boardgamegeek.com/thread/2642022/dice-throne-strategy) and the [BoardGameGeek Dice Throne strategy forum index](https://boardgamegeek.com/boardgame/268201/dice-throne/forums/67), which links the character-specific guides for the roster. I also used the detailed [Pale Lady / Werewolf guide](https://boardgamegeek.com/thread/3627338/character-strategy-series-pale-lady-werewolf), [Iceman guide](https://boardgamegeek.com/thread/3477180/character-strategy-series-iceman), [Spider-Man guide](https://boardgamegeek.com/thread/2949057/character-strategy-series-spider-man), and the [Marvel Dice Throne: X-Men overview](https://boardgamegeek.com/thread/3720014/marvel-dice-throne-x-men-two-boxes-eight-heroes-bo). These guides emphasize engine timing, resource breakpoints, control, defense quality, and matchup-dependent value.

Strategy-video cross-checks came from UNDEFENDABLE's [Season 1 quick-guide video](https://www.youtube.com/watch?v=GerT25zGqwU), [Barbarian guide](https://www.youtube.com/watch?v=HI05Hlt6xo0), and [Spider-Man guide](https://www.youtube.com/watch?v=gNsYPgCQ1fY). The videos are used for practical game-plan context; the values above remain explicit heuristics inferred from those plans and the token effects represented in the hero data.

This is intentionally not a universal tier list. The best token value changes with current health, opponent defense, hand/CP, existing stacks, upgrades, and whether the token can be spent immediately. The next refinement would be to pass those state variables into the evaluator instead of increasing the static map further.
