﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Trajectories;
using Object = UnityEngine.Object;

namespace BoosterGuidance
{
    public interface IEditable
    {
        string text { get; set; }
    }

    //An EditableDouble stores a double value and a text string. The user can edit the string.
    //Whenever the text is edited, it is parsed and the parsed value is stored in val. As a
    //convenience, a multiplier can be specified so that the stored value is actually
    //(multiplier * parsed value). If the parsing fails, the parsed flag is set to false and
    //the stored value is unchanged. There are implicit conversions between EditableDouble and
    //double so that if you are not doing text input you can treat an EditableDouble like a double.
    public class EditableDoubleMult : IEditable
    {
        [Persistent]
        protected double _val;
        public virtual double val
        {
            get { return _val; }
            set
            {
                _val = value;
                _text = (_val / multiplier).ToString();
            }
        }
        public readonly double multiplier;

        public bool parsed;
        [Persistent]
        protected string _text;
        public virtual string text
        {
            get { return _text; }
            set
            {
                _text = value;
                _text = Regex.Replace(_text, @"[^\d+-.]", ""); //throw away junk characters
                double parsedValue;
                parsed = double.TryParse(_text, out parsedValue);
                if (parsed) _val = parsedValue * multiplier;
            }
        }

        public EditableDoubleMult() : this(0) { }

        public EditableDoubleMult(double val, double multiplier = 1)
        {
            this.val = val;
            this.multiplier = multiplier;
            _text = (val / multiplier).ToString();
        }

        public static implicit operator double(EditableDoubleMult x)
        {
            return x.val;
        }
    }

    public class EditableDouble : EditableDoubleMult
    {
        public EditableDouble(double val)
            : base(val)
        {
        }

        public static implicit operator EditableDouble(double x)
        {
            return new EditableDouble(x);
        }
    }

    public class EditableAngle
    {
        [Persistent]
        public EditableDouble degrees = 0;
        [Persistent]
        public EditableDouble minutes = 0;
        [Persistent]
        public EditableDouble seconds = 0;
        [Persistent]
        public bool negative;

        public EditableAngle(double angle)
        {
            angle = MuUtils.ClampDegrees180(angle);

            negative = (angle < 0);
            angle = Math.Abs(angle);
            degrees = (int)angle;
            angle -= degrees;
            minutes = (int)(60 * angle);
            angle -= minutes / 60;
            seconds = Math.Round(3600 * angle);
        }

        public static implicit operator double(EditableAngle x)
        {
            return (x.negative ? -1 : 1) * (x.degrees + x.minutes / 60.0 + x.seconds / 3600.0);
        }

        public static implicit operator EditableAngle(double x)
        {
            return new EditableAngle(x);
        }

        public enum Direction { NS, EW }

        public void DrawEditGUI(Direction direction)
        {
            GUILayout.BeginHorizontal();
            degrees.text = GUILayout.TextField(degrees.text, GUILayout.Width(30));
            GUILayout.Label("°", GUILayout.ExpandWidth(false));
            minutes.text = GUILayout.TextField(minutes.text, GUILayout.Width(30));
            GUILayout.Label("'", GUILayout.ExpandWidth(false));
            seconds.text = GUILayout.TextField(seconds.text, GUILayout.Width(30));
            GUILayout.Label("\"", GUILayout.ExpandWidth(false));
            String dirString = (direction == Direction.NS ? (negative ? "S" : "N") : (negative ? "W" : "E"));
            if (GUILayout.Button(dirString, GUILayout.Width(25))) negative = !negative;
            GUILayout.EndHorizontal();
        }
    }

    public class EditableInt : IEditable
    {
        [Persistent]
        public int val;

        public bool parsed;
        [Persistent]
        public string _text;
        public virtual string text
        {
            get { return _text; }
            set
            {
                _text = value;
                _text = Regex.Replace(_text, @"[^\d+-]", ""); //throw away junk characters
                int parsedValue;
                parsed = int.TryParse(_text, out parsedValue);
                if (parsed) val = parsedValue;
            }
        }

        public EditableInt() : this(0) { }

        public EditableInt(int val)
        {
            this.val = val;
            _text = val.ToString();
        }

        public static implicit operator int(EditableInt x)
        {
            return x.val;
        }

        public static implicit operator EditableInt(int x)
        {
            return new EditableInt(x);
        }
    }

  public static class GuiUtils
  {
    public static void SimpleTextBox(string leftLabel, IEditable ed, string rightLabel = "", float width = 100, GUIStyle rightLabelStyle = null)
    {
      if (rightLabelStyle == null)
        rightLabelStyle = GUI.skin.label;
      GUILayout.BeginHorizontal();
      GUILayout.Label(leftLabel, rightLabelStyle, GUILayout.ExpandWidth(true));
      ed.text = GUILayout.TextField(ed.text, GUILayout.ExpandWidth(true), GUILayout.Width(width));
      GUILayout.Label(rightLabel, GUILayout.ExpandWidth(false));
      GUILayout.EndHorizontal();
    }

