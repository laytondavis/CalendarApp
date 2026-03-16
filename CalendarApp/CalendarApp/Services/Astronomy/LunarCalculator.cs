using CalendarApp.Services.Interfaces;

namespace CalendarApp.Services.Astronomy;

/// <summary>
/// Lunar phase calculator using Jean Meeus algorithms.
/// </summary>
public class LunarCalculator
{
    /// <summary>
    /// Calculates the next new moon after a given date.
    /// </summary>
    public DateTime CalculateNextNewMoon(DateTime afterDate)
    {
        // Use an integer lunation counter starting one step before the approximation.
        // Keeping k as an integer throughout the loop means each step is exactly one
        // synodic month — no fractional-ceiling bug that skips lunations.
        var k = (long)Math.Floor(ApproximateLunationNumber(afterDate)) - 1;
        var newMoon = CalculateNewMoon(k);

        while (newMoon <= afterDate)
        {
            k++;
            newMoon = CalculateNewMoon(k);
        }

        return newMoon;
    }

    /// <summary>
    /// Calculates the previous new moon before a given date.
    /// </summary>
    public DateTime CalculatePreviousNewMoon(DateTime beforeDate)
    {
        // Start one lunation after the approximation (symmetric to CalculateNextNewMoon).
        // When the correction terms push the actual new moon past the mean, ApproximateLunationNumber
        // can return k just below an integer, causing Math.Floor to land on the wrong lunation.
        var k = (long)Math.Floor(ApproximateLunationNumber(beforeDate)) + 1;
        var newMoon = CalculateNewMoon(k);

        while (newMoon >= beforeDate)
        {
            k--;
            newMoon = CalculateNewMoon(k);
        }

        return newMoon;
    }

    /// <summary>
    /// Gets the lunar phase for a given date.
    /// </summary>
    public LunarPhase GetLunarPhase(DateTime date)
    {
        var illumination = GetLunarIllumination(date);
        var age = GetLunarAge(date);
        var synodicMonth = 29.530588853;

        // Determine phase based on age in the synodic month
        var phaseRatio = age / synodicMonth;

        if (phaseRatio < 0.0625) return LunarPhase.NewMoon;
        if (phaseRatio < 0.1875) return LunarPhase.WaxingCrescent;
        if (phaseRatio < 0.3125) return LunarPhase.FirstQuarter;
        if (phaseRatio < 0.4375) return LunarPhase.WaxingGibbous;
        if (phaseRatio < 0.5625) return LunarPhase.FullMoon;
        if (phaseRatio < 0.6875) return LunarPhase.WaningGibbous;
        if (phaseRatio < 0.8125) return LunarPhase.ThirdQuarter;
        if (phaseRatio < 0.9375) return LunarPhase.WaningCrescent;
        return LunarPhase.NewMoon;
    }

    /// <summary>
    /// Gets the lunar illumination percentage (0-100).
    /// </summary>
    public double GetLunarIllumination(DateTime date)
    {
        var age = GetLunarAge(date);
        var synodicMonth = 29.530588853;

        // Calculate illumination using cosine function
        var phase = age / synodicMonth;
        var illumination = (1.0 - Math.Cos(2.0 * Math.PI * phase)) / 2.0;

        return illumination * 100.0;
    }

    /// <summary>
    /// Gets the age of the moon in days since the last new moon.
    /// </summary>
    private double GetLunarAge(DateTime date)
    {
        var prevNewMoon = CalculatePreviousNewMoon(date.AddDays(1)); // Add 1 day to handle edge case
        return (date - prevNewMoon).TotalDays;
    }

    /// <summary>
    /// Calculates the approximate lunation number for a date.
    /// </summary>
    private double ApproximateLunationNumber(DateTime date)
    {
        // Reference: January 6, 2000 was a new moon (k=0)
        var referenceDate = new DateTime(2000, 1, 6, 18, 14, 0);
        var synodicMonth = 29.530588853;

        return (date - referenceDate).TotalDays / synodicMonth;
    }

