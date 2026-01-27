# CalendarApp - Project Status & Requirements

**Last Updated:** January 26, 2026

---

## Project Overview

A cross-platform calendar application built with Uno Platform and C# featuring three calendar modes (Gregorian, Julian, Biblical), Google Calendar two-way sync, and a Google Calendar-like UI.

---

## Requirements Summary

| Requirement | Specification |
|-------------|---------------|
| **Platforms** | Windows, macOS, Linux, iOS*, Android, Web |
| **Architecture** | MVVM with CommunityToolkit.Mvvm |
| **Google Sync** | Two-way sync with full date conversion |
| **Views** | Day, Week, Month, Year, Schedule/Agenda |
| **Offline** | Full offline support with SQLite |
| **Theme** | User choice: manual or auto system detection |
| **Recurrence** | Full Google Calendar parity (RFC 5545 RRULE) |
| **Notifications** | Native push notifications |
| **Location** | GPS with manual fallback (user-selectable mode) |
| **Astronomy** | User choice: built-in (NOAA/Meeus algorithms) or web API |
| **Testing** | Unit tests + Integration tests |

*iOS requires macOS to build - removed from current Windows development

---

## Calendar Modes

### Mode 1: Gregorian (Standard)
- Days begin/end at **midnight**
- Year begins **January 1**
- Standard calendar calculations

### Mode 2: Julian
- Days begin/end at **midnight**
- Year begins **January 1**
- Uses `System.Globalization.JulianCalendar`
- Shows **cross-reference to Gregorian dates**

### Mode 3: Biblical
- Days begin at **sunset** (calculated from GPS or manual location)
- Month begins on the **first day following lunar conjunction**
- Year begins with the **first month after spring equinox**
- **Cross-reference to Gregorian calendar** (dates at noon during daylight)
- Fallback location: Jerusalem (31.7683, 35.2137)

---

## Technology Stack

### Core Framework
- **Uno Platform** with Uno.Sdk (net9.0)
- **Target Frameworks**: net9.0-android, net9.0-browserwasm, net9.0-desktop, net9.0

### NuGet Packages
```xml
<!-- Uno Features (via UnoFeatures) -->
Material, Dsp, Hosting, Toolkit, Logging, Mvvm, Configuration,
HttpKiota, Serialization, Localization, Navigation, ThemeService, SkiaRenderer

<!-- Data Storage -->
sqlite-net-pcl (1.9.172)
SQLitePCLRaw.bundle_green (2.1.10)

<!-- Planned (not yet added) -->
Google.Apis.Calendar.v3
Google.Apis.Auth
Ical.Net (for recurrence)
```

---

## Project Structure

```
CalendarApp/
├── CalendarApp.sln
├── PROJECT_STATUS.md                 # This file
├── Directory.Packages.props          # Centralized package versions
├── CalendarApp/
│   ├── CalendarApp.csproj
│   ├── App.xaml / App.xaml.cs        # DI registration
│   ├── Models/
│   │   ├── CalendarMode.cs           # Enum: Gregorian, Julian, Biblical
│   │   ├── CalendarDate.cs           # Date record with cross-reference
│   │   ├── CalendarEvent.cs          # Event model with sync status
│   │   ├── RecurrenceRule.cs         # RFC 5545 RRULE model
│   │   ├── Reminder.cs               # Reminder model
│   │   └── LocationInfo.cs           # Geographic location record
│   ├── Services/
│   │   ├── Interfaces/
│   │   │   ├── ICalendarCalculationService.cs
│   │   │   ├── IAstronomicalService.cs
│   │   │   ├── ILocationService.cs
│   │   │   └── IEventRepository.cs
│   │   ├── Calendar/
│   │   │   ├── GregorianCalendarService.cs
│   │   │   ├── JulianCalendarService.cs
│   │   │   └── BiblicalCalendarService.cs
│   │   ├── Astronomy/
│   │   │   ├── AstronomicalService.cs
│   │   │   ├── SolarCalculator.cs    # NOAA algorithms
│   │   │   └── LunarCalculator.cs    # Jean Meeus algorithms
│   │   ├── Data/
│   │   │   └── EventRepository.cs
│   │   └── Location/
│   │       └── LocationService.cs
│   ├── Data/
│   │   ├── CalendarDbContext.cs      # SQLite context
│   │   └── Entities/
│   │       ├── EventEntity.cs
│   │       ├── RecurrenceEntity.cs
│   │       ├── ReminderEntity.cs
│   │       ├── LocationEntity.cs
│   │       ├── SettingsEntity.cs
│   │       └── SyncStateEntity.cs
│   ├── Presentation/
│   │   ├── MainPage.xaml / .cs       # Month view UI
│   │   ├── MainViewModel.cs          # Calendar view model
│   │   └── SecondPage.xaml / .cs     # (template default)
│   ├── Converters/
│   │   └── CalendarConverters.cs     # XAML value converters
│   └── Styles/
│       └── ColorPaletteOverride.xaml
└── CalendarApp.Tests/                # Unit test project
```

