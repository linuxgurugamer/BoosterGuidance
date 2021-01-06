// Booster Landing Controller
//   - does boostback, coasting, re-entry burn, and final descent
//   - ideally this doesn't depend on KSP but thats not been completely possiblle

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP;

namespace BoosterGuidance
{
  public enum BLControllerPhase
  {
    Unset,
    BoostBack,
    Coasting,
    ReentryBurn,
    AeroDescent,
    LandingBurn
  }

  public class BLController : Controller
  {
    // Public parameters
    public double touchdownSpeed = 3;
    public double aeroDescentMaxAoA = 20;
    public double aeroDescentSteerKp = 0.01f;
    public double reentryBurnAlt = 70000;
    public double reentryBurnTargetSpeed = 700;
    public double reentryBurnSteerKp = 0.005f;
    public double reentryBurnMaxAoA = 20;
    public double landingBurnHeight = 0; // Maximum altitude to enable powered descent
    public double landingBurnSteerKp = 0.01f;
    public double landingBurnMaxAoA = 10;
    private double suicideFactor = 0.9f;
    private double lowestY = 0;
    private PIDclamp pid_reentry = new PIDclamp("reentrySteer", 1, 0, 0, 10);
    private PIDclamp pid_aero = new PIDclamp("aeroSteer", 1, 0, 0, 10);
    private PIDclamp pid_landing = new PIDclamp("landingSteer", 1, 0, 0, 10);
    public double igniteDelay = 3; // ignite engines this many seconds early
    public double simulationsPerSec = 10;
    public bool deployLandingGear = true;
    public double deployLandingGearHeight = 500;
    public double touchdownMargin = 30; // use touchdown speed from this height
    public double noSteerHeight = 200;
    public bool useFAR = false;

    // Private parameters
    private double minError = double.MaxValue;
    private System.IO.StreamWriter fp = null;
    private double logStartTime;
    private double logLastTime = 0;
    private double logInterval = 0.1f;
    private Transform logTransform;
    
    private Trajectories.VesselAerodynamicModel aeroModel = null;
    private double landingBurnAMax = 100; // amax when landing burn alt computed (so we can recalc if needed)
    private String logFilename; // basename for logging of several files

    // Outputs
    public Vector3d predWorldPos = Vector3d.zero;
    public BLControllerPhase phase = BLControllerPhase.Unset;
    public List<ModuleEngines> landingBurnEngines = null;
    public double steerGain = 0;
    public double elapsed_secs = 0;

    // Cache previous values - only calculate new at log interval
    private double lastt = 0;
    private double lastThrottle = 0;
    private Vector3d lastSteer = Vector3d.zero;

    public BLController()
    {
    }

    ~BLController()
    {
      StopLogging();
    }

    public BLController(BLController v) : base()
    {
      phase = v.phase;
      vessel = v.vessel;
      tgtLatitude = v.tgtLatitude;
      tgtLongitude = v.tgtLongitude;
      tgtAlt = v.tgtAlt;
      lowestY = v.lowestY;
      reentryBurnAlt = v.reentryBurnAlt;
      reentryBurnMaxAoA = v.reentryBurnMaxAoA;
      reentryBurnSteerKp = v.reentryBurnSteerKp;
      reentryBurnTargetSpeed = v.reentryBurnTargetSpeed;
      aeroModel = v.aeroModel;
      aeroDescentSteerKp = v.aeroDescentSteerKp;
      landingBurnHeight = v.landingBurnHeight;
      landingBurnAMax = v.landingBurnAMax;
      landingBurnSteerKp = v.landingBurnSteerKp;
      landingBurnEngines = v.landingBurnEngines;
      suicideFactor = v.suicideFactor;
      targetError = v.targetError;
      igniteDelay = v.igniteDelay;
      noSteerHeight = v.noSteerHeight;
    }

    public BLController(Vessel a_vessel, bool useFAR=false) : base()
    {
      AttachVessel(a_vessel, useFAR);
    }