    /// <summary>
    /// Calculates the date and time of a new moon for a given lunation number.
    /// Uses Jean Meeus' algorithm from "Astronomical Algorithms".
    /// </summary>
    private DateTime CalculateNewMoon(double k)
    {
        // Time in Julian centuries from J2000.0
        var t = k / 1236.85;

        // Mean phase of the Moon
        var jde = 2451550.09766
                + 29.530588861 * k
                + 0.00015437 * t * t
                - 0.000000150 * t * t * t
                + 0.00000000073 * t * t * t * t;

        // Sun's mean anomaly
        var m = ToRadians(2.5534
                + 29.10535670 * k
                - 0.0000014 * t * t
                - 0.00000011 * t * t * t);

        // Moon's mean anomaly
        var mp = ToRadians(201.5643
                + 385.81693528 * k
                + 0.0107582 * t * t
                + 0.00001238 * t * t * t
                - 0.000000058 * t * t * t * t);

        // Moon's argument of latitude
        var f = ToRadians(160.7108
                + 390.67050284 * k
                - 0.0016118 * t * t
                - 0.00000227 * t * t * t
                + 0.000000011 * t * t * t * t);

        // Longitude of ascending node
        var omega = ToRadians(124.7746
                - 1.56375588 * k
                + 0.0020672 * t * t
                + 0.00000215 * t * t * t);

        // Correction terms for new moon
        var e = 1.0 - 0.002516 * t - 0.0000074 * t * t;

        var correction =
            - 0.40720 * Math.Sin(mp)
            + 0.17241 * e * Math.Sin(m)
            + 0.01608 * Math.Sin(2.0 * mp)
            + 0.01039 * Math.Sin(2.0 * f)
            + 0.00739 * e * Math.Sin(mp - m)
            - 0.00514 * e * Math.Sin(mp + m)
            + 0.00208 * e * e * Math.Sin(2.0 * m)
            - 0.00111 * Math.Sin(mp - 2.0 * f)
            - 0.00057 * Math.Sin(mp + 2.0 * f)
            + 0.00056 * e * Math.Sin(2.0 * mp + m)
            - 0.00042 * Math.Sin(3.0 * mp)
            + 0.00042 * e * Math.Sin(m + 2.0 * f)
            + 0.00038 * e * Math.Sin(m - 2.0 * f)
            - 0.00024 * e * Math.Sin(2.0 * mp - m)
            - 0.00017 * Math.Sin(omega)
            - 0.00007 * Math.Sin(mp + 2.0 * m)
            + 0.00004 * Math.Sin(2.0 * mp - 2.0 * f)
            + 0.00004 * Math.Sin(3.0 * m)
            + 0.00003 * Math.Sin(mp + m - 2.0 * f)
            + 0.00003 * Math.Sin(2.0 * mp + 2.0 * f)
            - 0.00003 * Math.Sin(mp + m + 2.0 * f)
            + 0.00003 * Math.Sin(mp - m + 2.0 * f)
            - 0.00002 * Math.Sin(mp - m - 2.0 * f)
            - 0.00002 * Math.Sin(3.0 * mp + m)
            + 0.00002 * Math.Sin(4.0 * mp);

        jde += correction;

        // Additional corrections (planetary terms - simplified)
        var a1 = ToRadians(299.77 + 0.107408 * k - 0.009173 * t * t);
        var a2 = ToRadians(251.88 + 0.016321 * k);
        var a3 = ToRadians(251.83 + 26.651886 * k);
        var a4 = ToRadians(349.42 + 36.412478 * k);
        var a5 = ToRadians(84.66 + 18.206239 * k);
        var a6 = ToRadians(141.74 + 53.303771 * k);
        var a7 = ToRadians(207.14 + 2.453732 * k);
        var a8 = ToRadians(154.84 + 7.306860 * k);
        var a9 = ToRadians(34.52 + 27.261239 * k);
        var a10 = ToRadians(207.19 + 0.121824 * k);
        var a11 = ToRadians(291.34 + 1.844379 * k);
        var a12 = ToRadians(161.72 + 24.198154 * k);
        var a13 = ToRadians(239.56 + 25.513099 * k);
        var a14 = ToRadians(331.55 + 3.592518 * k);

        var additionalCorrection =
            0.000325 * Math.Sin(a1)
            + 0.000165 * Math.Sin(a2)
            + 0.000164 * Math.Sin(a3)
            + 0.000126 * Math.Sin(a4)
            + 0.000110 * Math.Sin(a5)
            + 0.000062 * Math.Sin(a6)
            + 0.000060 * Math.Sin(a7)
            + 0.000056 * Math.Sin(a8)
            + 0.000047 * Math.Sin(a9)
            + 0.000042 * Math.Sin(a10)
            + 0.000040 * Math.Sin(a11)
            + 0.000037 * Math.Sin(a12)
            + 0.000035 * Math.Sin(a13)
            + 0.000023 * Math.Sin(a14);

        jde += additionalCorrection;

        return FromJulianDay(jde);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

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

        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }
}
