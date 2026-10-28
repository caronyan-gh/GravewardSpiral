# GRAVEWARD SPIRAL Help

The same text as Help in the in-game Esc menu (and on the title screen). Topics:

- Controls
- Getting Started
- Town Facilities
- Characters
- Spells
- Exploring the Dungeon
- Camp
- Combat
- Class Features
- Items and Equipment
- Death and Rescue
- Options and More
- Main Differences from the SRD

## Controls

### Basic keys

Play with a keyboard or gamepad. Menus also support the mouse: click to confirm and use the wheel to scroll through a list.

- Confirm: Enter / Space / Z / gamepad A
- Cancel or go back: Esc / Backspace / X / gamepad B
- Move or select: arrow keys / WASD / D-pad / left stick
- Menu (camp in the maze): Tab / C / gamepad Start or Y
- Map: M / gamepad Back or X
- Page through lists: PageUp / PageDown / Q / E / gamepad LB / RB
- Toggle fullscreen: F11 / Alt+Enter

Some screens use Left and Right to switch tabs or pages. Check the controls shown at the bottom of each screen.

### Moving in the maze

- Up: move forward one square.
- Left and Right: turn on the spot.
- Down: step backward or turn around, depending on the "Back key" option.
- Q and E (gamepad LB and RB): move one square sideways without turning.
- Confirm: examine what is ahead or underfoot, such as a door, chest, or staircase.
- Hold Shift (gamepad L2) and press Down: perform the opposite of the "Back key" setting. If Down normally steps backward, you turn around; if it normally turns around, you step backward.

### The Esc menu

Press Esc to open the menu for Help, Options, and quitting. In the maze, it also offers Camp and "Suspend and Quit".

### Reading the help

Use Up and Down to select a heading in the contents on the left. The text on the right jumps to that heading. Scroll the text with Left, Right, PageUp, PageDown, or the mouse wheel. Press Cancel or the menu key to close Help.

### Changing keys

Edit input.json in the user data folder (%LOCALAPPDATA%\GravewardSpiral) to change key assignments. The fullscreen shortcut cannot be changed.

## Getting Started

### What kind of game is this

GRAVEWARD SPIRAL is a dungeon crawler in the style of early Wizardry, using rules based on the D&D 5th edition SRD 5.1. Prepare in town, explore with your companions, and bring treasure home.

The game adapts SRD 5.1 with its own rules and simplifications for dungeon exploration. Familiar spells and abilities may work differently. Use this help and the in-game descriptions when playing.

### Starting a new game

Choose "New Game" on the title screen, then select a scenario, clan, and difficulty.

- Scenario: choose a story scenario or "Free dungeon (no story)". For a free dungeon, you can set the map-generation seed, number of floors, and floor size.
- Clan: the roster of characters you bring on the adventure. Create a new clan or keep using one from a previous adventure.
- Difficulty: determines what happens after a party wipe, whether resurrection can fail, and how saving works. See "Difficulty" under "Options and More".

A new world may carry over the party from a previous world, selecting available companions starting with the most recent world. Check the members and formation at the Tavern before setting out.

### What to do first

- Create characters at the Training Grounds. For a brief guide to each class, see "Races and classes" under "Characters".
- Form a party of up to six at the Tavern. The first three members are the front row; the rest are the back row. Put melee fighters in front and companions using bows or spells in back.
- Check equipment under "Items". A weapon or armor in a pack is not equipped. Buy any gear or supplies you need at the Shop.
- Check your spellcasters' spells at the Training Grounds. Some classes must prepare spells before using them, even if they already know them. "Auto-fill" fills unused choices.
- Choose "Enter the Maze" in town to set out. You need a companion who can act.

Start on the shallow floors while learning combat and exploration. Return to town before HP and spell slots run too low.

### Returning to town

Press Confirm at the up stairs on B1 to return to town. The game saves automatically when you return.

## Town Facilities

### Tavern

Form and manage your party here. Add or remove companions and change their order. A party holds up to six members: the first three are the front row and the rest are the back row. You can also hear rumors from patrons.

### Inn

Staying at the inn gives a long rest, restoring HP, spell slots, and class feature uses. In this game, HP recovery depends on where you sleep. Fees are paid from the party's combined gold.

- Stable: free. Restores HP up to 50% of maximum. It does not reduce HP that is already above that amount.
- Room: costs 10 gold per character level for each guest. Restores HP fully.

If funds run short, members rest in party order as their fees can be paid. The stable also restores spell slots and class feature uses. For Hit Dice recovery, see "Long rests" under "Camp".

### Shop

Buy and sell gear, identify items, and remove curses. Purchases use the party's combined gold; sale proceeds are shared among party members.

### Temple

Resurrect dead companions and treat conditions such as poison and paralysis. You can check the price and success chance before attempting resurrection. See "Death and Rescue".

