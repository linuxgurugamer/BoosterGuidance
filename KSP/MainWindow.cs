﻿using System;
using System.Linq;
using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.Profiling;

namespace BoosterGuidance
{
  [KSPAddon(KSPAddon.Startup.Flight, false)]
  public class MainWindow : MonoBehaviour
  {
    // constantsBurnSt
    Color tgt_color = new Color(1, 1, 0, 0.5f);
    Color pred_color = new Color(1, 0.2f, 0.2f, 0.5f);

    // private
    int tab = 0;
    bool hidden = true;
    private static GuiUtils.TargetingCross targetingCross;
    private static GuiUtils.PredictionCross predictedCross;
    BLController activeController = null;
    DictionaryValueList<Guid, BLController> controllers = new DictionaryValueList<Guid, BLController>();
    BLController[] flying = { null, null, null, null, null }; // To connect to Fly() functions. Must be 5 or change EnableGuidance()
    Rect windowRect = new Rect(150, 150, 220, 564);
    EditableAngle tgtLatitude = 0;
    EditableAngle tgtLongitude = 0;
    EditableInt tgtAlt = 0;
    // Re-Entry Burn
    EditableInt reentryBurnAlt = 55000;
    EditableInt reentryBurnTargetSpeed = 700;
    float reentryBurnMaxAoA = 20; // will be set from reentryBurnSteerKp
    float reentryBurnSteerKp = 0.004f; // angle to steer = gain * targetError(in m)
    // Aero descent
    double aeroDescentMaxAoA = 0; // will be set from Kp
    float aeroDescentSteerLogKp = 5.5f;
    float aeroDescentSteerKdProp = 0; // Kd set to this prop. of aeroDescentSteerKp
    // Landing burn
    float landingBurnSteerLogKp = 2.5f;
    double landingBurnMaxAoA = 0; // will be set from Kp
    string numLandingBurnEngines = "current";
    ITargetable lastVesselTarget = null;
    double lastNavLat = 0;
    double lastNavLon = 0;

    // Advanced settings
    EditableInt touchdownMargin = 20;
    EditableDouble touchdownSpeed = 2;
    EditableInt noSteerHeight = 100;
    bool deployLandingGear = true;
    EditableInt deployLandingGearHeight = 500;
    EditableInt simulationsPerSec = 10;
    EditableInt igniteDelay = 3; // Needed for RO

    Vessel currentVessel = null; // to detect vessel switch
    bool showTargets = true;
    bool logging = false;
    bool pickingPositionTarget = false;
    string info = "Disabled";
    double pickLat, pickLon, pickAlt;

    // GUI Elements
    Color red = new Color(1, 0, 0, 0.5f);
    bool map;

    public MainWindow()
    {
      //Awake();
    }

    public void OnGUI()
    {
      if (!hidden)
      {
        windowRect = GUI.Window(0, windowRect, WindowFunction, "Booster Guidance");
      }
    }

    public void Awake()
    {
      if (targetingCross != null)
        targetingCross.enabled = false;
      if (predictedCross != null)
        predictedCross.enabled = false;
      if (MapView.MapIsEnabled)
      {
        targetingCross = PlanetariumCamera.fetch.gameObject.AddComponent<GuiUtils.TargetingCross>();
        predictedCross = PlanetariumCamera.fetch.gameObject.AddComponent<GuiUtils.PredictionCross>();
      }
      else
      {
        targetingCross = FlightCamera.fetch.mainCamera.gameObject.AddComponent<GuiUtils.TargetingCross>();
        predictedCross = FlightCamera.fetch.mainCamera.gameObject.AddComponent<GuiUtils.PredictionCross>();
      }
      map = MapView.MapIsEnabled;
      targetingCross.SetColor(Color.yellow);
      targetingCross.enabled = true;
      predictedCross.SetColor(Color.red);
      predictedCross.enabled = false;
    }

    public void OnDestroy()
    {
      hidden = true;
      targetingCross.enabled = false;
      predictedCross.enabled = false;
    }

    public List<BoosterGuidanceVesselSettings> GetSettingsModules(Vessel vessel)
    {
      List<BoosterGuidanceVesselSettings> modules = new List<BoosterGuidanceVesselSettings>();
      foreach (var part in vessel.Parts)
      {
        foreach (var mod in part.Modules)
        {
          //Debug.Log("[BoosterGuidance] vessel=" + vessel.name + "part=" + part.name + " module=" + mod.name + " modtype=" + mod.GetType());
          if (mod.GetType() == typeof(BoosterGuidanceVesselSettings))
            modules.Add((BoosterGuidanceVesselSettings)mod);
        }
      }
      if (modules.Count == 0)
        Debug.Log("[BoosterGuidance] No BoosterGuidanceVesselSettings module for vessel=" + vessel.name);
      return modules;
    }