    public static void SimpleLabel(string leftLabel, string rightLabel = "")
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label(leftLabel, GUILayout.ExpandWidth(true));
      GUILayout.Label(rightLabel, GUILayout.ExpandWidth(false));
      GUILayout.EndHorizontal();
    }

    public static void SimpleLabelInt(string leftLabel, int rightValue)
    {
      SimpleLabel(leftLabel, rightValue.ToString());
    }

    public static int ArrowSelector(int index, int numIndices, Action centerGuiAction)
    {
      if (numIndices == 0) return index;

      GUILayout.BeginHorizontal();
      if (numIndices > 1 && GUILayout.Button("<", GUILayout.ExpandWidth(false))) index = (index - 1 + numIndices) % numIndices;
      centerGuiAction();
      if (numIndices > 1 && GUILayout.Button(">", GUILayout.ExpandWidth(false))) index = (index + 1) % numIndices;
      GUILayout.EndHorizontal();

      return index;
    }

    public static int ArrowSelector(int index, int modulo, string label, bool expandWidth = true)
    {
      Action drawLabel = () => GUILayout.Label(label, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, stretchWidth = expandWidth });
      return ArrowSelector(index, modulo, drawLabel);
    }


    static public Transform SetUpTransform(CelestialBody body, double latitude, double longitude, double alt)
    {
      // Set up transform so Y is up and (0,0,0) is target position
      Vector3d origin = body.GetWorldSurfacePosition(latitude, longitude, alt);
      Vector3d vEast = body.GetWorldSurfacePosition(latitude, longitude - 0.1, alt) - origin;
      Vector3d vUp = body.GetWorldSurfacePosition(latitude, longitude, alt + 1) - origin;
      // Convert to body co-ordinates
      origin = body.transform.InverseTransformPoint(origin);
      vEast = body.transform.InverseTransformVector(vEast);
      vUp = body.transform.InverseTransformVector(vUp);

      GameObject go = new GameObject();
      // Need to rotation that converts (0,1,0) to vUp in the body transform
      Quaternion quat = Quaternion.FromToRotation(new Vector3(0, 1, 0), vUp);

      Transform o_transform = go.transform;
      o_transform.SetPositionAndRotation(origin, quat);
      o_transform.SetParent(body.transform, false);
      return o_transform;
    }

    public static bool GetMouseHit(CelestialBody body, Rect notRect, out RaycastHit hit)
    {
      hit = new RaycastHit();
      if ((notRect != null) && (notRect.Contains(Input.mousePosition)))
        return false;

      // Cast a ray from screen point
      Ray ray = FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition);
      //Ray ray = PlanetariumCamera.Camera.ScreenPointToRay(Input.mousePosition);
      return Physics.Raycast(ray.origin, ray.direction, out hit, Mathf.Infinity, 1 << 15);
    }

    public static void DrawVector(ref GameObject obj, ref LineRenderer line, Vector3 r_from, Vector3 r_to, Transform a_transform, Color color, bool show)
    {
      if (!show)
      {
        if (obj != null)
        {
          //Destroy(obj);
          obj = null;
          line = null;
        }
        return;
      }

      if (line == null)
      {
        obj = new GameObject("Steer");
        line = obj.AddComponent<LineRenderer>();
      }
      line.transform.parent = a_transform;
      line.useWorldSpace = true;
      line.material = new Material(Shader.Find("KSP/Alpha/Unlit Transparent"));
      line.material.color = color;
      line.startWidth = 0.3f;
      line.endWidth = 0.3f;
      line.positionCount = 2;
      if (a_transform != null)
      {
        line.SetPosition(0, a_transform.TransformPoint(r_from));
        line.SetPosition(1, a_transform.TransformPoint(r_to));
      }
      else
      {
        line.SetPosition(0, r_from);
        line.SetPosition(1, r_to);
      }
    }

    public class TargetingCross : MonoBehaviour
    {
      public const double markerSize = 2.0d; // in meters

      // I find use of statics weird, and the duplication of code in PredictionCross
      // but its the only way I've found to avoid an accumulation of targets in subsequent flights
      // It doesn't seem possible to trap all the deletions needs
      public static double impactLat = 0d;
      public static double impactLon = 0d;
      public static double impactAlt = 0d;
      private Vector3 screen_point;
      private double cross_dist = 0d;
      private Color color = Color.green;

      public Vector3? ImpactPosition { get; internal set; }
      public CelestialBody ImpactBody { get; internal set; }
      public Color Color { get; internal set; }

      public void SetLatLonAlt(CelestialBody body, double lat, double lon, double alt)
      {
        ImpactBody = body;
        impactLat = lat;
        impactLon = lon;
        impactAlt = alt;
        ImpactPosition = ImpactBody.GetWorldSurfacePosition(impactLat, impactLon, impactAlt) - ImpactBody.position;
      }

      public void SetColor(Color a_color)
      {
        color = a_color;
      }

      public void OnPostRender()
      {
        if (ImpactBody == null)
          return;

        if (!enabled)
          return;

        // only draw if visible on the camera
        screen_point = PlanetariumCamera.Camera.WorldToViewportPoint(ImpactPosition.Value + ImpactBody.position);

        // resize marker in respect to distance from camera.
        Vector3d cam_pos = (MapView.MapIsEnabled) ? ScaledSpace.ScaledToLocalSpace(PlanetariumCamera.Camera.transform.position) : (Vector3d)FlightCamera.fetch.mainCamera.transform.position;
        //cam_pos = ScaledSpace.ScaledToLocalSpace(PlanetariumCamera.Camera.transform.position) - ImpactBody.position;
        cam_pos = cam_pos - ImpactBody.position;
        cross_dist = System.Math.Max(Vector3.Distance(cam_pos, ImpactPosition.Value) / 80.0d, 1.0d);

        // draw ground marker at this position
        if (MapView.MapIsEnabled)
          GLUtils.DrawGroundMarker(ImpactBody, impactLat, impactLon, impactAlt, color, MapView.MapIsEnabled, 0, ImpactBody.Radius/50);
        else
          GLUtils.DrawGroundMarker(ImpactBody, impactLat, impactLon, impactAlt, color, MapView.MapIsEnabled, 0, Math.Min(Math.Max(markerSize * cross_dist,10), 15000));
      }
    }

    public class PredictionCross : MonoBehaviour
    {
      public const double markerSize = 2.0d; // in meters

      public static double impactLat = 0d;
      public static double impactLon = 0d;
      public static double impactAlt = 0d;
      private Vector3 screen_point;
      private Vector3 cam_pos;
      private double cross_dist = 0d;
      private Color color = Color.green;

      public Vector3? ImpactPosition { get; internal set; }
      public CelestialBody ImpactBody { get; internal set; }
      public Color Color { get; internal set; }

      public void SetLatLonAlt(CelestialBody body, double lat, double lon, double alt)
      {
        ImpactBody = body;
        impactLat = lat;
        impactLon = lon;
        impactAlt = alt;
        ImpactPosition = ImpactBody.GetWorldSurfacePosition(impactLat, impactLon, impactAlt) - ImpactBody.position;
      }

      public void SetColor(Color a_color)
      {
        color = a_color;
      }

      public void OnPostRender()
      {
        if (ImpactBody == null)
          return;

        if (!enabled)
          return;

        // only draw if visible on the camera
        screen_point = PlanetariumCamera.Camera.WorldToViewportPoint(ImpactPosition.Value + ImpactBody.position);

        // resize marker in respect to distance from camera.
        cam_pos = ScaledSpace.ScaledToLocalSpace(PlanetariumCamera.Camera.transform.position) - ImpactBody.position;
        cross_dist = System.Math.Max(Vector3.Distance(cam_pos, ImpactPosition.Value) / 80.0d, 1.0d);

        // draw ground marker at this position
        if (!MapView.MapIsEnabled)
          GLUtils.DrawGroundMarker(ImpactBody, impactLat, impactLon, impactAlt, color, MapView.MapIsEnabled, 0, System.Math.Min(markerSize * cross_dist, 15000));
        else
          GLUtils.DrawGroundMarker(ImpactBody, impactLat, impactLon, impactAlt, color, MapView.MapIsEnabled, 0);
      }
    }
  }

  public class Coordinates
  {
    public double latitude;
    public double longitude;

    public Coordinates(double latitude, double longitude)
    {
      this.latitude = latitude;
      this.longitude = longitude;
    }

    public static string ToStringDecimal(double latitude, double longitude, bool newline = false, int precision = 3)
    {
      double clampedLongitude = MuUtils.ClampDegrees180(longitude);
      double latitudeAbs = Math.Abs(latitude);
      double longitudeAbs = Math.Abs(clampedLongitude);
      return latitudeAbs.ToString("F" + precision) + "° " + (latitude > 0 ? "N" : "S") + (newline ? "\n" : ", ")
          + longitudeAbs.ToString("F" + precision) + "° " + (clampedLongitude > 0 ? "E" : "W");
    }

    public string ToStringDecimal(bool newline = false, int precision = 3)
    {
      return ToStringDecimal(latitude, longitude, newline, precision);
    }

    public static string ToStringDMS(double latitude, double longitude, bool newline = false)
    {
      double clampedLongitude = MuUtils.ClampDegrees180(longitude);
      return AngleToDMS(latitude) + (latitude > 0 ? " N" : " S") + (newline ? "\n" : ", ")
            + AngleToDMS(clampedLongitude) + (clampedLongitude > 0 ? " E" : " W");
    }

    public string ToStringDMS(bool newline = false)
    {
      return ToStringDMS(latitude, longitude, newline);
    }

    public static string AngleToDMS(double angle)
    {
      int degrees = (int)Math.Floor(Math.Abs(angle));
      int minutes = (int)Math.Floor(60 * (Math.Abs(angle) - degrees));
      int seconds = (int)Math.Floor(3600 * (Math.Abs(angle) - degrees - minutes / 60.0));

      return String.Format("{0:0}° {1:00}' {2:00}\"", degrees, minutes, seconds);
    }

  
  }
}