### Training Grounds

Create characters, inspect their status, level up, rename them, or delete them. You can also change spell selections here. "Export Clan" saves the roster to a file.

### Clan House

Your clan's shared base. Deposit or withdraw items, manage equipment, and use "View Status" to inspect current party members.

Upgrades require gold and sometimes materials. They increase storage and add facilities. Once you have beds, you can take a long rest for free. Basic beds restore HP up to 70% of maximum; better beds restore it fully. HP above the recovery limit is not reduced.

Other facilities offer benefits such as cheaper identification with a library. The screen shows the cost, materials, and facilities for an upgrade.

### Enter the Maze

Set out with the current party. At least one companion must be able to act.

### Other entries

- Items: change equipment, use items, give them to companions, or discard them.
- Visit People: speak to townsfolk. Available when there is someone to visit.
- The Tale of This Dungeon: reread the scenario's introduction and the town's request.
- Save and Quit: save your progress and return to the title screen.

## Characters

### Ability scores

Six ability scores describe a character's strengths.

- Strength: affects weapon force and physical tasks.
- Dexterity: affects agility, accuracy with weapons such as bows, and combat turn order.
- Constitution: affects maximum HP and checks to withstand effects such as poison.
- Intelligence: affects investigation, knowledge, and wizard spells.
- Wisdom: affects awareness and cleric, druid, and ranger spells.
- Charisma: affects social checks and bard, sorcerer, warlock, and paladin spells.

Checks use an ability modifier calculated as (score - 10) divided by 2, rounded down. A score of 10 gives +0, 14 gives +2, and 8 gives -1.

At creation, assign the standard values (15, 14, 13, 12, 10, 8) or use point buy. Ability scores are not rolled randomly. Starting gold depends on the class.

At class levels 4, 8, 12, 16 and 19 (fighters also at 6 and 14, rogues at 10), gain 2 ability points. At the training grounds, choose "Raise Ability Scores" to add 1 to a score per point (up to 20). Points can be saved, so you can use them to meet multiclass requirements. Raising your Constitution modifier also raises your max HP by your level.

### Races and classes

The nine races are Human, Elf, Dwarf, Halfling, Gnome, Half-Elf, Half-Orc, Tiefling, and Dragonborn. Besides ability score increases, races have traits.

- Dwarf: Takes half poison damage and has advantage on saves against being poisoned. +1 HP per level.
- Elf and Half-Elf: Advantage on saves against being charmed, and magic can't put you to sleep.
- Halfling: Rerolls a natural 1 on attacks, saves and death saves. Advantage on saves against being frightened.
- Dragonborn: Can breathe fire on a nearby enemy group in battle (listed with class features, recharges on a short rest). Takes half fire damage.
- Gnome: Advantage on Intelligence, Wisdom and Charisma saves against magic.
- Half-Orc: Once per long rest, drops to 1 HP instead of 0 (unless killed outright). Rolls one extra weapon die on melee critical hits.
- Tiefling: Takes half fire damage.

Darkvision currently has no effect on how the dungeon looks.

There are twelve classes.

- Fighter: specializes in weapon attacks and makes a straightforward front-row combatant.
- Barbarian: uses Rage to strengthen melee attacks and reduce physical damage.
- Paladin: fights with weapons while healing and protecting allies.
- Ranger: fights with weapons and gains spells as the class develops.
- Rogue: excels at skills and disarming traps. Sneak Attack adds damage when its conditions are met.
- Monk: fights with unarmed attacks and techniques fueled by ki.
- Cleric: uses healing and support magic and can counter undead.
- Druid: uses nature magic and transforms into beasts.
- Wizard: prepares spells from a spellbook to handle varied situations.
- Sorcerer: casts known spells and later gains the ability to empower damage spells.
- Warlock: has spell slots that recover on a short rest.
- Bard: supports companions with spells and inspiration.

### Leveling up

When a character has enough experience, level up at the Training Grounds. An arrow marks eligible characters in the list, and a notice also appears after battle.

As a game-specific progression rule, there is no cap on total character level. From level 21, each "epic" level lets you choose a boon.

### Multiclassing

Each time you level up, choose which class to advance. Taking a new class does not remove the levels or features of your existing classes.

To take a new class, meet the ability score requirements for your current classes and the new class. The selection screen shows the required scores. Unlike the narrower SRD multiclass proficiency rules, this game grants all weapon and armor proficiencies of the new class.

Total character level and individual class levels are different. A fighter 5 / barbarian 2 has a total level of 7, but their barbarian features work at level 2.

Learn and prepare spells separately for each spellcasting class. See "Multiclass spellcasting" under "Spells" for how this relates to spell slots.

### Proficiency

Proficiency represents training with weapons, armor, skills, or similar tasks. Attacks and checks you are proficient in receive a proficiency bonus based on total character level.