    public void AttachVessel(Vessel a_vessel, bool useFAR=false)
    {
      vessel = a_vessel;
      aeroModel = Trajectories.AerodynamicModelFactory.GetModel(vessel, vessel.mainBody, useFAR);
      lowestY = KSPUtils.FindLowestPointOnVessel(vessel);
    }

    public void InitReentryBurn(float kP, float maxAngle, double alt, double tgtSpeed)
    {
      reentryBurnSteerKp = kP;
      reentryBurnMaxAoA = maxAngle;
      reentryBurnAlt = alt;
      reentryBurnTargetSpeed = tgtSpeed;
    }

    public void InitAeroDescent(float kP, float maxAngle)
    {
      pid_aero = new PIDclamp("aeroSteer", kP, 0, 0, maxAngle);
      reentryBurnMaxAoA = maxAngle;
    }

    public void InitLandingBurn(float kP, float maxAngle)
    {
      pid_landing = new PIDclamp("landingSteer", kP, 0, 0, maxAngle);
      landingBurnMaxAoA = maxAngle;
    }

    public String SetLandingBurnEngines()
    {
      landingBurnEngines = KSPUtils.GetActiveEngines(vessel);
      if (landingBurnEngines.Count == 0)
        return "current";
      else
        return landingBurnEngines.Count.ToString();
    }

    public void SetTarget(double latitude, double longitude, double alt)
    {
      tgtLatitude = latitude;
      tgtLongitude = longitude;
      tgtAlt = alt;
    }

    public String UnsetLandingBurnEngines()
    {
      landingBurnEngines = null;
      return "current";
    }

    public String GetLandingBurnEngineString()
    {
      if ((vessel == null) || (landingBurnEngines == null))
        return "current";
      List<string> flags = new List<string>();
      bool set = false;
      foreach (var engine in KSPUtils.GetAllEngines(vessel))
      {
        if (landingBurnEngines.Contains(engine))
        {
          flags.Add("1");
          set = true;
        }
        else
          flags.Add("0");
      }
      if (set)
        return String.Join(",", flags.ToArray());
      else
        return "current";
    }

    public void SetLandingBurnEnginesFromString(string s)
    {
      if (vessel == null)
        return;
      if (s == "current")
        landingBurnEngines = null;
      else
      {
        string[] flags = s.Split(',');
        List<ModuleEngines> engines = KSPUtils.GetAllEngines(vessel);
        if (flags.Length != engines.Count)
        {
          Debug.Log("[BoosterGuidance] Vessel " + vessel.name + " has " + engines.Count + " but landing burn engines list has length " + flags.Length);
          landingBurnEngines = null;
        }
        for (int i = 0; i < flags.Length; i++)
        {
          if (flags[i] == "1")
            landingBurnEngines.Add(engines[i]);
          else if (flags[i] != "0")
            Debug.Log("[BoosterGuidance] Found invalid string '" + s + "' for landingBurnEngines. Expected a boolean list for active engines. e.g. 0,0,1,1,0 or current");
        }
      }
    }

    public void SetPhase(BLControllerPhase a_phase)
    {
      minError = double.MaxValue; // reset so boostback doesn't give up

      // Current phase unset and specified phase unset then find out suitable phase
      // otherwise use already set phase
      if ((phase == BLControllerPhase.Unset) && (a_phase == BLControllerPhase.Unset))
      {
        if (vessel.altitude > reentryBurnAlt)
          phase = BLControllerPhase.BoostBack;
        else
          phase = BLControllerPhase.AeroDescent;
      }
      else
        phase = a_phase;
      if (fp != null)
        LogSimulation();
    }

    public override string PhaseStr()
    {
      if (phase == BLControllerPhase.BoostBack)
        return "Boostback";
      if (phase == BLControllerPhase.Coasting)
        return "Coasting";
      if (phase == BLControllerPhase.ReentryBurn)
        return "Re-entry Burn";
      if (phase == BLControllerPhase.AeroDescent)
        return "Aero Descent";
      if (phase == BLControllerPhase.LandingBurn)
        return "Landing Burn";
      return "Unset";
    }

