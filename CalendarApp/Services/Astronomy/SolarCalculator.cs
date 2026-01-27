namespace CalendarApp.Services.Astronomy;

/// <summary>
/// Solar position calculator using NOAA algorithms.
/// Based on the NOAA Solar Calculator spreadsheet.
/// </summary>
public class SolarCalculator
{
    private const double SunriseAltitude = -0.833; // Standard altitude for sunrise/sunset

    public DateTime CalculateSunrise(DateTime date, double latitude, double longitude)
    {
        return CalculateSunTime(date, latitude, longitude, isSunrise: true);
    }

    public DateTime CalculateSunset(DateTime date, double latitude, double longitude)
    {
        return CalculateSunTime(date, latitude, longitude, isSunrise: false);
    }

    private DateTime CalculateSunTime(DateTime date, double latitude, double longitude, bool isSunrise)
    {
        // Julian Day
        var jd = ToJulianDay(date);

        // Julian Century
        var jc = (jd - 2451545.0) / 36525.0;

        // Sun's geometric mean longitude (degrees)
        var l0 = Mod360(280.46646 + jc * (36000.76983 + 0.0003032 * jc));

        // Sun's mean anomaly (degrees)
        var m = Mod360(357.52911 + jc * (35999.05029 - 0.0001537 * jc));

        // Earth's orbit eccentricity
        var e = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);