- Armor and shields: require proficiency to equip.
- Weapons: can be used without proficiency, but attacks receive no proficiency bonus.

Check equipment proficiencies on the status screen. Item descriptions also show which companions can equip an item and who is proficient.

### Reading the status screen

Inspect companions at the Training Grounds, at camp, or in the Clan House. The left panel shows a summary; the right panel shows details.

Use Left and Right to switch the right panel between skills and features, spells, equipment, and carried items. Companions with no castable spells have no spells page. Press Confirm or Cancel to close the screen.

"Slots 1: 3/4" means three of four level-1 spell slots remain. Other current/maximum values, such as HP and Hit Dice, help you track remaining resources.

### Dice and checks

The game rolls dice automatically. "1d20" means one twenty-sided die; "2d6+3" means two six-sided dice added together, plus three.

- Attack roll: add the attack bonus to the roll. A total at least as high as the target's AC hits. A natural 1 misses; a natural 20 hits.
- AC: how difficult a target is to hit. Higher is better.
- Saving throw (save): a check to resist an effect such as a spell or trap. A Dexterity save uses the relevant Dexterity bonuses.
- DC: the target number for a check. A saving throw or skill check succeeds when its total meets or exceeds the DC.
- Advantage and disadvantage: roll a d20 twice. Advantage uses the higher result; disadvantage uses the lower. If both apply, they cancel out.

Skills describe proficiency in tasks such as investigation or stealth. Checks use the ability modifier plus applicable proficiency, equipment, and other bonuses.

## Spells

### Spell slots and remaining uses

Spell slots are the uses available for casting spells. They have levels from 1 to 9. Casting a spell consumes one slot of the level used. A character's spells share the slots of each level.

With three level-1 slots, you can cast level-1 spells three times in total: the same spell three times or different spells. Cantrips consume no slots and can be cast any number of times.

Using a higher-level slot can increase a spell's damage, healing, or other effects. The description shown when selecting a spell reflects the casting level.

A long rest restores spell slots. Warlock slots also recover on a short rest.

### Known and prepared spells

Choosing which spells you can use is separate from having slots to cast them. Casting a spell does not remove it from your known or prepared list; it consumes a spell slot.

- Cleric, Druid, and Paladin: prepare the spells you want to use from the class spell list. Unprepared spells cannot be cast.
- Wizard: first choose spells for your spellbook, then prepare the ones you want to use. A spell must be prepared, not merely written in the book.
- Sorcerer, Bard, Ranger, and Warlock: cast known spells directly. No preparation is needed.

Cantrips are chosen separately. The number you can learn increases with class level.

### Choosing known and prepared spells

On spell selection screens at the Training Grounds or camp, press Confirm to mark or unmark a spell (◆), up to the limit. "Auto-fill" fills any unused choices.

If you have several spellcasting classes, Left and Right switch classes on this screen.

### Changing your choices

In this game, change prepared spells at any time at the Training Grounds, the inn, or camp, without waiting to complete a rest.

Rechoose cantrips, known spells, and a wizard's spellbook at the Training Grounds whenever you like. You do not need to wait for a level-up. This is a rule specific to this game.

### Choosing a spell to cast

Choose "Cast" in combat or "Cast Spell" at camp. First select the slot level using the tabs at the top, then choose a spell and its target. There is no separate slot-selection prompt afterward.

- The cantrip tab uses no slots. Tabs 1 to 9 select the slot level.
- Switch tabs with Left, Right, or a mouse click.
- A number such as "(3)" on the selected tab shows the remaining slots at that level.
- Red means 0 left, yellow 1, green 2–3, and blue 4 or more.
- Black means you do not have slots of that level yet. Gray means there are no selectable spells for that tab on the current screen.

Spells that improve with higher-level slots also appear on higher tabs. For example, choosing Cure Wounds from tab 2 spends one level-2 slot.

Even a spell that does not improve at higher levels appears on the lowest available higher-slot tab when its own level's slots cannot be used.

### Bonus-action spells

Eldritch Blast (2, 3 and 4 beams at total character level 5, 11 and 17) and Scorching Ray (3 rays at 2nd level, one more per higher slot) fire several beams, each with its own attack roll and damage. Once there are two or more, the spell list shows a Focused and a Spread line. Focused fires at one creature in the chosen group, moving to another in the group if it falls. Spread fires one beam at each of different creatures in the group, and any extra beams go to the survivors.

Spells cast as a bonus action, such as Healing Word or Shield of Faith, are followed by an automatic action that same turn. In the front row you attack with your weapon; in the back row you use an attack cantrip (or a weapon that reaches, or Dodge if neither). To only heal, use an action spell such as Cure Wounds.

### Where you can cast and restrictions

