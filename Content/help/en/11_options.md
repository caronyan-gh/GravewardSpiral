# Options and More

## Changing settings

Open Options from the title screen, the Esc menu, or camp. Options are split into Basic, Gameplay, and Other tabs; switch tabs with Q and E (L1 and R1 on a pad) or by clicking a tab. Select an option with Up and Down, then change it with Left, Right, or Confirm. Returning with Cancel saves the settings.

Basic tab:

- Language: Change the display language.
- BGM and sound volume: Adjust music and sound effects separately.
- Display: Switch between a window and fullscreen. F11 also toggles it.
- Window size: Pick a 16:9 size from 1280×720 up to what fits your screen. While fullscreen, this sets the size used when you return to a window.
- Story font: Change the font used for narrative text.
- Dungeon wireframe: Switch between color and monochrome.

Gameplay tab:

- Battle log speed: Change how quickly battle messages appear.
- Cursor repeat / Walk repeat: Set the time between cursor steps and between steps in the maze while you hold a key or button, from 10 to 1500 ms (smaller is faster). The wait before repeating starts follows from it: the shorter the time, the relatively longer the wait (3x at 10 ms, 1x at 1000 ms and above).
- Dungeon map: Choose minimap and map, map only, or no map.
- Back key: Choose whether Down steps backward or turns around. Hold Shift to use the opposite behavior.
- Maze mouse movement: Choose whether clicking the maze view moves the party (see "Moving in the maze" under Controls).
- Movement overlay: Faintly draw the mouse areas and arrows over the maze view. Available only when maze mouse movement is on.
- Spell level tabs: Choose whether the tabs above the spell list (in combat and camp) switch on click or when you hover the mouse over them.

Other tab:

- AI settings: Choose whether AI generates text for a free dungeon.
- Scenario folder: Open the folder where scenarios are stored.

## Optional AI

Normal play does not require AI or an API key. Text for free dungeons is generated automatically even without AI.

To have AI generate text, configure a Claude API key. API usage is billed to the account associated with that key.

## Difficulty

Choose difficulty when starting a new game. These settings are saved with that world.

- Wizardry-style (harsh): A wipe leaves bodies in the dungeon. Resurrection can fail. Frequent autosaves prevent reloading to undo mistakes.
- 5e-style (gentle): A wipe returns the party to town at the cost of half their gold. Resurrection always succeeds. Saves occur on returning to town and when suspending.
- Custom: Choose wipe consequences, resurrection rules, and saving behavior individually.

See Death and Rescue for details about rescue and resurrection.

## Clans and worlds

A clan is your group of developed characters. A world is the setting in which you play the chosen scenario. Members of the same clan can begin another adventure.

Choose a clan when starting a new game. The buttons under the clan list (Continue on the title screen) export a clan, import one, or disband one. An exported clan leaves the list, and importing the file brings it back as it was (to move to another PC, for example). When disbanding, you choose between the Recycle Bin and permanent deletion. A world whose scenario data is missing appears grayed out in that clan's scenario list; choose it to delete it. Exporting and importing use the Windows file window to choose the place. For export and disband, press the button first, then choose the clan. With a keyboard or pad, the Menu key moves to the buttons. You can import an exported clan even with no saves.

## Adding scenarios

Double-click a scenario file (.gssnro) or drag it onto the title screen to import it. Imported scenarios are available when starting a new game. An exported clan (.gsclan) can be imported the same way.

Open their storage location with Scenario Folder in Options. A separate scenario editor is also available for creating your own scenarios.

## Suspending and resuming

In the dungeon, choose Suspend and Quit from the Esc menu to save and return to the title screen. Choose Continue on the title screen to resume.
