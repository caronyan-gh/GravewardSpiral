# GRAVEWARD SPIRAL Help

The same text as Help in the in-game Esc menu (and on the title screen). Topics:

- Controls
- Getting Started
- Town Facilities
- Characters
- Spells
- Exploring the Maze
- Camp
- Combat
- Class Features
- Items and Equipment
- Death and the Temple
- Options and More

## Controls

### Basic keys

You can play with a keyboard or a gamepad. Menus also work with the mouse (click, and the wheel to scroll).

- Confirm: Enter / Space / Z / gamepad A
- Cancel: Esc / Backspace / X / gamepad B
- Move and choose: arrow keys / WASD / D-pad / left stick
- Menu (camp, in the maze): Tab / C / gamepad Start or Y
- Map: M / gamepad Back or X
- Page through lists: PageUp and PageDown / Q and E / gamepad LB and RB
- Toggle fullscreen: F11 or Alt+Enter

### Moving in the maze

- Up moves forward. Left and Right turn on the spot.
- Down does what the "Back key" option says: step back or turn around.
- Q and E (gamepad LB and RB) sidestep one square without turning.
- Confirm examines what is in front of you or under your feet (stairs, doors, chests, and so on).
- Hold Shift (gamepad L2) while pressing the back key to get the opposite action. If the option is "step back", you turn around instead.

### The Esc menu

Esc opens a small menu. From here you can open Help, Options, or quit (in the maze this is "Suspend and Quit"). In the maze you can also open Camp from here.

### Changing keys

The key assignments are stored in input.json in the user data folder (%LOCALAPPDATA%\GravewardSpiral). Edit that file to change them. Only the fullscreen toggle cannot be changed.

## Getting Started

### What kind of game is this

GRAVEWARD SPIRAL is a dungeon crawler in the style of early Wizardry. Its rules are based on the D&D 5th edition SRD 5.1. You prepare in town, enter the maze with a party, fight, and bring treasure home.

### Starting a new game

Choose "New Game" on the title screen and decide how you want to play.

- Scenario: pick a world with a story, or "Free dungeon (no story)". In a free dungeon you can set the seed, the number of floors, and the floor size.
- Clan: choose the clan (the roster of characters) to bring along. You can keep using a clan you played before, or make a new one.
- Difficulty: sets how harsh wipes, revivals, and saving are (see "Options and More").

When you start a new world, the party is carried over from the most recently played world, as long as it has members who can be used.

### What to do first

- Make characters at the Training Grounds.
- Form a party at the Tavern. A party holds up to 6. The front 3 are the front row and the other 3 are the back row. Only the front row can attack in melee.
- Buy gear at the Shop if you need it.
- Choose "Enter the Maze" in town. You cannot enter without a party that can act.

### Returning to town

You can return to town by the up stairs on B1. The game saves when you return to town.

## Town Facilities

### Tavern

Gather companions and form a party. A party holds up to 6. The first three are the front row and the rest are the back row. You can add members, remove them, and reorder them. You can also hear rumors from the patrons.

### Inn

Stay the night and take a long rest. Each member pays from their own money.

- Stable: you sleep on straw. HP is restored only up to half.
- Room: you rest in a proper bed. HP is fully restored.

At camp in the maze you can only take a short rest. The inn is the only place for a long rest.

### Shop

Buy and sell gear and items, identify items, and remove curses. Purchases are paid from party gold, and sale proceeds are shared among the party.

### Temple

Resurrects dead companions and cures poison, paralysis, and more. See "Death and the Temple" for details.

### Training Grounds

Create, inspect, level up, rename, or delete characters. You can also choose spells for spellcasters here. The roster can be exported as a clan.

### Clan House

The clan's home. Store items in its storehouse. Invest gold to rebuild it larger.

### Enter the Maze

Enter the maze with your party. You cannot go in without a party that can act.

### Other entries

- Items: look at your companions' items and change their equipment.
- Visit People: talk to the townsfolk. Selectable only when someone is available.
- The Tale of This Dungeon: in scenarios with a story, read the tale and the town's request again.
- Save and Quit: saves and returns to the title screen.