---

## Implementation Status

### Phase 1: Foundation - COMPLETE
- [x] Uno Platform solution created
- [x] NuGet packages configured (SQLite)
- [x] DI container setup
- [x] SQLite database entities created
- [x] Repository pattern implemented

### Phase 2: Calendar Services - COMPLETE
- [x] ICalendarCalculationService interface
- [x] GregorianCalendarService
- [x] JulianCalendarService
- [x] BiblicalCalendarService
- [x] IAstronomicalService interface
- [x] SolarCalculator (NOAA algorithms)
- [x] LunarCalculator (Meeus algorithms)
- [x] AstronomicalService orchestrator

### Phase 3: UI Implementation - PARTIAL
- [x] Theme system (Material)
- [x] Month view with calendar grid
- [x] Calendar mode selector (radio buttons)
- [x] Navigation (previous/next month)
- [x] Today button
- [x] Day cells with events preview
- [x] Cross-reference display
- [ ] Week view
- [ ] Day view
- [ ] Year view
- [ ] Agenda view
- [ ] Event editor page
- [ ] Settings page

### Phase 4: Recurring Events - NOT STARTED
- [ ] Ical.Net integration
- [ ] RecurrenceService
- [ ] RecurrenceExpander
- [ ] UI for editing recurrence

### Phase 5: Google Calendar Integration - NOT STARTED
- [ ] Google Cloud project setup
- [ ] OAuth 2.0 with PKCE
- [ ] GoogleCalendarService
- [ ] SyncService
- [ ] Conflict resolution

### Phase 6: Location & Notifications - PARTIAL
- [x] ILocationService interface
- [x] LocationService implementation (basic)
- [ ] Platform-specific GPS implementations
- [ ] INotificationService interface
- [ ] Platform-specific notification implementations
- [ ] ReminderSchedulingService

### Phase 7: Testing & Polish - NOT STARTED
- [ ] Unit tests for calendar calculations
- [ ] Integration tests for sync
- [ ] Platform testing
- [ ] Performance optimization
- [ ] Accessibility improvements

---

## Known Issues

1. **NuGet Restore Network Issues**: Intermittent network failures when restoring Uno packages. May need to retry when network is stable.

2. **iOS Build Requires macOS**: iOS target framework removed from project since development is on Windows.

---

## Key Algorithms

### Solar Calculations (SolarCalculator.cs)
- Based on NOAA Solar Calculator algorithms
- Calculates sunrise, sunset, solar noon
- Calculates equinoxes and solstices
- Accuracy: within 1-2 minutes for sunrise/sunset

### Lunar Calculations (LunarCalculator.cs)
- Based on Jean Meeus "Astronomical Algorithms"
- Calculates lunar phases (new moon, full moon, quarters)
- Calculates next/previous new moon from any date
- Used for Biblical calendar month boundaries

---

## DI Service Registration (App.xaml.cs)

```csharp
services.AddSingleton<CalendarDbContext>();
services.AddSingleton<IAstronomicalService, AstronomicalService>();
services.AddSingleton<ILocationService, LocationService>();
services.AddSingleton<GregorianCalendarService>();
services.AddSingleton<JulianCalendarService>();
services.AddSingleton<BiblicalCalendarService>();
services.AddSingleton<Func<CalendarMode, ICalendarCalculationService>>(sp => mode =>
{
    return mode switch
    {
        CalendarMode.Gregorian => sp.GetRequiredService<GregorianCalendarService>(),
        CalendarMode.Julian => sp.GetRequiredService<JulianCalendarService>(),
        CalendarMode.Biblical => sp.GetRequiredService<BiblicalCalendarService>(),
        _ => sp.GetRequiredService<GregorianCalendarService>()
    };
});
services.AddSingleton<IEventRepository, EventRepository>();
```

---

## Next Steps

1. **Retry NuGet restore** when network is stable
2. **Build and test** current implementation
3. **Implement additional UI views** (Week, Day, Year, Agenda)
4. **Add Google Calendar integration**
5. **Add platform-specific services** (GPS, notifications)
6. **Write unit tests**

---

## Build Commands

```bash
# Restore packages
dotnet restore CalendarApp.sln

# Build all platforms
dotnet build CalendarApp.sln

# Run desktop
dotnet run --project CalendarApp/CalendarApp.csproj -f net9.0-desktop

# Run tests
dotnet test CalendarApp.Tests
```

---

## References

- [Uno Platform Documentation](https://platform.uno/docs/)
- [NOAA Solar Calculator](https://gml.noaa.gov/grad/solcalc/)
- [Jean Meeus - Astronomical Algorithms](https://www.willbell.com/math/mc1.htm)
- [Google Calendar API](https://developers.google.com/calendar)
- [RFC 5545 - iCalendar](https://datatracker.ietf.org/doc/html/rfc5545)