- Combat lists spells usable in battle.
- Camp lists healing and other spells usable while exploring. Combat-only spells cannot be cast there.
- Anti-magic zones prevent spellcasting and the use of items that cast spells.
- Wearing armor or a shield without proficiency also prevents spellcasting.

### Concentration

A spell marked "Concentration" continues while its caster maintains concentration. A character can concentrate on only one spell at a time.

Casting another concentration spell ends the previous one. You can cast spells that do not require concentration while maintaining it.

Taking damage requires a Constitution save to maintain concentration. Becoming unable to act, such as falling unconscious, also ends it.

### Multiclass spellcasting

The spells you can learn and prepare depend on each class's own level. Slot counts depend on your combination of spellcasting classes, so you may have slots above the level of spells you can learn.

For example, a cleric 3 / wizard 2 has level-3 slots but can select cleric spells only up to level 2 and wizard spells up to level 1. The level-3 slots can still cast lower-level spells you have learned or prepared.

Warlock slots can also cast spells from other classes. When regular and warlock slots exist at the same level, warlock slots are spent first because they recover on a short rest.

## Exploring the Dungeon

### Moving around

Move one square at a time through a first-person wireframe dungeon. You can see up to four squares ahead. Move forward, step back, sidestep, and turn to explore. Floors are labeled B1, B2, and so on.

### Maps

The minimap in the upper-right corner shows your surroundings. Press M to open a larger map.

- Visited squares and discovered walls and doors are recorded automatically.
- The larger map shows only the area revealed by exploration. It does not reveal the full size of an unexplored floor.
- Secret doors appear as walls until discovered.
- In Options, choose minimap and map, map only, or no map. You can also explore with your own graph-paper map.

### Darkness and fog

- Darkness hides the view ahead, but exploration is still recorded on the map. A light spell or similar effect restores visibility while it lasts.
- Fog leaves the view ahead visible, but stops mapping and hides your current position on the map.

### Doors and keys

Press Confirm while facing a wall or door to investigate it.

- Searching a wall may reveal a secret door.
- A one-way door looks like a door from the side that allows passage, and a wall from the other side.
- A locked door can be opened with that floor's key, thieves' tools, Knock, or a kick. A failed kick may attract enemies.
- Keys found in the dungeon work on locks on the floor where they were found.

### Dungeon hazards

Spinners change your facing, and teleporters move you elsewhere. Pits damage the whole party, while chutes drop you onto another floor. You cannot cast spells in an anti-magic area.

Some floors wrap around: moving past one edge brings you to the opposite edge. If you lose your bearings, check the map and your surroundings.

### Treasure chests

A chest may appear after a victory. Choose a party member to inspect or disarm it.

- Inspect: Use Intelligence and Investigation proficiency to identify the trap. The result can be wrong, and a very poor attempt can trigger the trap.
- Disarm: Name the trap and try to remove it. Even the correct answer does not guarantee success. A character with high Dexterity, proficiency with thieves' tools, and those tools in their inventory is a good choice. Naming the wrong trap triggers it.
- Open: Open the chest without disarming it. Any trap has a high chance of going off.
- Leave: Walk away without opening it.

There are nine trap types: poison needle, gas, crossbow, bomb, stun, teleporter, mage blaster, priest blaster, and alarm. A chest trap that reduces a character to 0 HP kills them without the death saves used in combat. Opening a chest with little HP left can be dangerous.

### Stairs and elevators

Press Confirm on a staircase to use it. Elevators let you choose among floors you have already visited. The stairs up from B1 lead back to town.

### Notes

Notes recorded during exploration can be read again at camp.

## Camp

### Opening camp

Press a menu key such as Tab or C in the dungeon to open camp. You can also open it from the Esc menu. Use camp to rest, heal, change equipment, and prepare for further exploration.

### Camp actions

- Short Rest: Rest for one hour and spend Hit Dice to recover HP. Some class abilities and a warlock's spell uses also recover. Enemies may interrupt your rest.
- Cast: Cast healing spells and other spells available outside combat.
- Prepare Spells: Change the spells available to classes that prepare them. This does not restore spell uses.
- Items: Check party members' belongings and change their equipment.
- View Status: Check abilities, skills, spells, equipment, and inventory. Use Left and Right to change the right-hand page.
- Formation: Select party members in their new order. The first three form the front row.
- Read Notes: Review notes recorded during exploration.
- Previous Battle Log: Read the most recent battle from the beginning.
- Options: Adjust volume and other options.

Anti-magic areas prevent spellcasting at camp too.

### Short rests and Hit Dice

Hit Dice are a limited resource used to recover HP during short rests. Your maximum number equals your total character level. Spending one reduces the number remaining.

In this game, you do not select how many Hit Dice to spend. A short rest spends them automatically until HP is full or none remain. Each die restores HP based on its roll and the character's Constitution modifier.

