using System.Globalization;
using System.Linq;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static string BuildWeatherSpokenReply(
        WeatherReportSnapshot snapshot,
        WeatherDateEntity weatherDate,
        JiboExperienceCatalog catalog)
    {
        var unit = snapshot.UseCelsius ? "Celsius" : "Fahrenheit";
        var summary = string.IsNullOrWhiteSpace(snapshot.Summary)
            ? "partly cloudy"
            : snapshot.Summary.Trim().TrimEnd('.');
        var location = string.IsNullOrWhiteSpace(snapshot.LocationName)
            ? "your area"
            : NormalizeLocationForSpeech(snapshot.LocationName);

        if (weatherDate.ForecastDayOffset > 0)
        {
            if (weatherDate.ForecastDayOffset != 1)
            {
                var highText = snapshot.HighTemperature is null
                    ? null
                    : $"a high near {snapshot.HighTemperature.Value} degrees {unit}";
                var lowText = snapshot.LowTemperature is null
                    ? null
                    : $"a low around {snapshot.LowTemperature.Value} degrees {unit}";
                var tempRange = highText is null && lowText is null
                    ? string.Empty
                    : highText is not null && lowText is not null
                        ? $" with {highText} and {lowText}"
                        : $" with {highText ?? lowText}";
                var forecastLeadIn = string.IsNullOrWhiteSpace(weatherDate.ForecastLeadIn)
                    ? "Tomorrow"
                    : weatherDate.ForecastLeadIn;
                return $"Let's look at the weather. {forecastLeadIn} in {location}, it looks {summary}{tempRange}.";
            }

            var highValue = snapshot.HighTemperature ?? snapshot.Temperature;
            var lowValue = snapshot.LowTemperature ?? snapshot.Temperature;
            var introTemplate = ChooseWeatherTemplate(
                catalog.WeatherTomorrowIntroReplies,
                "Let's look at the weather.");
            var highLowTemplate = ChooseWeatherTemplate(
                catalog.WeatherTomorrowHighLowReplies,
                "Tomorrow's high will be ${skill.weather.tomorrow.highTemp} and the low will be ${skill.weather.tomorrow.lowTemp}.");
            var intro = RenderWeatherTemplate(
                introTemplate,
                location,
                summary,
                highValue,
                lowValue,
                unit,
                weatherDate.ForecastLeadIn ?? string.Empty);
            var highLow = RenderWeatherTemplate(
                highLowTemplate,
                location,
                summary,
                highValue,
                lowValue,
                unit,
                weatherDate.ForecastLeadIn ?? string.Empty);
            var forecastSentenceLeadIn = string.IsNullOrWhiteSpace(weatherDate.ForecastLeadIn)
                ? "Tomorrow"
                : weatherDate.ForecastLeadIn;
            return $"{intro} {forecastSentenceLeadIn} in {location}, it looks {summary}. {highLow}";
        }

        var currentIntro = RenderWeatherTemplate(
            ChooseWeatherTemplate(catalog.WeatherIntroReplies, "For your weather."),
            location,
            summary,
            snapshot.Temperature,
            snapshot.Temperature,
            unit,
            string.Empty);
        var currentHighLow = RenderWeatherTemplate(
            ChooseWeatherTemplate(
                catalog.WeatherTodayHighLowReplies,
                "Today's high is ${skill.weather.today.highTemp}, and the low is ${skill.weather.today.lowTemp}."),
            location,
            summary,
            snapshot.HighTemperature ?? snapshot.Temperature,
            snapshot.LowTemperature ?? snapshot.Temperature,
            unit,
            string.Empty);
        return
            $"{currentIntro} In {location}, it's {summary} and {snapshot.Temperature} degrees {unit}. {currentHighLow}";
    }

    private static string BuildCommuteSpokenReply(
        CommuteReportSnapshot snapshot,
        JiboExperienceCatalog catalog)
    {
        var duration = snapshot.DurationMinutes;
        var durationText = duration <= 1 ? "1 minute" : $"{duration} minutes";
        var minutesLeft = snapshot.MinutesUntilWork;
        var minutesLeftText = minutesLeft <= 1 ? "1 minute" : $"{Math.Abs(minutesLeft)} minutes";
        var mode = string.IsNullOrWhiteSpace(snapshot.Mode) ? "driving" : snapshot.Mode.Trim();
        var template = ChooseCommuteTemplate(snapshot, catalog, mode);
        var reply = RenderCommuteTemplate(template, durationText, minutesLeftText);

        if (minutesLeft is > 0 and < 30)
        {
            var minutesTemplate = ChooseShortestTemplate(catalog.CommuteMinutesLeftReplies)
                                  ?? "That's in about ${skill.commute.minsLeft} minutes.";
            reply = $"{reply} {RenderCommuteTemplate(minutesTemplate, durationText, minutesLeftText)}";
        }

        if (minutesLeft is <= 0 or >= 120)
            return reply.Replace("  ", " ", StringComparison.Ordinal).Trim();

        var departTemplate = ChooseCommuteDepartTimeTemplate(snapshot, catalog, mode);
        if (!string.IsNullOrWhiteSpace(departTemplate))
            reply = $"{reply} {RenderCommuteTemplate(departTemplate, durationText, minutesLeftText)}";

        return reply.Replace("  ", " ", StringComparison.Ordinal).Trim();
    }

    private string ChooseCommuteAppSetupReply(JiboExperienceCatalog catalog)
    {
        return SelectLegacyReply(
            catalog.CommuteAppSetupReplies, "I need your commute settings before I can give you a commute report.");
    }

    private static string ChooseCommuteTemplate(
        CommuteReportSnapshot snapshot,
        JiboExperienceCatalog catalog,
        string mode)
    {
        var minutesUntilWork = snapshot.MinutesUntilWork;
        var extraMinutes = Math.Max(0, snapshot.ExtraMinutes);
        var isLate = minutesUntilWork <= 0;
        var isHurry = minutesUntilWork is > 0 and <= 10;
        var isNormal = !isLate && !isHurry;
        var isFarAway = minutesUntilWork is > 120 or < -30;
        var hasTrafficSeverity = minutesUntilWork > 0;
        var isTerrible = hasTrafficSeverity && extraMinutes >= 15;
        var isPoor = hasTrafficSeverity && extraMinutes >= 5;

        var loweredMode = mode.Trim().ToLowerInvariant();
        IReadOnlyList<string> candidates = loweredMode switch
        {
            "walking" when isHurry => catalog.CommuteTransportHurryReplies,
            "walking" when isLate => catalog.CommuteTransportLateReplies,
            "walking" => catalog.CommuteTransportNormalReplies,
            "transit" when isHurry => catalog.CommuteTransportHurryReplies,
            "transit" when isLate => catalog.CommuteTransportLateReplies,
            "transit" => catalog.CommuteTransportNormalReplies,
            "bicycling" when isHurry => catalog.CommuteDriveHurryReplies,
            "bicycling" when isLate => catalog.CommuteDriveLateReplies,
            "bicycling" => catalog.CommuteDriveNormalReplies,
            _ when isFarAway => catalog.CommuteNowReplies,
            _ when isTerrible => catalog.CommuteDriveTerribleReplies,
            _ when isPoor => catalog.CommuteDrivePoorReplies,
            _ when isHurry => catalog.CommuteDriveHurryReplies,
            _ when isLate => catalog.CommuteDriveLateReplies,
            _ when isNormal => catalog.CommuteDriveNormalReplies,
            _ => catalog.CommuteNowReplies
        };

        if (candidates.Count == 0)
            return "For your commute, it should take about ${skill.commute.durationMins} minutes.";

        var selected = ChooseShortestTemplate(candidates);
        return string.IsNullOrWhiteSpace(selected)
            ? "For your commute, it should take about ${skill.commute.durationMins} minutes."
            : selected!;
    }

    private static string ChooseCommuteDepartTimeTemplate(
        CommuteReportSnapshot snapshot,
        JiboExperienceCatalog catalog,
        string mode)
    {
        var loweredMode = mode.Trim().ToLowerInvariant();
        var templates = snapshot.MinutesUntilWork <= 0
            ? catalog.CommuteDepartTimeNotNormalReplies
            : catalog.CommuteDepartTimeNormalReplies;

        if (templates.Count == 0) return string.Empty;

        var selected = ChooseShortestTemplate(templates);
        if (!string.IsNullOrWhiteSpace(selected)) return selected!;

        return loweredMode switch
        {
            "walking" => "If you leave at the usual time, that should work out fine.",
            "transit" => "If you leave at the usual time, that should work out fine.",
            _ => "If you leave at the usual time, that should work out fine."
        };
    }

    private static string RenderCommuteTemplate(string template, string durationText, string minutesLeftText)
    {
        return template
            .Replace("${skill.commute.durationMins}", durationText, StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.commute.minsLeft}", minutesLeftText, StringComparison.OrdinalIgnoreCase)
            .Replace("${speaker}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string? ChooseShortestTemplate(IEnumerable<string> templates)
    {
        return templates
            .Where(static template => !string.IsNullOrWhiteSpace(template))
            .OrderBy(static template => template.Length)
            .FirstOrDefault();
    }

    private static string BuildWeeklyForecastSpokenReply(
        IReadOnlyList<WeatherForecastCardSegment> segments,
        string? locationName,
        bool useCelsius,
        bool isThisWeekForecast)
    {
        if (segments.Count == 0) return "I couldn't build a forecast right now.";

        var location = string.IsNullOrWhiteSpace(locationName)
            ? "your area"
            : NormalizeLocationForSpeech(locationName);
        var unit = useCelsius ? "Celsius" : "Fahrenheit";
        var leadIn = isThisWeekForecast
            ? $"Here's the rest of this week's forecast in {location}."
            : $"I can share the next five-day forecast in {location}.";
        return
            $"{leadIn} {string.Join(" ", segments.Select(static segment => segment.SpokenLine))} Temperatures are in {unit}.";
    }

    private static IReadOnlyList<WeatherForecastCardSegment> BuildWeeklyForecastCardSegments(
        IReadOnlyList<(int DayOffset, WeatherReportSnapshot Snapshot)> snapshots,
        DateTimeOffset? referenceLocalTime)
    {
        if (snapshots.Count == 0) return [];

        var resolvedReference = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var referenceDate = resolvedReference.Date;
        return [.. snapshots
            .OrderBy(static item => item.DayOffset)
            .Take(MaxWeatherForecastDayOffset)
            .Select(item =>
            {
                var dayName = referenceDate.AddDays(item.DayOffset).ToString("dddd", CultureInfo.InvariantCulture);
                var summary = string.IsNullOrWhiteSpace(item.Snapshot.Summary)
                    ? "partly cloudy"
                    : item.Snapshot.Summary.Trim().TrimEnd('.');
                var high = item.Snapshot.HighTemperature ?? item.Snapshot.Temperature;
                var low = item.Snapshot.LowTemperature ?? item.Snapshot.Temperature;
                var iconReference = new DateTimeOffset(
                    resolvedReference.Date.AddDays(item.DayOffset).AddHours(12),
                    resolvedReference.Offset);
                var icon = ResolveWeatherAnimationIcon(item.Snapshot, iconReference);
                var unit = item.Snapshot.UseCelsius ? "C" : "F";
                var temperatureBand = ResolveWeatherTemperatureBand(high, item.Snapshot.UseCelsius);
                var spokenLine = $"{dayName}: {summary}, high {high}, low {low}.";
                return new WeatherForecastCardSegment(
                    dayName,
                    summary,
                    high,
                    low,
                    icon,
                    unit,
                    temperatureBand,
                    spokenLine);
            })];
    }

    private static IDictionary<string, object?> BuildWeeklyWeatherSkillPayload(
        string spokenReply,
        WeatherReportSnapshot snapshot,
        IReadOnlyList<WeatherForecastCardSegment> segments,
        DateTimeOffset? referenceLocalTime)
    {
        var payload = BuildWeatherSkillPayload(spokenReply, snapshot, referenceLocalTime);
        payload["weather_view_kind"] = "weatherWeekly";
        payload["weather_view_mode"] = "forecast";
        payload["weather_weekly_cards"] = segments
            .Select(static segment => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["weather_day"] = segment.DayName,
                ["weather_summary"] = segment.Summary,
                ["weather_icon"] = segment.Icon,
                ["weather_high"] = segment.High,
                ["weather_low"] = segment.Low,
                ["weather_unit"] = segment.Unit,
                ["weather_theme"] = segment.Theme,
                ["weather_spoken_line"] = segment.SpokenLine
            })
            .ToArray();
        return payload;
    }

    private static void AddWeatherRequestDiagnostics(
        IDictionary<string, object?> payload,
        string transcript,
        string normalizedTranscript,
        string? locationQuery,
        WeatherDateEntity weatherDate,
        bool isRangeForecastRequest,
        bool isThisWeekForecast,
        bool isNextWeekForecast)
    {
        payload["weather_request_transcript"] = transcript;
        payload["weather_request_normalized"] = normalizedTranscript;
        payload["weather_request_location_query"] = locationQuery;
        payload["weather_request_date_entity"] = weatherDate.DateEntity;
        payload["weather_request_forecast_day_offset"] = weatherDate.ForecastDayOffset;
        payload["weather_request_range"] = isRangeForecastRequest;
        payload["weather_request_this_week"] = isThisWeekForecast;
        payload["weather_request_next_week"] = isNextWeekForecast;
    }

    private static bool IsNextWeekForecastRequest(string normalizedTranscript, bool isRangeForecastRequest)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript) || !isRangeForecastRequest) return false;

        if (normalizedTranscript.Contains("next week", StringComparison.Ordinal)) return true;

        if (!normalizedTranscript.Contains("next", StringComparison.Ordinal)) return false;

        return normalizedTranscript.Contains("forecast next", StringComparison.Ordinal) ||
               normalizedTranscript.Contains("forecast for next", StringComparison.Ordinal);
    }

    private static bool IsRangeForecastRequest(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return false;

        if (normalizedTranscript.Contains("next week", StringComparison.Ordinal) ||
            normalizedTranscript.Contains("this week", StringComparison.Ordinal) ||
            normalizedTranscript.Contains("weekend", StringComparison.Ordinal))
            return true;

        return normalizedTranscript.Contains("forecast next", StringComparison.Ordinal) ||
               normalizedTranscript.Contains("forecast for next", StringComparison.Ordinal);
    }

    private static bool IsThisWeekForecastRequest(string normalizedTranscript, bool isRangeForecastRequest)
    {
        return isRangeForecastRequest &&
               !string.IsNullOrWhiteSpace(normalizedTranscript) &&
               normalizedTranscript.Contains("this week", StringComparison.Ordinal) &&
               !normalizedTranscript.Contains("weekend", StringComparison.Ordinal);
    }

    private static bool IsOpenEndedForecastRequest(
        string normalizedTranscript,
        WeatherDateEntity weatherDate,
        bool isRangeForecastRequest,
        string? locationQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript) ||
            !string.IsNullOrWhiteSpace(locationQuery) ||
            isRangeForecastRequest ||
            weatherDate.ForecastDayOffset > 0 ||
            !normalizedTranscript.Contains("forecast", StringComparison.Ordinal))
            return false;

        return !MatchesAny(
            normalizedTranscript,
            "today",
            "today s",
            "today's",
            "tonight",
            "right now",
            "current weather",
            "currently");
    }

    private static int ResolveThisWeekForecastEndOffset(DateTimeOffset? referenceLocalTime)
    {
        var resolvedReference = referenceLocalTime ?? DateTimeOffset.UtcNow;
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)resolvedReference.DayOfWeek + 7) % 7;
        var endOffset = Math.Min(MaxWeatherForecastDayOffset, daysUntilSunday);
        return Math.Max(1, endOffset);
    }

    private static bool ShouldDefaultForecastToTomorrow(
        string normalizedTranscript,
        WeatherDateEntity weatherDate,
        bool isRangeForecastRequest,
        bool isOpenEndedForecastRequest)
    {
        if (weatherDate.ForecastDayOffset > 0 ||
            isOpenEndedForecastRequest ||
            isRangeForecastRequest ||
            string.IsNullOrWhiteSpace(normalizedTranscript) ||
            !normalizedTranscript.Contains("forecast", StringComparison.Ordinal))
            return false;

        return !MatchesAny(
            normalizedTranscript,
            "today",
            "today s",
            "today's",
            "tonight",
            "right now",
            "current weather",
            "currently");
    }

    private static IDictionary<string, object?> BuildWeatherSkillPayload(
        string spokenReply,
        WeatherReportSnapshot snapshot,
        DateTimeOffset? referenceLocalTime)
    {
        var weatherIcon = ResolveWeatherAnimationIcon(snapshot, referenceLocalTime);
        var promptToken = ResolveWeatherPromptToken(weatherIcon);
        var highTemperature = snapshot.HighTemperature ?? snapshot.Temperature;
        var lowTemperature = snapshot.LowTemperature ?? snapshot.Temperature;
        var temperatureUnit = snapshot.UseCelsius ? "C" : "F";
        var temperatureBand = ResolveWeatherTemperatureBand(highTemperature, snapshot.UseCelsius);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["skillId"] = "report-skill",
            ["cloudSkill"] = "weather",
            ["esml"] =
                $"<speak><anim cat='weather' meta='{weatherIcon}' nonBlocking='true' /><break size='0.35'/><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{EscapeForEsml(spokenReply)}</es></speak>",
            ["mim_id"] = $"WeatherComment{promptToken}",
            ["mim_type"] = "announcement",
            ["prompt_id"] = $"WeatherComment{promptToken}_AN_13",
            ["prompt_sub_category"] = "AN",
            ["weather_view_enabled"] = true,
            ["weather_view_kind"] = "weatherHiLo",
            ["weather_view_mode"] = "current",
            ["weather_icon"] = weatherIcon,
            ["weather_summary"] = snapshot.Summary,
            ["weather_location"] = snapshot.LocationName,
            ["weather_high"] = highTemperature,
            ["weather_low"] = lowTemperature,
            ["weather_unit"] = temperatureUnit,
            ["weather_theme"] = temperatureBand
        };
    }

    private static string ResolveWeatherAnimationIcon(
        WeatherReportSnapshot snapshot,
        DateTimeOffset? referenceLocalTime)
    {
        var isDaytime = (referenceLocalTime ?? DateTimeOffset.UtcNow).Hour is >= 6 and < 18;
        var normalized = NormalizeCommandPhrase(
            $"{snapshot.Condition ?? string.Empty} {snapshot.Summary ?? string.Empty}");

        if (normalized.Contains("thunder", StringComparison.Ordinal) ||
            normalized.Contains("drizzle", StringComparison.Ordinal) ||
            normalized.Contains("rain", StringComparison.Ordinal))
            return "rain";

        if (normalized.Contains("snow", StringComparison.Ordinal)) return "snow";

        if (normalized.Contains("sleet", StringComparison.Ordinal) ||
            normalized.Contains("freezing rain", StringComparison.Ordinal) ||
            normalized.Contains("ice", StringComparison.Ordinal))
            return "sleet";

        if (normalized.Contains("fog", StringComparison.Ordinal) ||
            normalized.Contains("mist", StringComparison.Ordinal) ||
            normalized.Contains("haze", StringComparison.Ordinal) ||
            normalized.Contains("smoke", StringComparison.Ordinal))
            return "fog";

        if (normalized.Contains("wind", StringComparison.Ordinal)) return "wind";

        if (normalized.Contains("partly cloudy", StringComparison.Ordinal) ||
            normalized.Contains("scattered clouds", StringComparison.Ordinal) ||
            normalized.Contains("few clouds", StringComparison.Ordinal))
            return isDaytime ? "partly-cloudy-day" : "partly-cloudy-night";

        if (normalized.Contains("cloud", StringComparison.Ordinal) ||
            normalized.Contains("overcast", StringComparison.Ordinal))
            return "cloudy";

        if (normalized.Contains("clear", StringComparison.Ordinal) ||
            normalized.Contains("sunny", StringComparison.Ordinal))
            return isDaytime ? "clear-day" : "clear-night";

        return isDaytime ? "clear-day" : "clear-night";
    }

    private static string ResolveWeatherPromptToken(string weatherIcon)
    {
        return weatherIcon switch
        {
            "clear-day" => "ClearDay",
            "clear-night" => "ClearNight",
            "rain" => "Rain",
            "snow" => "Snow",
            "sleet" => "Sleet",
            "fog" => "Fog",
            "wind" => "Wind",
            "cloudy" => "Cloudy",
            "partly-cloudy-day" => "PartlyCloudyDay",
            "partly-cloudy-night" => "PartlyCloudyNight",
            _ => "Cloudy"
        };
    }

    private static string ResolveWeatherTemperatureBand(int highTemperature, bool useCelsius)
    {
        var hotThreshold = useCelsius ? 29 : 85;
        var coldThreshold = useCelsius ? 4 : 40;
        if (highTemperature > hotThreshold) return "Hot";

        if (highTemperature < coldThreshold) return "Cold";

        return "Normal";
    }

    private static string ChooseWeatherTemplate(IReadOnlyList<string> templates, string fallback)
    {
        var usableTemplates = templates.Where(static template => !string.IsNullOrWhiteSpace(template)).ToArray();
        if (usableTemplates.Length == 0) return fallback;

        return usableTemplates[0];
    }

    private static string RenderWeatherTemplate(
        string template,
        string location,
        string summary,
        int? highTemperature,
        int? lowTemperature,
        string unit,
        string forecastLeadIn)
    {
        var rendered = template
            .Replace("${skill.weather.today.highTemp}",
                highTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.today.lowTemp}",
                lowTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.tomorrow.highTemp}",
                highTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.tomorrow.lowTemp}",
                lowTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.summary}", summary, StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.location}", location, StringComparison.OrdinalIgnoreCase)
            .Replace("${skill.weather.prefix}",
                string.IsNullOrWhiteSpace(forecastLeadIn) ? string.Empty : forecastLeadIn,
                StringComparison.OrdinalIgnoreCase)
            .Replace("{high}", highTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("{low}", lowTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace("{unit}", unit, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return rendered;
    }

    private static string ChooseWeatherServiceDownReply(JiboExperienceCatalog catalog)
    {
        var template = ChooseWeatherTemplate(
            catalog.WeatherServiceDownReplies,
            "I can't access weather info right now, sorry.");
        return template.Trim();
    }

    private static string ChooseCommuteServiceDownReply(JiboExperienceCatalog catalog)
    {
        var template = ChooseWeatherTemplate(
            catalog.CommuteServiceDownReplies,
            "Sorry, commute information isn't available right now.");
        return template.Trim();
    }

    private string BuildCalendarSpokenReply(CalendarReportSnapshot snapshot, JiboExperienceCatalog catalog)
    {
        if (snapshot.EventSummaries.Count > 0 && snapshot.EventTimesOnAt.Count > 0)
        {
            var summary = snapshot.EventSummaries[0];
            var time = snapshot.EventTimesOnAt[0];
            var template = ChooseCalendarTemplate(
                catalog.CalendarReplies,
                "calendar summary",
                "Your calendar says ${skill.calendar.eventSummaries.shift()}, ${skill.calendar.eventTimesOnAt.shift()}.");
            if (template.Contains("${skill.calendar.eventSummaries.shift()}", StringComparison.OrdinalIgnoreCase) ||
                template.Contains("${skill.calendar.eventTimesOnAt.shift()}", StringComparison.OrdinalIgnoreCase))
                return template
                    .Replace("${skill.calendar.eventSummaries.shift()}", summary, StringComparison.OrdinalIgnoreCase)
                    .Replace("${skill.calendar.eventTimesOnAt.shift()}", time, StringComparison.OrdinalIgnoreCase)
                    .Replace("${speaker}", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("  ", " ", StringComparison.Ordinal)
                    .Trim();

            return $"Your calendar says {summary}, {time}.";
        }

        if (snapshot.TomorrowEventSummaries.Count > 0)
        {
            var template = ChooseCalendarTemplate(
                catalog.CalendarReplies,
                "calendar tomorrow",
                "Looking at your calendar, there's nothing scheduled for the rest of the day today. Here's what's going on tomorrow.");
            if (template.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
                return template
                    .Replace("${speaker}", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("  ", " ", StringComparison.Ordinal)
                    .Trim();

            return
                $"Looking at your calendar, there's nothing scheduled for the rest of the day today. Here's what's going on tomorrow: {snapshot.TomorrowEventSummaries[0]}.";
        }

        return ChooseCalendarNothingReply(catalog);
    }

    private static string ChooseCalendarTemplate(
        IReadOnlyList<string> templates,
        string mode,
        string fallback)
    {
        if (templates.Count == 0) return fallback;

        var loweredMode = mode.Trim().ToLowerInvariant();
        var filtered = templates.Where(template =>
        {
            var lowered = template.ToLowerInvariant();
            return loweredMode switch
            {
                "calendar summary" => lowered.Contains("event", StringComparison.OrdinalIgnoreCase) ||
                                      lowered.Contains("summary", StringComparison.OrdinalIgnoreCase),
                "calendar tomorrow" => lowered.Contains("tomorrow", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }).ToList();

        var selected = filtered.Count > 0
            ? filtered.OrderBy(static template => template.Length).First()
            : templates.OrderBy(static template => template.Length).FirstOrDefault();

        return string.IsNullOrWhiteSpace(selected) ? fallback : selected;
    }

    private string ChooseCalendarNothingReply(JiboExperienceCatalog catalog)
    {
        return catalog.CalendarNothingTodayReplies.Count > 0
            ? randomizer.Choose(catalog.CalendarNothingTodayReplies)
            : catalog.CalendarNothingReplies.Count > 0
                ? randomizer.Choose(catalog.CalendarNothingReplies)
                : "Looking at your calendar, I don't see anything scheduled today.";
    }

    private string ChooseCalendarServiceDownReply(JiboExperienceCatalog catalog)
    {
        return catalog.CalendarServiceDownReplies.Count > 0
            ? randomizer.Choose(catalog.CalendarServiceDownReplies)
            : "Looks like I can't access calendars right now. Sorry.";
    }
}
