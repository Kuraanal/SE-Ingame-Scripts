# Airlock Manager 

#### Author: Kuraanal
#### v0.7.5
#### Steam Workshop Link: [Here](https://steamcommunity.com/sharedfiles/filedetails/?id=2376267858)
***

A simple airlock script that use blocks **`customData`** field to configure.
This script should work with all doors that implements **`IMyDoor`**, even modded ones. Tested with all vanilla doors.
***
### Airlock Configuration

> ##### Blocks that can be added to an Airlock:
> - Doors (Doors, Hangar Doors, Gate...)
> - Air Vents
> - Lights

> ##### The minimum blocks for an airlock to be determined as complete is:
> - 1 Inner Door
> - 1 Outer Door
> - 1 Air Vent

#### Each block must have in their `CustumData` Field

**`name`** property should be the unique Airlock Name
**`position`** property is meant for doors. Should be either `Inner` or `Outer`

	[airlock]
	name=Airlock_Name
	position=Inner/Outer

#### Programable Block `CustomData` field must have the following

**`airlockList`** Property should be the names of the airlocks separeted by a comma **`,`** 

	[airlocks]
	airlockList=airlock1,airlock2,airlock3
***
### Script Setup

#### script installation
If the **`CustomData`** fields is present on the blocks and the programable block, nothing should be changed.

You can Adjust the property **`targetOuterPressure`** from **`0.0f`** to **`1.0f`** at the beginning of the script to adjust the pressure at which time the script will open the Outer doors **(And vent the rest of the Air outside)**

#### Cycling Airlock
Run the programable block with the folowwing arguments.

**`Airlock_Name`** should be a valid name as specified in the **`CustomData`** field of the programable Block.

>- **"Airlock_Name" IN**
>- **"Airlock_Name" OUT**
>- **"Airlock_Name" OPEN_ALL**
>- **"Airlock_Name" CLOSE_ALL**

**`OPEN_ALL`** and **`CLOSE_ALL`** open or close all doors regardless of the pressure situation.