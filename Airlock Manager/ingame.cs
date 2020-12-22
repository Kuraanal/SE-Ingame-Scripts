/*
 * R e a d m e
 * -----------
 * 
 * In this file you can include any instructions or other comments you want to have injected onto the 
 * top of your final script. You can safely delete this file if you do not want any such comments.
 */

public enum AirlockState
{
    WAITING = 0,
    DEPRESSURIZING = 2,
    PRESSURIZING = 3,
    CYCLE_IN = 4,
    CYCLE_OUT =5
};

MyIni _ini = new MyIni();
MyIniParseResult iniResult;
Dictionary<string, Airlock> airlocks = new Dictionary<string, Airlock>();
IMyTextPanel infoLcd;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;
    createAirlocks();
    infoLcd = GridTerminalSystem.GetBlockWithName("Airlock Info LCD") as IMyTextPanel;
}

public void Save()
{

}

public void Main(string argument, UpdateType updateSource)
{
    if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
    {
        // got triggered for toggling an Airlock
        List<string> arguments = new List<string>();
        arguments = argument.Split(',').ToList();
        switch (arguments[0])
        {
            case "cyclein":
                if(airlocks[arguments[1]].lastCycle == AirlockState.CYCLE_OUT & airlocks[arguments[1]].getAirlockPressure() <= 0.9f)
                    airlocks[arguments[1]].cycle = AirlockState.CYCLE_IN;
                break;
            case "cycleout":
                if (airlocks[arguments[1]].lastCycle == AirlockState.CYCLE_IN & airlocks[arguments[1]].getAirlockPressure() >= 0f)
                    airlocks[arguments[1]].cycle = AirlockState.CYCLE_OUT;
                break;
            case "toggle":
                if(airlocks[arguments[1]].lastCycle == AirlockState.CYCLE_IN)
                {
                    airlocks[arguments[1]].cycle = AirlockState.CYCLE_OUT;
                }
                else if (airlocks[arguments[1]].lastCycle == AirlockState.CYCLE_OUT)
                {
                    airlocks[arguments[1]].cycle = AirlockState.CYCLE_IN;
                }
                break;
            default:
                break;
        }
        airlocks[arguments[1]].checkCycle();
    }

    if ((updateSource & UpdateType.Update10) != 0)
    {
        // automatic call => update lcd if there is
        foreach(var airlock in airlocks) {
            infoLcd.WriteText(airlock.Key.ToString() + " pressure: " + airlock.Value.getAirlockPressure());

            if(airlock.Value.cycle != AirlockState.WAITING)
                    airlock.Value.checkCycle();
        }
    }

    if ((updateSource & UpdateType.Update100) != 0)
    {

    }
}

public void createAirlocks()
{
    List<IMyFunctionalBlock> airlockBlocks = new List<IMyFunctionalBlock>();
    List<string> airlocksNames = new List<string>();

    if (MyIni.HasSection(Me.CustomData, "airlocks"))
    {
        if (!_ini.TryParse(Me.CustomData, out iniResult))
            throw new Exception(iniResult.ToString());

        char delimiter = ',';
        airlocksNames = _ini.Get("airlocks", "airlocks").ToString().Split(delimiter).ToList();
    }

    foreach (string myAirlock in airlocksNames)
    {
        // Getting this Airlock doors
        GridTerminalSystem.GetBlocksOfType<IMyFunctionalBlock>(airlockBlocks, block => {
            MyIni blockIni = new MyIni();
            if (!blockIni.TryParse(block.CustomData, out iniResult))
                throw new Exception(iniResult.ToString());

            if (MyIni.HasSection(block.CustomData, "airlock") && blockIni.Get("airlock", "name").ToString() == myAirlock)
                return true;
            else
                return false;
        });

        Airlock thisAirlock = new Airlock(airlockBlocks);
        airlocks.Add(myAirlock, thisAirlock);
    }
}

public void resetAirlocks()
{
    airlocks.Clear();
    createAirlocks();
}

public class Airlock
{
    // Block Fields
    public List<IMyFunctionalBlock> blocks;
    private List<IMyDoor> doors = new List<IMyDoor>();
    private List<IMyLightingBlock> lights = new List<IMyLightingBlock>();
    private List<IMyAirVent> vents = new List<IMyAirVent>();
    private List<IMyButtonPanel> panels = new List<IMyButtonPanel>();

    private MyIni blockIni = new MyIni();
    private MyIniParseResult iniResult;

    // General Fields
    public AirlockState cycle = AirlockState.WAITING;
    public AirlockState lastCycle = AirlockState.WAITING;

    public Airlock(List<IMyFunctionalBlock> blocks)
    {
        this.blocks = blocks;
        this.cycle = AirlockState.CYCLE_OUT;
    }

    public void resetAirlockBlocks()
    {
        doors.Clear();
        lights.Clear();
        vents.Clear();
        panels.Clear();

        this.blocks.ForEach(block => {
            if (block is IMyDoor)
                this.doors.Add(block as IMyDoor);
            else if (block is IMyLightingBlock)
                this.lights.Add(block as IMyLightingBlock);
            else if (block is IMyAirVent)
                this.vents.Add(block as IMyAirVent);
            else if (block is IMyButtonPanel)
                this.panels.Add(block as IMyButtonPanel);
        });

        this.cycle = AirlockState.CYCLE_OUT;
    }