Once you run out, short rests cannot restore more HP this way. Spell uses and class abilities that recover on a short rest still recover even without Hit Dice.

If the rest ends without an ambush, each party member able to act studies and identifies one unidentified item.

### Long rests

Only short rests are available in the dungeon. Long rests are available at the inn, or at a clan house with beds.

A long rest restores spell uses and class abilities, and removes lingering conditions. HP recovery depends on where you sleep. See Town Facilities for details.

A long rest also restores half your total level in Hit Dice, rounded down, with a minimum of one. A level 5 character regains two, for example. One night's rest may not restore every Hit Die you spent.

### Lingering conditions

Poison, blindness, and some other conditions may remain after combat. Some wear off over time, while others require treatment. Use a spell, visit the temple, or take a long rest.

Lingering poison reduces HP as you walk. This walking damage alone cannot reduce HP below 1, but it leaves you vulnerable to the next battle or trap.

## Combat

### Formation and range

The party has three front-row and three back-row positions. Enemies appear in up to four groups, each marked as near or far.

- Melee weapon attacks can reach only near enemies from the front row.
- Back-row characters can attack with ranged weapons or spells.
- Ranged weapons and ranged spells can reach far enemies. Some spells, including melee spells, have more restricted targets; check their descriptions.

After the battle log finishes displaying, fallen party members move to the back of the formation. Change formation at camp or with Swap during combat.

### Each round

Choose an action for every party member at the start of a round. Once all actions are selected, both sides take their turns. Turn order is determined by a die roll and Dexterity at the start of combat, not by the order in which you enter commands.

Auto chooses actions for the remaining party members. You can allow or forbid spell use. Return to the previous character to revise an earlier command.

- Attack: Choose an enemy group and attack with an equipped weapon. Weapons merely carried in a bag are not used.
- Cast: Choose the cantrip tab or a spell-level tab, then the spell and target. Casting spends a slot of the selected tab's level.
- Cast (Empowered:n): Spend one sorcery point to reroll low damage dice for a spell. The number is your remaining points.
- Class Feature: Use Rage, Second Wind, or another class ability. See Class Features for details.
- Item: Use an item, or switch weapons or ammunition. Switching also uses that round's action.
- Dodge: Give attacks against you disadvantage until your next turn.
- Help: Choose an ally and give their next attack roll advantage.
- Hide: A successful Stealth check makes enemies less likely to target you and gives your next attack roll advantage.
- Swap: Exchange formation positions with an ally.
- Flee (All): Attempt to escape with the whole party. Escape can fail.

For advantage, disadvantage, and attack rolls, see Dice and Checks under Characters.

The battle log appears one line at a time. Press Confirm to advance, or hold it to speed through the log.

### Surprise and identification

If surprised, you cannot act in the first round. If enemies fail to notice you, you can act before they do.

Party members may identify enemies with an Intelligence check. Until identified, an enemy has a vague name such as Small Humanoid. Defeating one reveals that enemy type's identity.

### Fallen allies

Reaching 0 HP in combat usually causes unconsciousness. An unconscious character cannot act and makes death saves on their turn.

- Three successes stabilize them, ending death saves. They remain unconscious and cannot act.
- Three failures cause death. Attacks against an unconscious character add failures.
- Restoring HP with healing ends unconsciousness.
- Extremely large damage from a single hit can cause instant death.

After a victory, unconscious survivors rise with 1 HP. Dead characters require resurrection, such as treatment at the temple.

### Ongoing damage

Some attacks leave ongoing damage, such as burning or poison damage at the start of each turn. Depending on the effect, it ends after a set number of rounds or a successful save. Copies of the same effect do not stack, but different effects can apply together. Ongoing damage ends when combat ends.

These effects are separate from conditions such as Poisoned, which may linger after combat.

### Conditions

A condition's name appears before HP in the party list.

- Poisoned and Frightened give your attack rolls disadvantage.
- Blinded gives your attacks disadvantage and makes attacks against you more likely to hit.
- Paralyzed, Stunned, and Unconscious prevent you from acting.

Poisoned, Blinded, Deafened, Frightened, and Paralyzed may linger after combat. Treat them with spells, at the temple, or with a long rest.

### Rewards

Victory divides experience equally among party members and awards gold. A treasure chest may also appear. See Exploring the Dungeon for chest handling.

### Reviewing the log

Press Tab during combat to open the log. After combat, Previous Battle Log at camp lets you review the latest battle.

## Class Features

### Using class abilities

Class features are simplified from the SRD for dungeon play. Subclasses and some abilities or choices are not included.

Choose Class Feature in combat to use an ability you have learned. Other abilities work automatically.

Check learned abilities and remaining uses on the Skills page of the status screen.

### Multiclass abilities