## Characters

### Ability scores

There are six ability scores: Strength, Dexterity, Constitution, Intelligence, Wisdom, and Charisma. The modifier is (score - 10) divided by 2, rounded down.

Ability scores are not rolled at random when you create a character. You assign the standard values (15, 14, 13, 12, 10, 8) or use point buy. Starting gold is also a fixed amount for each class.

### Races and classes

There are nine SRD races (Human, Elf, Dwarf, Halfling, Gnome, Half-Elf, Half-Orc, Tiefling, Dragonborn) and twelve SRD classes (Fighter, Barbarian, Paladin, Ranger, Rogue, Monk, Cleric, Druid, Wizard, Sorcerer, Warlock, Bard).

### Leveling up

You gain experience by winning battles. When you have enough, level up at the Training Grounds. Characters who can level up are marked with an arrow in the list, and you are told after a battle, too.

There is no level cap. From level 21 you enter the "epic" levels, and each level lets you choose a boon.

### Multiclassing

When you level up, you choose which class to raise: your current class, or a new one. To take a new class you must meet its ability score requirement (for the new class and for your current ones). The requirement is shown on the selection screen.

Spells are learned and prepared separately for each spellcasting class. A newly taken class gives you all of that class's weapon and armor proficiencies.

### Proficiency

The proficiency bonus grows with level. You can check proficiencies in the character summary (the status screen).

- Armor and shields: you cannot equip them without proficiency.
- Weapons: you can use them without proficiency, but you do not get the proficiency bonus to hit.

Item descriptions show who in the party can wear the item and who is proficient. Check them before you buy.

## Spells

### Spell slots

Spells are cast by using spell slots of each level. Some spells can be cast stronger by using a higher slot. Cantrips use no slots and can be cast any number of times.

Slots are restored by a long rest at the inn. A warlock's slots are also restored by a short rest at camp.

### Known and prepared spells

Spellcasting classes work in two ways.

- Prepared (Cleric, Druid, Paladin): you choose spells to prepare from the class spell list.
- Wizard: you prepare spells from those written in your spellbook.
- Known (Sorcerer, Bard, Ranger, Warlock): you simply cast the spells you know. No preparation is needed.

You can learn more cantrips as you level up.

### The spell selection screen

On the selection screen, press Confirm to mark (◆) or unmark a spell, up to the limit. "Auto-fill" picks the missing ones for you. If you have more than one spellcasting class, press Left or Right to switch between them.

### Changing your choices

