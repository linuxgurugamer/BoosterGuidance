using System;
using System.Linq;
using System.Collections.Generic;
using KSP.Localization;
using UnityEngine;
using UnityEngine.Profiling;
using static BoosterGuidance.InitLog;

namespace BoosterGuidance
{
    public class BoosterGuidanceCore : PartModule
    {
        // Saved settings

        [KSPField(isPersistant = true, guiActive = false)]
        public bool tgtSet = false;

        [KSPField(isPersistant = true, guiActive = false)]
        public double tgtLatitude = 0;

        [KSPField(isPersistant = true, guiActive = false)]
        public double tgtLongitude = 0;

        [KSPField(isPersistant = true, guiActive = false)]
        public double tgtAlt = 0;

        [KSPField(isPersistant = true, guiActive = false)]
        public double reentryBurnAlt = 55000;

        [KSPField(isPersistant = true, guiActive = false)]
        public double reentryBurnTargetSpeed = 700;

        [KSPField(isPersistant = true, guiActive = false)]
        public float reentryBurnSteerKp = 0.01f;

        [KSPField(isPersistant = true, guiActive = false)]
        public float reentryBurnMaxAoA = 30;

        [KSPField(isPersistant = true, guiActive = false)]
        public float aeroDescentSteerKp = 10;

        [KSPField(isPersistant = true, guiActive = false)]
        public float aeroDescentMaxAoA = 15;

        [KSPField(isPersistant = true, guiActive = false)]
        public float landingBurnSteerKp = 10;

        [KSPField(isPersistant = true, guiActive = false)]
        public float landingBurnMaxAoA = 15;

        [KSPField(isPersistant = true, guiActive = false)]
        public int touchdownMargin = 20;

        [KSPField(isPersistant = true, guiActive = false)]
        public float touchdownSpeed = 2;

        [KSPField(isPersistant = true, guiActive = false)]
        public int noSteerHeight = 200;

        [KSPField(isPersistant = true, guiActive = false)]
        public bool deployLandingGear = true;

        [KSPField(isPersistant = true, guiActive = false)]
        public int deployLandingGearHeight = 500;

        [KSPField(isPersistant = true, guiActive = false)]
        public string landingBurnEngines = "current";

        [KSPField(isPersistant = true, guiActive = false)]
        public float igniteDelay = 3;

        [KSPField(isPersistant = true, guiActive = false)]
        public string phase = "Unset";

        [KSPField(isPersistant = true, guiActive = false)]
        public float aeroMult = 1;

        [KSPField(isPersistant = true, guiActive = false)]
        public bool enableRCS = true;
        [KSPField(isPersistant = true, guiActive = false)]
        public int actionGroup = 0;
        [KSPField(isPersistant = true, guiActive = false)]
        public float degreeRange = 10f;

        public double last_t = 0;
        public double last_throttle = 0;
        public Vector3d last_steer = Vector3d.zero;
        public bool logging = false;
        public bool useFAR = false;
        public bool debug = false;
        public string logFilename = "unset";
        private string info = "Disabled";
        private bool reportedLandingGear = false;

        // Flight controller with copy of these settings
        BLController controller = null;

        // List of all active controllers
        public static List<BLController> controllers = new List<BLController>();

        public void OnDestroy()
        {
            if (controller != null)
                DisableGuidance();
        }

        public void OnCrash()
        {
            // TODO - does this work?
            if ((controller != null) && (controller.vessel = FlightGlobals.ActiveVessel) && (controller.enabled))
                GuiUtils.ScreenMessage("Vessel crashed - Try increasing touchdown margin in the advanced tab");
        }

        // Find first BoosterGuidanceCore module for vessel
        static public BoosterGuidanceCore GetBoosterGuidanceCore(Vessel vessel)
        {
            foreach (var part in vessel.Parts)
            {
                foreach (var mod in part.Modules)
                {
                    if (mod.GetType() == typeof(BoosterGuidanceCore))
                    {
                        //Log.Info("vessel=" + vessel.name + " part=" + part.name + " module=" + mod.name + " modtype=" + mod.GetType());
                        return (BoosterGuidanceCore)mod;
                    }
                }
            }
            //Log.Info("No BoosterGuidanceCore module for vessel " + vessel.name);
            return null;
        }

        public void AttachVessel(Vessel vessel)
        {
            // Sets up Aero forces function again respected useFAR flag
            controller.AttachVessel(vessel, useFAR);
        }

        public void AddController(BLController controller)
        {
            controllers.Add(controller);
        }

        public void RemoveController(BLController controller)
        {
            controllers.Remove(controller);
        }

