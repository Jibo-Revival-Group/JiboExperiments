using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private static IEnumerable<string> ReadRules(TurnContext turn, string key)
    {
        if (!turn.Attributes.TryGetValue(key, out var value) || value is null) return [];

        return value switch
        {
            IReadOnlyList<string> typed => typed,
            IEnumerable<string> strings => strings,
            JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString() ?? string.Empty),
            _ => []
        };
    }

    private static IReadOnlyDictionary<string, string> ReadEntities(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("clientEntities", out var value) || value is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Object } json => json.EnumerateObject()
                .Where(static property => property.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            IReadOnlyDictionary<string, string> typed => typed,
            IDictionary<string, object?> dictionary => dictionary
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value?.ToString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DateTimeOffset? TryResolveReferenceLocalTime(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("context", out var value) || value is null) return null;

        try
        {
            var contextJson = value.ToString();
            if (string.IsNullOrWhiteSpace(contextJson)) return null;

            using var document = JsonDocument.Parse(contextJson);
            if (!document.RootElement.TryGetProperty("runtime", out var runtime) ||
                runtime.ValueKind != JsonValueKind.Object ||
                !runtime.TryGetProperty("location", out var location) ||
                location.ValueKind != JsonValueKind.Object ||
                !location.TryGetProperty("iso", out var iso) ||
                iso.ValueKind != JsonValueKind.String)
                return null;

            var isoValue = iso.GetString();
            return DateTimeOffset.TryParse(isoValue, out var parsed)
                ? parsed
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribePersonaAge(DateOnly referenceDate, DateOnly birthday)
    {
        if (referenceDate < birthday) return "just getting started";

        var totalDays = referenceDate.DayNumber - birthday.DayNumber;
        if (totalDays <= 31) return $"{FormatAgeUnit(totalDays, "day")} old";

        var totalMonths = (referenceDate.Year - birthday.Year) * 12 + referenceDate.Month - birthday.Month;
        if (referenceDate.Day < birthday.Day) totalMonths -= 1;

        totalMonths = Math.Max(totalMonths, 0);
        if (totalMonths < 12) return $"{FormatAgeUnit(totalMonths, "month")} old";

        var years = totalMonths / 12;
        var months = totalMonths % 12;
        return months == 0
            ? $"{FormatAgeUnit(years, "year")} old"
            : $"{FormatAgeUnit(years, "year")} and {FormatAgeUnit(months, "month")} old";
    }

    private static string FormatAgeUnit(int value, string singular)
    {
        var plural = value == 1 ? singular : $"{singular}s";
        return $"{value} {plural}";
    }

    private static bool IsYesNoTurn(TurnContext turn)
    {
        return ReadRules(turn, "listenRules")
            .Concat(ReadRules(turn, "clientRules"))
            .Concat(ReadRules(turn, "listenAsrHints"))
            .Any(IsYesNoRule);
    }

    private static string? ReadPrimaryYesNoRule(
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules)
    {
        return listenRules
            .Concat(clientRules)
            .FirstOrDefault(IsConstrainedYesNoRule);
    }

    private static bool IsYesNoRule(string rule)
    {
        return string.Equals(rule, "$YESNO", StringComparison.OrdinalIgnoreCase) ||
               IsConstrainedYesNoRule(rule);
    }

    private static bool IsConstrainedYesNoRule(string rule)
    {
        return string.Equals(rule, "clock/alarm_timer_change", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "clock/alarm_timer_none_set", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "create/is_it_a_keeper", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "settings/download_now_later", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "shared/yes_no", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "surprises-date/offer_date_fact", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "surprises-ota/want_to_download_now", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rule, "word-of-the-day/surprise", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAffirmativeYesNoIntent(string? yesNoRule)
    {
        if (string.Equals(yesNoRule, "word-of-the-day/surprise", StringComparison.OrdinalIgnoreCase))
            return "word_of_the_day";

        return string.Equals(yesNoRule, "surprises-date/offer_date_fact", StringComparison.OrdinalIgnoreCase)
            ? "proactive_offer_pizza_fact"
            : "yes";
    }

    private static string ResolveNegativeYesNoIntent(string? yesNoRule)
    {
        _ = yesNoRule;
        return "no";
    }

    private static bool MatchesAny(string loweredTranscript, params string[] candidates)
    {
        return candidates.Any(candidate => loweredTranscript.Contains(candidate, StringComparison.Ordinal));
    }

    private static bool IsAffirmativeReply(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return YesNoTranscriptClassifier.IsAffirmative(normalized);
    }

    private static bool IsNegativeReply(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return YesNoTranscriptClassifier.IsNegative(normalized);
    }

    private static YesNoReply TryClassifyYesNoReply(string normalizedTranscript)
    {
        return YesNoTranscriptClassifier.Classify(normalizedTranscript) switch
        {
            YesNoTranscriptClassification.Affirmative => YesNoReply.Affirmative,
            YesNoTranscriptClassification.Negative => YesNoReply.Negative,
            YesNoTranscriptClassification.Ambiguous => YesNoReply.Ambiguous,
            _ => YesNoReply.None
        };
    }

    private static bool IsTimeRequest(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        if (normalized is "time" or "the time" or "current time" or "what time is it" or "what s the time"
            or "what is the time") return true;

        return normalized.StartsWith("what time", StringComparison.Ordinal) ||
               normalized.StartsWith("tell me the time", StringComparison.Ordinal) ||
               normalized.StartsWith("show me the time", StringComparison.Ordinal);
    }

    private static bool IsDateRequest(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        return normalized is
            "what is the date" or
            "what s the date" or
            "what's the date" or
            "what date is it" or
            "today s date" or
            "today date" or
            "what's today's date" or
            "what is today s date" or
            "what s today s date" or
            "what's today s date" or
            "what's todays date" or
            "what is todays date" or
            "what s todays date";
    }

    private static bool IsWeatherRequest(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        if (IsWeatherTopicQuestion(normalized)) return true;

        if (MatchesAny(
                loweredTranscript,
                "weather",
                "forecast",
                "how is the weather",
                "how s the weather",
                "how's the weather",
                "check the weather",
                "weather report",
                "what's today s weather",
                "what's today's weather",
                "what is the weather",
                "what will the weather",
                "what will tomorrow s weather",
                "what will tomorrow's weather",
                "look up the forecast",
                "launch the weather skill",
                "what is today s humidity",
                "what is today's humidity",
                "what's the humidity",
                "what is the humidity",
                "what's today's forecast",
                "what s today's forecast",
                "what s today s forecast",
                "what is today s forecast",
                "what is today's forecast",
                "what's today's weather look like",
                "what s today's weather look like",
                "what s today s weather look like",
                "what is today s weather look like",
                "what is today's weather look like"))
            return true;

        return MatchesAny(
            loweredTranscript,
            "will it rain",
            "will it snow",
            "is it raining",
            "is it snowing",
            "is there going to be hail",
            "does it look like rain",
            "does it seem like snow",
            "is it going to rain",
            "is it going to snow",
            "do you think it will rain",
            "do you think it will snow") || WeatherConditionForecastPattern.IsMatch(loweredTranscript);
    }

    private static bool IsWeatherTopicQuestion(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return false;

        var mentionsWeatherTopic =
            normalizedTranscript.Contains("weather", StringComparison.Ordinal) ||
            normalizedTranscript.Contains("forecast", StringComparison.Ordinal) ||
            normalizedTranscript.Contains("temperature", StringComparison.Ordinal) ||
            normalizedTranscript.Contains("humidity", StringComparison.Ordinal);
        if (!mentionsWeatherTopic) return false;

        if (normalizedTranscript.StartsWith("what ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("how ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("check ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("show ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("tell ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("look up ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("launch ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("give me ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("temperature ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("forecast ", StringComparison.Ordinal) ||
            normalizedTranscript.StartsWith("weather ", StringComparison.Ordinal))
            return true;

        return WeatherTopicLocationPattern.IsMatch(normalizedTranscript);
    }

    private static string? TryResolveWeatherLocationQuery(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        var match = WeatherLocationPattern.Match(normalized);
        if (!match.Success) return null;

        var candidate = match.Groups["location"].Value.Trim();
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        candidate = WeatherLocationSuffixPattern.Replace(candidate, string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            GenericWeatherLocationTerms.Contains(candidate))
            return null;

        return string.IsNullOrWhiteSpace(candidate)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(candidate);
    }

    private static (double Latitude, double Longitude)? TryResolveWeatherCoordinates(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("context", out var contextValue) ||
            contextValue is null ||
            string.IsNullOrWhiteSpace(contextValue.ToString()))
            return null;

        try
        {
            using var document = JsonDocument.Parse(contextValue.ToString()!);
            if (!document.RootElement.TryGetProperty("runtime", out var runtime) ||
                runtime.ValueKind != JsonValueKind.Object ||
                !runtime.TryGetProperty("location", out var location) ||
                location.ValueKind != JsonValueKind.Object)
                return null;

            var latitude = TryReadDoubleProperty(location, "lat", "latitude");
            var longitude = TryReadDoubleProperty(location, "lng", "lon", "longitude");
            return latitude is not null && longitude is not null
                ? (latitude.Value, longitude.Value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveCurrentLocationName(TurnContext turn)
    {
        if (turn.Attributes.TryGetValue("currentLocation", out var currentLocationValue) &&
            currentLocationValue is string currentLocationText &&
            !string.IsNullOrWhiteSpace(currentLocationText))
            return currentLocationText.Trim();

        if (turn.Attributes.TryGetValue("location", out var locationValue) &&
            locationValue is string locationText &&
            !string.IsNullOrWhiteSpace(locationText))
            return locationText.Trim();

        if (!turn.Attributes.TryGetValue("context", out var contextValue) ||
            contextValue is null ||
            string.IsNullOrWhiteSpace(contextValue.ToString()))
            return null;

        try
        {
            using var document = JsonDocument.Parse(contextValue.ToString()!);
            if (!document.RootElement.TryGetProperty("runtime", out var runtime) ||
                runtime.ValueKind != JsonValueKind.Object)
                return null;

            string? resolvedLocation;
            if (runtime.TryGetProperty("location", out var location) &&
                location.ValueKind == JsonValueKind.Object)
            {
                resolvedLocation = TryReadStringProperty(location,
                    "displayName",
                    "name",
                    "city",
                    "locationName",
                    "placeName",
                    "label",
                    "title",
                    "address");
                if (!string.IsNullOrWhiteSpace(resolvedLocation)) return resolvedLocation;
            }

            if (!runtime.TryGetProperty("currentLocation", out var currentLocation) ||
                currentLocation.ValueKind != JsonValueKind.Object)
                return TryReadStringProperty(runtime, "locationName", "currentLocation", "city", "placeName");

            resolvedLocation = TryReadStringProperty(currentLocation,
                "displayName",
                "name",
                "city",
                "locationName",
                "placeName",
                "label",
                "title",
                "address");
            return !string.IsNullOrWhiteSpace(resolvedLocation)
                ? resolvedLocation
                : TryReadStringProperty(runtime, "locationName", "currentLocation", "city", "placeName");
        }
        catch
        {
            return null;
        }
    }

    private static GreetingPresenceProfile ResolveGreetingPresenceProfile(TurnContext turn)
    {
        if (!turn.Attributes.TryGetValue("context", out var contextValue) ||
            contextValue is null ||
            string.IsNullOrWhiteSpace(contextValue.ToString()))
            return GreetingPresenceProfile.Empty;

        try
        {
            using var document = JsonDocument.Parse(contextValue.ToString()!);
            if (!document.RootElement.TryGetProperty("runtime", out var runtime) ||
                runtime.ValueKind != JsonValueKind.Object)
                return GreetingPresenceProfile.Empty;

            var loopUsers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (runtime.TryGetProperty("loop", out var loop) &&
                loop.ValueKind == JsonValueKind.Object &&
                loop.TryGetProperty("users", out var users) &&
                users.ValueKind == JsonValueKind.Array)
                foreach (var user in users.EnumerateArray())
                {
                    var id = TryReadStringProperty(user, "id");
                    var firstName = TryReadStringProperty(user, "firstName");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(firstName))
                        loopUsers[id] = firstName;
                }

            var speakerId = string.Empty;
            var peoplePresentIds = new List<string>();
            if (runtime.TryGetProperty("perception", out var perception) &&
                perception.ValueKind == JsonValueKind.Object)
            {
                if (perception.TryGetProperty("speaker", out var speaker))
                    speakerId = speaker.ValueKind switch
                    {
                        JsonValueKind.String => speaker.GetString() ?? string.Empty,
                        JsonValueKind.Object => TryReadStringProperty(speaker, "id", "looperID", "looperId") ??
                                                string.Empty,
                        _ => speakerId
                    };

                if (perception.TryGetProperty("peoplePresent", out var peoplePresent) &&
                    peoplePresent.ValueKind == JsonValueKind.Array)
                    // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
                    foreach (var person in peoplePresent.EnumerateArray())
                    {
                        var personId = person.ValueKind switch
                        {
                            JsonValueKind.String => person.GetString(),
                            JsonValueKind.Object => TryReadStringProperty(person, "id", "looperID", "looperId"),
                            _ => null
                        };

                        if (!string.IsNullOrWhiteSpace(personId) &&
                            !string.Equals(personId, "NOT_TRAINED", StringComparison.OrdinalIgnoreCase))
                            peoplePresentIds.Add(personId);
                    }
            }

            var triggerLooperId = turn.Attributes.TryGetValue("triggerLooperId", out var rawTriggerLooperId)
                ? rawTriggerLooperId?.ToString()
                : null;
            var primaryPersonId = !string.IsNullOrWhiteSpace(speakerId)
                ? speakerId
                : !string.IsNullOrWhiteSpace(triggerLooperId)
                    ? triggerLooperId
                    : peoplePresentIds.FirstOrDefault();

            return new GreetingPresenceProfile(
                primaryPersonId,
                string.IsNullOrWhiteSpace(speakerId) ? null : speakerId,
                peoplePresentIds,
                loopUsers);
        }
        catch
        {
            return GreetingPresenceProfile.Empty;
        }
    }

    private static string? TryReadStringProperty(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (source.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();

        return null;
    }

    private static double? TryReadDoubleProperty(JsonElement source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (source.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var parsed))
                return parsed;

        return null;
    }

    private static bool? ShouldUseCelsius(TurnContext turn, string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        if (normalized.Contains("celsius", StringComparison.Ordinal) ||
            normalized.Contains("centigrade", StringComparison.Ordinal))
            return true;

        if (normalized.Contains("fahrenheit", StringComparison.Ordinal)) return false;

        var entities = ReadEntities(turn);
        if (entities.TryGetValue("temperatureUnit", out var entityUnit))
        {
            if (entityUnit.Contains("celsius", StringComparison.OrdinalIgnoreCase)) return true;

            if (entityUnit.Contains("fahrenheit", StringComparison.OrdinalIgnoreCase)) return false;
        }

        var locale = turn.Locale ?? string.Empty;
        if (locale.EndsWith("-US", StringComparison.OrdinalIgnoreCase)) return false;

        return null;
    }

    private static WeatherDateEntity ResolveWeatherDateEntity(
        TurnContext turn,
        string transcript,
        string normalizedTranscript,
        DateTimeOffset? referenceLocalTime)
    {
        normalizedTranscript = string.IsNullOrWhiteSpace(normalizedTranscript)
            ? NormalizeCommandPhrase(transcript)
            : normalizedTranscript;

        if (TryResolveWeatherDateEntityFromTranscript(normalizedTranscript, referenceLocalTime,
                out var entityFromTranscript)) return entityFromTranscript;

        var entities = ReadEntities(turn);
        if (TryResolveWeatherDateEntityFromClientEntities(entities, referenceLocalTime, out var entityFromClient) &&
            ShouldAcceptClientWeatherDateEntity(normalizedTranscript))
            return entityFromClient;

        return WeatherDateEntity.None;
    }

    private static bool TryResolveWeatherDateEntityFromTranscript(
        string normalizedTranscript,
        DateTimeOffset? referenceLocalTime,
        out WeatherDateEntity weatherDate)
    {
        weatherDate = WeatherDateEntity.None;
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return false;

        if (normalizedTranscript.Contains("day after tomorrow", StringComparison.Ordinal))
        {
            weatherDate = new WeatherDateEntity("day_after_tomorrow", 2, "The day after tomorrow");
            return true;
        }

        if (MatchesAny(normalizedTranscript, "tomorrow", "tomorrow s", "tomorrow's"))
        {
            weatherDate = new WeatherDateEntity("tomorrow", 1, "Tomorrow");
            return true;
        }

        if (referenceLocalTime is not null &&
            TryResolveWeatherTimeRangeOffset(normalizedTranscript, referenceLocalTime.Value, out var rangeOffset,
                out var rangeLeadIn) &&
            rangeOffset > 0)
        {
            weatherDate = new WeatherDateEntity("range", rangeOffset, rangeLeadIn);
            return true;
        }

        if (referenceLocalTime is null ||
            !TryResolveWeatherDayOfWeekOffset(normalizedTranscript, referenceLocalTime.Value, out var dayOffset,
                out var dayName) ||
            dayOffset <= 0)
            return false;

        weatherDate = new WeatherDateEntity("weekday", dayOffset, $"On {dayName}");
        return true;
    }

    private static bool ShouldAcceptClientWeatherDateEntity(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return true;

        if (HasExplicitWeatherDateCue(normalizedTranscript)) return false;

        if (HasWeatherLocationClause(normalizedTranscript)) return false;

        return !normalizedTranscript.Contains("forecast", StringComparison.Ordinal);
    }

    private static bool HasExplicitWeatherDateCue(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return false;

        return MatchesAny(
            normalizedTranscript,
            "today",
            "today s",
            "today's",
            "tonight",
            "tomorrow",
            "tomorrow s",
            "tomorrow's",
            "day after tomorrow",
            "this week",
            "next week",
            "weekend",
            "monday",
            "tuesday",
            "wednesday",
            "thursday",
            "friday",
            "saturday",
            "sunday") || WeatherDayOfWeekPattern.IsMatch(normalizedTranscript);
    }

    private static bool HasWeatherLocationClause(string normalizedTranscript)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript)) return false;

        return WeatherTopicLocationPattern.IsMatch(normalizedTranscript) ||
               WeatherLocationPattern.IsMatch(normalizedTranscript);
    }

    private static bool TryResolveWeatherDateEntityFromClientEntities(
        IReadOnlyDictionary<string, string> clientEntities,
        DateTimeOffset? referenceLocalTime,
        out WeatherDateEntity weatherDate)
    {
        weatherDate = WeatherDateEntity.None;
        if (!TryReadClientWeatherDateValue(clientEntities, out var rawDateValue)) return false;

        var normalizedDate = NormalizeCommandPhrase(rawDateValue);
        if (normalizedDate.Contains("day after tomorrow", StringComparison.Ordinal))
        {
            weatherDate = new WeatherDateEntity("day_after_tomorrow", 2, "The day after tomorrow");
            return true;
        }

        if (MatchesAny(normalizedDate, "tomorrow", "tomorrow s", "tomorrow's"))
        {
            weatherDate = new WeatherDateEntity("tomorrow", 1, "Tomorrow");
            return true;
        }

        if (referenceLocalTime is not null &&
            TryResolveWeatherTimeRangeOffset(normalizedDate, referenceLocalTime.Value, out var rangeOffset,
                out var rangeLeadIn) &&
            rangeOffset > 0)
        {
            weatherDate = new WeatherDateEntity("range", rangeOffset, rangeLeadIn);
            return true;
        }

        DateOnly targetDate;
        if (DateOnly.TryParse(rawDateValue, out var parsedDate))
            targetDate = parsedDate;
        else if (DateTimeOffset.TryParse(rawDateValue, out var parsedDateTimeOffset))
            targetDate = DateOnly.FromDateTime(parsedDateTimeOffset.DateTime);
        else
            return false;

        var referenceDate = DateOnly.FromDateTime((referenceLocalTime ?? DateTimeOffset.UtcNow).DateTime);
        var dayOffset = targetDate.DayNumber - referenceDate.DayNumber;
        if (dayOffset <= 0) return false;

        weatherDate = dayOffset == 1
            ? new WeatherDateEntity("tomorrow", 1, "Tomorrow")
            : new WeatherDateEntity(
                "date",
                dayOffset,
                $"On {targetDate.ToDateTime(TimeOnly.MinValue).ToString("dddd", CultureInfo.InvariantCulture)}");
        return true;
    }

    private static bool TryReadClientWeatherDateValue(
        IReadOnlyDictionary<string, string> clientEntities,
        out string dateValue)
    {
        foreach (var key in WeatherDateEntityKeys)
        {
            if (!clientEntities.TryGetValue(key, out var rawValue) ||
                string.IsNullOrWhiteSpace(rawValue))
                continue;

            dateValue = rawValue.Trim();
            return true;
        }

        dateValue = string.Empty;
        return false;
    }

    private static bool TryResolveWeatherDayOfWeekOffset(
        string normalizedTranscript,
        DateTimeOffset referenceLocalTime,
        out int dayOffset,
        out string dayName)
    {
        dayOffset = 0;
        dayName = string.Empty;

        var match = WeatherDayOfWeekPattern.Match(normalizedTranscript);
        if (!match.Success) return false;

        if (!TryParseDayOfWeek(match.Groups["day"].Value, out var targetDay)) return false;

        var currentDay = referenceLocalTime.DayOfWeek;
        var daysUntil = ((int)targetDay - (int)currentDay + 7) % 7;
        if (daysUntil == 0)
        {
            if (match.Groups["next"].Success)
                daysUntil = 7;
            else if (match.Groups["this"].Success)
                daysUntil = 0;
        }

        if (daysUntil <= 0) return false;

        dayOffset = daysUntil;
        dayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(targetDay.ToString().ToLowerInvariant());
        return true;
    }

    private static bool TryResolveWeatherTimeRangeOffset(
        string normalizedTranscript,
        DateTimeOffset referenceLocalTime,
        out int dayOffset,
        out string leadIn)
    {
        dayOffset = 0;
        leadIn = string.Empty;

        var hasNextWeek = normalizedTranscript.Contains("next week", StringComparison.Ordinal);
        if (hasNextWeek)
        {
            dayOffset = 7;
            leadIn = "Next week";
            return true;
        }

        var hasThisWeek = normalizedTranscript.Contains("this week", StringComparison.Ordinal);
        if (!hasThisWeek) return false;

        dayOffset = referenceLocalTime.DayOfWeek == DayOfWeek.Saturday ? 1 : 2;
        leadIn = "Later this week";
        return true;
    }

    private static bool TryParseDayOfWeek(string dayToken, out DayOfWeek dayOfWeek)
    {
        dayOfWeek = DayOfWeek.Sunday;
        return dayToken switch
        {
            "monday" => AssignDayOfWeek(DayOfWeek.Monday, out dayOfWeek),
            "tuesday" => AssignDayOfWeek(DayOfWeek.Tuesday, out dayOfWeek),
            "wednesday" => AssignDayOfWeek(DayOfWeek.Wednesday, out dayOfWeek),
            "thursday" => AssignDayOfWeek(DayOfWeek.Thursday, out dayOfWeek),
            "friday" => AssignDayOfWeek(DayOfWeek.Friday, out dayOfWeek),
            "saturday" => AssignDayOfWeek(DayOfWeek.Saturday, out dayOfWeek),
            "sunday" => AssignDayOfWeek(DayOfWeek.Sunday, out dayOfWeek),
            _ => false
        };
    }

    private static bool AssignDayOfWeek(DayOfWeek value, out DayOfWeek target)
    {
        target = value;
        return true;
    }

    private static bool IsWelcomeBackGreeting(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "i am back",
            "i m back",
            "im back",
            "i am home",
            "i m home",
            "im home",
            "i'm back",
            "i'm home",
            "welcome back");
    }

    private static bool IsGoodMorningGreeting(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "good morning",
            "morning jibo",
            "morning, jibo");
    }

    private static bool IsGoodAfternoonGreeting(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "good afternoon",
            "afternoon jibo",
            "afternoon, jibo");
    }

    private static bool IsGoodEveningGreeting(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "good evening",
            "evening jibo",
            "evening, jibo");
    }

    private static bool IsGoodNightGreeting(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "good night",
            "night jibo",
            "night, jibo");
    }

    private static bool IsDanceQuestion(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return normalized is
                   "do you like to dance" or
                   "do you like dancing" or
                   "do you enjoy dancing" or
                   "do you love to dance" or
                   "do you love dancing" or
                   "what kind of dance do you like" or
                   "what kind of dancing do you like" or
                   "what is your favorite dance" or
                   "what s your favorite dance" or
                   "what's your favorite dance" or
                   "what is your favourite dance" or
                   "what s your favourite dance" or
                   "what's your favourite dance" or
                   "what dance do you like" or
                   "which dance do you like" ||
               normalized.Contains("your favorite dance", StringComparison.Ordinal) ||
               normalized.Contains("your favourite dance", StringComparison.Ordinal);
    }

    private static bool IsDanceAbilityQuestion(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return normalized is
                   "can you dance" or
                   "could you dance" or
                   "would you dance" or
                   "would you please dance" or
                   "could you please dance" or
                   "will you dance" or
                   "will you please dance" or
                   "do you dance" or
                   "are you able to dance" or
                   "do you know how to dance" or
                   "are you good at dancing" or
                   "are you good at dance" or
                   "can you please dance" or
                   "can you do a dance" or
                   "can you do the dance" or
                   "can you please do a dance" or
                   "could you do a dance" or
                   "could you please do a dance" or
                   "would you do a dance" or
                   "would you please do a dance" ||
               normalized.StartsWith("can you dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("can you please dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("could you dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("could you please dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("would you dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("would you please dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("will you dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("will you please dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("do you know how to dance ", StringComparison.Ordinal);
    }

    private static bool IsDanceCommand(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return normalized is
                   "dance" or
                   "boogie" or
                   "do a dance" or
                   "do your dance" or
                   "show me a dance" or
                   "show us a dance" or
                   "show me your dance" or
                   "show us your dance" or
                   "dance for me" or
                   "dance for us" or
                   "dance with me" or
                   "dance with us" or
                   "bust a move" or
                   "bust some moves" or
                   "do some dancing" or
                   "start dancing" or
                   "lets dance" or
                   "let s dance" ||
               normalized.StartsWith("dance ", StringComparison.Ordinal) ||
               normalized.StartsWith("boogie ", StringComparison.Ordinal);
    }

    private static bool IsTwerkCommand(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return normalized is
                   "twerk" or
                   "do a twerk" or
                   "do the twerk" or
                   "show me a twerk" or
                   "show us a twerk" or
                   "can you twerk" or
                   "can you please twerk" or
                   "will you twerk" or
                   "will you please twerk" or
                   "would you twerk" or
                   "would you please twerk" or
                   "could you twerk" or
                   "could you please twerk" ||
               normalized.StartsWith("twerk ", StringComparison.Ordinal);
    }

    private static bool IsRobotBirthdayQuestion(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        if (MatchesAny(
                normalized,
                "when is your birthday",
                "when s your birthday",
                "what s your birthday",
                "what is your birthday",
                "when is your bday",
                "when s your bday",
                "what s your bday",
                "what is your bday",
                "when were you born",
                "what day is your birthday"))
            return true;

        return (normalized.Contains("your birthday", StringComparison.Ordinal) ||
                normalized.Contains("your bday", StringComparison.Ordinal) ||
                normalized.Contains("your birth date", StringComparison.Ordinal))
               && !normalized.Contains("my birthday", StringComparison.Ordinal);
    }

    private static bool IsNameSetStatement(string loweredTranscript)
    {
        return TryExtractNameFact(loweredTranscript) is not null;
    }

    private static bool IsNameRecallQuestion(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "what is my name",
            "what s my name",
            "what's my name",
            "who am i",
            "do you remember my name",
            "do you know me",
            "do you remember me",
            "who is this",
            "can you recognize me");
    }

    private static bool IsVerifyMeRequest(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return MatchesAny(
            normalized,
            "verify me",
            "verify",
            "verification",
            "verification code",
            "give me my verification code",
            "what is my verification code",
            "what's my verification code");
    }

    private static string? TryExtractNameFact(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        var prefixes = new[]
        {
            "my name is ",
            "call me "
        };

        return (from prefix in prefixes
            where normalized.StartsWith(prefix, StringComparison.Ordinal)
            select normalized[prefix.Length..].Trim()
            into name
            select string.IsNullOrWhiteSpace(name) ? null : name).FirstOrDefault();
    }

    private static bool IsUserBirthdayRecallQuestion(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return MatchesAny(
            normalized,
            "when is my birthday",
            "when s my birthday",
            "what is my birthday",
            "what s my birthday",
            "when is my bday",
            "when s my bday",
            "what is my bday",
            "what s my bday",
            "when is my birth date",
            "when s my birth date",
            "what is my birth date",
            "what s my birth date",
            "when is my birthdate",
            "when s my birthdate",
            "what is my birthdate",
            "what s my birthdate",
            "do you remember my birthday",
            "do you remember my bday",
            "do you remember my birth date",
            "do you remember my birthdate",
            "tell me my birthday",
            "tell me my bday",
            "tell me my birth date",
            "tell me my birthdate");
    }

    private static bool IsUserBirthdaySetStatement(string loweredTranscript)
    {
        return TryExtractBirthdayFact(loweredTranscript) is not null;
    }

    private static bool IsUserBirthdaySetAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return BirthdaySetMarkers.Any(marker => normalized.Contains(marker.TrimEnd(), StringComparison.Ordinal));
    }

    private static bool IsUserBirthdayRecallAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return (normalized.Contains("my birthday", StringComparison.Ordinal) ||
                normalized.Contains("my bday", StringComparison.Ordinal) ||
                normalized.Contains("my birth date", StringComparison.Ordinal) ||
                normalized.Contains("my birthdate", StringComparison.Ordinal)) &&
               (normalized.StartsWith("when", StringComparison.Ordinal) ||
                normalized.StartsWith("what", StringComparison.Ordinal) ||
                normalized.StartsWith("tell me", StringComparison.Ordinal) ||
                normalized.StartsWith("do you remember", StringComparison.Ordinal));
    }

    private static string? TryExtractBirthdayFact(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        return (from marker in BirthdaySetMarkers
                let markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal)
                where markerIndex >= 0
                select normalized[(markerIndex + marker.Length)..].Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool IsPreferenceRecallQuestion(string loweredTranscript)
    {
        return TryExtractPreferenceLookupCategory(loweredTranscript) is not null;
    }

    private static bool IsPreferenceSetStatement(string loweredTranscript)
    {
        return TryExtractPreferenceSet(loweredTranscript) is not null;
    }

    private static bool IsPreferenceSetAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        if (IsPreferenceRecallAttempt(normalized)) return false;

        return normalized.Contains("my favorite", StringComparison.Ordinal) ||
               normalized.Contains("my favourite", StringComparison.Ordinal) ||
               PreferenceReverseMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static bool IsPreferenceRecallAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return TryExtractPreferenceLookupCategory(normalized) is not null ||
               normalized.StartsWith("what is my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("what s my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("what's my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("what is my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("what s my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("what's my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("what is my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("what s my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("what's my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("do you remember my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you remember my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you remember my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("do you know my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you know my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you know my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("do you recall my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you recall my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("do you recall my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("can you tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("can you tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("can you tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("can you please tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("can you please tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("can you please tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("could you tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("could you please tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you please tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you please tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("would you tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("would you please tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you please tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you please tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("please tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("please tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("please tell me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("could you remind me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you remind me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("could you remind me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("would you remind me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you remind me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("would you remind me my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("what was my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("what was my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("what was my fave", StringComparison.Ordinal) ||
               normalized.StartsWith("tell me my favorite", StringComparison.Ordinal) ||
               normalized.StartsWith("tell me my favourite", StringComparison.Ordinal) ||
               normalized.StartsWith("tell me my fave", StringComparison.Ordinal);
    }

    private static string? TryExtractPreferenceLookupCategory(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        var prefixes = new[]
        {
            "what is my favorite ",
            "what s my favorite ",
            "what's my favorite ",
            "do you remember my favorite ",
            "do you know my favorite ",
            "do you recall my favorite ",
            "can you tell me my favorite ",
            "can you please tell me my favorite ",
            "could you tell me my favorite ",
            "could you please tell me my favorite ",
            "would you tell me my favorite ",
            "would you please tell me my favorite ",
            "please tell me my favorite ",
            "could you remind me my favorite ",
            "would you remind me my favorite ",
            "what was my favorite ",
            "tell me my favorite ",
            "what is my favourite ",
            "what s my favourite ",
            "what's my favourite ",
            "do you remember my favourite ",
            "do you know my favourite ",
            "do you recall my favourite ",
            "can you tell me my favourite ",
            "can you please tell me my favourite ",
            "could you tell me my favourite ",
            "could you please tell me my favourite ",
            "would you tell me my favourite ",
            "would you please tell me my favourite ",
            "please tell me my favourite ",
            "could you remind me my favourite ",
            "would you remind me my favourite ",
            "what was my favourite ",
            "tell me my favourite ",
            "what is my fave ",
            "what s my fave ",
            "what's my fave ",
            "do you remember my fave ",
            "do you know my fave ",
            "do you recall my fave ",
            "can you tell me my fave ",
            "can you please tell me my fave ",
            "could you tell me my fave ",
            "could you please tell me my fave ",
            "would you tell me my fave ",
            "would you please tell me my fave ",
            "please tell me my fave ",
            "could you remind me my fave ",
            "would you remind me my fave ",
            "what was my fave ",
            "tell me my fave "
        };

        var category = (from prefix in prefixes
            where normalized.StartsWith(prefix, StringComparison.Ordinal)
            select normalized[prefix.Length..].Trim()
            into candidate
            select string.IsNullOrWhiteSpace(candidate) ? null : candidate).FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(category)) return category;

        return TryExtractEmbeddedPreferenceCategory(normalized, "do you know what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you know what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you know what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you remember what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you remember what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you remember what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you recall what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you recall what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "do you recall what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you please tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you please tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "can you please tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you please tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you please tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you please tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you please tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you please tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you please tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "please tell me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "please tell me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "please tell me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you remind me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you remind me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "could you remind me what my", "fave") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you remind me what my", "favorite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you remind me what my", "favourite") ??
               TryExtractEmbeddedPreferenceCategory(normalized, "would you remind me what my", "fave") ??
               TryExtractTellMeWhatPreferenceCategory(normalized, "favorite") ??
               TryExtractTellMeWhatPreferenceCategory(normalized, "favourite") ??
               TryExtractTellMeWhatPreferenceCategory(normalized, "fave");
    }


    private static string? TryExtractTellMeWhatPreferenceCategory(string normalized, string spelling)
    {
        return TryExtractEmbeddedPreferenceCategory(normalized, "tell me what my", spelling);
    }

    private static string? TryExtractEmbeddedPreferenceCategory(string normalized, string lead, string spelling)
    {
        var prefix = $"{lead} {spelling} ";
        const string suffix = " is";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal) ||
            !normalized.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var category = normalized[prefix.Length..^suffix.Length].Trim();
        return string.IsNullOrWhiteSpace(category) ? null : category;
    }

    private static (string Category, string Value)? TryExtractPreferenceSet(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        foreach (var marker in PreferenceSetMarkers)
        {
            var markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0) continue;

            var preferencePhrase = normalized[(markerIndex + marker.Length)..];
            const string splitMarker = " is ";
            var splitIndex = preferencePhrase.IndexOf(splitMarker, StringComparison.Ordinal);
            if (splitIndex <= 0 || splitIndex >= preferencePhrase.Length - splitMarker.Length)
            {
                var fallbackPreference = TryExtractPreferenceSetWithoutCopula(preferencePhrase);
                if (fallbackPreference is not null) return fallbackPreference;

                continue;
            }

            var category = preferencePhrase[..splitIndex].Trim();
            var value = preferencePhrase[(splitIndex + splitMarker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(value)) return (category, value);
        }

        if (normalized.StartsWith("what ", StringComparison.Ordinal) ||
            normalized.StartsWith("do you remember ", StringComparison.Ordinal))
            return null;

        foreach (var marker in PreferenceReverseMarkers)
        {
            var markerIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex <= 0 || markerIndex >= normalized.Length - marker.Length) continue;

            var value = normalized[..markerIndex].Trim();
            var category = normalized[(markerIndex + marker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(value)) return (category, value);
        }

        return null;
    }

    private static (string Category, string Value)? TryExtractPreferenceSetWithoutCopula(string preferencePhrase)
    {
        if (string.IsNullOrWhiteSpace(preferencePhrase)) return null;

        var normalized = preferencePhrase.Trim();
        if (normalized.Contains(" is ", StringComparison.Ordinal) ||
            normalized.Contains(" are ", StringComparison.Ordinal) ||
            normalized.EndsWith(" is", StringComparison.Ordinal) ||
            normalized.EndsWith(" are", StringComparison.Ordinal))
            return null;

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        var category = parts[0];
        var value = string.Join(' ', parts.Skip(1)).Trim();
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(value)) return null;

        return (category, value);
    }

    private static bool IsImportantDateSetStatement(string loweredTranscript)
    {
        return TryExtractImportantDateSet(loweredTranscript) is not null;
    }

    private static bool IsImportantDateRecallQuestion(string loweredTranscript)
    {
        return TryExtractImportantDateLookupLabel(loweredTranscript) is not null;
    }

    private static (string Label, string Value)? TryExtractImportantDateSet(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        var mapping = new (string Prefix, string Label)[]
        {
            ("our anniversary is ", "anniversary"),
            ("my anniversary is ", "anniversary"),
            ("our wedding anniversary is ", "anniversary")
        };

        foreach (var (prefix, label) in mapping)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var value = normalized[prefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(value)) return (label, value);
        }

        return null;
    }

    private static string? TryExtractImportantDateLookupLabel(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);
        var candidates = new[]
        {
            "when is our anniversary",
            "when s our anniversary",
            "when's our anniversary",
            "when is my anniversary",
            "what is our anniversary",
            "do you remember our anniversary"
        };

        return candidates.Any(candidate => string.Equals(normalized, candidate, StringComparison.Ordinal))
            ? "anniversary"
            : null;
    }

    private static bool IsAffinitySetStatement(string loweredTranscript)
    {
        return TryExtractAffinitySet(loweredTranscript) is not null;
    }

    private static bool IsAffinitySetAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return PegasusUserAffinitySetPrefixes.Any(prefix => MatchesPrefixOrStem(normalized, prefix.Prefix));
    }

    private static bool IsAffinityRecallQuestion(string loweredTranscript)
    {
        return TryExtractAffinityLookup(loweredTranscript) is not null;
    }

    private static bool IsAffinityRecallAttempt(string loweredTranscript)
    {
        var normalized = NormalizeCommandPhrase(loweredTranscript);
        return PegasusUserAffinityLookupPrefixes.Any(prefix => MatchesPrefixOrStem(normalized, prefix.Prefix));
    }

    private static bool MatchesPrefixOrStem(string normalized, string prefix)
    {
        return normalized.StartsWith(prefix, StringComparison.Ordinal) ||
               string.Equals(normalized, prefix.TrimEnd(), StringComparison.Ordinal);
    }

    private static (string Item, PersonalAffinity Affinity)? TryExtractAffinitySet(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);

        foreach (var (prefix, affinity) in PegasusUserAffinitySetPrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var item = normalized[prefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(item)) return (item, affinity);
        }

        return null;
    }

    private static (string Item, PersonalAffinity? ExpectedAffinity)? TryExtractAffinityLookup(string transcript)
    {
        var normalized = NormalizeCommandPhrase(transcript);

        foreach (var (prefix, expectedAffinity) in PegasusUserAffinityLookupPrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var item = normalized[prefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(item)) return (item, expectedAffinity);
        }

        return null;
    }

    private static string DescribeAffinityAsVerb(PersonalAffinity affinity)
    {
        return affinity switch
        {
            PersonalAffinity.Love => "love",
            PersonalAffinity.Like => "like",
            PersonalAffinity.Dislike => "dislike",
            _ => "like"
        };
    }

    private static PersonalMemoryTenantScope ResolveTenantScope(TurnContext turn, string? personId = null)
    {
        var accountId = ReadTenantAttribute(turn, "accountId") ?? "usr_openjibo_owner";
        var loopId = ReadTenantAttribute(turn, "loopId") ?? "openjibo-default-loop";
        var deviceId = turn.DeviceId ?? ReadTenantAttribute(turn, "deviceId") ?? "unknown-device";
        var resolvedPersonId = !string.IsNullOrWhiteSpace(personId)
            ? personId
            : ReadTenantAttribute(turn, "personId") ?? ReadTenantAttribute(turn, "speakerId");
        return new PersonalMemoryTenantScope(accountId, loopId, deviceId, resolvedPersonId);
    }

    private static string? ReadTenantAttribute(TurnContext turn, string key)
    {
        return turn.Attributes.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }

    private static string? TryResolveRadioGenre(string loweredTranscript)
    {
        foreach (var (phrase, station) in RadioGenreAliases)
            if (loweredTranscript.Contains(phrase, StringComparison.Ordinal))
                return station;

        return null;
    }

    private static string FormatRadioGenreForSpeech(string station)
    {
        return station switch
        {
            "EightiesAndNinetiesHits" => "eighties and nineties hits",
            "ChristianAndGospel" => "Christian and gospel",
            "ClassicRock" => "classic rock",
            "CollegeRadio" => "college radio",
            "HipHop" => "hip hop",
            "NewsAndTalk" => "news and talk",
            "ReggaeAndIsland" => "reggae and island music",
            "SoftRock" => "soft rock",
            _ => station
        };
    }

    private static ClockTimerValue? TryParseTimerValue(string loweredTranscript, bool allowImplicit = false)
    {
        if (!allowImplicit && !loweredTranscript.Contains("timer", StringComparison.Ordinal)) return null;

        var hours = ExtractDurationValue(loweredTranscript, "hour");
        var minutes = ExtractDurationValue(loweredTranscript, "minute");
        var seconds = ExtractDurationValue(loweredTranscript, "second");

        if (hours is null && minutes is null && seconds is null) return null;

        return new ClockTimerValue(
            (hours ?? 0).ToString(),
            (minutes ?? 0).ToString(),
            seconds is null ? "null" : seconds.Value.ToString());
    }

    private static ClockAlarmValue? TryParseAlarmValue(
        string loweredTranscript,
        bool allowImplicit = false,
        DateTimeOffset? referenceLocalTime = null)
    {
        if (!allowImplicit && !loweredTranscript.Contains("alarm", StringComparison.Ordinal)) return null;

        var compactMatch = CompactAlarmPattern.Match(loweredTranscript);
        if (compactMatch.Success)
        {
            var compact = compactMatch.Groups["compact"].Value;
            if (int.TryParse(compact, out var compactValue))
            {
                var compactHour = compact.Length switch
                {
                    3 or 4 => compactValue / 100,
                    _ => -1
                };
                var compactMinute = compact.Length switch
                {
                    3 or 4 => compactValue % 100,
                    _ => -1
                };
                if (compactHour is >= 1 and <= 12 && compactMinute is >= 0 and <= 59)
                {
                    var compactAmPm = ResolveAmPm(compactMatch.Groups["ampm"].Value, compactHour, compactMinute,
                        referenceLocalTime);
                    return new ClockAlarmValue($"{compactHour}:{compactMinute:00}", compactAmPm);
                }
            }
        }

        var match = SplitAlarmPattern.Match(loweredTranscript);
        if (!match.Success) return null;

        var hourToken = match.Groups["hour"].Value;
        var minuteToken = match.Groups["minute"].Success ? match.Groups["minute"].Value : "00";
        var hour = ParseNumberToken(hourToken);
        if (hour is null or < 1 or > 12) return null;

        var minute = ParseNumberToken(minuteToken);
        if (minute is null or < 0 or > 59) return null;

        var ampm = ResolveAmPm(match.Groups["ampm"].Value, hour.Value, minute.Value, referenceLocalTime);
        return new ClockAlarmValue($"{hour}:{minute:00}", ampm);
    }

    private static string ResolveAmPm(string token, int hour, int minute, DateTimeOffset? referenceLocalTime)
    {
        var normalized = token.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        if (normalized.StartsWith("p", StringComparison.OrdinalIgnoreCase)) return "pm";

        if (normalized.StartsWith("a", StringComparison.OrdinalIgnoreCase)) return "am";

        return referenceLocalTime.HasValue
            ? ResolveNextOccurrenceAmPm(hour, minute, referenceLocalTime.Value)
            : "am";
    }

    private static string ResolveNextOccurrenceAmPm(int hour, int minute, DateTimeOffset referenceLocalTime)
    {
        var amCandidate = BuildAlarmCandidate(referenceLocalTime, hour, minute, false);
        var pmCandidate = BuildAlarmCandidate(referenceLocalTime, hour, minute, true);
        return amCandidate <= pmCandidate ? "am" : "pm";
    }

    private static DateTimeOffset BuildAlarmCandidate(DateTimeOffset referenceLocalTime, int hour, int minute,
        bool isPm)
    {
        var hour24 = hour % 12;
        if (isPm) hour24 += 12;

        var candidate = new DateTimeOffset(
            referenceLocalTime.Year,
            referenceLocalTime.Month,
            referenceLocalTime.Day,
            hour24,
            minute,
            0,
            referenceLocalTime.Offset);

        if (candidate <= referenceLocalTime) candidate = candidate.AddDays(1);

        return candidate;
    }

    private static bool HasStructuredTimerValue(IReadOnlyDictionary<string, string> clientEntities)
    {
        return clientEntities.ContainsKey("hours") ||
               clientEntities.ContainsKey("minutes") ||
               clientEntities.ContainsKey("seconds");
    }

    private static bool HasStructuredAlarmValue(IReadOnlyDictionary<string, string> clientEntities)
    {
        return clientEntities.TryGetValue("time", out var time) &&
               !string.IsNullOrWhiteSpace(time);
    }

    private static ClockTimerValue? TryReadStructuredTimerValue(IReadOnlyDictionary<string, string> clientEntities)
    {
        if (!HasStructuredTimerValue(clientEntities)) return null;

        clientEntities.TryGetValue("hours", out var hours);
        clientEntities.TryGetValue("minutes", out var minutes);
        clientEntities.TryGetValue("seconds", out var seconds);
        return new ClockTimerValue(
            string.IsNullOrWhiteSpace(hours) ? "0" : hours,
            string.IsNullOrWhiteSpace(minutes) ? "0" : minutes,
            string.IsNullOrWhiteSpace(seconds) ? "null" : seconds);
    }

    private static ClockAlarmValue? TryReadStructuredAlarmValue(IReadOnlyDictionary<string, string> clientEntities)
    {
        if (!clientEntities.TryGetValue("time", out var time) || string.IsNullOrWhiteSpace(time)) return null;

        clientEntities.TryGetValue("ampm", out var ampm);
        return new ClockAlarmValue(time, string.IsNullOrWhiteSpace(ampm) ? "am" : ampm.ToLowerInvariant());
    }

    private static string? ResolveClockDomain(
        IReadOnlyDictionary<string, string> clientEntities,
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules,
        string? lastClockDomain)
    {
        if (clientEntities.TryGetValue("domain", out var clientDomain) &&
            !string.IsNullOrWhiteSpace(clientDomain))
            return clientDomain;

        if (!string.IsNullOrWhiteSpace(lastClockDomain)) return lastClockDomain;

        var combinedRules = clientRules.Concat(listenRules).ToArray();
        if (combinedRules.Any(rule =>
                rule.Contains("timer", StringComparison.OrdinalIgnoreCase) &&
                !rule.Contains("alarm_timer_query_menu", StringComparison.OrdinalIgnoreCase)))
            return "timer";

        return combinedRules.Any(rule =>
            rule.Contains("alarm", StringComparison.OrdinalIgnoreCase) &&
            !rule.Contains("alarm_timer_query_menu", StringComparison.OrdinalIgnoreCase))
            ? "alarm"
            : null;
    }

    private static bool IsTimerRequest(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "set a timer",
            "set timer",
            "start a timer",
            "start timer",
            "timer for");
    }

    private static bool IsAlarmRequest(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "set an alarm",
            "set alarm",
            "wake me up",
            "alarm for");
    }

    private static bool IsCancelRequest(string? clientIntent, string loweredTranscript)
    {
        var normalizedTranscript = NormalizeCommandPhrase(loweredTranscript);
        return string.Equals(clientIntent, "cancel", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clientIntent, "stop", StringComparison.OrdinalIgnoreCase) ||
               normalizedTranscript is "cancel" or "stop" or "never mind" or "nevermind";
    }

    private static bool IsGlobalStopRequest(
        string loweredTranscript,
        string? clientIntent,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        if (string.Equals(clientIntent, "stop", StringComparison.OrdinalIgnoreCase) &&
            IsGlobalCommandsDomain(clientEntities))
            return true;

        var normalizedTranscript = NormalizeCommandPhrase(loweredTranscript);
        return normalizedTranscript is "stop" or "stop it" or "stop that" or "stop talking" or "be quiet"
                   or "be silent" or "shut up" or "silence" or "never mind" or "nevermind" or "forget it" ||
               MatchesAny(normalizedTranscript, "that s enough", "that will do", "that ll do", "cut it out",
                   "cut that out", "no more music", "no more dancing", "quiet down");
    }

    private static bool IsVolumeQueryRequest(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "volume controls",
            "volume control",
            "volume menu",
            "volume level",
            "show volume",
            "show the volume",
            "open volume",
            "open the volume",
            "what is your volume",
            "what's your volume",
            "how is your volume",
            "how s your volume");
    }

    private static bool IsAlarmDeleteRequest(string loweredTranscript)
    {
        var normalizedTranscript = NormalizeCommandPhrase(loweredTranscript);
        return AlarmDeletePattern.IsMatch(normalizedTranscript);
    }

    private static bool IsVolumeUpRequest(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "turn it up",
            "turn this up",
            "turn that up",
            "turn up the volume",
            "turn the volume up",
            "turn volume up",
            "turn your volume up",
            "increase the volume",
            "increase your volume",
            "raise the volume",
            "raise your volume",
            "make it louder",
            "make that louder",
            "speak louder",
            "talk louder",
            "be louder",
            "louder");
    }

    private static bool IsVolumeDownRequest(string loweredTranscript)
    {
        return MatchesAny(
            loweredTranscript,
            "turn it down",
            "turn this down",
            "turn that down",
            "turn down the volume",
            "turn the volume down",
            "turn volume down",
            "turn your volume down",
            "decrease the volume",
            "decrease your volume",
            "lower the volume",
            "lower your volume",
            "make it quieter",
            "make that quieter",
            "make it softer",
            "speak quieter",
            "talk quieter",
            "be quieter",
            "quieter",
            "softer");
    }

    private static string? ResolveVolumeLevel(string loweredTranscript,
        IReadOnlyDictionary<string, string> clientEntities)
    {
        if (clientEntities.TryGetValue("volumeLevel", out var entityValue) &&
            TryNormalizeVolumeLevel(entityValue) is { } structuredLevel)
            return structuredLevel;

        return TryResolveVolumeLevel(loweredTranscript);
    }

    private static string? TryResolveVolumeLevel(string loweredTranscript)
    {
        if (!loweredTranscript.Contains("volume", StringComparison.Ordinal) &&
            !loweredTranscript.Contains("loudness", StringComparison.Ordinal))
            return null;

        if (MatchesAny(loweredTranscript, "max volume", "maximum volume", "volume max", "volume maximum")) return "10";

        if (MatchesAny(loweredTranscript, "min volume", "minimum volume", "volume min", "volume minimum")) return "1";

        var normalizedTranscript = NormalizeCommandPhrase(loweredTranscript);
        var homophoneMatch = VolumeToValueHomophonePattern.Match(normalizedTranscript);
        if (homophoneMatch.Success &&
            TryNormalizeVolumeLevel(homophoneMatch.Groups["value"].Value) is { } homophoneLevel)
            return homophoneLevel;

        var match = VolumeLevelPattern.Match(normalizedTranscript);
        return !match.Success ? null : TryNormalizeVolumeLevel(match.Groups["value"].Value);
    }

    private static string NormalizeCommandPhrase(string value)
    {
        var normalized = TranscriptTextNormalizer.NormalizeLooseText(value);
        if (string.Equals(normalized, "uh huh", StringComparison.Ordinal) ||
            normalized.StartsWith("uh huh ", StringComparison.Ordinal))
            return normalized;

        return TranscriptTextNormalizer.StripLeadingPhrases(normalized, CommandLeadPhrases);
    }

    private static string? TryNormalizeVolumeLevel(string token)
    {
        if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase)) return "null";

        var parsed = ParseNumberToken(token);
        return parsed is >= 1 and <= 10
            ? parsed.Value.ToString()
            : null;
    }

    private static bool IsGlobalCommandsDomain(IReadOnlyDictionary<string, string> clientEntities)
    {
        return clientEntities.TryGetValue("domain", out var domain) &&
               string.Equals(domain, "global_commands", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsClockTimerValueTurn(
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules)
    {
        return clientRules.Concat(listenRules).Any(static rule =>
            rule.Contains("clock/", StringComparison.OrdinalIgnoreCase) &&
            rule.Contains("timer", StringComparison.OrdinalIgnoreCase) &&
            rule.Contains("value", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsClockAlarmValueTurn(
        IReadOnlyList<string> clientRules,
        IReadOnlyList<string> listenRules)
    {
        return clientRules.Concat(listenRules).Any(static rule =>
            rule.Contains("clock/", StringComparison.OrdinalIgnoreCase) &&
            rule.Contains("alarm", StringComparison.OrdinalIgnoreCase) &&
            rule.Contains("value", StringComparison.OrdinalIgnoreCase));
    }

    private static int? ExtractDurationValue(string loweredTranscript, string unitStem)
    {
        var pattern = new Regex($@"\b(?<value>\d+|[a-z\-]+(?:\s+[a-z\-]+)?)\s+{unitStem}s?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = pattern.Match(loweredTranscript);
        if (!match.Success) return null;

        var valueToken = match.Groups["value"].Value.Trim();
        var parsed = ParseNumberToken(valueToken);
        if (parsed is not null) return parsed;

        var parts = valueToken.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return parts.Length > 0
                ? ParseNumberToken(parts[^1])
                : null;

        parsed = ParseNumberToken(string.Join(' ', parts.TakeLast(2)));
        if (parsed is not null) return parsed;

        return parts.Length > 0
            ? ParseNumberToken(parts[^1])
            : null;
    }

    private static int? ParseNumberToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal);
        if (int.TryParse(normalized, out var numeric)) return numeric;

        if (!normalized.Contains(' '))
            return normalized switch
            {
                "a" or "an" => 1,
                "one" => 1,
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                "eleven" => 11,
                "twelve" => 12,
                "thirteen" => 13,
                "fourteen" => 14,
                "fifteen" => 15,
                "sixteen" => 16,
                "seventeen" => 17,
                "eighteen" => 18,
                "nineteen" => 19,
                "twenty" => 20,
                "thirty" => 30,
                "forty" => 40,
                "fifty" => 50,
                _ => null
            };

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return normalized switch
            {
                "a" or "an" => 1,
                "one" => 1,
                "two" => 2,
                "three" => 3,
                "four" => 4,
                "five" => 5,
                "six" => 6,
                "seven" => 7,
                "eight" => 8,
                "nine" => 9,
                "ten" => 10,
                "eleven" => 11,
                "twelve" => 12,
                "thirteen" => 13,
                "fourteen" => 14,
                "fifteen" => 15,
                "sixteen" => 16,
                "seventeen" => 17,
                "eighteen" => 18,
                "nineteen" => 19,
                "twenty" => 20,
                "thirty" => 30,
                "forty" => 40,
                "fifty" => 50,
                _ => null
            };

        var first = ParseNumberToken(parts[0]);
        var second = ParseNumberToken(parts[1]);
        if (first is >= 20 && second is >= 0 and < 10) return first + second;

        return normalized switch
        {
            "a" or "an" => 1,
            "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            "eleven" => 11,
            "twelve" => 12,
            "thirteen" => 13,
            "fourteen" => 14,
            "fifteen" => 15,
            "sixteen" => 16,
            "seventeen" => 17,
            "eighteen" => 18,
            "nineteen" => 19,
            "twenty" => 20,
            "thirty" => 30,
            "forty" => 40,
            "fifty" => 50,
            _ => null
        };
    }

    private sealed record ClockTimerValue(string Hours, string Minutes, string Seconds);

    private sealed record ClockAlarmValue(string Time, string AmPm);

    private sealed record PizzaMimPrompt(string PromptId, string Esml);

    private sealed record ProactivityCandidate(string IntentName, int Weight);

    private sealed record ProactiveFactCategory(string CategoryName, IReadOnlyList<string> Replies);

    private sealed record PizzaSignal(PersonalAffinity? Affinity);

    private sealed record GreetingPresenceProfile(
        string? PrimaryPersonId,
        string? SpeakerId,
        IReadOnlyList<string> PeoplePresentIds,
        IReadOnlyDictionary<string, string> LoopUserFirstNames)
    {
        public static GreetingPresenceProfile Empty { get; } = new(
            null,
            null,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        public bool HasKnownIdentity => !string.IsNullOrWhiteSpace(PrimaryPersonId);
    }

    private sealed record WeatherDateEntity(string? DateEntity, int ForecastDayOffset, string? ForecastLeadIn)
    {
        public static WeatherDateEntity None { get; } = new(null, 0, null);
    }

    private enum YesNoReply
    {
        None = 0,
        Affirmative = 1,
        Negative = 2,
        Ambiguous = 3
    }

    private sealed record WeatherForecastCardSegment(
        string DayName,
        string Summary,
        int High,
        int Low,
        string Icon,
        string Unit,
        string Theme,
        string SpokenLine);
}