        // Sun's equation of center
        var mRad = ToRadians(m);
        var c = Math.Sin(mRad) * (1.914602 - jc * (0.004817 + 0.000014 * jc))
              + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * jc)
              + Math.Sin(3 * mRad) * 0.000289;

        // Sun's true longitude
        var sunLong = l0 + c;

        // Sun's apparent longitude
        var omega = 125.04 - 1934.136 * jc;
        var lambda = sunLong - 0.00569 - 0.00478 * Math.Sin(ToRadians(omega));

        // Mean obliquity of the ecliptic
        var eps0 = 23.0 + (26.0 + (21.448 - jc * (46.8150 + jc * (0.00059 - jc * 0.001813))) / 60.0) / 60.0;

        // Corrected obliquity
        var eps = eps0 + 0.00256 * Math.Cos(ToRadians(omega));

        // Sun's declination
        var sinDec = Math.Sin(ToRadians(eps)) * Math.Sin(ToRadians(lambda));
        var declination = ToDegrees(Math.Asin(sinDec));

        // Equation of time (minutes)
        var y = Math.Tan(ToRadians(eps / 2.0));
        y *= y;
        var eqTime = 4.0 * ToDegrees(
            y * Math.Sin(2.0 * ToRadians(l0))
            - 2.0 * e * Math.Sin(mRad)
            + 4.0 * e * y * Math.Sin(mRad) * Math.Cos(2.0 * ToRadians(l0))
            - 0.5 * y * y * Math.Sin(4.0 * ToRadians(l0))
            - 1.25 * e * e * Math.Sin(2.0 * mRad)
        );

        // Hour angle for sunrise/sunset
        var latRad = ToRadians(latitude);
        var decRad = ToRadians(declination);
        var ha = Math.Acos(
            Math.Cos(ToRadians(90.833)) / (Math.Cos(latRad) * Math.Cos(decRad))
            - Math.Tan(latRad) * Math.Tan(decRad)
        );
        var haDeg = ToDegrees(ha);

        // Solar noon (minutes from midnight)
        var solarNoon = 720.0 - 4.0 * longitude - eqTime;

        // Sunrise/Sunset time (minutes from midnight)
        double sunTime;
        if (isSunrise)
        {
            sunTime = solarNoon - haDeg * 4.0;
        }
        else
        {
            sunTime = solarNoon + haDeg * 4.0;
        }

        // Convert to DateTime
        var hours = (int)(sunTime / 60.0);
        var minutes = (int)(sunTime % 60.0);
        var seconds = (int)((sunTime * 60.0) % 60.0);

        return new DateTime(date.Year, date.Month, date.Day, hours, minutes, seconds);
    }

    public DateTime CalculateVernalEquinox(int year)
    {
        // Jean Meeus algorithm for vernal equinox
        var y = (year - 2000.0) / 1000.0;
        var jde = 2451623.80984
                + 365242.37404 * y
                + 0.05169 * y * y
                - 0.00411 * y * y * y
                - 0.00057 * y * y * y * y;

        return FromJulianDay(ApplyEquinoxCorrections(jde, year));
    }

    public DateTime CalculateAutumnalEquinox(int year)
    {
        var y = (year - 2000.0) / 1000.0;
        var jde = 2451810.21715
                + 365242.01767 * y
                - 0.11575 * y * y
                + 0.00337 * y * y * y
                + 0.00078 * y * y * y * y;

        return FromJulianDay(ApplyEquinoxCorrections(jde, year));
    }

    public DateTime CalculateSummerSolstice(int year)
    {
        var y = (year - 2000.0) / 1000.0;
        var jde = 2451716.56767
                + 365241.62603 * y
                + 0.00325 * y * y
                + 0.00888 * y * y * y
                - 0.00030 * y * y * y * y;

        return FromJulianDay(ApplyEquinoxCorrections(jde, year));
    }

    public DateTime CalculateWinterSolstice(int year)
    {
        var y = (year - 2000.0) / 1000.0;
        var jde = 2451900.05952
                + 365242.74049 * y
                - 0.06223 * y * y
                - 0.00823 * y * y * y
                + 0.00032 * y * y * y * y;

        return FromJulianDay(ApplyEquinoxCorrections(jde, year));
    }

    private double ApplyEquinoxCorrections(double jde, int year)
    {
        // Periodic terms correction (simplified)
        var t = (jde - 2451545.0) / 36525.0;
        var w = 35999.373 * t - 2.47;
        var lambda = 1.0 + 0.0334 * Math.Cos(ToRadians(w)) + 0.0007 * Math.Cos(ToRadians(2.0 * w));

        // Sum of periodic terms (simplified approximation)
        var s = 485 * Math.Cos(ToRadians(324.96 + 1934.136 * t))
              + 203 * Math.Cos(ToRadians(337.23 + 32964.467 * t))
              + 199 * Math.Cos(ToRadians(342.08 + 20.186 * t))
              + 182 * Math.Cos(ToRadians(27.85 + 445267.112 * t))
              + 156 * Math.Cos(ToRadians(73.14 + 45036.886 * t));

        return jde + (0.00001 * s / lambda);
    }

    private static double ToJulianDay(DateTime date)
    {
        var y = date.Year;
        var m = date.Month;
        var d = date.Day + date.Hour / 24.0 + date.Minute / 1440.0 + date.Second / 86400.0;

        if (m <= 2)
        {
            y -= 1;
            m += 12;
        }

        var a = (int)(y / 100.0);
        var b = 2 - a + (int)(a / 4.0);

        return (int)(365.25 * (y + 4716)) + (int)(30.6001 * (m + 1)) + d + b - 1524.5;
    }

    private static DateTime FromJulianDay(double jd)
    {
        var z = (int)(jd + 0.5);
        var f = jd + 0.5 - z;

        int a;
        if (z < 2299161)
        {
            a = z;
        }
        else
        {
            var alpha = (int)((z - 1867216.25) / 36524.25);
            a = z + 1 + alpha - (int)(alpha / 4.0);
        }

        var b = a + 1524;
        var c = (int)((b - 122.1) / 365.25);
        var d = (int)(365.25 * c);
        var e = (int)((b - d) / 30.6001);

        var day = b - d - (int)(30.6001 * e);
        var month = e < 14 ? e - 1 : e - 13;
        var year = month > 2 ? c - 4716 : c - 4715;

        var hours = f * 24.0;
        var hour = (int)hours;
        var minutes = (hours - hour) * 60.0;
        var minute = (int)minutes;
        var second = (int)((minutes - minute) * 60.0);

        return new DateTime(year, month, day, hour, minute, second);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
    private static double Mod360(double degrees) => degrees - 360.0 * Math.Floor(degrees / 360.0);
}