    public void LogSimulation()
    {
      String name = PhaseStr().Replace(" ", "_");
      Vector3d tgt_r = vessel.mainBody.GetWorldSurfacePosition(tgtLatitude, tgtLongitude, tgtAlt);
      BLController tc = new BLController(this);
      Simulate.ToGround(tgtAlt, vessel, aeroModel, vessel.mainBody, tc, tgt_r, out targetT, logFilename + ".Simulate." + name + ".dat", logTransform, vessel.missionTime - logStartTime);
      Simulate.ToGround(tgtAlt, vessel, aeroModel, vessel.mainBody, null, tgt_r, out targetT, logFilename + ".Simulate.Free.dat", logTransform, vessel.missionTime - logStartTime);
    }


    // Note: filename is the basename from which we appent
    //  .actual.dat
    //  .after_boostback.dat
    public void StartLogging(string filename)
    {
      if ((filename != "") && (fp == null))
      {
        fp = new System.IO.StreamWriter(filename+".Actual.dat");
        logFilename = filename;
        logTransform = Targets.SetUpTransform(vessel.mainBody, tgtLatitude, tgtLongitude, tgtAlt);
        fp.WriteLine("time phase x y z vx vy vz ax ay az att_err amin amax steer_gain target_error totalMass");
        logStartTime = vessel.missionTime;
        LogSimulation();
      }
    }

    public void StopLogging()
    {
      if (fp != null)
      {
        fp.Close();
      }
      fp = null;
    }

    // Vector adjustment to steer vector (must be descending)
    // If gain is positive steer aerodynamically (towards target)
    // otherwise steer to fire thrust in opposite direction
    private Vector3d GetSteerAdjust(Vector3d tgtError, double gain, double maxAoA)
    {
      Vector3d adj = tgtError * gain / 45.0;
      double maxAdj = maxAoA / 45.0; // approx as 45 degress sideways component so unit vector is 1
      if (adj.magnitude > maxAdj)
        adj = Vector3d.Normalize(adj) * maxAdj;
      return adj;
    }

    private double CalculateSteerGain(double throttle, Vector3d vel_air, Vector3d r, double y, double totalMass)
    {
      // TODO - Get this working for <45 degrees, ideally just 1 degree, but caching in aero forces is quantizing the AoA I think
      Vector3d Faero = aeroModel.GetForces(vessel.mainBody, r, vel_air, Math.PI); // 180 degrees (retrograde);
      // Find lift by just considering change in aero force vector
      Vector3d Faero2 = aeroModel.GetForces(vessel.mainBody, r, vel_air, Math.PI - (15 * Math.PI / 180));
      // Calculate lift vector orthogonal to the drag vector when retrograde
      Vector3d Flift = Vector3d.Project(Faero2, Vector3d.Normalize(Faero));

      double sideFA = Flift.magnitude; // aero dynamic lift at 5 degrees AoA
      double thrust = minThrust + throttle * (maxThrust - minThrust);
      double sideFT = thrust * Math.Sin(15 * Math.PI / 180); // sideways component of thrust at 15 degrees
      double gain = 0;
      // When its a toss up whether thrust or aerodynamic steering is better gain can become very high or infinite
      // Dont steer in the region since the gain estimate might also have the wrong sign
      if (Math.Abs(sideFA - sideFT) > 0)
        gain = totalMass / (sideFA - sideFT);
      //Debug.Log("[BoosterGuidance] sideFA(45 degrees)=" + sideFA + " sideFT(45 degrees)=" + sideFT + " throttle=" + throttle + " alt="+ vessel.altitude + " gain=" + gain);

      return Math.Sign(gain);
    }

