/*Change this to represent the pressure in the airlock before it open the outer doors.
 *
 * - Leave as is if you want vacuum.
 * - Change to your planet Air Pressure level
 *
 */
float targetOuterPressure = 0.05f;

readonly MyIni _ini = new MyIni();
readonly MyCommandLine _commandLine= new MyCommandLine();
MyIniParseResult _iniResult;

readonly Dictionary<string, Airlock> airlocks = new Dictionary<string, Airlock>(StringComparer.CurrentCultureIgnoreCase);
IMyTextSurface _pbPanel;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;
    GetAirlocks();
    _pbPanel = Me.GetSurface(0);
}

public void Save()
{

}

public void Main(string argument, UpdateType updateSource)
{
    if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal)) != 0)
    {
        if (_commandLine.TryParse(argument))
        {
            string targetAirlock = _commandLine.Argument(0).ToString();
            string command = _commandLine.Argument(1).ToString();

            switch (command)
            {
                case "IN":
                    if(airlocks[targetAirlock].CurrentState != Airlock.AirlockState.CYCLED_IN)
                        airlocks[targetAirlock].ChangeState(Airlock.AirlockState.CYCLED_IN);
                    break;
                case "OUT":
                    if (airlocks[targetAirlock].CurrentState != Airlock.AirlockState.CYCLED_OUT)
                        airlocks[targetAirlock].ChangeState(Airlock.AirlockState.CYCLED_OUT);
                    break;
                case "OPEN_ALL":
                    if (airlocks[targetAirlock].CurrentState != Airlock.AirlockState.EMERGENCY_OPENED)
                        airlocks[targetAirlock].ChangeState(Airlock.AirlockState.EMERGENCY_OPENED);
                    break;
                case "CLOSE_ALL":
                    if (airlocks[targetAirlock].CurrentState != Airlock.AirlockState.EMERGENCY_CLOSED)
                        airlocks[targetAirlock].ChangeState(Airlock.AirlockState.EMERGENCY_CLOSED);
                    break;

                default:
                    break;
            }
        }
    }
    if ((updateSource & UpdateType.Update10) != 0)
    {
        foreach(var airlock in airlocks) {
            if(airlock.Value.CurrentState != Airlock.AirlockState.CYCLED_IN
                || airlock.Value.CurrentState != Airlock.AirlockState.CYCLED_OUT
                || airlock.Value.CurrentState != Airlock.AirlockState.EMERGENCY_OPENED
                || airlock.Value.CurrentState != Airlock.AirlockState.EMERGENCY_CLOSED)
                    airlock.Value.CheckState();
        }
    }
    if ((updateSource & UpdateType.Update100) != 0)
    {
        StringBuilder _output = new StringBuilder("AIRLOCK STATUS");
        _output.AppendLine();
        foreach (var airlock in airlocks)
        {
            _output.AppendLine("Airlock: " + airlock.Key.ToString() + "|| Pressure: " + airlock.Value.GetAirlockPressure());
            _output.AppendLine("Current State: " + airlock.Value.CurrentState.ToString() + "|| Target State: " + airlock.Value.TargetState.ToString());
            _output.AppendLine();
        }
        _pbPanel.WriteText(_output.ToString());
    }
}

public void GetAirlocks()
{
    List<string> airlocksNames = new List<string>();
    List<IMyFunctionalBlock> matchingBlocks = new List<IMyFunctionalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyFunctionalBlock>(matchingBlocks, block => MyIni.HasSection(block.CustomData, "airlock"));

    if (!_ini.TryParse(Me.CustomData, out _iniResult))
        throw new Exception(_iniResult.ToString());

    if (MyIni.HasSection(Me.CustomData, "airlocks"))
    {
        char delimiter = ',';
        airlocksNames = _ini.Get("airlocks", "airlockslist").ToString().Split(delimiter).ToList();
    }

    airlocksNames.ForEach(airlockName =>
    {
        List<IMyFunctionalBlock> airlockBlocks = new List<IMyFunctionalBlock>();
        airlockBlocks = matchingBlocks.Where(block => block.CustomData.Contains("name=" + airlockName)).ToList();

        Airlock thisAirlock = new Airlock(airlockBlocks, targetOuterPressure);
        airlocks.Add(airlockName, thisAirlock);
    });
}

public class Airlock
{
    public enum AirlockState
    {
        DEPRESSURIZING = 1,
        PRESSURIZING = 2,
        CYCLE_IN = 3,
        CYCLED_IN = 4,
        CYCLE_OUT = 5,
        CYCLED_OUT = 6,
        EMERGENCY_OPEN = 97,
        EMERGENCY_OPENED = 98,
        EMERGENCY_CLOSE = 99,
        EMERGENCY_CLOSED = 100
    };

    private const string POSITION_OUTER_NAME = "Outer";
    private const string POSITION_INNER_NAME = "Inner";

    private readonly Color LIGHT_WARNING_COLOR = Color.OrangeRed;
    private readonly Color LIGHT_READY_COLOR = Color.DarkGreen;
    private const float LIGHT_BLINK_INTERVAL = 1.0f;
    private const float LIGHT_BLINK_LENGTH = 50.0f;

