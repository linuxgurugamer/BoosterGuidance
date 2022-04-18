
using KSP.Localization;
using ToolbarControl_NS;
using UnityEngine;
using KSP_Log;


namespace BoosterGuidance
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class InitLog : MonoBehaviour
    {
        public static KSP_Log.Log Log;

        public static void SetLogLevel(int i)
        {
            Log.SetLevel((Log.LEVEL)i);
        }

        protected void Awake()
        {
            Log = new KSP_Log.Log("BoosterGuidance"
#if DEBUG
                , KSP_Log.Log.LEVEL.INFO
#endif
                );
        }
    }

    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class RegisterToolbar : MonoBehaviour
    {
        private void Start()
        {
            ToolbarControl.RegisterMod(MainWindow.MODID, MainWindow.MODNAME);
        }
    }
}