    public void checkCycle()
    {
        if(this.cycle == AirlockState.CYCLE_IN)
        {
            //enable all doors
            doors.ForEach(door => door.Enabled = true);
            //toggle all doors (all doors closed)
            doors.ForEach(door => door.CloseDoor());
            //set lights (Set to warning)
            lights.ForEach(light => {
                if (!blockIni.TryParse(light.CustomData, out iniResult))
                    throw new Exception(iniResult.ToString());

                if (blockIni.Get("airlock", "position").ToString() == "warning")
                    light.Enabled = true;
                else
                    light.Color = Color.Red;
            });

            //set panels (disable all)
            panels.ForEach(panel => panel.ApplyAction("OnOff_Off"));

            //for each door check if closed
            // if yes change state
            int closedDoors = 0;
            doors.ForEach(door =>
            {
                if(door.Status == DoorStatus.Closed)
                {
                    closedDoors++;
                    door.Enabled = false;
                }
            });

            if (closedDoors == doors.Count)
                this.cycle = AirlockState.PRESSURIZING;
        }
        else if (this.cycle == AirlockState.CYCLE_OUT)
        {
            //enable all doors
            doors.ForEach(door => door.Enabled = true);
            //toggle all doors (all doors closed)
            doors.ForEach(door => door.CloseDoor());
            //set lights (Set to warning)
            lights.ForEach(light => {
                if (!blockIni.TryParse(light.CustomData, out iniResult))
                    throw new Exception(iniResult.ToString());

                if (blockIni.Get("airlock", "position").ToString() == "warning")
                    light.Enabled = true;
                else
                    light.Color = Color.Red;
            });

            //set panels (disable all)
            panels.ForEach(panel => panel.ApplyAction("OnOff_Off"));

            //for each door check if closed
            // if yes change state
            int closedDoors = 0;
            doors.ForEach(door =>
            {
                if (door.Status == DoorStatus.Closed)
                {
                    closedDoors++;
                    door.Enabled = false;
                }
            });

            if (closedDoors == doors.Count)
                this.cycle = AirlockState.DEPRESSURIZING;
        }
        else if (this.cycle == AirlockState.DEPRESSURIZING)
        {

            //set vent (enable + depressurize off)
            vents.ForEach(vent => {
                vent.Enabled = true;
                vent.Depressurize = true;
            }
            );

            if (getAirlockPressure() == 0)
            {
                //finished depressurizing
                //toggle outer doors open
                doors.ForEach(door =>
                {
                    if (!blockIni.TryParse(door.CustomData, out iniResult))
                        throw new Exception(iniResult.ToString());

                    if (blockIni.Get("airlock", "position").ToString() == "outer")
                    {
                        door.Enabled = true;
                        door.OpenDoor();
                    }
                });

                //set lights to normal
                lights.ForEach(light => {
                    if (!blockIni.TryParse(light.CustomData, out iniResult))
                        throw new Exception(iniResult.ToString());

                    if (blockIni.Get("airlock", "position").ToString() == "outer")
                        light.Color = Color.Green;
                    else if (blockIni.Get("airlock", "position").ToString() == "warning")
                        light.Enabled = false;
                });
                //enable all panels
                panels.ForEach(panel => panel.ApplyAction("OnOff_On"));

                //for each door check if outer = open
                // if yes change state to waiting
                this.cycle = AirlockState.WAITING;
                this.lastCycle = AirlockState.CYCLE_OUT;
            }
        }
        else if(this.cycle == AirlockState.PRESSURIZING)
        {


            //set vent (enable + depressurize off)
            vents.ForEach(vent => {
                vent.Enabled = true;
                vent.Depressurize = false;
            }
            );

            if (getAirlockPressure() >= 0.9f)
            {
                //finished pressurizing
                //toggle inner doors open
                doors.ForEach(door =>
                {
                    if (!blockIni.TryParse(door.CustomData, out iniResult))
                        throw new Exception(iniResult.ToString());

                    if (blockIni.Get("airlock", "position").ToString() == "inner")
                    {
                        door.Enabled = true;
                        door.OpenDoor();
                    }
                });

                //set lights to normal
                lights.ForEach(light => {
                    if (!blockIni.TryParse(light.CustomData, out iniResult))
                        throw new Exception(iniResult.ToString());

                    if (blockIni.Get("airlock", "position").ToString() == "inner")
                        light.Color = Color.Green;
                    else if (blockIni.Get("airlock", "position").ToString() == "warning")
                        light.Enabled = false;
                });
                //enable all panels
                panels.ForEach(panel => panel.ApplyAction("OnOff_On"));
                //for each door check if inner = open
                // if yes change state to waiting

                this.cycle = AirlockState.WAITING;
                this.lastCycle = AirlockState.CYCLE_IN;
            }
        }
    }

    public float getAirlockPressure()
    {
        float pressure = 0;
        blocks.ForEach(block =>
        {
            if(block is IMyAirVent)
            {
                IMyAirVent myAirVent = block as IMyAirVent;
                pressure = myAirVent.GetOxygenLevel();
            }
        });

        return pressure;
    }

}