using System;
using KSP.IO;
using KSP.UI.Screens;
using UnityEngine;

namespace KerbalSimpit.Providers
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KerbalSimpitAutopilotProvider : MonoBehaviour
    {
	// Inbound messages
	private EventData<byte, object> AutopilotSetChannel;

	// Outbound messages
	private EventData<byte, object> AutopilotStateChannel;

	private volatile byte autopilotBuffer;
	private const byte UNCHANGED=0xff;

	private volatile VesselAutopilot.AutopilotMode currentStateBuffer;

	public void Start()
	{
	    autopilotBuffer=UNCHANGED;
	    currentStateBuffer=VesselAutopilot.AutopilotMode.StabilityAssist;

	    AutopilotSetChannel = GameEvents.FindEvent<EventData<byte, object>>("onSerialReceived20");
	    if (AutopilotSetChannel != null) AutopilotSetChannel.Add(setSASModeCallback);
	    AutopilotStateChannel = GameEvents.FindEvent<EventData<byte,object>>("toSerial28");
	}

	public void OnDestroy()
	{
	    if (AutopilotSetChannel != null) AutopilotSetChannel.Remove(setSASModeCallback);
	}

	public void Update()
	{
	    updateCurrentState();
	    if (autopilotBuffer!=UNCHANGED)
	    {
		setSASMode(autopilotBuffer);
		autopilotBuffer=UNCHANGED;
	    }
	}

	private bool updateCurrentState()
	{
	    VesselAutopilot.AutopilotMode newState=FlightGlobals.ActiveVessel.Autopilot.Mode;
	    if (newState!=currentStateBuffer)
	    {
		if (AutopilotStateChannel != null)
		    AutopilotStateChannel.Fire(OutboundPackets.AutopilotMode,(byte)newState);
		currentStateBuffer=newState;
		return true;
	    }
	    return false;
	}

	public void setSASModeCallback(byte ID, object Data)
	{
	    byte[] payload = (byte[])Data;
	    autopilotBuffer = payload[0];
	}

	private void setSASMode(byte mode)
	{
	    if (KSPit.Config.Verbose) Debug.Log("KerbalSimpit: Setting autopilot mode");
	    FlightGlobals.ActiveVessel.Autopilot.SetMode((VesselAutopilot.AutopilotMode)mode);
	}
    }
}