        public void CopyToOtherCore(BoosterGuidanceCore other)
        {
            other.deployLandingGear = deployLandingGear;
            other.deployLandingGearHeight = deployLandingGearHeight;
            other.landingBurnEngines = landingBurnEngines;
            other.landingBurnMaxAoA = landingBurnMaxAoA;
            other.landingBurnSteerKp = landingBurnSteerKp;
            other.aeroDescentMaxAoA = aeroDescentMaxAoA;
            other.aeroDescentSteerKp = aeroDescentSteerKp;
            other.reentryBurnAlt = reentryBurnAlt;
            other.reentryBurnMaxAoA = reentryBurnMaxAoA;
            other.reentryBurnSteerKp = reentryBurnSteerKp;
            other.reentryBurnTargetSpeed = reentryBurnTargetSpeed;
            other.tgtAlt = tgtAlt;
            other.tgtLatitude = tgtLatitude;
            other.tgtSet = tgtSet;
            other.tgtLongitude = tgtLongitude;
            other.touchdownMargin = touchdownMargin;
            other.touchdownSpeed = touchdownSpeed;
            other.igniteDelay = igniteDelay;
            other.phase = phase;
        }

        public void SetTarget(double latitude, double longitude, double alt)
        {
            tgtSet = true;
            tgtLatitude = latitude;
            tgtLongitude = longitude;
            tgtSet = true;

            tgtAlt = (int)alt;
            if (controller != null)
                controller.SetTarget(latitude, longitude, alt);
        }

        public void SetPhase(BLControllerPhase phase)
        {
            if (controller != null)
                controller.SetPhase(phase);
        }

        public BLControllerPhase Phase()
        {
            if (controller != null)
                return controller.phase;
            else
                return BLControllerPhase.Unset;
        }

        public string SetLandingBurnEngines()
        {
            List<ModuleEngines> activeEngines = KSPUtils.GetActiveEngines(vessel);
            // get string
            List<string> s = new List<string>();
            int num = 0;
            foreach (var engine in KSPUtils.GetAllEngines(vessel))
            {
                if (activeEngines.Contains(engine))
                {
                    s.Add("1");
                    num++;
                }
                else
                    s.Add("0");
            }
            landingBurnEngines = String.Join(",", s.ToArray());
            Log.Info("landingBurnEngines=" + landingBurnEngines);
            if (controller != null)
                controller.SetLandingBurnEnginesFromString(landingBurnEngines);
            return num.ToString();
        }

        public string UnsetLandingBurnEngines()
        {
            Log.Info("UnsetLandingBurnEngines");
            landingBurnEngines = "current";
            if (controller != null)
                controller.SetLandingBurnEnginesFromString(landingBurnEngines);
            return landingBurnEngines;
        }

        public double LandingBurnHeight()
        {
            if (controller != null)
                return controller.landingBurnHeight;
            else
                return 0;
        }

        public bool Enabled()
        {
            return (controller != null) && (controller.enabled);
        }

        // BoosterGuidanceCore params changed so update controller
        public void Changed()
        {
            if (controller != null)
            {
                controller.InitReentryBurn(reentryBurnSteerKp, reentryBurnMaxAoA, reentryBurnAlt, reentryBurnTargetSpeed);
                controller.InitAeroDescent(aeroDescentSteerKp, aeroDescentMaxAoA);
                controller.InitLandingBurn(landingBurnSteerKp, landingBurnMaxAoA);
                controller.SetTarget(tgtLatitude, tgtLongitude, tgtAlt);
                controller.touchdownMargin = touchdownMargin;
                controller.touchdownSpeed = touchdownSpeed;
                controller.deployLandingGear = deployLandingGear;
                controller.deployLandingGearHeight = deployLandingGearHeight;
                controller.igniteDelay = igniteDelay;
                controller.SetLandingBurnEnginesFromString(landingBurnEngines);
            }
            CopyToOtherCores();
        }

        public void CopyToOtherCores()
        {
            foreach (var part in vessel.Parts)
            {
                foreach (var mod in part.Modules)
                {
                    if (mod.GetType() == typeof(BoosterGuidanceCore))
                    {
                        var other = (BoosterGuidanceCore)mod;
                        CopyToOtherCore(other);
                    }
                }
            }
        }

        [KSPAction("Toggle BoosterGuidance")]
        public void ToggleGuidance(KSPActionParam param)
        {
            if (controller == null)
                EnableGuidance(param);
            else
                DisableGuidance(param);
        }

        public void EnableGuidance()
        {
            KSPActionParam param = new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate);
            EnableGuidance(param);

            FireEvent(actionGroup);
            if (enableRCS)
                FireEvent(12);
        }