    private const float TARGET_INNER_PRESSURE = 0.75f;
    private float TARGET_OUTER_PRESSURE = 0.05f;

    private const int TICKS_MIN_TIME = 100;
    private int ticksSinceLastChange = 0;

    private AirlockState currentState = AirlockState.CYCLE_OUT;
    private AirlockState targetState = AirlockState.CYCLED_OUT;

    private readonly List<IMyDoor> _innerDoors = new List<IMyDoor>();
    private readonly List<IMyDoor> _outerDoors = new List<IMyDoor>();
    private readonly List<IMyLightingBlock> _lights = new List<IMyLightingBlock>();
    private readonly List<IMyAirVent> _vents = new List<IMyAirVent>();
    private readonly List<IMyButtonPanel> _panels = new List<IMyButtonPanel>();

    private readonly MyIni _blockIni = new MyIni();
    private MyIniParseResult iniResult;

    public AirlockState CurrentState {
        get { return currentState; }
    }

    public AirlockState TargetState {
        get { return targetState; }
    }


    public Airlock(List<IMyFunctionalBlock> blocks, float targetOuterPressure)
    {
        TARGET_OUTER_PRESSURE = targetOuterPressure;
        SetAirlockBlocks(blocks);
        ChangeState(AirlockState.CYCLED_OUT);
    }

    public void CheckState()
    {
        if (IsComplete())
        {
            switch (currentState)
            {
                case AirlockState.DEPRESSURIZING: // innerDoors = closed, outerDoors = closed, vents = ON & DEPRESSURIZING = ON {Check for pressure = 0}
                    _vents.ForEach(vent => {
                        vent.Enabled = true;
                        vent.Depressurize = true;
                    });
                    if (ticksSinceLastChange < TICKS_MIN_TIME)
                    {
                        ticksSinceLastChange++;
                        if (GetAirlockPressure() > TARGET_OUTER_PRESSURE)
                            break;
                    }
                    currentState = AirlockState.CYCLED_OUT;
                    SetOuterDoors(DoorStatus.Open);
                    break;
                case AirlockState.PRESSURIZING: // innerDoors = closed, outerDoors = closed, vents = ON & DEPRESSURIZING = OFF {Check for pressure = 1}
                    _vents.ForEach(vent => {
                        vent.Enabled = true;
                        vent.Depressurize = false;
                    });
                    if (GetAirlockPressure() < TARGET_INNER_PRESSURE)
                        break;
                    currentState = AirlockState.CYCLED_IN;
                    break;
                case AirlockState.CYCLE_IN: // innerDoors = closed, outerDoors = closing, vents = OFF {check for doors = closed}
                    SetOuterDoors(DoorStatus.Closed);
                    SetWarningLights();
                    if (!CheckOuterDoorState(DoorStatus.Closed))
                        break;
                    currentState = AirlockState.PRESSURIZING;
                    disableDoors(_outerDoors);
                    break;
                case AirlockState.CYCLED_IN: // innerDoors = opened, outerDoors = closed, vents = OFF
                    SetInnerDoors(DoorStatus.Open);
                    if (!CheckInnerDoorState(DoorStatus.Open))
                        break;
                    DisableBlocks();
                    SetReadyLights();
                    break;
                case AirlockState.CYCLE_OUT: // innerDoors = closing, outerDoors = closed, vents = OFF {check for doors = closed}
                    SetWarningLights();
                    SetInnerDoors(DoorStatus.Closed);
                    if (!CheckInnerDoorState(DoorStatus.Closed))
                        break;
                    currentState = AirlockState.DEPRESSURIZING;
                    ticksSinceLastChange = 0;
                    disableDoors(_innerDoors);
                    break;
                case AirlockState.CYCLED_OUT: // innerDoors = closed, outerDoors = opened, vents = OFF
                    if (!CheckOuterDoorState(DoorStatus.Open))
                        break;
                    DisableBlocks();
                    SetReadyLights();
                    break;
                case AirlockState.EMERGENCY_OPEN: // innerDoors = opening, outerDoors = opening, vents = OFF
                    SetWarningLights();
                    SetInnerDoors(DoorStatus.Open);
                    SetOuterDoors(DoorStatus.Open);
                    if (!CheckInnerDoorState(DoorStatus.Open) || !CheckOuterDoorState(DoorStatus.Open))
                        break;
                    currentState = AirlockState.EMERGENCY_OPENED;
                    break;
                case AirlockState.EMERGENCY_OPENED: // innerDoors = opened, outerDoors = opened, vents = OFF
                    DisableBlocks();
                    break;
                case AirlockState.EMERGENCY_CLOSE: // innerDoors = closing, outerDoors = closing, vents = OFF
                    SetWarningLights();
                    SetInnerDoors(DoorStatus.Closed);
                    SetOuterDoors(DoorStatus.Closed);
                    if (!CheckInnerDoorState(DoorStatus.Closed) || !CheckOuterDoorState(DoorStatus.Closed))
                        break;
                    currentState = AirlockState.EMERGENCY_CLOSED;
                    break;
                case AirlockState.EMERGENCY_CLOSED: // innerDoors = closed, outerDoors = closed, vents = OFF
                    DisableBlocks();
                    break;
                default:
                    break;
            }
        }
    }

