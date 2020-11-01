﻿// Utility functions that depend on KSP

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoosterGuidance
{
  public class KSPUtils
  {
    // Find Y offset to lowest part from origin of the vessel
    public static double FindLowestPointOnVessel(Vessel vessel)
    {
      Vector3 CoM, up;

      CoM = vessel.localCoM;
      Vector3 bottom = Vector3.zero; // Offset from CoM
      up = FlightGlobals.getUpAxis(CoM); //Gets up axis
      Vector3 pos = vessel.GetWorldPos3D();
      Vector3 distant = pos - 1000 * up; // distant below craft
      double miny = 0;
      foreach (Part p in vessel.parts)
      {
        if (p.collider != null) //Makes sure the part actually has a collider to touch ground
        {
          Vector3 pbottom = p.collider.ClosestPointOnBounds(distant); //Gets the bottom point
          double y = Vector3.Dot(up, pbottom - pos); // relative to centre of vessel
          if (y < miny)
          {
            bottom = pbottom;
            miny = y;
          }
        }
      }
      return miny;
    }

    public static List<ModuleEngines> ComputeMinMaxThrust(Vessel vessel, out double minThrust, out double maxThrust, bool log = false)
    {
      List<ModuleEngines> allEngines = new List<ModuleEngines>();
      int numEngines = 0;
      minThrust = 0;
      maxThrust = 0;
      foreach (Part part in vessel.parts)
      {
        if (log)
          Debug.Log("part=" + part);
        part.isEngine(out List<ModuleEngines> engines);
        foreach (ModuleEngines engine in engines)
        {
          Vector3d relpos = vessel.transform.InverseTransformPoint(part.transform.position);
          float isp = (engine.realIsp > 0) ? engine.realIsp : 280; // guess!
          float pressure = (float)FlightGlobals.getStaticPressure() * 0.01f; // so 1.0 at Kerbin sea level?
          float atmMaxThrust = engine.MaxThrustOutputAtm(true, true, pressure, FlightGlobals.getExternalTemperature());
          if (log)
            Debug.Log("  engine=" + engine + " relpos=" + relpos + " isp=" + isp + " MinThrust=" + engine.GetEngineThrust(isp, 0) + " MaxThrust=" + atmMaxThrust + " operational=" + engine.isOperational);
          if (engine.isOperational)
          {
            minThrust += engine.GetEngineThrust(isp, 0); // can't get atmMinThrust (this ignore throttle limiting but thats ok)
            maxThrust += atmMaxThrust; // this uses throttle limiting and should give vac thrust as pressure/temp specified too
            allEngines.Add(engine);
            numEngines++;
          }
        }
      }
      return allEngines;
    }

    public static double GetCurrentThrust(List<ModuleEngines> allEngines)
    {
      double thrust = 0;
      foreach (ModuleEngines engine in allEngines)
        thrust += engine.GetCurrentThrust();
      return thrust;
    }

    public static double MinHeightAtMinThrust(double y, double vy, double amin, double g)
    {
      double minHeight = 0;
      if (amin < g)
        return -float.MaxValue;
      double tHover = -vy / amin; // time to come to hover
      minHeight = y + vy * tHover + 0.5 * amin * tHover * tHover - 0.5 * g * tHover * tHover;
      return minHeight;
    }

    // Compute engine thrust if one set of symmetrical engines is shutdown
    // (primarily for a Falcon 9 landing to shutdown engines for slow touchdown)
    public static List<ModuleEngines> ShutdownOuterEngines(Vessel vessel, float desiredThrust, bool log = false)
    {
      List<ModuleEngines> shutdown = new List<ModuleEngines>();

      // Find engine parts and sort by closest to centre first
      List<(double, ModuleEngines)> allEngines = new List<(double, ModuleEngines)>();
      foreach (Part part in vessel.GetActiveParts())
      {
        Vector3 relpos = vessel.transform.InverseTransformPoint(part.transform.position);
        part.isEngine(out List<ModuleEngines> engines);
        double dist = Math.Sqrt(relpos.x * relpos.x + relpos.z * relpos.z);
        foreach (ModuleEngines engine in engines)
          allEngines.Add((dist, engine));
      }
      allEngines.Sort();

      // Loop through engines starting a closest to axis
      // Accumulate minThrust, once minThrust exceeds desiredThrust shutdown this and all
      // further out engines
      float minThrust = 0, maxThrust = 0;
      double shutdownDist = float.MaxValue;
      foreach (var engDist in allEngines)
      {
        ModuleEngines engine = engDist.Item2;
        if (engine.isOperational)
        {
          minThrust += engine.GetEngineThrust(engine.realIsp, 0);
          maxThrust += engine.GetEngineThrust(engine.realIsp, 1);
          if (shutdownDist == float.MaxValue)
          {
            if ((minThrust < desiredThrust) && (desiredThrust < maxThrust)) // good amount of thrust
              shutdownDist = engDist.Item1 + 0.1f;
            if (minThrust > desiredThrust)
              shutdownDist = engDist.Item1 - 0.1f;
          }

          if (engDist.Item1 > shutdownDist)
          {
            if (log)
              Debug.Log("[BoosterGuidance] ComputeShutdownMinMaxThrust(): minThrust=" + minThrust + " desiredThrust=" + desiredThrust + " SHUTDOWN");
            engine.Shutdown();
            shutdown.Add(engine);
          }
          else
            if (log)
            Debug.Log("[BoosterGuidance] ComputeShutdownMinMaxThrust(): minThrust=" + minThrust + " desiredThrust=" + desiredThrust + " KEEP");
        }
      }
      Debug.Log(shutdown.Count + " engines shutdown");
      return shutdown;
    }
  }
}
