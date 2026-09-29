using System.Globalization;

namespace Carshow.Utilities.Convertor
{
    public static class DateConvertor
    {
        private static readonly PersianCalendar _pc = new PersianCalendar();

        public static string ToPersianDateString(this DateTime dateTime)
         {
            int year = _pc.GetYear(dateTime);
            int month = _pc.GetMonth(dateTime);
            int day = _pc.GetDayOfMonth(dateTime);

            return $"{year:0000}/{month:00}/{day:00}";
        }

        public static string ToPersianDateTimeString(this DateTime dateTime)
        {
            int year = _pc.GetYear(dateTime);
            int month = _pc.GetMonth(dateTime);
            int day = _pc.GetDayOfMonth(dateTime);

            int hour = _pc.GetHour(dateTime);
            int minute = _pc.GetMinute(dateTime);
            int second = _pc.GetSecond(dateTime);

            return $"{year:0000}/{month:00}/{day:00} {hour:00}:{minute:00}:{second:00}";
        }
        public static DateTime PersianToGregorian(string persian)
        {
            var parts = persian.Split('/', '-', '.');
            int y = int.Parse(parts[0]);
            int m = int.Parse(parts[1]);
            int d = int.Parse(parts[2]);
            return _pc.ToDateTime(y, m, d, 0, 0, 0, 0);
        }

        public static string GregorianToPersian(DateTime dt)
        {
            int y = _pc.GetYear(dt);
            int m = _pc.GetMonth(dt);
            int d = _pc.GetDayOfMonth(dt);
            return $"{y:0000}/{m:00}/{d:00}";
        }
    }
}
