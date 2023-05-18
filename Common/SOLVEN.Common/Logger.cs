using System;
namespace SOLVEN.Common
{
    public static class Logger
    {
        public static void Log(string message)
        {
            string log = string.Format("{0} - {1}", GetDateTimeString(), message);
            Console.WriteLine(log);
            
        }

        public static void Log(string format, params object[] args)
        {
            string message = string.Format(format, args);
            Log(message);
        }

        public static string GetDateTimeString()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss");
        }
    }
}
