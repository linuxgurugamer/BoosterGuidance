Forum user @oyster_catcher write this mod Dec, 2020, original thread is here:  https://forum.kerbalspaceprogram.com/index.php?/topic/198760-181-1101-boostguidance-v101/

This mod aims to autonomously guide a Booster to land on a drone ship or the launch site (or anywhere else) using a combination of Boostback, Re-entry Burn, Aerodynamic Descent and Landing Burn, i.e. SpaceX Falcon 9 style.

Select your landing point either by clicking on it, typing the latitude+longitude+altitude or selecting a target (e.g. drone ship).  Its aimed to be reliable and fairly easy to use.  It also works in Realism Overhaul where limited throttle able engines and limited ignitions mean you can't play fast and lose with your engines.  With suitable engines and using the right number active you can achieve in Realism Overhaul which gives a real feeling of satisfaction.

The mod relies on accurately simulating the entire trajectory to landing including burns later in the flight which means guidance should be accurate without the need for any heroic maneuvers as the impact of say the reentry burn is properly factored in. Booster Guidance will continuously monitor the target error and aim to reduce it in boostback, re-entry burn, aerodynamic descent and in the landing burn steering either via engines or via grid-fins depending on the phase of flight.  You can set the steering gains during the flight to give the grid fins more/less to do, and configure the reentry burn via altitude and target velocity.  Some more advanced settings allow you to set safety margins, speed of final descent, at what height to give up steering and more. You can also log the whole flight and plot it to examine in detail what happened and how closely you could copy SpaceX.

Availability

Source code: https://github.com/linuxgurugamer/BoosterGuidance
Download:https://spacedock.info/mod/3018/Booster Guidance Boosted
License: GPLv3
Available via CKAN

New Dependency

Click Through Blocker
ToolbarController
SpaceTuxLibrary
Here's some videos showing it working with a single stage Falcon 9 booster

https://youtu.be/GMeiB5LbwnY


In Realism Overhaul with a 2-stage Falcon 9 rocket where I make a half-hearted attempt to get the 2nd-stage to orbit.

https://youtu.be/6bmgMRa4-6k


Limitations

Clicking the target only works  close to the terrain or there are physical structure (some KSP/Unity Raycast limitation I don't understand)
In Realism Overhaul you may need to manually reduce the gain close to landing to avoid large oscillations
Landing legs deployment is unreliable
Guiding more than one booster simultaneously should be possible but is currently unreliable. My goal is a Falcon Heavy two booster landing. That will be awesome!
It should work on other planets too though the trajectory should be sub-orbital to impact the ground within about 10 minutes as simulation time is limited for CPU reasons
Changes by Linuxgurugamer

Removed need to have FAR installed to compile, now accessing FAR via Reflection
Reorganized repo
Added InstallChecker and AssemblyVersion
Moved DLL into Plugins directory
Added missing Localization folder
Added support for the ClickThroughBlocker
Added support for the ToolbarController
Merged code in BoosterGuidanceApp.cs into the MainWindow.cs 
Added code to hide window controls if target is not set
Increased width of window slightly
Added sample F9 built with Tundra Exploration (will need to be manually moved into save game)
Consolidated all logging functions into a single class
Moved Logging files into <KSPDIR>/Logs/BoosterGuidance/
Fixed issue with NullRefs caused by inconsistent logging calls
Added automatic enabling of RCS
Added automatic triggering of a desired action group
Added limitation of engine thrust vs where vessel is pointing
Moved Enable/disable guidance button to top of buttons, and changed Enable color to green, Disable color to red