        public static Dictionary<int, String> KM_dictAGNames = new Dictionary<int, String> {
            { 0,  "Stage" },
            { 1,  "Custom01" },
            { 2,  "Custom02" },
            { 3,  "Custom03" },
            { 4,  "Custom04" },
            { 5,  "Custom05" },
            { 6,  "Custom06" },
            { 7,  "Custom07" },
            { 8,  "Custom08" },
            { 9,  "Custom09" },
            { 10, "Custom10" },
            { 11, "Light" },
            { 12, "RCS" },
            { 13, "SAS" },
            { 14, "Brakes" },
            { 15, "Abort" },
            { 16, "Gear" },
            { 17, "Beep" },
        };

        public static Dictionary<int, KSPActionGroup> KM_dictAG = new Dictionary<int, KSPActionGroup> {
            { 0,  KSPActionGroup.None },
            { 1,  KSPActionGroup.Custom01 },
            { 2,  KSPActionGroup.Custom02 },
            { 3,  KSPActionGroup.Custom03 },
            { 4,  KSPActionGroup.Custom04 },
            { 5,  KSPActionGroup.Custom05 },
            { 6,  KSPActionGroup.Custom06 },
            { 7,  KSPActionGroup.Custom07 },
            { 8,  KSPActionGroup.Custom08 },
            { 9,  KSPActionGroup.Custom09 },
            { 10, KSPActionGroup.Custom10 },
            { 11, KSPActionGroup.Light },
            { 12, KSPActionGroup.RCS },
            { 13, KSPActionGroup.SAS },
            { 14, KSPActionGroup.Brakes },
            { 15, KSPActionGroup.Abort },
            { 16, KSPActionGroup.Gear }
        };

        public void FireEvent(int eventID)
        {
            if (eventID > 0)
            {
                Log.Info("Fire Event " + KM_dictAGNames[eventID]);
                vessel.ActionGroups.ToggleGroup(KM_dictAG[eventID]);
            }
        }