Each ability is learned and scales with levels in its own class. A fighter 5 / barbarian 2 can use both the fighter's Action Surge and the barbarian's Rage.

Extra Attack from multiple classes does not stack. The highest attack count among your classes applies.

### Recovering uses

Second Wind, Action Surge, ki, Channel Divinity, and Wild Shape recover on a short rest.

Bardic Inspiration recovers on a short rest from bard level 5 onward. Before that, it requires a long rest, as do Rage, Lay on Hands, sorcery points, and similar resources.

### Fighter and barbarian abilities

- Second Wind: Recover your own HP. It is a bonus action, so you also attack that turn.
- Action Surge: Make two normal attack actions in succession. Extra Attack applies, but bonus attacks such as an off-hand attack are not doubled.
- Rage: Attack and enter a rage. For this battle, deal more damage with Strength melee attacks and take half damage from bludgeoning, piercing, and slashing. You cannot cast spells, and any spell you were concentrating on ends. From barbarian level 2, your melee attacks while raging have advantage, but so do attacks against you.

### Paladin and monk abilities

- Lay on Hands: Heal an ally using a pool equal to five times your paladin level. If the target is poisoned and enough points remain, five points first cure poison. The remaining pool restores missing HP.
- Divine Smite: When a melee attack hits, spend a spell slot to add radiant damage.
- Flurry of Blows: Spend ki to make two unarmed strikes in addition to your normal attacks.
- Patient Defense: Spend ki to attack and Dodge.
- Stunning Strike: Spend ki on a hit. If the enemy fails its save, it becomes Stunned.

### Cleric, bard, and druid abilities

- Turn Undead: Prevent undead that fail their saves from acting. Taking damage ends this effect. Sufficiently weak undead are destroyed instead.
- Bardic Inspiration: Help an ally's attacks or saves. A bonus die is used automatically when it might turn a failure into a success. It is a bonus action, so you also use an attack cantrip or attack that turn.
- Wild Shape: Transform into a beast. Wolf is available at druid level 2, with black bear added at level 4. Dire wolf and brown bear become available at level 8. Wolves can knock enemies prone; bears have more HP and attack twice. You fight with the beast's HP and AC and return to your normal form when its HP runs out or the battle ends. While transformed you can't use items, and you can't cast spells below druid level 18.

### Empowered spells

From sorcerer level 3, Cast (Empowered:n) appears below Cast in combat. The number is your remaining sorcery points.

This command spends one point to reroll low damage dice for a spell. Normal Cast does not spend sorcery points. Auto does not use empowered casting.

This is the game's only Metamagic option. Other Metamagic choices and conversions between sorcery points and spell slots are not available.

### Automatic abilities

These abilities work without selecting a combat command. Levels below refer to levels in the relevant class.

- Fighting Style: Fighters, paladins, and rangers learn this ability. Equipment determines whether Archery, Two-Weapon Fighting, Great Weapon Fighting, Dueling, or Defense applies.
- Paladin auras: From level 6, a paladin able to act improves the whole party's saves with Aura of Protection. At level 10, Aura of Courage protects allies from fear.
- Rogue's Uncanny Dodge: From level 5, reduce damage from an attack. At level 7, Evasion further reduces damage that allows a Dexterity save for reduced damage.
- Monk's Martial Arts and Evasion: Without armor or a shield, you can use Dexterity with unarmed strikes and monk weapons (shortswords and simple melee weapons that aren't heavy or two-handed), and make an extra unarmed strike after attacking. With armor or a shield, Martial Arts stops working and Flurry of Blows can't be chosen. Evasion becomes available at level 7.
- Wizard's Arcane Recovery: Recover some spell slots during a short rest, once between long rests.
- Bard's Song of Rest: Restore additional HP to allies who spent Hit Dice healing during a short rest.
- Warlock invocations: Automatically learn Agonizing Blast at level 2, Thirsting Blade at level 5, and Lifedrinker at level 12. Thirsting Blade grants two attacks, and Lifedrinker drains HP with melee attacks. This game does not offer invocation or Pact Boon selection.

## Items and Equipment

### Bag capacity

In this game, items are limited by backpack slots rather than weight. The starting bag has six slots. Larger bags can be bought in town or found in the dungeon. Some identical consumables can share a stack.

Keys and other important items are shared by the party and do not occupy individual bag slots.

A full bag can prevent you from collecting loot or removing equipment. Before exploring, sell unwanted items or store them in the clan house warehouse to free space.

### Changing equipment

Use Left and Right on the items screen to switch between equipment and the bag. Choose an equipment slot and then an item, or select an item in the bag to equip it.

Weapon slots include main hand, off hand, ranged weapon, and ammunition. Other slots hold armor, a bag, two rings, a necklace, a cloak, and equipment for the head, hands, feet, and waist.