    public void ChangeState(AirlockState state)
    {
        targetState = state;
        switch (state)
        {
            case AirlockState.CYCLED_IN:
                currentState = AirlockState.CYCLE_IN;
                break;
            case AirlockState.CYCLED_OUT:
                currentState = AirlockState.CYCLE_OUT;
                break;
            case AirlockState.EMERGENCY_OPENED:
                currentState = AirlockState.EMERGENCY_OPEN;
                break;
            case AirlockState.EMERGENCY_CLOSED:
                currentState = AirlockState.EMERGENCY_CLOSE;
                break;
            default:
                break;
        }

        CheckState();
    }

    private void SetAirlockBlocks(List<IMyFunctionalBlock> airlockBlocks)
    {
        airlockBlocks.ForEach(block => {
            if (block is IMyDoor)
            {
                if(!_blockIni.TryParse(block.CustomData, out iniResult))
                    throw new Exception(iniResult.ToString());

                if (_blockIni.Get("Airlock", "POSITION").ToString() == POSITION_INNER_NAME)
                    _innerDoors.Add(block as IMyDoor);
                else if (_blockIni.Get("Airlock", "POSITION").ToString() == POSITION_OUTER_NAME)
                    _outerDoors.Add(block as IMyDoor);
            }
            else if (block is IMyLightingBlock)
                _lights.Add(block as IMyLightingBlock);
            else if (block is IMyAirVent)
                _vents.Add(block as IMyAirVent);
            else if (block is IMyButtonPanel)
                _panels.Add(block as IMyButtonPanel);
        });
    }

    public float GetAirlockPressure()
    {
        return _vents[0].GetOxygenLevel();
    }

    private bool IsComplete()
    {
        if (_innerDoors.Count < 1)
            return false;

        if (_outerDoors.Count < 1)
            return false;

        if (_vents.Count < 1)
            return false;

        return true;
    }

    private void SetInnerDoors(DoorStatus targetState)
    {
        _innerDoors.ForEach(door =>
        {
            door.Enabled = true;
            if (targetState == DoorStatus.Closed)
                door.CloseDoor();
            if (targetState == DoorStatus.Open)
            {
                door.OpenDoor();
            }
        });
    }

    private void SetOuterDoors(DoorStatus targetState)
    {
        _outerDoors.ForEach(door =>
        {
            door.Enabled = true;
            if (targetState == DoorStatus.Closed)
                door.CloseDoor();
            if (targetState == DoorStatus.Open)
            {
                door.OpenDoor();
            }
        });
    }

    private void enableDoors(List<IMyDoor> doors)
    {
        doors.ForEach(block => block.Enabled = true);
    }

    private void disableDoors(List<IMyDoor> doors)
    {
        doors.ForEach(block => block.Enabled = false);
    }

    private bool CheckInnerDoorState(DoorStatus targetDoorState)
    {
        if (targetDoorState == DoorStatus.Open)
        {
            int openedDoors = 0;

            _innerDoors.ForEach(door => {
                if (door.Status == DoorStatus.Open)
                    openedDoors++;
            });

            if (openedDoors == _innerDoors.Count)
                return true;
        }

        if (targetDoorState == DoorStatus.Closed)
        {
            int closedDoors = 0;

            _innerDoors.ForEach(door => {
                if (door.Status == DoorStatus.Closed)
                    closedDoors++;
            });

            if (closedDoors == _innerDoors.Count)
                return true;
        }

        return false;
    }

    private bool CheckOuterDoorState(DoorStatus targetState)
    {
        if (targetState == DoorStatus.Open)
        {
            int openedDoors = 0;

            _outerDoors.ForEach(door => {
                if (door.Status == DoorStatus.Open)
                    openedDoors++;
            });

            if (openedDoors == _outerDoors.Count)
                return true;
        }

        if (targetState == DoorStatus.Closed)
        {
            int closedDoors = 0;

            _outerDoors.ForEach(door => {
                if (door.Status == DoorStatus.Closed)
                    closedDoors++;
            });

            if (closedDoors == _outerDoors.Count)
                return true;
        }

        return false;
    }

    private void SetWarningLights()
    {
        _lights.ForEach(light => {
            light.Color = LIGHT_WARNING_COLOR;
            light.BlinkLength = LIGHT_BLINK_LENGTH;
            light.BlinkIntervalSeconds = LIGHT_BLINK_INTERVAL;
        });
    }

    private void SetReadyLights()
    {
        _lights.ForEach(light => {
            light.Color = LIGHT_READY_COLOR;
            light.BlinkLength = 0f;
            light.BlinkIntervalSeconds = 0f;
        });
    }

    private void DisableBlocks()
    {
        _innerDoors.ForEach(door => door.Enabled = false);
        _outerDoors.ForEach(door => door.Enabled = false);
        _vents.ForEach(vent => vent.Enabled = false);
    }

    public void SetTargetOuterPressure(float pressure)
    {
        TARGET_OUTER_PRESSURE = pressure;
    }

}