using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BoosterGuidance.InitLog;

namespace BoosterGuidance
{
    public static class Utils
    {
        static System.IO.StreamWriter actual = null;
        static System.IO.StreamWriter free = null;
        static System.IO.StreamWriter unset = null;
        static System.IO.StreamWriter simuate = null;
        public enum LogType { none, actual, free, unset, simuate };

        const string LOGDIR = "Logs/BoosterGuidance/";

        static bool loggingActive = false;
        static public bool LoggingActive { get { return loggingActive; } }

        static public void StartLogging(string shipName)
        {
            if (!loggingActive)
            {
                string LogsPath = KSPUtil.ApplicationRootPath + "Logs";
                if (!Directory.Exists(LogsPath))
                    Directory.CreateDirectory(LogsPath);
                if (!Directory.Exists(KSPUtil.ApplicationRootPath + LOGDIR))
                    Directory.CreateDirectory(KSPUtil.ApplicationRootPath + LOGDIR);

                actual = new System.IO.StreamWriter(LOGDIR + shipName + ".Actual.dat");
                free = new System.IO.StreamWriter(LOGDIR + shipName + "..Free.dat");
                unset = new System.IO.StreamWriter(LOGDIR + shipName + ".Simulate.Unset.dat");
                simuate = new System.IO.StreamWriter(LOGDIR + shipName + ".Simulate.dat");
                loggingActive = true;
            }
        }

        static public void EndLogging()
        {
            if (loggingActive)
            {
                actual.Close();
                free.Close();
                unset.Close();
                simuate.Close();
                loggingActive = false;
            }
        }
        static public void Log(LogType logtype, string str)
        {
            if (loggingActive)
            {
                switch (logtype)
                {
                    case LogType.actual: actual.WriteLine(str); break;
                    case LogType.free: free.WriteLine(str); break;
                    case LogType.unset: unset.WriteLine(str); break;
                    case LogType.simuate: simuate.WriteLine(str); break;
                }
            }
        }
    }
}