- Attacks automatically choose a weapon equipped in the main-hand or ranged slot based on range and damage. Weapons in the bag are not used.
- Equip a light melee weapon in each hand to gain an off-hand attack.
- Armor and shields require proficiency.
- Weapons can be used without proficiency, but do not receive your proficiency bonus on attack rolls.
- Item descriptions show which party members can equip an item and who is proficient with it.

### Ammunition and thrown weapons

Ordinary arrows and bolts do not need replenishing. Equipped special ammunition is consumed when you attack.

Thrown weapons are recovered after a victory, but may be lost if you flee. Weapons that return automatically are an exception.

### Identification

Unidentified magic items appear with names such as Longsword?, Ring?, or Mysterious Potion. Identify them to learn their effects.

- Pay the shop for identification.
- Use Identify or a scroll of Identify.
- Take a short rest at camp. Each party member able to act studies one unidentified item. An ambush prevents identification.

### Cursed items

A cursed item cannot be removed once equipped. Identification does not reveal the curse, and the item may appear to be something else. Its true nature is revealed when equipped or when the curse is removed.

Use the shop's curse removal service or the Remove Curse spell to break the curse.

### Attunement

Some magic items only grant their effects when attuned. Each character can attune to up to three items at once.

In this game, attunement does not require a rest. Equipping an item automatically attunes it if required and space is available. Without space, the item can be equipped but does not grant its effects.

To change attuned items, unequip one you are already attuned to, then re-equip the item you want to attune. An item's description states whether attunement is required and notes the three-item limit.

### Trading and transfers

Shop purchases use the party's combined gold. Sale proceeds are divided among party members.

You can transfer items between allies and choose how many to transfer from a stack. Important items cannot be discarded.

### Equipment with additional effects

Deeper floors may contain rare equipment with extra effects. Check the item's description to see what it does.

## Death and Rescue

### Unconsciousness and death

Reaching 0 HP in combat usually causes unconsciousness. An unconscious character makes death saves and dies after three failures. Healing can still save them while they are unconscious.

Extremely large damage can cause instant death. Reaching 0 HP from a treasure chest trap also causes death without death saves.

This game adds Wizardry-style ash and permanent loss rules. Failed resurrection can lead to these further states.

- Ash: Resurrection from death failed. Another resurrection attempt is still possible.
- Lost: Resurrection from ash also failed. This character cannot be recovered.

### Temple treatment and resurrection

The temple offers paid treatment and resurrection.

- Resurrect: Restore a dead or ashen character to life. Check the cost and success chance before requesting it. Higher Constitution improves the chance.
- Treat: Remove lingering conditions such as Poisoned or Paralyzed.

A failed resurrection turns a dead character to ash. Failure when resurrecting ashes causes permanent loss.

On the 5e-style (gentle) difficulty setting, resurrection always succeeds, but still costs gold.

### A party wipe

Difficulty determines what happens after a wipe.

- Harsh: Everyone's body remains where the party fell. Those characters cannot join a party at the tavern. Form a rescue party with other characters and return for them.
- Gentle: The party is brought back to town. Unconscious characters recover with 1 HP, but those already dead remain dead. Each character loses half their gold.

### Rescuing bodies

Form a rescue party at the tavern, travel to the square where the party fell, and press Confirm to investigate. Recovered bodies join empty party positions.

The party limit is six, including bodies. A full party has no room to carry anyone back. Leave enough positions empty for those you intend to rescue. A five-member rescue party can bring back one body.

Recovery alone does not revive a character. Return to town and request resurrection at the temple.

### Saving and suspending

Harsh settings autosave frequently, including inside the dungeon. You cannot reload an earlier state to undo a mistake.

Gentle settings save when returning to town or suspending in the dungeon. To stop playing, use Suspend and Quit or another menu option that saves before exiting.

## Options and More

### Changing settings

Open Options from the title screen, the Esc menu, or camp. Select an option with Up and Down, then change it with Left, Right, or Confirm. Returning with Cancel saves the settings.

- Language: Change the display language.
- BGM and sound volume: Adjust music and sound effects separately.
- Battle log speed: Change how quickly battle messages appear.
- Dungeon wireframe: Switch between color and monochrome.
- Story font: Change the font used for narrative text.
- Dungeon map: Choose minimap and map, map only, or no map.
- Back key: Choose whether Down steps backward or turns around. Hold Shift to use the opposite behavior.
- AI settings: Choose whether AI generates text for a free dungeon.
- Scenario folder: Open the folder where scenarios are stored.

### Optional AI

Normal play does not require AI or an API key. Text for free dungeons is generated automatically even without AI.

To have AI generate text, configure a Claude API key. API usage is billed to the account associated with that key.

### Difficulty

Choose difficulty when starting a new game. These settings are saved with that world.