    public override void GetControlOutputs(
                    Vessel vessel,
                    double totalMass, 
                    Vector3d r, // world pos
                    Vector3d v, // world velocity
                    Vector3d att, // attitude
                    double alt, // altitude
                    double minThrust, double maxThrust,
                    double t,
                    CelestialBody body,
                    Vector3d tgt_r, // target in world co-ordinates
                    bool simulate, // if true just go retrograde (no corrections)
                    out double throttle, out Vector3d steer,
                    out bool landingGear, // true if landing gear requested (stays true)
                    bool bailOutLandingBurn = false) // if set too true on RO set throttle=0 if thrust > gravity at landing

    {
      // height of lowest point with additional margin
      double y = alt - tgtAlt - touchdownMargin + lowestY;
      Vector3d up = Vector3d.Normalize(r - body.position);
      Vector3d vel_air = v - body.getRFrmVel(r);
      double vy = Vector3d.Dot(vel_air, up); // TODO - Or vel_air?
      double throttleGain = 0;
      double amin = minThrust / totalMass;
      double amax = maxThrust / totalMass;
      float minThrottle = 0.01f;
      BLControllerPhase lastPhase = phase;

      // Actual height from all objects when low in case we land in the wrong place
      if ((y < 500) && (!simulate))
      {
        y = vessel.GetHeightFromSurface() - touchdownMargin + lowestY;
        //if (!simulate)
        //  Debug.Log("[BoosterGuidance] using heightFromSurface y=" + y);
      }

      System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
      timer.Start();

      steer = lastSteer;
      throttle = lastThrottle;
      landingGear = (y < deployLandingGearHeight) && (deployLandingGear);

      double g = FlightGlobals.getGeeForceAtPosition(r).magnitude;

      if (amax > 0) // check in case out of fuel or something
        throttleGain = 5 / (amax - amin); // cancel velocity over 1.25 seconds

      if ((t < lastt + 1.0/Math.Max(simulationsPerSec,0.1)) && (y>500))
        return;

      // No thrust - retrograde relative to surface (default and Coasting phase
      throttle = 0;
      steer = -Vector3d.Normalize(vel_air);

      Vector3d error = Vector3d.zero;
      attitudeError = 0;
      if (!simulate)
      {
        BLController tc = new BLController(this);
        // Only simulate phases beyond boostback so boostback minimizes error and simulate includes just
        // the remaining phases and doesn't try to redo reentry burn for instance
        if (phase == BLControllerPhase.BoostBack)
          tc.phase = BLControllerPhase.Coasting;
        predWorldPos = Simulate.ToGround(tgtAlt, vessel, aeroModel, body, tc, tgt_r, out targetT);
        landingBurnHeight = tc.landingBurnHeight; // Update from simulation
        tgt_r = body.GetWorldSurfacePosition(tgtLatitude, tgtLongitude, tgtAlt);
        error = predWorldPos - tgt_r;
        error = Vector3d.Exclude(up, error); // make sure error is horizontal
        targetError = error.magnitude;
      }

      // BOOSTBACK
      if (phase == BLControllerPhase.BoostBack)
      {
        // Aim to close max of 20% of error in 1 second
        steer = -Vector3d.Normalize(error);
        // Safety checks in inverse cosine
        attitudeError = HGUtils.angle_between(att, steer);
        double dv = error.magnitude / targetT; // estimated delta V needed
        double ba = 0;
        if (attitudeError < 10+dv*0.5) // more accuracy needed when close to target
          ba = Math.Max(0.3 * dv, 10 / targetT);
        throttle = Mathf.Clamp((float)((ba - amin) / (0.01 + amax - amin)), minThrottle, 1);
        // Stop if error has grown significantly
        if ((targetError > minError * 1.5) || (targetError < 10))
        {
          if (targetError < 5000) // check if error changes dramatically but still far from target
            phase = BLControllerPhase.Coasting;
        }
        minError = Math.Min(targetError, minError);
        if ((y < reentryBurnAlt) && (vy < 0)) // falling
          phase = BLControllerPhase.ReentryBurn;
      }

      // COASTING
      if (phase == BLControllerPhase.Coasting)
      {
        if ((y < reentryBurnAlt) && (vy < 0))
          phase = BLControllerPhase.ReentryBurn;
      }

      // Set default gains for steering
      steerGain = 0;

      // RE-ENTRY BURN
      if (phase == BLControllerPhase.ReentryBurn)
      {
        
        double errv = vel_air.magnitude - reentryBurnTargetSpeed;

        if (errv > 0)
        {
          double newThrottle = HGUtils.LinearMap((double)y, (double)reentryBurnAlt, (double)reentryBurnAlt - 8000, 0.1, 1);
          double da = g + Math.Max(errv * 0.4,10); // attempt to cancel 40% of extra velocity in 1 second and min of 10m/s/s
          newThrottle = (da - amin)/(0.01 + amax - amin) ; 
          throttle = Math.Max(minThrottle, newThrottle);
        }
        else
          phase = BLControllerPhase.AeroDescent;

        if (!simulate)
        {
          pid_reentry.kp = reentryBurnSteerKp * CalculateSteerGain(throttle, vel_air, r, y, totalMass);
          steerGain = pid_reentry.kp;
          double ang = pid_reentry.Update(error.magnitude, Time.deltaTime);
          steer = -Vector3d.Normalize(vel_air) + GetSteerAdjust(error, ang, reentryBurnMaxAoA);
        }
      }

      // desired velocity - used in AERO DESCENT and LANDING BURN
      double dvy = -touchdownSpeed;
      double av = Math.Max(0.1, landingBurnAMax - g);
      
      // AERO DESCENT
      if (phase == BLControllerPhase.AeroDescent)
      {
        pid_aero.kp = aeroDescentSteerKp * CalculateSteerGain(0, vel_air, r, y, totalMass);
        steerGain = pid_aero.kp;
        double ang = pid_aero.Update(error.magnitude, Time.deltaTime);
        steer = -Vector3d.Normalize(vel_air) + GetSteerAdjust(error, ang, aeroDescentMaxAoA);

        double landingMinThrust, landingMaxThrust;
        KSPUtils.ComputeMinMaxThrust(vessel, out landingMinThrust, out landingMaxThrust, false, landingBurnEngines);
        double newLandingBurnAMax = landingMaxThrust / totalMass;

        if (Math.Abs(landingBurnAMax - newLandingBurnAMax) > 0.02)
        {
          landingBurnAMax = landingMaxThrust / totalMass; // update so we don't continually recalc
          landingBurnHeight = Simulate.CalculateLandingBurnHeight(tgtAlt, r, v, vessel, totalMass, landingMinThrust, landingMaxThrust, aeroModel, vessel.mainBody, this, 100, "landing_burn.dat");
        }
        if (y - vel_air.magnitude * igniteDelay <= landingBurnHeight) // Switch to landing burn N secs earlier to allow RO engine start up time
        {
          lowestY = KSPUtils.FindLowestPointOnVessel(vessel);
          phase = BLControllerPhase.LandingBurn;
        }
        // Interpolate to avoid rapid swings
        steer = Vector3d.Normalize(att * 0.75 + steer * 0.25); // simple interpolation to damp rapid oscillations

        //if (!simulate)
        //  Debug.Log("[BoosterGuidance] vel_air=" + Vector3d.Normalize(vel_air).magnitude + " adj=" + GetSteerAdjust(error, ang, aeroDescentMaxAoA).magnitude + " ang="+ang+" maxAoA="+aeroDescentMaxAoA);
      }

      // LANDING BURN (suicide burn)
      if (phase == BLControllerPhase.LandingBurn)
      {
        av = Math.Max(0.1, landingBurnAMax - g); // wrong on first iteration
        if (y > 0)
          dvy = -Math.Sqrt((1 + suicideFactor) * av * y) - touchdownSpeed; // Factor is 2 for perfect suicide burn, lower for margin and hor vel
        if (amax > 0)
        {
          double err_dv = vy - dvy; // +ve is velocity too high
          double da = g - (5 * err_dv); // required accel to change vy, cancel out g (only works if vertical)
          throttle = HGUtils.Clamp((da - amin) / (0.01 + amax - amin), minThrottle, 1);
          // compensate if not vertical as need more vertical component of thrust
          throttle = throttle / Math.Max(0.1, Vector3.Dot(att, up));
        }
        if ((!simulate) && (y > noSteerHeight))
        {
          double ang;
          // If almost no throttle then still use aero steering gain
          pid_landing.kp = landingBurnSteerKp * CalculateSteerGain(throttle, vel_air, r, y, totalMass);
          steerGain = pid_landing.kp;
          ang = pid_landing.Update(error.magnitude, Time.deltaTime);
          // Steer retrograde with added up component to damp oscillations at slow speed near ground
          steer = -Vector3d.Normalize(vel_air - 20*up) + GetSteerAdjust(error, ang, landingBurnMaxAoA);
        }
        else
        {
          // Just cancel velocity with significant upwards component to stay upright
          steer = -Vector3d.Normalize(vel_air - 20 * up);
        }
    
        // Decide to shutdown engines for final touch down? (within 3 secs)
        // Criteria should be if
        // height
        double minHeight = KSPUtils.MinHeightAtMinThrust(y, vy, amin, g);
        // Criteria for shutting down engines
        // - we could not reach ground at minimum thrust (would ascend)
        // - falling less than 20m/s (otherwise can decide to shutdown engines when still high and travelling fast)
        // This is particulary done to stop the simulation never hitting the ground and making pretty circles through the sky
        // until the maximum time is exceeded. The predicted impact position will vary widely and this was incur a lot of time to calculate
        bool cant_reach_ground = (minHeight > 0) && (vy > -30);
        if ((cant_reach_ground) && (bailOutLandingBurn))
          throttle = 0;

        // Interpolate to avoid rapid swings
        steer = Vector3d.Normalize(att * 0.75 + steer * 0.25); // simple interpolation to damp rapid oscillations
      }

      // Logging
      //Debug.Log("[BoosterGuidance] Check log");
      if (fp != null)
      {
        //Debug.Log("[BoosterGuidance] Check log t="+t+" logLastTime="+logLastTime+" logInterval="+logInterval);
        //if (t > logLastTime + logInterval)
        //{
        Vector3d a = att * (amin + throttle * (amax - amin)); // this assumes engine is ignited though
        Vector3d tr = logTransform.InverseTransformPoint(r);
        Vector3d tv = logTransform.InverseTransformVector(vel_air);
        Vector3d ta = logTransform.InverseTransformVector(a);
        fp.WriteLine("{0:F1} {1} {2:F1} {3:F1} {4:F1} {5:F1} {6:F1} {7:F1} {8:F1} {9:F1} {10:F1} {11:F1} {12:F1} {13:F1} {14:F3} {15:F1} {16:F2}", t - logStartTime, phase, tr.x, tr.y, tr.z, tv.x, tv.y, tv.z, a.x, a.y, a.z, attitudeError, amin, amax, steerGain, targetError, totalMass);
        logLastTime = t;
        //}
      }

      lastt = t;
      steer = Vector3d.Normalize(steer);
      attitudeError = HGUtils.angle_between(att, steer);

      throttle = HGUtils.Clamp(throttle, 0, 1);

      // Log simulate to ground when phase changes
      // So the logging is done at the start of the new phase
      if ((lastPhase != phase) && (fp != null))
        LogSimulation();

      // Cache
      lastSteer = steer;
      lastThrottle = throttle;

      elapsed_secs = timer.ElapsedMilliseconds * 0.001;

      // Set info message
      if (vessel.checkLanded())
        info = string.Format("Landed {0:F1}m from target", targetError);
      else
        info = string.Format("Err: {0:F0}m {1:F0}° Time: {2:F0}s [{3:F0}ms]", targetError, attitudeError, targetT, elapsed_secs * 1000);
    }
  }  
}