- Prepared spells can be swapped at any time, in town (Training Grounds) and at camp.
- Cantrips and known spells (including a wizard's spellbook) can be re-chosen at the Training Grounds at any time. Sorcerers, bards, warlocks, and the like can swap their known spells and cantrips whenever they like. This is looser than 5th edition, and it is a deliberate rule of this game.

### Where you can cast

- In combat: choose "Cast" in the command list, then the spell, the slot, and the target.
- At camp: use "Cast Spell" for healing and other spells that work in the maze. Combat-only spells cannot be cast at camp.
- In anti-magic zones, neither spells nor spell items work.

You cannot cast spells while wearing armor or a shield you are not proficient with.

## Exploring the Maze

### Moving around

The maze is a grid, shown in a first-person line-art view. You can see up to four squares ahead. Move forward, turn, and sidestep to explore. Each floor you enter is named B1, B2, and so on.

### Maps

A small map (the automap) of your surroundings is shown at the top right. Press M to open the full map.

- The map shows the squares you walked through and the walls and doors you saw.
- The full map shows only the area around what you know. You cannot tell how large the unexplored parts are.
- Secret doors are drawn as walls until you find them.
- In Options, "Maze Map" lets you choose mini-map and map, map only, or none. With "none", you keep your own notes on graph paper.

### Darkness and fog

- Darkness: you cannot see ahead. The map still records where you go. Light spells and the like let you see for a while.
- Fog: you can see ahead, but the map does not record anything. In fog, your own position is not shown on the map either.

### Doors and keys

- Secret doors can sometimes be found by searching.
- A one-way door looks like a door from the side you can pass through and like a wall from the other side.
- A locked door can be opened with the floor's key, thieves' tools, the Knock spell, or by kicking it open. If kicking fails, enemies may hear the noise and come.
- Keys are found in the maze. A key opens the locked doors on the floor where you found it.

### Traps and the like

The maze has spinners, teleporters, pits, chutes, anti-magic zones, and other features. On some floors, leaving one edge brings you out on the opposite side. Everyone in the party takes damage from a pit.

### Chests

A chest may appear after a victory. You can choose from these actions.

- Inspect: look for the type of trap. If it fails, you may be misled into thinking it is a different trap.
- Disarm Trap: choose the trap's name and try to disarm it. If the name is wrong, the trap goes off.
- Open It: if there is a trap, it usually goes off.
- Leave.

There are nine kinds of traps: poison needle, gas, crossbow bolt, explosion, stun, teleporter, mage blaster, priest blaster, and alarm.

### Stairs and elevators

Use Confirm to climb or descend stairs. Elevators take you to floors you have already visited. The up stairs on B1 lead back to town.

### Notes

At camp in the maze, you can read the notes you have jotted down.

## Camp

### What camp is

Press the menu key (Tab or C, for example) in the maze to open it. You can also open it from the Esc menu. Camp is where you rest in the maze and get your spells, items, and formation in order.

### What you can do

- Short Rest (1 hour): spend Hit Dice to recover HP. Warlocks also regain their spell slots. Each member who can act studies one unidentified item and learns what it is. Enemies may come while you rest, and then a battle begins.
- Cast Spell: cast healing and other spells that work in the maze.
- Prepare Spells: change your prepared spells.
- Items: look at your companions' items and change their equipment.
- View Status: check your companions' condition.
- Change Formation: choose a new order from the front. The first three become the front row.
- Read Notes: read the notes you jotted down.
- Last battle log: read what happened in the most recent battle from the start.
- Options: change volume and other settings.

### Things to know about resting

In the maze you can only take a short rest. A long rest, which fully restores HP and spell slots, is possible only at the inn in town.

Hit Dice are limited. Once they run out, a short rest no longer restores HP. After a battle, conditions such as poison and blindness remain (they fade a little with each step). While poisoned, you lose HP with every step. A long rest clears them all.

In anti-magic zones, you cannot cast spells even at camp.

## Combat

### Formation and range

The party is split into a front row of three and a back row of three. Enemies appear in up to four groups, and each group is either near or far.

- Melee attacks reach only the near groups, and only from the front row.
- Back-row members fight with ranged attacks (such as bows) and spells.
- Ranged attacks and spells can reach far groups too.

Fallen members move to the back after the log has been shown. You can change the formation at camp, or with "Swap" during a battle.

### How a round works

At the start of a round, you choose each member's action in turn. When everyone has chosen, the round plays out. "Auto" lets you decide the rest at once (with or without spells). "Back to previous member" lets you choose again.

The actions are as follows.

- Attack: choose a group and attack with a weapon.
- Cast: choose the spell, the slot, and the target.
- Spell (Empower: n): sorcerers only. Spend a point to cast a damage spell stronger.
- Class Feature: use a class feature such as Rage or Second Wind (see "Class Features").
- Item: use an item. You can also switch weapons or ammunition.
- Dodge: take a defensive stance.
- Help: assist an ally.
- Hide: hide in the shadows.
- Swap: trade places with another member.
- Run (all): the whole party tries to escape. It can fail if the way out is blocked.

The log is shown one line at a time. Press Confirm to go on, and hold it to fast-forward.

### Before the fight

If you are caught off guard, you cannot act in the first round. If the enemies have not noticed you, you get the first move instead. You learn what an enemy really is when someone in the party passes an Intelligence check. Until then it appears under a name like "small figure". Defeating one also reveals it.

### When someone falls

At 0 HP a member falls unconscious. Each round they make a death saving throw. Enough successes stabilize them, and enough failures kill them. If a fallen member is hit again, their wounds get worse.

After a victory, fallen members get up with 1 HP, and conditions that last only during the battle disappear. Those who died do not get up.

### Ongoing damage

Some effects, like poison, fire or infernal wounds, deal damage at the start of each turn. They stop after their rounds run out or on a successful save. The same effect does not stack, but different ones do. They stop when the battle ends.

### Conditions

There are conditions such as unconscious, paralyzed, petrified, poisoned, frightened, charmed, and blinded. A condition's name is shown before the HP in the party list. Poison, blindness, deafness, and fear linger for a while after a battle. The temple or spells can cure them.

### After a victory

Experience is divided equally among the party. You also get gold, and sometimes a chest appears.

### Reading the log again

After a battle, "Last battle log" at camp lets you read the most recent battle again. During a battle you can also open it with Tab.

## Class Features

### Multiclassing

With several classes, each class's features work at that class's level. A fighter 5 / barbarian 2, for example, can use both Action Surge and Rage. Extra Attack does not stack; the highest count applies.

The features you have and the uses left appear under "Class Features" on the character status screen.

### Uses

Uses come back when you rest. Second Wind, Action Surge, ki, Channel Divinity and Wild Shape return on a short rest. The rest return on a long rest at the inn.

### Class feature command

Choose them with "Class Feature" in battle.

- Fighter: Second Wind (regain HP), Action Surge (attack twice over).
- Barbarian: Rage (for this battle, melee damage rises and you take half bludgeoning, piercing and slashing damage; no spells).
- Paladin: Lay on Hands (restore an ally's HP from a pool; also cures poison), Divine Smite (spend a spell slot to add radiant damage to a melee hit).
- Monk: Flurry of Blows, Patient Defense, Stunning Strike (all spend ki).
- Cleric: Turn Undead (turns undead; weak undead are destroyed).
- Bard: Bardic Inspiration (adds a die to an ally's attack or save).
- Druid: Wild Shape (choose a wolf or a bear form: the wolf is hard to hit and knocks foes down, the bear has more HP and attacks twice; from L8, a dire wolf or a brown bear. You return when the beast's HP runs out).

### Empowered spell

From level 3, a sorcerer gets "Spell (Empower: n)" under "Spell" in battle, where n is the sorcery points left. It casts a damage spell with its low dice rerolled. The normal "Spell" never spends points, and auto battle never empowers.

### Passive features

Some features work without choosing them.

- Fighting Style (fighter, paladin, ranger): one style that fits your weapon works: Archery, Two-Weapon, Great Weapon, Dueling or Defense.
- Paladin's Aura of Protection (L6): while the paladin stands, the whole party's saves rise. Aura of Courage (L10) keeps the party from being frightened.
- Rogue's Uncanny Dodge (L5) and Evasion (L7), monk's Martial Arts and Evasion (L7), and more.
- Wizard's Arcane Recovery: regain a few spell slots on a short rest (once per long rest).
- Bard's Song of Rest: allies regain extra HP on a short rest.
- Warlock invocations: Agonizing Blast (L2), Thirsting Blade (L5, two attacks) and Lifedrinker (L12, melee hits drain HP) come automatically with level.

## Items and Equipment

### Carrying items

Weight is not counted. How much you can carry is set by the slots of your backpack. The first bag has 6 slots, and you can buy larger bags in town. Even larger bags may turn up in the maze. Consumables stack.

Important items such as keys go into a separate slot shared by the whole party.

### Equipping

On the items screen, you put equipment into places such as hands, armor, bag, rings, neck, cloak, head, hands, feet, and waist. For weapons, the best one from the main hand and ranged slots is used automatically. If you hold light weapons in both hands, you also attack with the off hand.

- Armor and shields cannot be equipped without proficiency.
- Weapons can be used without proficiency, but you do not get the proficiency bonus to hit.
- An item's description shows who in the party can wear it and who is proficient.

### Identification

Magic items are unidentified when you find them. They appear as "Longsword?", "Ring?", or "Strange potion". There are three ways to learn what they are.

- Have the shop identify them.
- Use the Identify spell or a scroll.
- Take a short rest at camp. Each member who can act studies one unidentified item. If enemies come during the rest, you learn nothing.

### Curses

A cursed item cannot be removed once you equip it. Identification does not reveal curses (the same as in 5th edition). A cursed item looks like an ordinary, genuine item. Its true nature shows when you equip it and when you lift the curse.

Lift a curse with "Remove Curse" at the shop, or with the Remove Curse spell.

### Attunement

Some magic items, such as rings, require attunement. An item that needs it attunes when you equip it, if you have a free attunement slot. You can be attuned to up to three items. The item's description says "Requires attunement (up to 3)".

### Buying and selling

You can buy and sell gear and items at the shop. Purchases are paid from party gold, and sale proceeds are shared among the party. Important items cannot be thrown away.

### Rare equipment

Deeper floors hold rare equipment with extra effects. The extra effects are shown in the description.

## Death and the Temple

### Stages of death

At 0 HP a member falls unconscious. If death saves fail, they die. Beyond death there are two more stages.

- Ashes: a dead companion whose resurrection failed.
- Lost: a companion whose resurrection from ashes failed. They do not return.

### The temple

At the temple, you pay gold for the following.

- Resurrection: bring a dead or ashen companion back to life. The gold needed and the chance of success are shown when you choose. A higher Constitution makes success more likely.
- Cure: heal lingering conditions such as poison and paralysis.

If a resurrection fails, a dead companion turns to ashes. If a resurrection from ashes fails, that companion is lost.

In a world set to "5e-style (gentle)", resurrection always succeeds (it still costs gold).

### When the party is wiped out

What happens after a wipe depends on the difficulty.

- Harsh setting: everyone's bodies stay where the party fell. They cannot be added to a party at the tavern. Take another party to that square in the maze and inspect with Confirm to carry the bodies home. Resurrect them at the temple.
- Gentle setting: passing adventurers carry you to town. Those who were down get up with 1 HP, and everyone loses half of the gold they carried.

### Saving

On the harsh setting, the game auto-saves often even inside the maze, and you cannot redo. On the gentle setting, the game saves when you return to town and when you suspend in the maze.

## Options and More

### The options screen

Open it from the title screen, the Esc menu, or camp. Use Up and Down to choose an item, and Left, Right, or Confirm to change it. Pressing Cancel to go back saves your changes.

- Language: switch the display language.
- Music Volume and Sound Effects Volume.
- Combat Log Speed.
- Maze Line Art: color or monochrome.
- Story font.
- Maze Map: mini-map and map, map only, or none.
- Back key: whether Down in the maze steps back or turns around.
- AI settings: whether an AI writes the text for free dungeons (a Claude API key is required).
- Scenario folder: opens the folder where scenarios are kept.

### Difficulty

You choose the difficulty when you start a new game. It is stored with that save.

- Wizardry-style (harsh): after a wipe the bodies stay in the maze. Resurrection can fail. Saving is automatic and you cannot redo.
- 5e-style (gentle): after a wipe you are carried to town (you lose half your gold). Resurrection always succeeds. The game saves only in town and when you suspend.
- Custom: choose the harshness of wipes, resurrection, and saving one by one.

### Clans and worlds

Your roster of characters can be carried over as a clan. You can also export it to a file with "Export Clan" at the Training Grounds. You choose the clan when you start a new game.

### Adding scenarios

Drag a scenario folder or zip onto the title screen to install it. Installed scenarios can be chosen under "New Game". You can open the scenario folder from "Scenario folder" in Options.

A scenario editor for making your own scenarios is also provided as a separate tool.

### Suspending

In the maze, use "Suspend and Quit" in the Esc menu to save and stop. Resume later with "Continue" on the title screen.