    public void Save(Vessel vessel, BLControllerPhase phase)
    {
      foreach (var module in GetSettingsModules(vessel))
      {
        module.tgtLatitude = (float)tgtLatitude;
        module.tgtLongitude = (float)tgtLongitude;
        module.tgtAlt = tgtAlt;
        module.reentryBurnAlt = (int)reentryBurnAlt;
        module.reentryBurnTargetSpeed = (int)reentryBurnTargetSpeed;
        module.reentryBurnSteerKp = reentryBurnSteerKp;
        module.aeroDescentSteerKp = Mathf.Exp(aeroDescentSteerLogKp);
        module.landingBurnSteerKp = Mathf.Exp(landingBurnSteerLogKp);
        module.touchdownMargin = touchdownMargin;
        module.touchdownSpeed = (float)touchdownSpeed;
        module.noSteerHeight = noSteerHeight;
        module.deployLandingGear = deployLandingGear;
        module.deployLandingGearHeight = deployLandingGearHeight;
        if (phase == BLControllerPhase.BoostBack)
          module.phase = "BoostBack";
        else if (phase == BLControllerPhase.Coasting)
          module.phase = "Coasting";
        else if (phase == BLControllerPhase.ReentryBurn)
          module.phase = "ReentryBurn";
        else if (phase == BLControllerPhase.AeroDescent)
          module.phase = "AeroDescent";
        else if (phase == BLControllerPhase.LandingBurn)
          module.phase = "LandingBurn";
        else
          module.phase = "Unset";
      }
      Debug.Log("[BoosterGuidance] Vessel settings saved for " + vessel.name);
    }

    public void Load(Vessel vessel, out BLControllerPhase phase)
    {
      phase = BLControllerPhase.Unset;
      foreach (var module in GetSettingsModules(vessel))
      {
        tgtLatitude = module.tgtLatitude;
        tgtLongitude = module.tgtLongitude;
        tgtAlt = (int)module.tgtAlt;
        reentryBurnAlt = module.reentryBurnAlt;
        reentryBurnSteerKp = module.reentryBurnSteerKp;
        reentryBurnTargetSpeed = module.reentryBurnTargetSpeed;
        aeroDescentSteerLogKp = Mathf.Log(module.aeroDescentSteerKp);
        landingBurnSteerLogKp = Mathf.Log(module.landingBurnSteerKp);
        touchdownMargin = module.touchdownMargin;
        touchdownSpeed = module.touchdownSpeed;
        noSteerHeight = module.noSteerHeight;
        deployLandingGear = module.deployLandingGear;
        deployLandingGearHeight = module.deployLandingGearHeight;
        if (module.phase == "BoostBack")
          phase = BLControllerPhase.BoostBack;
        else if (module.phase == "Re-entry Burn")
          phase = BLControllerPhase.ReentryBurn;
        else if (module.phase == "Coasting")
          phase = BLControllerPhase.Coasting;
        else if (module.phase == "Aero Descent")
          phase = BLControllerPhase.AeroDescent;
        else if (module.phase == "Landing Burn")
          phase = BLControllerPhase.LandingBurn;
        else
          phase = BLControllerPhase.Unset;
        Debug.Log("[BoosterGuidance] Vessel settings loaded from " + vessel.name);
      }
      
    }

    void SetEnabledColors(bool phaseEnabled)
    {
      if (phaseEnabled)
      {
        GUI.skin.button.normal.textColor = new Color(1, 1, 1, 1);
        GUI.skin.label.normal.textColor = new Color(1, 1, 1, 1);
        GUI.skin.toggle.normal.textColor = new Color(1, 1, 1, 1);
        GUI.skin.box.normal.textColor = new Color(1, 1, 1, 1);
        GUI.skin.textArea.normal.textColor = new Color(1, 1, 1, 1);
        GUI.skin.textField.normal.textColor = new Color(1, 1, 1, 1);
      }
      else
      {
        GUI.skin.button.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        GUI.skin.label.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        GUI.skin.toggle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        GUI.skin.box.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        GUI.skin.textArea.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        GUI.skin.textField.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
      }
    }