        [KSPAction("Enable BoosterGuidance")]
        public void EnableGuidance(KSPActionParam param)
        {
            controller = new BLController(vessel, useFAR);
            reportedLandingGear = false;
            Changed(); // updates controller

            if ((tgtLatitude == 0) && (tgtLongitude == 0) && (tgtAlt == 0))
            {
                GuiUtils.ScreenMessage(Localizer.Format("#BoosterGuidance_NoTargetSet"));
                return;
            }
            if (!controller.enabled)
            {
                Log.Info("Enabled Guidance for vessel " + FlightGlobals.ActiveVessel.name);
                Vessel vessel = FlightGlobals.ActiveVessel;
                Targets.RedrawTarget(vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
                vessel.Autopilot.Enable(VesselAutopilot.AutopilotMode.StabilityAssist);
                if (logging)
                    StartLogging();
                vessel.OnFlyByWire += new FlightInputCallback(Fly);
            }
            controller.SetPhase(BLControllerPhase.Unset);
            controller.enabled = true;
            AddController(controller);
        }

        public void StartLogging()
        {
            if (controller != null)
                controller.StartLogging(logFilename);
        }

        public void StopLogging()
        {
            if (controller != null)
                controller.StopLogging();
        }

        public void DisableGuidance()
        {
            KSPActionParam param = new KSPActionParam(KSPActionGroup.None, KSPActionType.Activate);
            DisableGuidance(param);
        }

        [KSPAction("Disable BoosterGuidance")]
        public void DisableGuidance(KSPActionParam param)
        {
            if (controller != null)
            {
                RemoveController(controller);
                controller.StopLogging();
            }
            if ((vessel) && vessel.enabled) // extra checks
            {
                vessel.OnFlyByWire -= new FlightInputCallback(Fly);
                vessel.Autopilot.Disable();
            }
            GuiUtils.ScreenMessage(Localizer.Format("#BoosterGuidance_DisabledGuidance"));
            controller = null;
        }

        /// //////////////////////////////////////////////////////////////////////////////////////////////////////
        // Following two methods come from the following thread:
        // https://forum.kerbalspaceprogram.com/index.php?/topic/130485-problems-getting-relative-pitch-and-yaw-from-vessel-heading/

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ActiveVessel"></param>
        /// <param name="targetVector"></param>
        /// <returns></returns>
        private double[] getOffsetFromHeading(Vessel ActiveVessel, Vector3d targetVector)
        {
            Vector3d yawComponent = Vector3d.Exclude(ActiveVessel.GetTransform().forward, targetVector);
            Vector3d yawCross = Vector3d.Cross(yawComponent, ActiveVessel.GetTransform().right);
            double yaw = SignedVectorAngle(yawComponent, ActiveVessel.GetTransform().up, yawCross);

            Vector3d pitchComponent = Vector3d.Exclude(ActiveVessel.GetTransform().right, targetVector);
            Vector3d pitchCross = Vector3d.Cross(pitchComponent, ActiveVessel.GetTransform().forward);
            double pitch = SignedVectorAngle(pitchComponent, ActiveVessel.GetTransform().up, pitchCross);

            if (Math.Abs(yaw) > 90)
            {
                yaw = -yaw;
                // This condition makes sure progradePitch doesn't wrap from -x to 360-x
                if (pitch > 0)
                {
                    pitch = pitch - 180;
                }
                else
                {
                    pitch = pitch + 180;
                }
            }
            return new double[] { pitch, yaw };
        }

        private double SignedVectorAngle(Vector3d referenceVector, Vector3d otherVector, Vector3d normal)
        {
            Vector3d perpVector;
            double angle;
            //Use the geometry object normal and one of the input vectors to calculate the perpendicular vector
            perpVector = Vector3d.Cross(normal, referenceVector);
            //Now calculate the dot product between the perpendicular vector (perpVector) and the other input vector
            angle = Vector3d.Angle(referenceVector, otherVector);
            angle *= Math.Sign(Vector3d.Dot(perpVector, otherVector));

            return angle;
        }

        /// //////////////////////////////////////////////////////////////////////////////////////////////////////

        public void Fly(FlightCtrlState state)
        {
            if (vessel == null)
                return;
            double throttle = last_throttle;
            Vector3d steer = last_steer;
            double steerAngle = 0;
            double minThrust;
            double maxThrust;

            KSPUtils.ComputeMinMaxThrust(vessel, out minThrust, out maxThrust);

            Vector3d tgt_r = vessel.mainBody.GetWorldSurfacePosition(tgtLatitude, tgtLongitude, tgtAlt);

            string msg = "";
            bool landingGear = false;
            bool bailOutLandingBurn = true; // cut thrust if near ground and have too much thrust to reach ground
            double elapsedTimeSinceLastUpdate = vessel.missionTime - last_t;
            if ((elapsedTimeSinceLastUpdate > 0.1) || (vessel.altitude < tgtAlt + 500))
            {
                msg = controller.GetControlOutputs(vessel, vessel.GetTotalMass(), vessel.GetWorldPos3D() - vessel.mainBody.position, vessel.GetObtVelocity(), vessel.transform.up, minThrust, maxThrust,
                controller.vessel.missionTime, vessel.mainBody, false, out throttle, out steer, out landingGear, bailOutLandingBurn, debug);
                last_throttle = throttle;
                last_steer = steer;
                last_t = vessel.missionTime;
                var steerAngleOffsets = getOffsetFromHeading(vessel, steer);
                steerAngle = Math.Sqrt(steerAngleOffsets[0] * steerAngleOffsets[0] + steerAngleOffsets[1] * steerAngleOffsets[1]);
            }

            if ((landingGear) && (!reportedLandingGear))
            {
                KSPUtils.DeployLandingGear(vessel);
                if (vessel == FlightGlobals.ActiveVessel)
                    GuiUtils.ScreenMessage(Localizer.Format("#BoosterGuidance_DeployingLandingGear"));
            }

            if ((msg != "") && (vessel == FlightGlobals.ActiveVessel))
                GuiUtils.ScreenMessage(msg);

            if (vessel.checkLanded())
            {
                DisableGuidance();
                state.mainThrottle = 0;
                return;
            }

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

            // Draw predicted position if controlling that vessel
            if (vessel == FlightGlobals.ActiveVessel)
            {
                double lat, lon, alt;
                // prediction is for position of planet at current time compensating for
                // planet rotation
                vessel.mainBody.GetLatLonAlt(controller.predBodyRelPos + controller.vessel.mainBody.position, out lat, out lon, out alt);
                alt = vessel.mainBody.TerrainAltitude(lat, lon); // Make on surface
                Targets.RedrawPrediction(vessel.mainBody, lat, lon, alt + 1); // 1m above ground

                Targets.DrawSteer(vessel.vesselSize.x * Vector3d.Normalize(steer), null, Color.green);
            }
            if (steerAngle <= degreeRange)
                state.mainThrottle = (float)throttle;
            else
                state.mainThrottle = 0;
            vessel.Autopilot.SAS.lockedMode = false;
            vessel.Autopilot.SAS.SetTargetOrientation(steer, false);
        }

        public string Info()
        {
            // update if present, otherwise use last message
            // e.g. distance from target at landing
            if (controller != null)
                info = controller.info;
            return info;
        }
    }
}