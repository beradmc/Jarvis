using System;
using System.Globalization;

namespace JarvisCSharp.Utils
{
    public static class DateTimeHelper
    {
        public static string FormatTurkishDateTime(DateTime dateTime)
        {
            var culture = new CultureInfo("tr-TR");
            return dateTime.ToString("dddd, dd MMMM yyyy HH:mm:ss", culture);
        }

        public static string GetCurrentTurkishDateTime()
        {
            return FormatTurkishDateTime(DateTime.Now);
        }

        public static string GetCurrentTurkishDate()
        {
            var culture = new CultureInfo("tr-TR");
            return DateTime.Now.ToString("dddd, dd MMMM yyyy", culture);
        }

        public static string GetCurrentTurkishTime()
        {
            var culture = new CultureInfo("tr-TR");
            return DateTime.Now.ToString("HH:mm:ss", culture);
        }
    }
}