- Wizardry-style (harsh): A wipe leaves bodies in the dungeon. Resurrection can fail. Frequent autosaves prevent reloading to undo mistakes.
- 5e-style (gentle): A wipe returns the party to town at the cost of half their gold. Resurrection always succeeds. Saves occur on returning to town and when suspending.
- Custom: Choose wipe consequences, resurrection rules, and saving behavior individually.

See Death and Rescue for details about rescue and resurrection.

### Clans and worlds

A clan is your group of developed characters. A world is the setting in which you play the chosen scenario. Members of the same clan can begin another adventure.

Choose a clan when starting a new game. Export Clan at the training grounds saves the character roster to a file.

### Adding scenarios

Drag a scenario folder or zip file onto the title screen to install it. Installed scenarios are available when starting a new game.

Open their storage location with Scenario Folder in Options. A separate scenario editor is also available for creating your own scenarios.

### Suspending and resuming

In the dungeon, choose Suspend and Quit from the Esc menu to save and return to the title screen. Choose Continue on the title screen to resume.

## Main Differences from the SRD

This game adapts the D&D 5th edition SRD 5.1 for dungeon exploration. These are the main changes that may surprise players familiar with the SRD. See the other help topics and in-game descriptions for details.

### Spells and classes

- Preparing spells: Change prepared spells at the training grounds, inn, or camp at any time. You do not need to complete a rest.
- Reselecting spells: Reselect cantrips, known spells, and wizard spellbook choices at the training grounds at any time.
- Bonus actions: Bonus-action spells (such as Healing Word), Second Wind, and Bardic Inspiration are followed by an automatic action that same turn: the front row attacks; the back row uses an attack cantrip (or a weapon that reaches). To only heal, use an action spell such as Cure Wounds.
- Class features: There are no subclasses. Some features and choices are omitted, and abilities are adapted for dungeon combat.
- Sorcerer: The only Metamagic option spends one sorcery point to reroll low spell damage dice. Sorcery points and spell slots cannot be converted into each other.
- Warlock: Invocations are learned automatically at fixed levels. There is no invocation or Pact Boon selection.
- Multiclassing: Gain all weapon and armor proficiencies of each new class you take.
- Ability Score Improvements and feats: Gain 2 points at class levels 4, 8, 12, 16 and 19 (fighters also at 6 and 14, rogues at 10) and spend them one at a time at the training grounds. There are no feats (the only SRD 5.1 feat is for grappling, which this game does not have).
- Fighting Style: You don't choose one. It follows what you wield (ranged weapon: Archery, two light weapons: Two-Weapon Fighting, two-handed: Great Weapon Fighting, one-handed melee: Dueling, no weapon: Defense). The status screen shows your current style.
- Wild Shape: While transformed, your AC is the beast's fixed value. Unarmored Defense and gear don't add to it.
- Level cap: Total character level has no cap. From level 21 onward, gain an epic boon with each level.

### Combat

- Reactions: Reactions such as Counterspell, Deflect Missiles and Uncanny Dodge are used automatically when they apply. You do not choose whether to use them.
- Dodge: Attacks against you have disadvantage. It does not give advantage on Dexterity saves.
- Action Surge: Make one more set of weapon attacks. It cannot be used to cast another spell.
- Bardic Inspiration: The die is used automatically, and spent, only when it would turn a failed attack or save into a success.

### Exploration and equipment

- Party and distance: Up to six members form a front row of three and a back row of three. Combat distance uses these rows and near or far enemy groups.
- Inventory: Carrying capacity depends on free bag slots rather than weight.
- Ammunition: Ordinary arrows and bolts need no replenishment. Special ammunition is consumed with each attack.
- Attunement: No rest is required. Equipping an item attunes it automatically if an attunement slot is available. You can't attune to a second copy of the same item (as in 5e).
- Armor and shields without proficiency: They can't be equipped (in 5e you can wear them, but they only hurt you, such as by stopping spellcasting).
- Protective gear: Some items, such as Bracers of Defense and the Robe of the Archmagi, simply add to AC instead of working only without armor.
- High magic bonuses: Weapons and armor above +4 have their attack and AC bonus capped (+4 gives +3; +5 and higher give +4). Damage keeps the full bonus.

### Resting and death

- Short rests: Hit Dice are spent automatically until HP is full or no dice remain. If the rest ends without an ambush, each member able to act identifies one unidentified item.
- Long rests: Available only at the town inn or a clan house with beds. The HP recovery limit depends on where you sleep; the stables restore HP up to 50% of maximum.
- Chest traps: Reaching 0 HP from a chest trap causes death without death saves.
- Resurrection and wipes: Depending on settings, failed resurrection can turn a character to ash or cause permanent loss. A wipe may require a rescue party to retrieve bodies from the dungeon.

The 5e-style (gentle) difficulty also includes this game's own rules. It does not reproduce the SRD unchanged.