    void WindowFunction(int windowID)
    {
      OnUpdate();
      SetEnabledColors(true);
      // Close button
      if (GUI.Button(new Rect(windowRect.width - 18, 2, 16, 16), ""))
      {
        Hide();
        return;
      }

      if (currentVessel != FlightGlobals.ActiveVessel)
      {
        BLControllerPhase phase = BLControllerPhase.Unset;
        if (currentVessel != null)
          Debug.Log("[BoosterGuidance] Changed vessel old=" + currentVessel.name + " new=" + FlightGlobals.ActiveVessel.name);
        else
          Debug.Log("[BoosterGuidance] Changed vessel new=" + FlightGlobals.ActiveVessel.name);

        // Use existing controller attached to vessel?
        try
        {
          // Already have an associated controller?
          activeController = controllers[FlightGlobals.ActiveVessel.id];
          UpdateWindow(activeController);
        }
        catch (KeyNotFoundException)
        {
          // No associated controller - vessel not previously controller in this game session
          activeController = new BLController();
          controllers[FlightGlobals.ActiveVessel.id] = activeController;
          // Load settings from BoosterGuidanceSettings module into window settings
          Load(FlightGlobals.ActiveVessel, out phase);
          UpdateController(activeController); // updates controller from loaded settings
          Debug.Log("[BoosterGuidance] Setting phase " + phase + " from loaded vessel");
          if (phase != BLControllerPhase.Unset)
            EnableGuidance(phase);
        }
        RedrawTarget(FlightGlobals.ActiveVessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        currentVessel = FlightGlobals.ActiveVessel;
      }

      // Show/hide targets
      targetingCross.enabled = showTargets;
      predictedCross.enabled = showTargets && (activeController != null) && (activeController.enabled);

      // Check for target being set
      if (currentVessel.targetObject != lastVesselTarget)
      {
        if (currentVessel.targetObject != null)
        {
          Vessel target = currentVessel.targetObject.GetVessel();
          tgtLatitude = target.latitude;
          tgtLongitude = target.longitude;
          tgtAlt = (int)target.altitude;
          GuiUtils.ScreenMessage("Target set to " + target.name);
        }
        lastVesselTarget = currentVessel.targetObject;
      }

      // Check for navigation target
      NavWaypoint nav = NavWaypoint.fetch;
      if (nav.IsActive)
      {
        // Does current nav position differ from last one used? A hack because
        // a can't see a way to check if the nav waypoint has changed
        // Doing it this way means lat and lon in window can be edited without them
        // getting locked to the nav waypoint
        if ((lastNavLat != nav.Latitude) || (lastNavLon != nav.Longitude))
        {
          tgtLatitude = nav.Latitude;
          tgtLongitude = nav.Longitude;
          lastNavLat = nav.Latitude;
          lastNavLon = nav.Longitude;
          // This is VERY unreliable
          //tgtAlt = (int)nav.Altitude;
          tgtAlt = (int)currentVessel.mainBody.TerrainAltitude(tgtLatitude, tgtLongitude);
          GuiUtils.ScreenMessage("Target set to " + nav.name);
        }
      }
      else
      {
        lastNavLat = 0;
        lastNavLon = 0;
      }

      // Check for unloaded vessels
      for (int i = 0; i < flying.Length; i++)
      {
        // Slot is filled, vessel exists and not current vessel
        if ((flying[i] != null) && (flying[i].vessel != null) && (flying[i] != activeController))
        {
          if (!flying[i].vessel.loaded)
          {
            GuiUtils.ScreenMessage("[BoosterGuidance] Guidance disabled for " + flying[i].vessel.name + " as out of physics range");
            DisableGuidance(flying[i]);
          }
        }
      }

      // Set Angle-of-Attack from gains
      reentryBurnMaxAoA = (reentryBurnSteerKp / 0.005f) * 30;
      aeroDescentMaxAoA = 30 * (aeroDescentSteerLogKp / 7);
      landingBurnMaxAoA = 30 * (landingBurnSteerLogKp / 7);

      tab = GUILayout.Toolbar(tab, new string[] { "Main", "Advanced" });
      bool changed = false;
      switch(tab)
      {
        case 0:
          changed = MainTab(windowID);
          break;
        case 1:
          changed = AdvancedTab(windowID);
          break;
      }

      if (changed)
      {
        if (activeController != null)
          UpdateController(activeController); // copy settings from window
        if (activeController.vessel != null)
          Save(activeController.vessel, activeController.phase);
      }
    }

    bool AdvancedTab(int windowID)
    {
      // Suicide factor
      // Margin
      // Touchdown speed
      // No steer height
   
      GUILayout.BeginHorizontal();
      deployLandingGear = GUILayout.Toggle(deployLandingGear, "Deploy landing gear");
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Deploy gear height", deployLandingGearHeight, "m", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("No steer height", noSteerHeight, "m", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Touchdown margin", touchdownMargin, "m", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Touchdown speed", touchdownSpeed, "m/s", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Engine startup", igniteDelay, "s", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Simulations /sec", simulationsPerSec, "", 65);
      GUILayout.EndHorizontal();

      // Show all active vessels
      GUILayout.Space(10);
      GUILayout.BeginHorizontal();
      GUILayout.Label("Other vessels:");
      GUILayout.EndHorizontal();
      for (int i = 0; i < flying.Length; i++)
      {
        // Slot is filled, vessel exists and not current vessel
        if ((flying[i] != null) && (flying[i].vessel != null) && (flying[i] != activeController))
        {
          GUILayout.BeginHorizontal();
          GUILayout.Label(flying[i].vessel.name);
          GUILayout.FlexibleSpace();
          if (GUILayout.Button("X", GUILayout.Width(26))) // Cancel guidance
            DisableGuidance(flying[i]);
          GUILayout.EndHorizontal();

          GUILayout.BeginHorizontal();
          GUILayout.Label("  alt:" + (int)flying[i].vessel.altitude + "m  err:" + (int)flying[i].targetError + "m");
          GUILayout.EndHorizontal();

        }
      }

      GUI.DragWindow();
      return GUI.changed;
    }

    bool MainTab(int windowID)
    {
      bool targetChanged = false;

      // Check targets are on map vs non-map
      if (map != MapView.MapIsEnabled)
        Awake();


      BLControllerPhase phase = activeController.phase;
      if (!activeController.enabled)
        phase = BLControllerPhase.Unset;

      // Target:

      // Draw any Controls inside the window here
      GUILayout.Label("Target");//Target coordinates:

      GUILayout.BeginHorizontal();
      double step = 1.0 / (60 * 60); // move by 1 arc second
      tgtLatitude.DrawEditGUI(EditableAngle.Direction.NS);
      if (GUILayout.Button("▲"))
      {
        tgtLatitude += step;
        targetChanged = true;
      }
      if (GUILayout.Button("▼"))
      {
        tgtLatitude -= step;
        targetChanged = true;
      }
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      tgtLongitude.DrawEditGUI(EditableAngle.Direction.EW);
      if (GUILayout.Button("◄"))
      {
        tgtLongitude -= step;
        targetChanged = true;
      }
      if (GUILayout.Button("►"))
      {
        tgtLongitude += step;
        targetChanged = true;
      }
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Pick Target"))
        PickTarget();
      if (GUILayout.Button("Set Here"))
        SetTargetHere();
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      EditableInt preTgtAlt = tgtAlt;
      GuiUtils.SimpleTextBox("Target altitude", tgtAlt, "m", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      showTargets = GUILayout.Toggle(showTargets, "Show targets");
      
      // TODO - Need to be specific to controller so works when switching vessel
      bool prevLogging = logging;
      logging = GUILayout.Toggle(logging, "Logging");
      if (activeController.enabled)
      {
        if ((!prevLogging) && (logging)) // logging switched on
          StartLogging();
        if ((prevLogging) && (!logging)) // logging switched off
          StopLogging();
      }
      GUILayout.EndHorizontal();

      // Info box
      GUILayout.BeginHorizontal();
      GUILayout.Label(info);
      GUILayout.EndHorizontal();

      // Boostback
      SetEnabledColors((phase == BLControllerPhase.BoostBack) || (phase == BLControllerPhase.Unset));
      GUILayout.BeginHorizontal();
      if (GUILayout.Button(new GUIContent("Boostback", "Enable thrust towards target when out of atmosphere")))
        EnableGuidance(BLControllerPhase.BoostBack);
      GUILayout.EndHorizontal();

      // Coasting
      SetEnabledColors((phase == BLControllerPhase.Coasting) || (phase == BLControllerPhase.Unset));
      GUILayout.BeginHorizontal();
      if (GUILayout.Button(new GUIContent("Coasting", "Turn to retrograde attitude and wait for Aero Descent phase")))
        EnableGuidance(BLControllerPhase.Coasting);
      GUILayout.EndHorizontal();

      // Re-Entry Burn
      SetEnabledColors((phase == BLControllerPhase.ReentryBurn) || (phase == BLControllerPhase.Unset));
      GUILayout.BeginHorizontal();
      if (GUILayout.Button(new GUIContent("Re-Entry Burn", "Ignite engine on re-entry to reduce overheating")))
        EnableGuidance(BLControllerPhase.ReentryBurn);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Enable altitude", reentryBurnAlt, "m", 65);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GuiUtils.SimpleTextBox("Target speed", reentryBurnTargetSpeed, "m/s", 40);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Steer", GUILayout.Width(40));
      reentryBurnSteerKp = GUILayout.HorizontalSlider(reentryBurnSteerKp, 0, 0.005f);
      GUILayout.Label(((int)(reentryBurnMaxAoA)).ToString()+ "°(max)", GUILayout.Width(60));
      GUILayout.EndHorizontal();

      // Aero Descent
      SetEnabledColors((phase == BLControllerPhase.AeroDescent) || (phase == BLControllerPhase.Unset));
      GUILayout.BeginHorizontal();
      if (GUILayout.Button(new GUIContent("Aero Descent", "No thrust aerodynamic descent, steering with gridfins within atmosphere")))
        EnableGuidance(BLControllerPhase.AeroDescent);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Steer", GUILayout.Width(40));
      aeroDescentSteerLogKp = GUILayout.HorizontalSlider(aeroDescentSteerLogKp, 0, 7);
      GUILayout.Label(((int)aeroDescentMaxAoA).ToString() + "°(max)", GUILayout.Width(60));
      GUILayout.EndHorizontal();
      
      // Landing Burn
      SetEnabledColors((phase == BLControllerPhase.LandingBurn) || (phase == BLControllerPhase.Unset));
      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Landing Burn"))
        EnableGuidance(BLControllerPhase.LandingBurn);
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Enable altitude");
      String text = "n/a";
      if (activeController != null)
      {
        if (activeController.landingBurnHeight > 0)
          text = ((int)(activeController.landingBurnHeight + tgtAlt)).ToString() + "m";
        else
        {
          if (activeController.landingBurnHeight < 0)
            text = "too heavy";
        }
      }
      GUILayout.Label(text, GUILayout.Width(60));
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Engines");
      GUILayout.Label(numLandingBurnEngines);
      if (activeController != null)
      {
        if (numLandingBurnEngines == "current")  // Save active engines
        {
          if (GUILayout.Button("Set"))  // Set to currently active engines
            numLandingBurnEngines = activeController.SetLandingBurnEngines();
        }
        else
        {
          if (GUILayout.Button("Unset"))  // Set to currently active engines
          {
            numLandingBurnEngines = "current";
            activeController.UnsetLandingBurnEngines();
          }
        }
      }
      GUILayout.EndHorizontal();

      GUILayout.BeginHorizontal();
      GUILayout.Label("Steer", GUILayout.Width(40));
      landingBurnSteerLogKp = GUILayout.HorizontalSlider(landingBurnSteerLogKp, 0, 7);
      GUILayout.Label(((int)(landingBurnMaxAoA)).ToString() + "°(max)", GUILayout.Width(60));
      GUILayout.EndHorizontal();

      // Activate guidance
      SetEnabledColors(true); // back to normal
      GUILayout.BeginHorizontal();
      if (!activeController.enabled)
      {
        if (GUILayout.Button("Enable Guidance"))
          EnableGuidance(BLControllerPhase.Unset);
      }
      else
      {
        if (GUILayout.Button("Disable Guidance"))
          DisableGuidance(activeController);
      }

      GUILayout.EndHorizontal();

      GUI.DragWindow();
      return (GUI.changed) || targetChanged;
    }


    public void Show()
    {
      hidden = false;
      targetingCross.enabled = showTargets;
      predictedCross.enabled = showTargets;
    }

    public void Hide()
    {
      hidden = true;
      targetingCross.enabled = false;
      predictedCross.enabled = false;
    }

    public void UpdateController(BLController controller)
    {
      if (controller != null)
      {
        controller.reentryBurnAlt = reentryBurnAlt;
        controller.reentryBurnTargetSpeed = reentryBurnTargetSpeed;
        controller.reentryBurnSteerKp = reentryBurnSteerKp;
        controller.landingBurnMaxAoA = landingBurnMaxAoA;
        controller.tgtLatitude = tgtLatitude;
        controller.tgtLongitude = tgtLongitude;
        controller.tgtAlt = tgtAlt;
        controller.suicideFactor = 0.75;
        controller.landingBurnSteerKp = Math.Exp(landingBurnSteerLogKp);
        controller.aeroDescentMaxAoA = aeroDescentMaxAoA;
        controller.aeroDescentSteerKp = Math.Exp(aeroDescentSteerLogKp);
        controller.aeroDescentSteerKdProp = aeroDescentSteerKdProp;
        // Note that the Kp gain in the PIDs below is set by combining the relevant Kp from above
        // and a gain factor based on air resistance an throttle to determine whether to steer
        // aerodynamically or by thrust, and how sensitive the vessel is to that
        controller.pid_aero = new PIDclamp("aero", 1, 0, 0, (float)aeroDescentMaxAoA);
        controller.pid_landing = new PIDclamp("landing", 1, 0, 0, (float)landingBurnMaxAoA);
        controller.igniteDelay = igniteDelay;
        controller.noSteerHeight = noSteerHeight;
        controller.deployLandingGear = deployLandingGear;
        controller.deployLandingGearHeight = deployLandingGearHeight;
        controller.touchdownMargin = touchdownMargin;
        controller.touchdownSpeed = (float)touchdownSpeed;
        controller.simulationsPerSec = (float)simulationsPerSec;
      }
    }

    // Controller changed - update window
    public void UpdateWindow(BLController controller)
    {
      reentryBurnAlt = (int)controller.reentryBurnAlt;
      reentryBurnTargetSpeed = (int)controller.reentryBurnTargetSpeed;
      // TODO: Read from PID
      aeroDescentSteerLogKp = Mathf.Log((float)controller.aeroDescentSteerKp);
      reentryBurnAlt = (int)controller.reentryBurnAlt;
      reentryBurnTargetSpeed = (int)controller.reentryBurnTargetSpeed;
      landingBurnSteerLogKp = Mathf.Log((float)controller.landingBurnSteerKp);
      tgtLatitude = controller.tgtLatitude;
      tgtLongitude = controller.tgtLongitude;
      tgtAlt = (int)controller.tgtAlt;
      if (controller.landingBurnEngines != null)
        numLandingBurnEngines = controller.landingBurnEngines.Count.ToString();
      else
        numLandingBurnEngines = "current";
      igniteDelay = (int)controller.igniteDelay;
      noSteerHeight = (int)controller.noSteerHeight;
      deployLandingGear = controller.deployLandingGear;
      deployLandingGearHeight = (int)controller.deployLandingGearHeight;
      simulationsPerSec = (int)controller.simulationsPerSec;
    }

    void StartLogging()
    {
      if (activeController != null)
      {
        string name = activeController.vessel.name;
        name = name.Replace(" ", "_");
        name = name.Replace("(", "");
        name = name.Replace(")", "");
        Transform logTransform = RedrawTarget(activeController.vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        activeController.StartLogging(name, logTransform);
      }
    }

    void StopLogging()
    {
      if (activeController != null)
        activeController.StopLogging();
    }

    void OnPickingPositionTarget()
    {
      if (Input.GetKeyDown(KeyCode.Escape))
      {
        // Previous position
        RedrawTarget(activeController.vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        pickingPositionTarget = false;
      }
      RaycastHit hit;
      Vessel vessel = FlightGlobals.ActiveVessel;
      bool isHit = false;

      if (!MapView.MapIsEnabled)
      {
        if (GuiUtils.GetMouseHit(vessel.mainBody, windowRect, MapView.MapIsEnabled, out hit))
        {
          isHit = true;
          // Moved or picked
          vessel.mainBody.GetLatLonAlt(hit.point, out pickLat, out pickLon, out pickAlt);
        }
      }
      if (!isHit)
      {
        if (GuiUtils.GetBodyRayIntersect(vessel.mainBody, MapView.MapIsEnabled, out pickLat, out pickLon, out pickAlt))
          isHit = true;
      }
      

      if (isHit)
      {
        RedrawTarget(vessel.mainBody, pickLat, pickLon, pickAlt);

        if (Input.GetMouseButton(0))  // Picked
        {
          tgtLatitude = pickLat;
          tgtLongitude = pickLon;
          tgtAlt = (int)pickAlt;
          pickingPositionTarget = false;
          string message = "Picked target";
          GuiUtils.ScreenMessage(message);
          UpdateController(activeController);
          Save(FlightGlobals.ActiveVessel, activeController.phase);
        }
      }
    }

    void OnUpdate()
    {
      // Redraw targets
      if (!pickingPositionTarget)
      {
        // Need to redraw as size changes (may be less often)
        RedrawTarget(FlightGlobals.ActiveVessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
      }
      else
        OnPickingPositionTarget();
    }


    void PickTarget()
    {
      showTargets = true;
      pickingPositionTarget = true;
      string message = "Click to select a target";
      GuiUtils.ScreenMessage(message);
    }


    void SetTargetHere()
    {
      tgtLatitude = FlightGlobals.ActiveVessel.latitude;
      tgtLongitude = FlightGlobals.ActiveVessel.longitude;
      double lowestY = KSPUtils.FindLowestPointOnVessel(FlightGlobals.ActiveVessel);
      tgtAlt = (int)FlightGlobals.ActiveVessel.altitude + (int)lowestY;
      RedrawTarget(FlightGlobals.ActiveVessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt + activeController.lowestY);
      GuiUtils.ScreenMessage("Target set to vessel");
    }

    Transform RedrawTarget(CelestialBody body, double lat, double lon, double alt)
    {
      Transform transform = GuiUtils.SetUpTransform(body, lat, lon, alt);
      targetingCross.enabled = showTargets;
      targetingCross.SetLatLonAlt(body, lat, lon, alt);
      return transform;
    }
    

    Transform RedrawPrediction(CelestialBody body, double lat, double lon, double alt)
    {
      predictedCross.enabled = showTargets && (activeController != null);
      predictedCross.SetLatLonAlt(body, lat, lon, alt);
      return null;
    }
    
    int GetSlotFromVessel(Vessel vessel)
    {
      for (int j = 0; j < flying.Length; j++)
      {
        if ((flying[j] != null) && (flying[j].vessel == vessel))
          return j;
      }
      return -1;
    }
    
    int GetFreeSlot()
    {
      for (int j = 0; j < flying.Length; j++)
      {
        if (flying[j] == null)
          return j;
      }
      return -1;
    }

    void EnableGuidance(BLControllerPhase phase)
    {
      if (!activeController.enabled)
      {
        Debug.Log("[BoosterGuidance] Enable Guidance for vessel " + FlightGlobals.ActiveVessel.name);
        Vessel vessel = FlightGlobals.ActiveVessel;
        RedrawTarget(vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        vessel.Autopilot.Enable(VesselAutopilot.AutopilotMode.StabilityAssist);
        activeController.AttachVessel(vessel);

        // Find slot for controller
        int i = GetFreeSlot();
        if (i != -1)
        {
          flying[i] = activeController;
          Debug.Log("[BoosterGuidance] Allocating slot " + i + " name=" + vessel.name + "(id="+vessel.id+") to " + activeController);
        }
        else
        {
          GuiUtils.ScreenMessage("All " + flying.Length + " guidance slots used");
          return;
        }

        if (i == 0)
          vessel.OnFlyByWire += new FlightInputCallback(Fly0); // 1st vessel
        if (i == 1)
          vessel.OnFlyByWire += new FlightInputCallback(Fly1); // 2nd vessel
        if (i == 2)
          vessel.OnFlyByWire += new FlightInputCallback(Fly2); // 3rd vessel
        if (i == 3)
          vessel.OnFlyByWire += new FlightInputCallback(Fly3); // 4th vessel
        if (i == 4)
          vessel.OnFlyByWire += new FlightInputCallback(Fly4); // 5th vessel
      }
      activeController.SetPhase(phase);
      GuiUtils.ScreenMessage("Enabled " + activeController.PhaseStr());
      activeController.enabled = true;
      StartLogging();
    }


    void DisableGuidance(BLController controller)
    {
      if (controller.enabled)
      {
        Vessel vessel = controller.vessel;
        vessel.Autopilot.Disable();
        int i = GetSlotFromVessel(vessel);
        Debug.Log("[BoosterGuidance] DisableGuidance() slot=" + i + " name="+vessel.name+"(id="+vessel.id+")");
        if (i == 0)
          vessel.OnFlyByWire -= new FlightInputCallback(Fly0);
        if (i == 1)
          vessel.OnFlyByWire -= new FlightInputCallback(Fly1);
        if (i == 2)
          vessel.OnFlyByWire -= new FlightInputCallback(Fly2);
        if (i == 3)
          vessel.OnFlyByWire -= new FlightInputCallback(Fly3);
        if (i == 4)
          vessel.OnFlyByWire -= new FlightInputCallback(Fly4);
        // Free up slot
        if (i != -1)
          flying[i] = null;
        controller.StopLogging();
        controller.phase = BLControllerPhase.Unset;
        if (controller == activeController)
        {
          GuiUtils.ScreenMessage("Guidance disabled!");
          predictedCross.enabled = false;
        }
        controller.enabled = false;
      }
    }
    public void Fly0(FlightCtrlState state)
    {
      Fly(flying[0], state);
    }

    public void Fly1(FlightCtrlState state)
    {
      Fly(flying[1], state);
    }

    public void Fly2(FlightCtrlState state)
    {
      Fly(flying[2], state);
    }

    public void Fly3(FlightCtrlState state)
    {
      Fly(flying[3], state);
    }

    public void Fly4(FlightCtrlState state)
    {
      Fly(flying[4], state);
    }

    public void Fly(BLController controller, FlightCtrlState state)
    {
      double throttle;
      Vector3d steer;
      double minThrust;
      double maxThrust;


      if (controller == null)
        return;
      Vessel vessel = controller.vessel;

      if (vessel.checkLanded())
      {
        GuiUtils.ScreenMessage("Vessel " + controller.vessel.name + " landed!");
        state.mainThrottle = 0;
        DisableGuidance(controller);
        // Find distance from target
        if (activeController == controller)
          info = string.Format("Landed {0:F1}m from target", controller.targetError);
        return;
      }

      KSPUtils.ComputeMinMaxThrust(vessel, out minThrust, out maxThrust);

      Vector3d tgt_r = vessel.mainBody.GetWorldSurfacePosition(tgtLatitude, tgtLongitude, tgtAlt);

      bool landingGear, gridFins;
      controller.GetControlOutputs(vessel, vessel.GetTotalMass(), vessel.GetWorldPos3D(), vessel.GetObtVelocity(), vessel.transform.up, vessel.altitude, minThrust, maxThrust,
        controller.vessel.missionTime, vessel.mainBody, tgt_r, false, out throttle, out steer, out landingGear, out gridFins);
      //Debug.Log("[BoosterGuidance] alt=" + controller.vessel.altitude + " gear_height=" + controller.deployLandingGearHeight + " deploy=" + controller.deployLandingGear+" deploy_now="+landingGear);
      if ((landingGear) && KSPUtils.DeployLandingGears(vessel))
        GuiUtils.ScreenMessage("Deploying landing gear");
      //if (gridFins)
      //  ScreenMessages.PostScreenMessage("Deploying grid fins", 1.0f, ScreenMessageStyle.UPPER_CENTER);
    

      // Set active engines in landing burn
      if (controller.phase == BLControllerPhase.LandingBurn)
      {
        if (controller.landingBurnEngines != null)
        {
          foreach (ModuleEngines engine in KSPUtils.GetAllEngines(vessel))
          {
            if (controller.landingBurnEngines.Contains(engine))
            {
              if (!engine.isOperational)
                engine.Activate();
            }
            else
            {
              if (engine.isOperational)
                engine.Shutdown();
            }
          }
        }
      }   

      if ((tgtLatitude == 0) && (tgtLongitude == 0) && (tgtAlt == 0))
      { 
        // No target. Set target to below craft
        controller.tgtLatitude = vessel.latitude;
        controller.tgtLongitude = vessel.longitude;
        controller.tgtAlt = vessel.mainBody.TerrainAltitude(tgtLatitude, tgtLongitude);
        if (activeController == controller)
        {
          UpdateWindow(controller);
          RedrawTarget(controller.vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        }
      }

      // Draw predicted position if controlling that vessel
      if (controller == activeController)
      {
        double lat, lon, alt;
        // prediction is for position of planet at current time compensating for
        // planet rotation
        vessel.mainBody.GetLatLonAlt(controller.predWorldPos, out lat, out lon, out alt);
        alt = vessel.mainBody.TerrainAltitude(lat, lon); // Make on surface
        RedrawPrediction(vessel.mainBody, lat, lon, alt + 1); // 1m above grou
        info = string.Format("Err: {0:F0}m {1:F0}° Time: {2:F0}s [{3:F0}ms]", controller.targetError, controller.attitudeError, controller.targetT, controller.elapsed_secs * 1000);

      }
      state.mainThrottle = (float)throttle;
      vessel.Autopilot.SAS.lockedMode = false;
      vessel.Autopilot.SAS.SetTargetOrientation(steer, false);
    }
  }
}